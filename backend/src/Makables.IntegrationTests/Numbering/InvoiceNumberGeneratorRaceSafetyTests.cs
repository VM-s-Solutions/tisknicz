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
/// Closes ADR 0009 lines 143–144 compliance items for the invoice
/// generator on a real Postgres: rollback safety (legally required —
/// invoice numbers are gap-free under § 29 zákon o DPH), concurrent-
/// allocator serialisation, and first-allocation race semantics. SQLite
/// doesn't honour <c>FOR UPDATE</c> the same way, so these assertions
/// could not be made against the in-memory provider used by the
/// unit-test suite. Mirrors <see cref="OrderNumberGeneratorRaceSafetyTests"/>
/// shape so an investigator can diff the two files and see the only
/// differences are the scope name + number prefix.
///
/// <para>
/// Per-test isolation is handled by truncating <c>numbering_sequence</c>
/// (+ <c>invoices</c> for the post-test pin) in the constructor; the
/// harness keeps the seeded CZ <c>country_configuration</c> row across
/// tests.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class InvoiceNumberGeneratorRaceSafetyTests
{
    // A fixed UTC moment that resolves to 2026 in Europe/Prague. Keeps
    // the asserted year segment stable regardless of when the test runs.
    private static readonly DateTimeOffset FixedNowUtc =
        new(2026, 5, 24, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgresHarness _harness;

    public InvoiceNumberGeneratorRaceSafetyTests(PostgresHarness harness)
    {
        _harness = harness;
        _harness.ResetMutableTablesAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Two_concurrent_NextAsync_calls_produce_consecutive_numbers()
    {
        // Pre-seed FV-CZ-20260001 so the FOR UPDATE has a row to lock.
        // This models the steady-state operation that every invoice
        // issued after the first one of the year experiences.
        await SeedFirstAllocationAsync();

        // Independent DbContexts so each call owns its own connection and
        // transaction — this is what makes the FOR UPDATE row-lock the
        // serialisation point, mirroring two real concurrent
        // IssueInvoice commands running under UnitOfWorkPipelineBehavior.
        var genA = BuildGenerator(out var dbA);
        var genB = BuildGenerator(out var dbB);
        await using (dbA)
        await using (dbB)
        {
            var taskA = RunInTransactionAsync(dbA, ct => genA.NextAsync("CZ", ct));
            var taskB = RunInTransactionAsync(dbB, ct => genB.NextAsync("CZ", ct));

            var results = await Task.WhenAll(taskA, taskB);

            // The pre-seed already consumed 0001, so the next two numbers
            // are 0002 + 0003 — contiguous, gap-free, zero duplicates.
            // Gap-free is the legally-required invariant for invoices.
            results.Should().BeEquivalentTo(
                new[] { "FV-CZ-20260002", "FV-CZ-20260003" },
                because: "FOR UPDATE serialises the two callers; invoice " +
                         "numbers must be contiguous with zero duplicates and " +
                         "zero gaps (CZ § 29 zákon o DPH gap-free requirement)");
        }
    }

    [Fact]
    public async Task Failed_command_leaves_last_used_value_unchanged()
    {
        // Allocate once successfully so the sequence row exists with
        // last_used_value = 1.
        var genWarm = BuildGenerator(out var dbWarm);
        await using (dbWarm)
        {
            var first = await RunInTransactionAsync(dbWarm, ct => genWarm.NextAsync("CZ", ct));
            first.Should().Be("FV-CZ-20260001");
        }

        // Now allocate again inside a transaction that we explicitly roll
        // back. The increment must NOT be observed by a fresh reader —
        // this is what makes invoice numbering legally gap-free.
        var genCold = BuildGenerator(out var dbCold);
        await using (dbCold)
        {
            await using var tx = await dbCold.Database.BeginTransactionAsync();
            var doomed = await genCold.NextAsync("CZ", CancellationToken.None);
            doomed.Should().Be("FV-CZ-20260002",
                because: "the allocator returns 0002 inside the txn before rollback");
            await dbCold.SaveChangesAsync();
            await tx.RollbackAsync();
        }

        // Assert via an independent connection that the row's
        // LastUsedValue is still 1 (not 2). Closes ADR 0009 line 143 for
        // the invoice generator.
        await using var dbAssert = _harness.CreateDbContext();
        var row = await dbAssert.Set<NumberingSequence>()
            .AsNoTracking()
            .FirstOrDefaultAsync(s =>
                s.CountryCode == "CZ" && s.Scope == NumberingScope.Invoice && s.Year == 2026);

        row.Should().NotBeNull();
        row!.LastUsedValue.Should().Be(1,
            because: "the rolled-back transaction must not have consumed the second invoice number — " +
                     "gap-free is a CZ legal requirement, not a nice-to-have");
    }

    [Fact]
    public async Task First_allocation_race_creates_row_exactly_once()
    {
        // Empty numbering_sequence (TRUNCATE in ctor guarantees this).
        // Two concurrent allocators race to INSERT the
        // (CZ, invoice, 2026) row. Same contract pin as
        // OrderNumberGeneratorRaceSafetyTests' first-allocation race:
        // the PK_numbering_sequence is NOT mapped in
        // UniqueConstraintTranslator so the loser surfaces
        // UniqueConstraintViolationException verbatim. The caller's next
        // attempt finds the freshly-committed row and increments cleanly.
        // T-0062 Copilot review R3.
        var genA = BuildGenerator(out var dbA);
        var genB = BuildGenerator(out var dbB);

        Task<string>? taskA = null;
        Task<string>? taskB = null;
        try
        {
            taskA = RunInTransactionAsync(dbA, ct => genA.NextAsync("CZ", ct));
            taskB = RunInTransactionAsync(dbB, ct => genB.NextAsync("CZ", ct));

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

        // Same two scheduling paths as the order generator:
        //   (1) both SELECT FOR UPDATEs return empty → both INSERT →
        //       loser gets 23505 → exactly one succeeds, one faults;
        //   (2) one transaction commits its INSERT before the other's
        //       SELECT FOR UPDATE runs → second sees the row, locks it,
        //       and increments → both succeed with consecutive numbers.
        if (succeeded.Length == 1)
        {
            var winner = await succeeded[0];
            winner.Should().Be("FV-CZ-20260001");
            faulted.Should().HaveCount(1);
            var loserError = faulted[0].Exception!.GetBaseException();
            loserError.Should().BeOfType<UniqueConstraintViolationException>(
                because: "the allocator does not retry on a lost first-allocation race; " +
                         "the unique-PK violation must surface as the typed exception so " +
                         "UnitOfWorkPipelineBehavior can translate it (or T-0068b's caller " +
                         "can retry the IssueInvoice command idempotently)");
        }
        else
        {
            succeeded.Should().HaveCount(2);
            var winners = await Task.WhenAll(succeeded);
            winners.Should().BeEquivalentTo(new[] { "FV-CZ-20260001", "FV-CZ-20260002" });
            faulted.Should().BeEmpty();
        }

        // Pin the post-race invariant: exactly one row exists.
        await using var dbAssert = _harness.CreateDbContext();
        var rowCount = await dbAssert.Set<NumberingSequence>()
            .AsNoTracking()
            .CountAsync(s =>
                s.CountryCode == "CZ" && s.Scope == NumberingScope.Invoice && s.Year == 2026);
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

    private InvoiceNumberGenerator BuildGenerator(out MakablesDbContext db)
    {
        db = _harness.CreateDbContext();
        var configs = new CountryConfigurationRepository(db);
        var clock = new FakeClock(FixedNowUtc);
        return new InvoiceNumberGenerator(db, clock, configs);
    }

    /// <summary>
    /// Open an explicit transaction on the supplied context, run the
    /// generator inside it, commit, and return the number. The
    /// generator's FOR UPDATE lock only survives if the caller owns the
    /// transaction — the production code path gets this from
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
