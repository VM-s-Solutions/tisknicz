using FluentAssertions;
using Makables.Core.AppServices.Features.Admin;
using static Makables.Core.AppServices.Features.Admin.GetPlatformRevenueSeries;
using Makables.Core.AppServices.Features.Auth;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Orders.Queries;
using Makables.TestUtilities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Admin;

/// <summary>
/// T-0192 revenue-series handler. Two things are worth pinning here, and
/// they are the two the chart's honesty rests on.
///
/// <para>
/// First, the <b>ladder</b>: each range must ask the read side for the span
/// and bucket width it claims on screen. A drift there mislabels the axis of
/// every chart drawn from it.
/// </para>
///
/// <para>
/// Second, the <b>gap fill</b>: the database returns only buckets that
/// contain orders, and a line chart that silently skips its empty days draws
/// a straight run between two distant points — which reads as steady trade
/// during a week with no sales at all. Zero must be plotted as zero.
/// </para>
/// </summary>
public sealed class PlatformRevenueSeriesHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 22, 14, 30, 0, TimeSpan.Zero);

    private readonly IOrderQueries _orders = Substitute.For<IOrderQueries>();
    private readonly IClock _clock = new FakeClock(Now);
    private readonly ICountryConfigurationRepository _countries =
        Substitute.For<ICountryConfigurationRepository>();

    public PlatformRevenueSeriesHandlerTests() =>
        _countries.GetByCodeAsync("CZ", Arg.Any<CancellationToken>())
            .Returns(CountryConfiguration.Create(
                "CZ", "CZK", "cs-CZ", "Europe/Prague", "+420", "d. M. yyyy",
                2100, "DIČ", "DIČ DPH", "IČO",
                "comgate", "packeta", "ares", "resend",
                "JVM YORE s.r.o.", "00000000",
                reducedVatRateBp: 1200, invoicingMode: InvoicingMode.None,
                platformFeeRateBp: 1500, defaultShippingPriceMinor: 7900));

    private GetPlatformRevenueSeries.Handler Sut() => new(
        _orders,
        _clock,
        _countries,
        Options.Create(new AuthDefaultCountryOptions()),
        NullLogger<GetPlatformRevenueSeries.Handler>.Instance);

    private void ReturnsBuckets(params PlatformRevenueBucketDto[] buckets) =>
        _orders.GetPlatformRevenueSeriesAsync(
                Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(),
                Arg.Any<RevenueBucketGranularity>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(buckets);

    private static DateTimeOffset Utc(int y, int m, int d, int h = 0) =>
        new(y, m, d, h, 0, 0, TimeSpan.Zero);

    // === The ladder ===

    [Theory]
    [InlineData(RevenueRange.Day, RevenueBucketGranularity.Hour, 24)]
    [InlineData(RevenueRange.Week, RevenueBucketGranularity.Day, 7)]
    [InlineData(RevenueRange.Month, RevenueBucketGranularity.Day, 30)]
    [InlineData(RevenueRange.Quarter, RevenueBucketGranularity.Day, 90)]
    [InlineData(RevenueRange.HalfYear, RevenueBucketGranularity.Week, 26)]
    [InlineData(RevenueRange.Year, RevenueBucketGranularity.Month, 12)]
    public async Task Each_range_returns_its_documented_bucket_width_and_point_count(
        RevenueRange range, RevenueBucketGranularity expectedGranularity, int expectedPoints)
    {
        ReturnsBuckets();

        var result = await Sut().Handle(new Query(range), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Range.Should().Be(range);
        result.Value.Granularity.Should().Be(expectedGranularity);
        result.Value.Points.Should().HaveCount(expectedPoints);
    }

    [Fact]
    public async Task The_read_side_receives_the_window_and_bucket_width_the_response_reports()
    {
        ReturnsBuckets();

        var result = await Sut().Handle(new Query(RevenueRange.Week), CancellationToken.None);

        await _orders.Received(1).GetPlatformRevenueSeriesAsync(
            result.Value!.FromInclusive,
            result.Value.ToExclusive,
            RevenueBucketGranularity.Day,
            "Europe/Prague",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_timezone_the_database_buckets_in_comes_from_the_country_row()
    {
        // The SQL truncates with date_trunc(field, ts, zone); handing it the
        // wrong zone silently shifts every bucket boundary.
        ReturnsBuckets();

        await Sut().Handle(new Query(RevenueRange.Month), CancellationToken.None);

        await _orders.Received(1).GetPlatformRevenueSeriesAsync(
            Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(),
            Arg.Any<RevenueBucketGranularity>(), "Europe/Prague", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_window_ends_at_the_clock_so_the_last_bucket_is_the_one_in_progress()
    {
        ReturnsBuckets();

        var result = await Sut().Handle(new Query(RevenueRange.Month), CancellationToken.None);

        result.Value!.ToExclusive.Should().Be(Now);
        result.Value.Points[^1].BucketStart.Should().Be(Utc(2026, 8, 21, 22), "22 August local");
    }

    [Fact]
    public async Task The_currency_comes_from_the_country_configuration()
    {
        ReturnsBuckets();

        var result = await Sut().Handle(new Query(RevenueRange.Month), CancellationToken.None);

        result.Value!.Currency.Should().Be("CZK");
    }

    [Fact]
    public async Task Reports_the_timezone_the_buckets_were_truncated_in()
    {
        // The caller labels its axis from this. Formatting a bucket instant in
        // the browser's own zone would draw a chart whose axis disagreed with
        // its own data.
        ReturnsBuckets();

        var result = await Sut().Handle(new Query(RevenueRange.Month), CancellationToken.None);

        result.Value!.TimeZoneId.Should().Be("Europe/Prague");
    }

    [Fact]
    public async Task A_missing_country_configuration_degrades_to_UTC_rather_than_failing()
    {
        _countries.GetByCodeAsync("CZ", Arg.Any<CancellationToken>())
            .Returns((CountryConfiguration?)null);
        ReturnsBuckets();

        var result = await Sut().Handle(new Query(RevenueRange.Week), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Currency.Should().Be("CZK");
        result.Value.TimeZoneId.Should().Be("UTC");
        await _orders.Received(1).GetPlatformRevenueSeriesAsync(
            Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(),
            Arg.Any<RevenueBucketGranularity>(), "UTC", Arg.Any<CancellationToken>());
    }

    // === Gap filling + pass-through ===

    [Fact]
    public async Task A_span_with_no_sales_at_all_is_a_full_run_of_zeros_not_an_empty_series()
    {
        ReturnsBuckets();

        var result = await Sut().Handle(new Query(RevenueRange.Week), CancellationToken.None);

        result.Value!.Points.Should().HaveCount(7);
        result.Value.Points.Should().OnlyContain(p =>
            p.PaidOrderCount == 0 && p.GrossVolumeMinor == 0 && p.PlatformFeeMinor == 0);
    }

    [Fact]
    public async Task A_bucket_that_exists_lands_on_its_own_slot_and_the_rest_stay_zero()
    {
        // 20 August 2026 local opens at 22:00 UTC on the 19th.
        ReturnsBuckets(new PlatformRevenueBucketDto(
            BucketStart: Utc(2026, 8, 19, 22),
            PaidOrderCount: 3,
            GrossVolumeMinor: 1_737_00,
            PlatformFeeMinor: 225_00,
            MakerPayoutMinor: 1_512_00,
            RefundedMinor: 579_00));

        var result = await Sut().Handle(new Query(RevenueRange.Week), CancellationToken.None);

        var points = result.Value!.Points;
        points.Should().HaveCount(7);
        var filled = points.Should().ContainSingle(p => p.PaidOrderCount == 3).Subject;
        filled.BucketStart.Should().Be(Utc(2026, 8, 19, 22));
        filled.GrossVolumeMinor.Should().Be(1_737_00);
        filled.PlatformFeeMinor.Should().Be(225_00);
        filled.MakerPayoutMinor.Should().Be(1_512_00);
        filled.RefundedMinor.Should().Be(579_00, "a refund is reported, never netted into the fee");
        points.Where(p => p.BucketStart != filled.BucketStart)
            .Should().OnlyContain(p => p.PlatformFeeMinor == 0);
    }

    [Fact]
    public async Task Points_are_ascending_and_gap_free()
    {
        ReturnsBuckets(
            new PlatformRevenueBucketDto(Utc(2026, 8, 21, 22), 1, 100, 10, 90, 0),
            new PlatformRevenueBucketDto(Utc(2026, 8, 16, 22), 2, 200, 20, 180, 0));

        var result = await Sut().Handle(new Query(RevenueRange.Week), CancellationToken.None);

        var starts = result.Value!.Points.Select(p => p.BucketStart).ToList();
        starts.Should().BeInAscendingOrder();
        starts.Should().OnlyHaveUniqueItems();
    }

    // === FillGaps directly: the "never hide money" contract ===

    [Fact]
    public void A_bucket_the_grid_did_not_expect_is_appended_never_dropped()
    {
        // Should be unreachable — the C# grid mirrors date_trunc. But the two
        // are computed by different engines from different tz databases, and
        // a silent drop would make money vanish from the chart with nothing
        // to show for it. Surfacing the stray point keeps it diagnosable.
        var grid = new[] { Utc(2026, 8, 20), Utc(2026, 8, 21) };
        var buckets = new[]
        {
            new PlatformRevenueBucketDto(Utc(2026, 8, 20), 1, 100, 10, 90, 0),
            new PlatformRevenueBucketDto(Utc(2026, 8, 20, 13), 5, 500, 50, 450, 0),
        };

        var points = GetPlatformRevenueSeries.FillGaps(buckets, grid);

        points.Should().HaveCount(3);
        points.Sum(p => p.PlatformFeeMinor).Should().Be(60);
        points.Select(p => p.BucketStart).Should().BeInAscendingOrder();
    }

    [Fact]
    public void An_empty_grid_still_surfaces_whatever_the_database_found()
    {
        var buckets = new[] { new PlatformRevenueBucketDto(Utc(2026, 8, 20), 1, 100, 10, 90, 0) };

        GetPlatformRevenueSeries.FillGaps(buckets, [])
            .Should().ContainSingle().Which.PlatformFeeMinor.Should().Be(10);
    }

    // === Validator ===

    [Fact]
    public void Validator_rejects_a_range_outside_the_enum()
    {
        var result = new GetPlatformRevenueSeries.Validator().Validate(new Query((RevenueRange)99));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.ErrorCode.Should().Be(BusinessErrorMessage.InvalidEnumValue);
    }

    [Theory]
    [InlineData(RevenueRange.Day)]
    [InlineData(RevenueRange.Week)]
    [InlineData(RevenueRange.Month)]
    [InlineData(RevenueRange.Quarter)]
    [InlineData(RevenueRange.HalfYear)]
    [InlineData(RevenueRange.Year)]
    public void Validator_accepts_every_declared_range(RevenueRange range)
    {
        new GetPlatformRevenueSeries.Validator().Validate(new Query(range)).IsValid.Should().BeTrue();
    }
}
