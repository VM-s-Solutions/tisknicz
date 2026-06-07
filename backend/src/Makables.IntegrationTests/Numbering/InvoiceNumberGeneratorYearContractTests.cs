using FluentAssertions;
using Makables.Infra.Database;
using Makables.Infra.Database.Numbering;
using Makables.Infra.Database.Repositories;
using Makables.IntegrationTests.Common;
using Makables.TestUtilities;

namespace Makables.IntegrationTests.Numbering;

/// <summary>
/// Pins the TZ-aware year contract for the invoice generator per the
/// T-0068a migration (ADR 0009 amendment). The year segment of the
/// invoice number comes from <c>CountryConfiguration.TimeZoneId</c>
/// applied to <c>UtcNow</c>, not from <c>UtcNow.Year</c> directly. For
/// invoices this is a real CZ tax-law compliance issue — a 23:30 Prague
/// invoice on Dec 31 must bucket into the local-year sequence (what the
/// customer sees on the document), not the previous UTC year.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class InvoiceNumberGeneratorYearContractTests
{
    private readonly PostgresHarness _harness;

    public InvoiceNumberGeneratorYearContractTests(PostgresHarness harness)
    {
        _harness = harness;
        _harness.ResetMutableTablesAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// 2026-12-31T22:30:00Z = 23:30 local Prague on 2026-12-31. Year is still 2026.
    /// 2026-12-31T23:30:00Z = 00:30 local Prague on 2027-01-01. Year flips to 2027.
    /// Europe/Prague is UTC+1 in winter (no DST around the year boundary),
    /// so the 1-hour offset is stable. Closes ADR 0009 amendment line for
    /// the invoice generator.
    /// </summary>
    [Theory]
    [InlineData("2026-12-31T22:30:00Z", 2026)]
    [InlineData("2026-12-31T23:30:00Z", 2027)]
    public async Task NextAsync_buckets_invoices_by_country_local_year_not_UTC(
        string utcInstant,
        int expectedYear)
    {
        var nowUtc = DateTimeOffset.Parse(utcInstant, System.Globalization.CultureInfo.InvariantCulture);
        // Each [InlineData] runs in its own test instance, so the ctor's
        // TRUNCATE gives us a clean numbering_sequence per case.
        await using var db = _harness.CreateDbContext();
        var configs = new CountryConfigurationRepository(db);
        var generator = new InvoiceNumberGenerator(db, new FakeClock(nowUtc), configs);

        await using var tx = await db.Database.BeginTransactionAsync();
        var number = await generator.NextAsync("CZ", CancellationToken.None);
        await db.SaveChangesAsync();
        await tx.CommitAsync();

        // Format is FV-CZ-{YYYY}{NNNN} → assert year segment + first
        // allocation produces 0001.
        number.Should().StartWith($"FV-CZ-{expectedYear}",
            because: $"23:30 vs 00:30 Prague-local on Dec 31 must map to country-local year " +
                     $"{expectedYear}, not the UTC year of {nowUtc:yyyy-MM-dd HH:mm}Z — " +
                     $"§ 29 zákon o DPH requires the year on the invoice to match the local " +
                     $"calendar at issuance time");
        number.Should().Be($"FV-CZ-{expectedYear}0001");
    }
}
