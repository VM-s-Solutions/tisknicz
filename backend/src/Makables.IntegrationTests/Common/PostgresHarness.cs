using Makables.Infra.Database;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Makables.IntegrationTests.Common;

/// <summary>
/// xunit collection fixture spinning up a single <c>postgres:16-alpine</c>
/// container for the lifetime of the test collection. Per T-0062 (user
/// decision Q3): every Phase-4 race-sensitive test inherits this harness
/// rather than rolling its own container.
///
/// <para>
/// Lifecycle: <see cref="InitializeAsync"/> starts the container, opens
/// a one-shot <see cref="MakablesDbContext"/> against it, and applies
/// every EF Core migration via <see cref="DatabaseFacade.MigrateAsync"/>.
/// The initial-schema migration already seeds the CZ
/// <c>country_configuration</c> + <c>countries</c> rows, so race tests
/// do not need to seed those tables themselves. <see cref="DisposeAsync"/>
/// disposes the container.
/// </para>
///
/// <para>
/// Production parity: the image (<c>postgres:16-alpine</c>) matches the
/// Azure Postgres Flexible Server major version per the deploy Bicep, so
/// tests catch version-specific lock semantics (the whole point of
/// pinning Testcontainers over an in-memory provider for race tests
/// per ADR 0009 lines 143-144).
/// </para>
///
/// <para>
/// Per-test isolation: <see cref="ResetMutableTablesAsync"/> truncates
/// every table that race tests mutate while leaving
/// <c>country_configuration</c> + <c>countries</c> intact. We chose
/// TRUNCATE over a per-test transaction-rollback strategy because the
/// generator under test uses <c>SELECT ... FOR UPDATE</c> inside an
/// explicit transaction owned by the surrounding command (per ADR 0009);
/// wrapping each test in an outer rollback transaction would mask
/// exactly the commit semantics we are trying to verify. TRUNCATE on
/// an empty-ish DB is sub-millisecond per call.
/// </para>
///
/// <para>
/// Usage: a test class declares <c>[Collection(PostgresCollection.Name)]</c>
/// and accepts <see cref="PostgresHarness"/> in its constructor. The
/// collection definition (<see cref="PostgresCollection"/>) keeps a
/// single container shared across the collection's tests.
/// </para>
/// </summary>
public sealed class PostgresHarness : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    /// <summary>
    /// Connection string to the running container. Stable for the
    /// lifetime of the fixture.
    /// </summary>
    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // Apply every migration once at container start. Closes the
        // T-0123 migration-coverage gap for this surface: if any new
        // migration breaks the model snapshot, every race test fails at
        // harness initialisation rather than silently passing on SQLite.
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    /// <summary>
    /// Build a fresh <see cref="MakablesDbContext"/> bound to the running
    /// container. The caller owns disposal. Each test (or each scope
    /// within a test) should construct its own context so EF change
    /// tracking stays isolated.
    /// </summary>
    public MakablesDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<MakablesDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new MakablesDbContext(options);
    }

    /// <summary>
    /// TRUNCATE every table that race tests mutate. Leaves seed tables
    /// (<c>countries</c>, <c>country_configuration</c>) intact so tests
    /// don't have to re-seed the CZ row between runs.
    /// </summary>
    public async Task ResetMutableTablesAsync(CancellationToken cancellationToken = default)
    {
        await using var db = CreateDbContext();
        // CASCADE so future tickets that add FK-bearing tables (e.g. T-0066
        // order_payment_attempts referencing orders) don't break this
        // reset call as the schema grows. The seed tables are explicitly
        // excluded.
        //
        // T-0063: extended to truncate the upstream aggregates the
        // CreateOrder integration tests need to seed fresh — users,
        // addresses, makers, categories, products. Race-sensitive tests
        // (T-0062) only mutated numbering_sequence + orders so the
        // narrower list was enough; the CreateOrder tests need a clean
        // slate up the join graph too.
        // T-0067: extended to truncate outbox_event because MarkOrderPaid
        // enqueues 2 outbox rows per webhook delivery and downstream tests
        // assert on the count for the order they just seeded.
        // T-0068a: extended to truncate invoices so the
        // InvoiceRepository / InvoiceNumberGenerator integration tests
        // start each test with a clean slate. The order matters for FK
        // RESTRICT (order_id → orders) but CASCADE on TRUNCATE bypasses
        // FK constraints anyway.
        // T-0079: extended to truncate order_messages (the CASCADE off
        // orders would catch it via the new FK, but explicit is cheaper
        // to reason about).
        // T-0105: extended to truncate admin_audit_log — the refund
        // integration tests assert exactly-one audit row per command and
        // the append-only table has no FK back to orders, so the orders
        // CASCADE never clears it. TRUNCATE is legal here: the
        // trg_admin_audit_log_reject_* triggers fire on UPDATE/DELETE
        // statements only (row-level), not TRUNCATE.
        // T-0106: extended to truncate disputes (the CASCADE off orders
        // would catch it via the FK, but explicit is cheaper to reason
        // about — T-0079 precedent).
        // T-0101: extended to truncate payout_batches — the CreatePayoutBatch
        // integration tests assert exactly-one batch row per run and the
        // orders/invoices FK is ON DELETE RESTRICT (a plain DELETE on orders
        // would fail), but TRUNCATE ... CASCADE bypasses FK constraints, so
        // truncating both tables together clears the claim links cleanly.
        // T-0100: extended to truncate reviews (the CASCADE off orders +
        // makers would catch it via the new FKs, but explicit is cheaper to
        // reason about — T-0079/T-0106 precedent).
        // T-0110: extended to truncate refresh_tokens — the GDPR-erasure
        // integration tests seed tokens per run and there is no FK from
        // refresh_tokens back to users (the erasure hard-deletes them
        // directly), so the orders/users CASCADE never clears them.
        // T-0110 (fold): also one_time_tokens + login_attempt_buckets — the
        // erasure e2e now seeds both (M-1/M-2 PII purge assertions) and
        // neither has an FK back to users, so nothing CASCADE-clears them.
        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE numbering_sequence, invoices, payout_batches, order_messages, disputes, reviews, orders, products, makers, categories, addresses, refresh_tokens, one_time_tokens, login_attempt_buckets, users, outbox_event, admin_audit_log RESTART IDENTITY CASCADE;",
            cancellationToken);
    }
}

/// <summary>
/// xunit collection definition so every Postgres-backed integration test
/// class shares one container. Tests opt in via
/// <c>[Collection(PostgresCollection.Name)]</c>.
/// </summary>
[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresHarness>
{
    public const string Name = "postgres";
}
