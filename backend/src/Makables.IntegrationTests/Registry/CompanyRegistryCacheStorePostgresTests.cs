using FluentAssertions;
using Makables.Infra.Database;
using Makables.Infra.Database.Registry;
using Makables.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Makables.IntegrationTests.Registry;

/// <summary>
/// T-0160 — pins the cache-store upsert against REAL Postgres. The
/// `payload` column is `jsonb` there, and a text parameter is not
/// implicitly cast in VALUES (42804) — the SQLite harness degrades jsonb
/// to TEXT, which is exactly why the original bug shipped and only blew
/// up live on dev (first real ARES lookup of the T-0153 walk). The
/// insert AND the conflict-update paths must both round-trip.
/// </summary>
public sealed class CompanyRegistryCacheStorePostgresTests : IClassFixture<PostgresHarness>
{
    private readonly PostgresHarness _harness;

    public CompanyRegistryCacheStorePostgresTests(PostgresHarness harness) => _harness = harness;

    private CompanyRegistryCacheStore CreateSut()
    {
        var factory = Substitute.For<IDbContextFactory<MakablesDbContext>>();
        factory.CreateDbContextAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(_harness.CreateDbContext()));
        factory.CreateDbContext().Returns(_ => _harness.CreateDbContext());
        return new CompanyRegistryCacheStore(factory);
    }

    [Fact]
    public async Task Upsert_inserts_and_conflict_updates_against_real_jsonb_column()
    {
        var sut = CreateSut();
        var ico = "27074358";
        var t1 = new DateTimeOffset(2026, 7, 23, 10, 0, 0, TimeSpan.Zero);

        await sut.UpsertAsync("ares", ico, """{"companyName":"Avast Software s.r.o."}""",
            fetchedAt: t1, expiresAt: t1.AddHours(24), CancellationToken.None);

        var inserted = await sut.GetAsync("ares", ico, CancellationToken.None);
        inserted.Should().NotBeNull("the insert path must survive the jsonb column");
        inserted!.PayloadJson.Should().Contain("Avast Software");
        inserted.FetchedAt.Should().Be(t1);

        // Conflict-update path (EXCLUDED pseudo-row).
        var t2 = t1.AddHours(1);
        await sut.UpsertAsync("ares", ico, """{"companyName":"Avast Software s.r.o. (refetched)"}""",
            fetchedAt: t2, expiresAt: t2.AddHours(24), CancellationToken.None);

        var updated = await sut.GetAsync("ares", ico, CancellationToken.None);
        updated.Should().NotBeNull();
        updated!.PayloadJson.Should().Contain("refetched");
        updated.FetchedAt.Should().Be(t2);
    }
}
