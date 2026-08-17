using FluentAssertions;
using Makables.Core.Domain.Identity;
using Makables.Infra.Database;
using Makables.Infra.Database.Identity;
using Makables.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Makables.IntegrationTests.Identity;

/// <summary>
/// T-0114 — pins <see cref="AuthRetentionStore"/> against REAL Postgres.
///
/// <para>
/// The unit tests run the same three raw DELETEs on SQLite, and the T-0160
/// incident is the reason that is not enough on its own: SQLite degrades
/// column types (there, <c>jsonb</c> → TEXT), so a statement can pass every
/// unit test and still fail on the live database. Here the timestamps are
/// real <c>timestamptz</c> and <c>locked_until</c> is really nullable, so the
/// comparison and the NULL-tolerant lockout guard are exercised as deployed.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AuthRetentionStorePostgresTests(PostgresHarness harness)
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 16, 3, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Cutoff = Now.AddDays(-30);

    private AuthRetentionStore CreateSut()
    {
        var factory = Substitute.For<IDbContextFactory<MakablesDbContext>>();
        factory.CreateDbContextAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(harness.CreateDbContext()));
        factory.CreateDbContext().Returns(_ => harness.CreateDbContext());
        return new AuthRetentionStore(factory);
    }

    [Fact]
    public async Task Purge_removes_expired_artifacts_and_keeps_live_ones_on_real_postgres()
    {
        await harness.ResetMutableTablesAsync();

        await using (var seed = harness.CreateDbContext())
        {
            seed.Add(RefreshTokenExpiring("rt-old", Cutoff.AddDays(-1)));
            seed.Add(RefreshTokenExpiring("rt-live", Now.AddDays(10)));
            seed.Add(OneTimeTokenExpiring("ott-old", Cutoff.AddDays(-1)));
            seed.Add(OneTimeTokenExpiring("ott-live", Now.AddHours(1)));
            // locked_until NULL — the guard's IS NULL leg.
            seed.Add(LoginAttemptBucket.Create("stale@example.com", Cutoff.AddDays(-1)));
            seed.Add(LoginAttemptBucket.Create("recent@example.com", Cutoff.AddDays(1)));
            await seed.SaveChangesAsync();
        }

        var result = await CreateSut().PurgeExpiredAsync(Cutoff, CancellationToken.None);

        result.Should().Be(new AuthRetentionPurgeResult(1, 1, 1));

        await using var assert = harness.CreateDbContext();
        (await assert.Set<RefreshToken>().Select(t => t.Id).ToListAsync())
            .Should().Equal("rt-live");
        (await assert.Set<OneTimeToken>().Select(t => t.Id).ToListAsync())
            .Should().Equal("ott-live");
        (await assert.Set<LoginAttemptBucket>().Select(b => b.Id).ToListAsync())
            .Should().Equal("recent@example.com");
    }

    [Fact]
    public async Task Locked_bucket_survives_a_cutoff_past_its_last_attempt_on_real_postgres()
    {
        await harness.ResetMutableTablesAsync();

        await using (var seed = harness.CreateDbContext())
        {
            var locked = LoginAttemptBucket.Create("locked@example.com", Cutoff.AddDays(-1));
            locked.RegisterFailedAttempt(Cutoff.AddDays(-1), threshold: 1, window: TimeSpan.FromDays(40));
            seed.Add(locked);
            await seed.SaveChangesAsync();
        }

        var result = await CreateSut().PurgeExpiredAsync(Cutoff, CancellationToken.None);

        result.LoginAttemptBuckets.Should().Be(0);
    }

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
}
