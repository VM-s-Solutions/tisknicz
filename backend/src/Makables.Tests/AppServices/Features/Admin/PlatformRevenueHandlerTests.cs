using FluentAssertions;
using Makables.Core.AppServices.Features.Admin;
using static Makables.Core.AppServices.Features.Admin.GetPlatformRevenue;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Orders.Queries;
using Makables.TestUtilities;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Admin;

/// <summary>
/// T-0186 admin earnings panel handler. The handler owns exactly one
/// decision — turning the <see cref="RevenueWindow"/> enum into a half-open
/// <c>[from, to)</c> instant pair off <see cref="IClock"/> — so that is what
/// these pin: each window's length, that the window ends at the clock's now
/// (never at wall-clock time), that the read-side totals pass through
/// untouched, and that an empty window is zeros rather than a failure.
/// </summary>
public sealed class PlatformRevenueHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 22, 14, 30, 0, TimeSpan.Zero);

    private readonly IOrderQueries _orders = Substitute.For<IOrderQueries>();
    private readonly IClock _clock = new FakeClock(Now);

    private GetPlatformRevenue.Handler Sut() => new(_orders, _clock);

    private void ReturnsRevenue(PlatformRevenueDto dto) =>
        _orders.GetPlatformRevenueAsync(
                Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(dto);

    private static PlatformRevenueDto Zeroed() => new(0, 0, 0, 0, 0, "CZK");

    [Theory]
    [InlineData(RevenueWindow.Day, 1)]
    [InlineData(RevenueWindow.Week, 7)]
    [InlineData(RevenueWindow.Month, 30)]
    public async Task Window_spans_its_documented_number_of_days_back_from_the_clock(
        RevenueWindow window, int expectedDays)
    {
        ReturnsRevenue(Zeroed());

        var result = await Sut().Handle(new GetPlatformRevenue.Query(window), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ToExclusive.Should().Be(Now);
        result.Value.FromInclusive.Should().Be(Now.AddDays(-expectedDays));
        // The read side must receive exactly the window the response reports —
        // a drift between the two would show the operator a total for a
        // different period than the one labelled on screen.
        await _orders.Received(1).GetPlatformRevenueAsync(
            Now.AddDays(-expectedDays), Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Echoes_the_requested_window_back_to_the_caller()
    {
        ReturnsRevenue(Zeroed());

        var result = await Sut().Handle(
            new GetPlatformRevenue.Query(RevenueWindow.Month), CancellationToken.None);

        result.Value!.Window.Should().Be(RevenueWindow.Month);
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

        var result = await Sut().Handle(
            new GetPlatformRevenue.Query(RevenueWindow.Week), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.PaidOrderCount.Should().Be(12);
        result.Value.GrossVolumeMinor.Should().Be(5_790_00);
        result.Value.PlatformFeeMinor.Should().Be(750_00);
        result.Value.MakerPayoutMinor.Should().Be(5_040_00);
        result.Value.RefundedMinor.Should().Be(579_00);
        result.Value.Currency.Should().Be("CZK");
    }

    [Fact]
    public async Task Window_with_no_sales_is_zeros_not_a_failure()
    {
        ReturnsRevenue(Zeroed());

        var result = await Sut().Handle(
            new GetPlatformRevenue.Query(RevenueWindow.Day), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.PaidOrderCount.Should().Be(0);
        result.Value.PlatformFeeMinor.Should().Be(0);
    }

    [Fact]
    public void Validator_rejects_a_window_outside_the_enum()
    {
        var result = new GetPlatformRevenue.Validator()
            .Validate(new GetPlatformRevenue.Query((RevenueWindow)99));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.ErrorCode.Should().Be(BusinessErrorMessage.InvalidEnumValue);
    }

    [Theory]
    [InlineData(RevenueWindow.Day)]
    [InlineData(RevenueWindow.Week)]
    [InlineData(RevenueWindow.Month)]
    public void Validator_accepts_every_declared_window(RevenueWindow window)
    {
        new GetPlatformRevenue.Validator()
            .Validate(new GetPlatformRevenue.Query(window))
            .IsValid.Should().BeTrue();
    }
}
