using FluentAssertions;
using Makables.Core.AppServices.Features.Admin;
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
/// T-0192 admin earnings panel handler. The handler owns exactly two
/// decisions — which month is being asked about, and which pair of instants
/// that month is — so that is what these pin: the default is the month in
/// progress in the COUNTRY'S timezone (not UTC's), an explicit month is
/// honoured, the read-side totals pass through untouched, and an empty month
/// is zeros rather than a failure.
///
/// <para>
/// The timezone is deliberately exercised rather than stubbed away: reading
/// a Czech month as a UTC month misfiles the first two hours of every month,
/// and nothing downstream would notice.
/// </para>
/// </summary>
public sealed class PlatformRevenueHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 22, 14, 30, 0, TimeSpan.Zero);

    private readonly IOrderQueries _orders = Substitute.For<IOrderQueries>();
    private readonly IClock _clock = new FakeClock(Now);
    private readonly ICountryConfigurationRepository _countries =
        Substitute.For<ICountryConfigurationRepository>();

    public PlatformRevenueHandlerTests() => WithTimeZone("Europe/Prague");

    private void WithTimeZone(string timeZoneId) =>
        _countries.GetByCodeAsync("CZ", Arg.Any<CancellationToken>())
            .Returns(CountryConfiguration.Create(
                "CZ", "CZK", "cs-CZ", timeZoneId, "+420", "d. M. yyyy",
                2100, "DIČ", "DIČ DPH", "IČO",
                "comgate", "packeta", "ares", "resend",
                "JVM YORE s.r.o.", "00000000",
                reducedVatRateBp: 1200, invoicingMode: InvoicingMode.None,
                platformFeeRateBp: 1500, defaultShippingPriceMinor: 7900));

    private void WithoutCountryConfig() =>
        _countries.GetByCodeAsync("CZ", Arg.Any<CancellationToken>())
            .Returns((CountryConfiguration?)null);

    private GetPlatformRevenue.Handler Sut() => new(
        _orders,
        _clock,
        _countries,
        Options.Create(new AuthDefaultCountryOptions()),
        NullLogger<GetPlatformRevenue.Handler>.Instance);

    private void ReturnsRevenue(PlatformRevenueDto dto) =>
        _orders.GetPlatformRevenueAsync(
                Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(dto);

    private static PlatformRevenueDto Zeroed() => new(0, 0, 0, 0, 0, "CZK");

    private static DateTimeOffset Utc(int y, int m, int d, int h = 0) =>
        new(y, m, d, h, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Defaults_to_the_month_in_progress()
    {
        ReturnsRevenue(Zeroed());

        var result = await Sut().Handle(new GetPlatformRevenue.Query(null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Year.Should().Be(2026);
        result.Value.Month.Should().Be(8);
    }

    [Fact]
    public async Task The_default_month_follows_the_country_clock_not_UTC()
    {
        // 22:30 UTC on 31 August is 00:30 on 1 September in Prague. The panel
        // must open on September; opening on August would show a full month's
        // total under next month's heading for two hours, every month.
        var handler = new GetPlatformRevenue.Handler(
            _orders,
            new FakeClock(Utc(2026, 8, 31, 22).AddMinutes(30)),
            _countries,
            Options.Create(new AuthDefaultCountryOptions()),
            NullLogger<GetPlatformRevenue.Handler>.Instance);
        ReturnsRevenue(Zeroed());

        var result = await handler.Handle(new GetPlatformRevenue.Query(null, null), CancellationToken.None);

        result.Value!.Year.Should().Be(2026);
        result.Value.Month.Should().Be(9);
    }

    [Fact]
    public async Task An_explicit_month_is_read_as_that_countrys_month()
    {
        ReturnsRevenue(Zeroed());

        var result = await Sut().Handle(new GetPlatformRevenue.Query(2026, 8), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Year.Should().Be(2026);
        result.Value.Month.Should().Be(8);
        result.Value.FromInclusive.Should().Be(Utc(2026, 7, 31, 22));
        result.Value.ToExclusive.Should().Be(Utc(2026, 8, 31, 22));
        // The read side must receive exactly the window the response reports
        // — a drift between the two would show the operator a total for a
        // different period than the one labelled on screen.
        await _orders.Received(1).GetPlatformRevenueAsync(
            Utc(2026, 7, 31, 22), Utc(2026, 8, 31, 22), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_winter_month_carries_the_winter_offset()
    {
        ReturnsRevenue(Zeroed());

        var result = await Sut().Handle(new GetPlatformRevenue.Query(2026, 1), CancellationToken.None);

        result.Value!.FromInclusive.Should().Be(Utc(2025, 12, 31, 23));
        result.Value.ToExclusive.Should().Be(Utc(2026, 1, 31, 23));
    }

    [Theory]
    [InlineData(2026, null)]
    [InlineData(null, 3)]
    public async Task A_half_supplied_month_falls_back_to_the_month_in_progress(int? year, int? month)
    {
        ReturnsRevenue(Zeroed());

        var result = await Sut().Handle(new GetPlatformRevenue.Query(year, month), CancellationToken.None);

        result.Value!.Year.Should().Be(2026);
        result.Value.Month.Should().Be(8);
    }

    [Fact]
    public async Task A_missing_country_configuration_degrades_to_UTC_rather_than_failing()
    {
        WithoutCountryConfig();
        ReturnsRevenue(Zeroed());

        var result = await Sut().Handle(new GetPlatformRevenue.Query(2026, 8), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.FromInclusive.Should().Be(Utc(2026, 8, 1));
        result.Value.ToExclusive.Should().Be(Utc(2026, 9, 1));
    }

    [Fact]
    public async Task An_unusable_timezone_id_degrades_to_UTC_rather_than_failing()
    {
        WithTimeZone("Mars/Olympus_Mons");
        ReturnsRevenue(Zeroed());

        var result = await Sut().Handle(new GetPlatformRevenue.Query(2026, 8), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.FromInclusive.Should().Be(Utc(2026, 8, 1));
    }

    [Fact]
    public async Task Flags_the_month_in_progress_so_the_caller_cannot_page_into_the_future()
    {
        ReturnsRevenue(Zeroed());

        var current = await Sut().Handle(new GetPlatformRevenue.Query(2026, 8), CancellationToken.None);
        var past = await Sut().Handle(new GetPlatformRevenue.Query(2026, 7), CancellationToken.None);
        var future = await Sut().Handle(new GetPlatformRevenue.Query(2026, 9), CancellationToken.None);

        current.Value!.IsCurrentMonth.Should().BeTrue();
        past.Value!.IsCurrentMonth.Should().BeFalse();
        future.Value!.IsCurrentMonth.Should().BeFalse("only the month in progress is the current one");
    }

    [Fact]
    public async Task The_default_read_is_always_flagged_as_the_current_month()
    {
        ReturnsRevenue(Zeroed());

        var result = await Sut().Handle(new GetPlatformRevenue.Query(null, null), CancellationToken.None);

        result.Value!.IsCurrentMonth.Should().BeTrue();
    }

    [Fact]
    public async Task Passes_read_side_totals_through_untouched()
    {
        // Minor units throughout — the handler must not rescale or net
        // anything; the fee is NOT reduced by the refund line.
        ReturnsRevenue(new PlatformRevenueDto(
            PaidOrderCount: 12,
            GrossVolumeMinor: 5_790_00,
            PlatformFeeMinor: 750_00,
            MakerPayoutMinor: 5_040_00,
            RefundedMinor: 579_00,
            Currency: "CZK"));

        var result = await Sut().Handle(new GetPlatformRevenue.Query(2026, 8), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.PaidOrderCount.Should().Be(12);
        result.Value.GrossVolumeMinor.Should().Be(5_790_00);
        result.Value.PlatformFeeMinor.Should().Be(750_00);
        result.Value.MakerPayoutMinor.Should().Be(5_040_00);
        result.Value.RefundedMinor.Should().Be(579_00);
        result.Value.Currency.Should().Be("CZK");
    }

    [Fact]
    public async Task A_month_with_no_sales_is_zeros_not_a_failure()
    {
        ReturnsRevenue(Zeroed());

        var result = await Sut().Handle(new GetPlatformRevenue.Query(2026, 2), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.PaidOrderCount.Should().Be(0);
        result.Value.PlatformFeeMinor.Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    [InlineData(-1)]
    public void Validator_rejects_a_month_outside_the_calendar(int month)
    {
        var result = new GetPlatformRevenue.Validator()
            .Validate(new GetPlatformRevenue.Query(2026, month));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.ErrorCode.Should().Be(BusinessErrorMessage.MinValue);
    }

    [Theory]
    [InlineData(1999)]
    [InlineData(999_999)]
    public void Validator_rejects_a_year_outside_the_sane_range(int year)
    {
        // Not a business rule — a clamp so a hand-typed year cannot overflow
        // DateTime inside the calendar helper.
        var result = new GetPlatformRevenue.Validator()
            .Validate(new GetPlatformRevenue.Query(year, 8));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.ErrorCode.Should().Be(BusinessErrorMessage.MinValue);
    }

    [Fact]
    public void Validator_accepts_an_absent_month()
    {
        new GetPlatformRevenue.Validator()
            .Validate(new GetPlatformRevenue.Query(null, null))
            .IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(12)]
    public void Validator_accepts_every_month_of_the_calendar(int month)
    {
        new GetPlatformRevenue.Validator()
            .Validate(new GetPlatformRevenue.Query(2026, month))
            .IsValid.Should().BeTrue();
    }
}
