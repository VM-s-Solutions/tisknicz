using FluentAssertions;
using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Numbering;
using Makables.Core.Domain.SeedWork;
using Makables.Infra.Database;
using Makables.Infra.Database.Numbering;
using Makables.Infra.Database.Repositories;
using Makables.IntegrationTests.Common;
using Makables.TestUtilities;
using Microsoft.EntityFrameworkCore;

namespace Makables.IntegrationTests.Numbering;

/// <summary>
/// Closes ADR 0009 lines 143–144 compliance items for the order
/// generator on a real Postgres: rollback safety + concurrent-allocator
/// serialisation. SQLite doesn't honour <c>FOR UPDATE</c> the same way,
/// so these assertions could not be made against the in-memory provider
/// used by the unit-test suite.
///
/// <para>
/// Per-test isolation is handled by truncating <c>numbering_sequence</c>
/// in the constructor; the harness keeps the seeded CZ
/// <c>country_configuration</c> row across tests.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OrderNumberGeneratorRaceSafetyTests
{
    // A fixed UTC moment that resolves to 2026 in Europe/Prague. Keeps
    // the asserted year segment stable regardless of when the test runs.
    private static readonly DateTimeOffset FixedNowUtc =
        new(2026, 5, 24, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgresHarness _harness;

    public OrderNumberGeneratorRaceSafetyTests(PostgresHarness harness)
    {
        _harness = harness;
        // Synchronous reset is fine here — xunit ctors must be sync, and
        // GetAwaiter().GetResult() on a TRUNCATE round-trip is sub-ms.
        _harness.ResetMutableTablesAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Two_concurrent_NextAsync_calls_produce_consecutive_numbers()
    {
        // Pre-seed the row so the FOR UPDATE has something to lock. This
        // models the steady-state operation that every order placed after
        // the first one of the year experiences. The "empty-row race" is
        // a separate concern covered by
        // First_allocation_race_creates_row_exactly_once below.
        await SeedFirstAllocationAsync();

        // Independent DbContexts so each call owns its own connection and
        // transaction — this is what makes the FOR UPDATE row-lock the
        // serialisation point, mirroring two real concurrent commands
        // running under UnitOfWorkPipelineBehavior.
        var genA = BuildGenerator(out var dbA);
        var genB = BuildGenerator(out var dbB);
        await using (dbA)
        await using (dbB)
        {
            // Each call must run inside its own transaction so the FOR UPDATE
            // row lock survives across the SELECT and the subsequent commit
            // — Npgsql otherwise commits each command in an implicit txn and
            // releases the lock immediately (the allocator XML doc calls this
            // out as the caller's responsibility).
            var taskA = RunInTransactionAsync(dbA, ct => genA.NextAsync("CZ", ct));
            var taskB = RunInTransactionAsync(dbB, ct => genB.NextAsync("CZ", ct));

            var results = await Task.WhenAll(taskA, taskB);

            // The pre-seed already consumed 0001, so the next two numbers
            // are 0002 + 0003 — contiguous, no duplicates, no gaps.
            results.Should().BeEquivalentTo(
                new[] { "M-CZ-20260002", "M-CZ-20260003" },
                because: "FOR UPDATE serialises the two callers; sequence values " +
                         "must be contiguous with zero duplicates");
        }
    }

    [Fact]
    public async Task Failed_command_leaves_last_used_value_unchanged()
    {
        // Allocate once successfully so the sequence row exists with
        // last_used_value = 1. This is the "happy then sad" path.
        var genWarm = BuildGenerator(out var dbWarm);
        await using (dbWarm)
        {
            var first = await RunInTransactionAsync(dbWarm, ct => genWarm.NextAsync("CZ", ct));
            first.Should().Be("M-CZ-20260001");
        }

        // Now allocate again inside a transaction that we explicitly roll
        // back. The increment must NOT be observed by a fresh reader.
        var genCold = BuildGenerator(out var dbCold);
        await using (dbCold)
        {
            await using var tx = await dbCold.Database.BeginTransactionAsync();
            var doomed = await genCold.NextAsync("CZ", CancellationToken.None);
            doomed.Should().Be("M-CZ-20260002",
                because: "the allocator returns 0002 inside the txn before rollback");
            await dbCold.SaveChangesAsync();
            await tx.RollbackAsync();
        }

        // Assert via an independent connection that the row's
        // LastUsedValue is still 1 (not 2). Closes ADR 0009 line 143.
        await using var dbAssert = _harness.CreateDbContext();
        var row = await dbAssert.Set<NumberingSequence>()
            .AsNoTracking()
            .FirstOrDefaultAsync(s =>
                s.CountryCode == "CZ" && s.Scope == NumberingScope.Order && s.Year == 2026);

        row.Should().NotBeNull();
        row!.LastUsedValue.Should().Be(1,
            because: "the rolled-back transaction must not have consumed the second number");
    }

    [Fact]
    public async Task First_allocation_race_creates_row_exactly_once()
    {
        // Empty numbering_sequence (TRUNCATE in ctor guarantees this).
        // Two concurrent allocators race to INSERT the (CZ, order, 2026)
        // row. The current allocator (NumberingSequenceAllocator) does
        // NOT retry on a lost first-allocation race — both transactions
        // see an empty result from SELECT FOR UPDATE (nothing to lock),
        // both add a new entity, and the second SaveChangesAsync surfaces
        // the unique-PK violation as UniqueConstraintViolationException
        // via MakablesDbContext.SaveChangesAsync's 23505 translator.
        //
        // This test pins that behaviour so a future allocator rewrite
        // (e.g. adding ON CONFLICT or a separate "ensure row exists"
        // transaction) cannot regress silently. Runtime contract: the
        // loser currently surfaces UniqueConstraintViolationException
        // verbatim (the PK_numbering_sequence constraint is intentionally
        // NOT mapped in UniqueConstraintTranslator — same policy as
        // ix_orders_order_number per the file's "defence-in-depth
        // invariants stay unmapped" rule, since a monotonic generator
        // should never produce a colliding insert). The caller's next
        // attempt finds the freshly-committed row and increments cleanly
        // to 0002. If the team later decides this race should surface as
        // a typed BusinessResult.Failure(Conflict), the fix is to add
        // PK_numbering_sequence to UniqueConstraintTranslator + a comment
        // explaining the policy carve-out. T-0062 Copilot review R3.
        var genA = BuildGenerator(out var dbA);
        var genB = BuildGenerator(out var dbB);

        Task<string>? taskA = null;
        Task<string>? taskB = null;
        try
        {
            taskA = RunInTransactionAsync(dbA, ct => genA.NextAsync("CZ", ct));
            taskB = RunInTransactionAsync(dbB, ct => genB.NextAsync("CZ", ct));

            // Wait without throwing so we can inspect both outcomes.
            await Task.WhenAll(
                taskA.ContinueWith(_ => { }, TaskScheduler.Default),
                taskB.ContinueWith(_ => { }, TaskScheduler.Default));
        }
        finally
        {
            await dbA.DisposeAsync();
            await dbB.DisposeAsync();
        }

        var outcomes = new[] { taskA!, taskB! };
        var succeeded = outcomes.Where(t => t.Status == TaskStatus.RanToCompletion).ToArray();
        var faulted = outcomes.Where(t => t.IsFaulted).ToArray();

        // The race can finish two ways depending on Postgres' scheduling:
        //   (1) both SELECT FOR UPDATEs return empty → both INSERT →
        //       loser gets 23505 → exactly one succeeds, one faults;
        //   (2) one transaction commits its INSERT before the other's
        //       SELECT FOR UPDATE runs → second sees the row, locks it,
        //       and increments → both succeed with consecutive numbers.
        // Both are acceptable allocator behaviours; both leave the table
        // with exactly one row. The flake-resistant assertion is on the
        // post-race invariant, not which scheduling path was taken.

        if (succeeded.Length == 1)
        {
            // Path 1: loser surfaces the unique-PK violation.
            var winner = await succeeded[0];
            winner.Should().Be("M-CZ-20260001");
            faulted.Should().HaveCount(1);
            var loserError = faulted[0].Exception!.GetBaseException();
            loserError.Should().BeOfType<UniqueConstraintViolationException>(
                because: "the allocator does not retry on a lost first-allocation race; " +
                         "the unique-PK violation must surface as the typed exception so " +
                         "UnitOfWorkPipelineBehavior can translate it to BusinessResult.Failure(Conflict)");
        }
        else
        {
            // Path 2: scheduling let the second caller see the committed row.
            succeeded.Should().HaveCount(2);
            var winners = await Task.WhenAll(succeeded);
            winners.Should().BeEquivalentTo(new[] { "M-CZ-20260001", "M-CZ-20260002" });
            faulted.Should().BeEmpty();
        }

        // Pin the post-race invariant: exactly one row exists.
        await using var dbAssert = _harness.CreateDbContext();
        var rowCount = await dbAssert.Set<NumberingSequence>()
            .AsNoTracking()
            .CountAsync(s =>
                s.CountryCode == "CZ" && s.Scope == NumberingScope.Order && s.Year == 2026);
        rowCount.Should().Be(1,
            because: "the (country, scope, year) PK must prevent duplicate rows under race");
    }

    private async Task SeedFirstAllocationAsync()
    {
        var gen = BuildGenerator(out var db);
        await using (db)
        {
            await RunInTransactionAsync(db, ct => gen.NextAsync("CZ", ct));
        }
    }

    private OrderNumberGenerator BuildGenerator(out MakablesDbContext db)
    {
        db = _harness.CreateDbContext();
        var configs = new CountryConfigurationRepository(db);
        var clock = new FakeClock(FixedNowUtc);
        return new OrderNumberGenerator(db, clock, configs);
    }

    /// <summary>
    /// Open an explicit transaction on the supplied context, run the
    /// generator inside it, commit, and return the number. The generator's
    /// FOR UPDATE lock only survives if the caller owns the transaction —
    /// the production code path gets this from
    /// <c>UnitOfWorkPipelineBehavior</c>; tests must do it themselves.
    /// </summary>
    private static async Task<string> RunInTransactionAsync(
        MakablesDbContext db,
        Func<CancellationToken, Task<string>> work)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var number = await work(CancellationToken.None);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return number;
    }
}
