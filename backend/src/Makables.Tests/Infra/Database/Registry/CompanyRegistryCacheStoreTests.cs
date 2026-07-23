using FluentAssertions;
using Makables.Core.Domain.Registry;
using Makables.Infra.Database;
using Makables.Infra.Database.Registry;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Makables.Tests.Infra.Database.Registry;

/// <summary>
/// T-0113 unit tests for <see cref="CompanyRegistryCacheStore.EvictFetchedBeforeAsync"/>.
/// The eviction is a set-based delete on a dedicated
/// <see cref="MakablesDbContext"/> from an
/// <see cref="IDbContextFactory{TContext}"/>; we back the factory with the
/// established in-memory SQLite harness (same shape as
/// <c>AdminReadAuditWriterTests.SqliteFactoryHarness</c>) so the delete runs
/// against real EF Core behaviour.
/// </summary>
public sealed class CompanyRegistryCacheStoreTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 20, 2, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task EvictFetchedBeforeAsync_deletes_only_rows_fetched_before_the_cutoff()
    {
        using var harness = SqliteFactoryHarness.Create();
        await SeedAsync(harness.Factory,
            Entry("ares", "111", fetchedAt: Now.AddDays(-10)), // past 7-day window → evict
            Entry("ares", "222", fetchedAt: Now.AddDays(-6)),  // still usable → keep
            Entry("ares", "333", fetchedAt: Now.AddHours(-1))); // fresh → keep

        var sut = new CompanyRegistryCacheStore(harness.Factory);
        var cutoff = Now.AddDays(-7);

        var deleted = await sut.EvictFetchedBeforeAsync(cutoff, CancellationToken.None);

        deleted.Should().Be(1);

        await using var assertDb = harness.Factory.CreateDbContext();
        var remaining = await assertDb.Set<CompanyRegistryCacheEntry>()
            .Select(e => e.RegistrationNumber)
            .OrderBy(n => n)
            .ToListAsync();
        remaining.Should().Equal("222", "333");
    }

    [Fact]
    public async Task EvictFetchedBeforeAsync_boundary_is_strict_less_than()
    {
        using var harness = SqliteFactoryHarness.Create();
        // A row fetched EXACTLY at the cutoff is NOT deleted (strict <), so the
        // stale-fallback window is never trimmed by a boundary rounding error.
        await SeedAsync(harness.Factory, Entry("ares", "999", fetchedAt: Now.AddDays(-7)));

        var sut = new CompanyRegistryCacheStore(harness.Factory);
        var deleted = await sut.EvictFetchedBeforeAsync(Now.AddDays(-7), CancellationToken.None);

        deleted.Should().Be(0);
    }

    [Fact]
    public async Task EvictFetchedBeforeAsync_on_empty_table_returns_zero()
    {
        using var harness = SqliteFactoryHarness.Create();
        var sut = new CompanyRegistryCacheStore(harness.Factory);

        var deleted = await sut.EvictFetchedBeforeAsync(Now, CancellationToken.None);

        deleted.Should().Be(0);
    }

    private static CompanyRegistryCacheEntry Entry(string registry, string ico, DateTimeOffset fetchedAt) =>
        CompanyRegistryCacheEntry.Create(
            registryCode: registry,
            registrationNumber: ico,
            payloadJson: "{}",
            fetchedAt: fetchedAt,
            expiresAt: fetchedAt.AddHours(24));

    private static async Task SeedAsync(
        IDbContextFactory<MakablesDbContext> factory,
        params CompanyRegistryCacheEntry[] entries)
    {
        await using var db = factory.CreateDbContext();
        db.Set<CompanyRegistryCacheEntry>().AddRange(entries);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// In-memory SQLite scaffolding exposing a real
    /// <see cref="IDbContextFactory{TContext}"/> over one open connection —
    /// mirrors the production own-context path the store resolves.
    /// </summary>
    private sealed class SqliteFactoryHarness : IDisposable
    {
        private readonly SqliteConnection _connection;

        public IDbContextFactory<MakablesDbContext> Factory { get; }

        private SqliteFactoryHarness(SqliteConnection connection, IDbContextFactory<MakablesDbContext> factory)
        {
            _connection = connection;
            Factory = factory;
        }

        public static SqliteFactoryHarness Create()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();

            var options = new DbContextOptionsBuilder<MakablesDbContext>()
                .UseSqlite(connection)
                .Options;

            using (var seedDb = new MakablesDbContext(options))
            {
                seedDb.Database.EnsureCreated();
            }

            return new SqliteFactoryHarness(connection, new InlineDbContextFactory(options));
        }

        public void Dispose() => _connection.Dispose();

        private sealed class InlineDbContextFactory(DbContextOptions<MakablesDbContext> options)
            : IDbContextFactory<MakablesDbContext>
        {
            public MakablesDbContext CreateDbContext() => new(options);
        }
    }
}
