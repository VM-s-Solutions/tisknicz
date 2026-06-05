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
        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE numbering_sequence, orders, products, makers, categories, addresses, users RESTART IDENTITY CASCADE;",
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
