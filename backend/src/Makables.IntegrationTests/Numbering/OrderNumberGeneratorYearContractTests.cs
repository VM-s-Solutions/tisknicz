using FluentAssertions;
using Makables.Infra.Database;
using Makables.Infra.Database.Numbering;
using Makables.Infra.Database.Repositories;
using Makables.IntegrationTests.Common;
using Makables.TestUtilities;

namespace Makables.IntegrationTests.Numbering;

/// <summary>
/// Pins the TZ-aware year contract introduced in T-0062 (user decision
/// Q2): the year segment of the order number comes from
/// <c>CountryConfiguration.TimeZoneId</c> applied to <c>UtcNow</c>, not
/// from <c>UtcNow.Year</c> directly. This is the test that proves Q2 was
/// the right call — without it, a 23:30 Prague order on Dec 31 would
/// bucket into the previous year's sequence.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OrderNumberGeneratorYearContractTests
{
    private readonly PostgresHarness _harness;

    public OrderNumberGeneratorYearContractTests(PostgresHarness harness)
    {
        _harness = harness;
        _harness.ResetMutableTablesAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// 2026-12-31T22:30:00Z = 23:30 local Prague on 2026-12-31. Year is still 2026.
    /// 2026-12-31T23:30:00Z = 00:30 local Prague on 2027-01-01. Year flips to 2027.
    /// Europe/Prague is UTC+1 in winter (no DST around the year boundary), so the
    /// 1-hour offset is stable.
    /// </summary>
    [Theory]
    [InlineData("2026-12-31T22:30:00Z", 2026)]
    [InlineData("2026-12-31T23:30:00Z", 2027)]
    public async Task NextAsync_buckets_orders_by_country_local_year_not_UTC(
        string utcInstant,
        int expectedYear)
    {
        var nowUtc = DateTimeOffset.Parse(utcInstant, System.Globalization.CultureInfo.InvariantCulture);
        // Each [InlineData] runs in its own test instance, so the ctor's
        // TRUNCATE gives us a clean numbering_sequence per case.
        await using var db = _harness.CreateDbContext();
        var configs = new CountryConfigurationRepository(db);
        var generator = new OrderNumberGenerator(db, new FakeClock(nowUtc), configs);

        await using var tx = await db.Database.BeginTransactionAsync();
        var number = await generator.NextAsync("CZ", CancellationToken.None);
        await db.SaveChangesAsync();
        await tx.CommitAsync();

        // Format is M-CZ-{YYYY}{NNNN} → year segment is chars 5..8.
        number.Should().StartWith($"M-CZ-{expectedYear}",
            because: $"23:30 vs 00:30 Prague-local on Dec 31 must map to country-local year " +
                     $"{expectedYear}, not the UTC year of {nowUtc:yyyy-MM-dd HH:mm}Z");
        number.Should().Be($"M-CZ-{expectedYear}0001");
    }

    [Fact]
    public async Task NextAsync_throws_when_country_configuration_missing()
    {
        await using var db = _harness.CreateDbContext();
        var configs = new CountryConfigurationRepository(db);
        var generator = new OrderNumberGenerator(db, new FakeClock(), configs);

        // "ZZ" is not a serviced country; there is no row in
        // country_configuration. Per the impl's contract this is a
        // seed-data integrity issue, surfaced as InvalidOperationException.
        var act = async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            await generator.NextAsync("ZZ", CancellationToken.None);
        };

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*country code 'ZZ'*");
    }
}
