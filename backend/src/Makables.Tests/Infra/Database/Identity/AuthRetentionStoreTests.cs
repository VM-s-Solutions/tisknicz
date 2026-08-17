using FluentAssertions;
using Makables.Core.Domain.Identity;
using Makables.Infra.Database;
using Makables.Infra.Database.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Makables.Tests.Infra.Database.Identity;

/// <summary>
/// T-0114 unit tests for <see cref="AuthRetentionStore.PurgeExpiredAsync"/> —
/// three set-based deletes on a dedicated <see cref="MakablesDbContext"/> from
/// an <see cref="IDbContextFactory{TContext}"/>, backed here by the in-memory
/// SQLite harness the T-0113 <c>CompanyRegistryCacheStoreTests</c> established
/// so the deletes run against real EF Core behaviour.
///
/// <para>
/// What must not regress: an artifact that is still redeemable, or a lockout
/// that is still in force, is never deleted. The cutoff is strict
/// less-than on every table.
/// </para>
/// </summary>
public sealed class AuthRetentionStoreTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 16, 3, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Cutoff = Now.AddDays(-30);

    // ---- refresh tokens ----

    [Fact]
    public async Task Deletes_only_refresh_tokens_expired_before_the_cutoff()
    {
        using var harness = SqliteFactoryHarness.Create();
        await SeedAsync(harness.Factory,
            RefreshTokenExpiring(id: "rt-old", expiresAt: Cutoff.AddDays(-1)),
            RefreshTokenExpiring(id: "rt-recent", expiresAt: Cutoff.AddDays(1)),
            RefreshTokenExpiring(id: "rt-live", expiresAt: Now.AddDays(10)));

        var result = await new AuthRetentionStore(harness.Factory)
            .PurgeExpiredAsync(Cutoff, CancellationToken.None);

        result.RefreshTokens.Should().Be(1);
        (await RemainingIdsAsync<RefreshToken>(harness)).Should().Equal("rt-live", "rt-recent");
    }

    [Fact]
    public async Task Refresh_token_cutoff_is_strict_less_than()
    {
        using var harness = SqliteFactoryHarness.Create();
        await SeedAsync(harness.Factory, RefreshTokenExpiring(id: "rt-exact", expiresAt: Cutoff));

        var result = await new AuthRetentionStore(harness.Factory)
            .PurgeExpiredAsync(Cutoff, CancellationToken.None);

        result.RefreshTokens.Should().Be(0);
    }

    [Fact]
    public async Task Revoked_but_unexpired_refresh_token_survives()
    {
        // Reuse detection (ADR 0012) needs the revoked row to still be there
        // while the token could plausibly be replayed. Once expired it is
        // rejected on expiry alone, so retention is free to remove it.
        using var harness = SqliteFactoryHarness.Create();
        var revoked = RefreshTokenExpiring(id: "rt-revoked", expiresAt: Now.AddDays(5));
        revoked.Revoke(Now.AddDays(-1));
        await SeedAsync(harness.Factory, revoked);

        var result = await new AuthRetentionStore(harness.Factory)
            .PurgeExpiredAsync(Cutoff, CancellationToken.None);

        result.RefreshTokens.Should().Be(0);
        (await RemainingIdsAsync<RefreshToken>(harness)).Should().Equal("rt-revoked");
    }

    // ---- one-time tokens ----

    [Fact]
    public async Task Deletes_only_one_time_tokens_expired_before_the_cutoff()
    {
        using var harness = SqliteFactoryHarness.Create();
        await SeedAsync(harness.Factory,
            OneTimeTokenExpiring(hash: "ott-old", expiresAt: Cutoff.AddDays(-1)),
            OneTimeTokenExpiring(hash: "ott-recent", expiresAt: Cutoff.AddDays(1)),
            OneTimeTokenExpiring(hash: "ott-live", expiresAt: Now.AddHours(1)));

        var result = await new AuthRetentionStore(harness.Factory)
            .PurgeExpiredAsync(Cutoff, CancellationToken.None);

        result.OneTimeTokens.Should().Be(1);
        (await RemainingIdsAsync<OneTimeToken>(harness)).Should().Equal("ott-live", "ott-recent");
    }

    [Fact]
    public async Task Consumed_but_unexpired_one_time_token_survives()
    {
        using var harness = SqliteFactoryHarness.Create();
        var consumed = OneTimeTokenExpiring(hash: "ott-consumed", expiresAt: Now.AddHours(1));
        consumed.Consume(Now.AddMinutes(-5));
        await SeedAsync(harness.Factory, consumed);

        var result = await new AuthRetentionStore(harness.Factory)
            .PurgeExpiredAsync(Cutoff, CancellationToken.None);

        result.OneTimeTokens.Should().Be(0);
    }

    // ---- login attempt buckets ----

    [Fact]
    public async Task Deletes_only_stale_login_attempt_buckets()
    {
        using var harness = SqliteFactoryHarness.Create();
        await SeedAsync(harness.Factory,
            LoginAttemptBucket.Create("old@example.com", Cutoff.AddDays(-1)),
            LoginAttemptBucket.Create("recent@example.com", Cutoff.AddDays(1)));

        var result = await new AuthRetentionStore(harness.Factory)
            .PurgeExpiredAsync(Cutoff, CancellationToken.None);

        result.LoginAttemptBuckets.Should().Be(1);
        (await RemainingIdsAsync<LoginAttemptBucket>(harness))
            .Should().Equal("recent@example.com");
    }

    [Fact]
    public async Task Bucket_with_a_lockout_past_the_cutoff_survives_even_if_its_last_attempt_is_stale()
    {
        // Defensive: a sane retention window makes this unreachable (lockouts
        // last minutes), but a misconfigured short window must not release
        // someone who is currently locked out.
        using var harness = SqliteFactoryHarness.Create();
        var locked = LoginAttemptBucket.Create("locked@example.com", Cutoff.AddDays(-1));
        locked.RegisterFailedAttempt(Cutoff.AddDays(-1), threshold: 1, window: TimeSpan.FromDays(40));
        await SeedAsync(harness.Factory, locked);

        var result = await new AuthRetentionStore(harness.Factory)
            .PurgeExpiredAsync(Cutoff, CancellationToken.None);

        result.LoginAttemptBuckets.Should().Be(0);
        (await RemainingIdsAsync<LoginAttemptBucket>(harness))
            .Should().Equal("locked@example.com");
    }

    // ---- shape ----

    [Fact]
    public async Task Empty_tables_return_an_all_zero_result()
    {
        using var harness = SqliteFactoryHarness.Create();

        var result = await new AuthRetentionStore(harness.Factory)
            .PurgeExpiredAsync(Cutoff, CancellationToken.None);

        result.Should().Be(AuthRetentionPurgeResult.Empty);
        result.Total.Should().Be(0);
    }

    [Fact]
    public async Task Purge_is_idempotent_a_second_run_removes_nothing()
    {
        using var harness = SqliteFactoryHarness.Create();
        await SeedAsync(harness.Factory,
            RefreshTokenExpiring(id: "rt-old", expiresAt: Cutoff.AddDays(-1)));
        var sut = new AuthRetentionStore(harness.Factory);

        (await sut.PurgeExpiredAsync(Cutoff, CancellationToken.None)).Total.Should().Be(1);
        (await sut.PurgeExpiredAsync(Cutoff, CancellationToken.None)).Total.Should().Be(0);
    }

    [Fact]
    public async Task Counts_are_reported_per_table()
    {
        using var harness = SqliteFactoryHarness.Create();
        await SeedAsync(harness.Factory,
            RefreshTokenExpiring(id: "rt-1", expiresAt: Cutoff.AddDays(-1)),
            RefreshTokenExpiring(id: "rt-2", expiresAt: Cutoff.AddDays(-2)),
            OneTimeTokenExpiring(hash: "ott-1", expiresAt: Cutoff.AddDays(-1)),
            LoginAttemptBucket.Create("gone@example.com", Cutoff.AddDays(-3)));

        var result = await new AuthRetentionStore(harness.Factory)
            .PurgeExpiredAsync(Cutoff, CancellationToken.None);

        result.Should().Be(new AuthRetentionPurgeResult(2, 1, 1));
        result.Total.Should().Be(4);
    }

    // ---- helpers ----

    private static RefreshToken RefreshTokenExpiring(string id, DateTimeOffset expiresAt)
    {
        var token = RefreshToken.IssueNew(
            id: id,
            userId: "user-1",
            tokenHash: $"hash-{id}",
            familyId: $"family-{id}",
            expiresAt: expiresAt,
            countryCode: "CZ",
            userAgent: "test-agent",
            ipAddress: "203.0.113.7");

        // RefreshToken is Auditable; the AuditableSaveChangesInterceptor that
        // stamps created_by is wired in DI, not on this bare harness context.
        token.MarkCreated("test", expiresAt.AddDays(-30));
        return token;
    }

    private static OneTimeToken OneTimeTokenExpiring(string hash, DateTimeOffset expiresAt) =>
        OneTimeToken.Issue(
            tokenHash: hash,
            userId: "user-1",
            purpose: OneTimeTokenPurpose.EmailConfirmation,
            expiresAt: expiresAt,
            now: expiresAt.AddHours(-1),
            ipAddress: "203.0.113.7");

    private static async Task SeedAsync(
        IDbContextFactory<MakablesDbContext> factory,
        params object[] entities)
    {
        await using var db = factory.CreateDbContext();
        db.AddRange(entities);
        await db.SaveChangesAsync();
    }

    private static async Task<List<string>> RemainingIdsAsync<TEntity>(SqliteFactoryHarness harness)
        where TEntity : class
    {
        await using var db = harness.Factory.CreateDbContext();
        return await db.Set<TEntity>()
            .Select(e => EF.Property<string>(e, "Id"))
            .OrderBy(id => id)
            .ToListAsync();
    }

    private sealed class SqliteFactoryHarness : IDisposable
    {
        private readonly SqliteConnection _connection;

        public IDbContextFactory<MakablesDbContext> Factory { get; }

        private SqliteFactoryHarness(
            SqliteConnection connection,
            IDbContextFactory<MakablesDbContext> factory)
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
