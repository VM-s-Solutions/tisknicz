using FluentAssertions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Orders;
using NSubstitute;

namespace Makables.Tests.Domain.Orders;

/// <summary>
/// Pure-domain tests for the T-0072 <see cref="Order.Ship"/> signature
/// extension: 4th optional <c>trackingUrl</c> parameter wired through
/// to <see cref="Order.ShippingCarrierTrackingUrl"/>, set-once invariant,
/// length-cap, null-passthrough for personal-pickup.
/// </summary>
public class OrderShipTrackingUrlTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-08T10:00:00Z");

    private static Order BuildAcceptedOrder(
        ShippingMethod method = ShippingMethod.ZasilkovnaPickupPoint)
    {
        var o = Order.Create(
            id: "ord-1", orderNumber: "M-CZ-20260042",
            customerUserId: "user-1", makerId: "maker-1", productId: "prod-1",
            contactName: "Anna", contactEmail: "a@b.cz", contactPhone: "+420",
            productPriceAmountMinor: 50000, shippingPriceAmountMinor: 7900,
            platformFeeAmountMinor: 7500, makerPayoutAmountMinor: 50400,
            totalAmountMinor: 57900, currency: "CZK", vatRateBp: 2100,
            shippingMethod: method,
            zasilkovnaPickupPointId: method == ShippingMethod.ZasilkovnaPickupPoint ? "pp-42" : null,
            countryCode: "CZ");
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        o.MarkAsPaid(clock, "tx-1");
        o.Accept(clock);
        return o;
    }

    [Fact]
    public void Ship_with_trackingUrl_sets_ShippingCarrierTrackingUrl()
    {
        var order = BuildAcceptedOrder();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);

        var result = order.Ship(
            clock,
            shippingCarrierRef: "ref-1",
            autoDeliverWindowDays: 7,
            trackingUrl: "https://tracking.packeta.com/Zref-1");

        result.IsSuccess.Should().BeTrue();
        order.ShippingCarrierTrackingUrl.Should().Be("https://tracking.packeta.com/Zref-1");
        order.ShippingCarrierRef.Should().Be("ref-1");
    }

    [Fact]
    public void Ship_with_null_trackingUrl_leaves_ShippingCarrierTrackingUrl_null()
    {
        // Mirrors the T-0073 personal-pickup path.
        var order = BuildAcceptedOrder(ShippingMethod.PersonalPickup);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);

        var result = order.Ship(
            clock,
            shippingCarrierRef: null,
            autoDeliverWindowDays: 7,
            trackingUrl: null);

        result.IsSuccess.Should().BeTrue();
        order.ShippingCarrierTrackingUrl.Should().BeNull();
        order.ShippingCarrierRef.Should().BeNull();
    }

    [Fact]
    public void Ship_omitting_trackingUrl_arg_compiles_and_leaves_field_null()
    {
        // The 4th arg is optional. Existing 3-arg callers still work.
        var order = BuildAcceptedOrder(ShippingMethod.PersonalPickup);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);

        var result = order.Ship(clock, shippingCarrierRef: null, autoDeliverWindowDays: 7);

        result.IsSuccess.Should().BeTrue();
        order.ShippingCarrierTrackingUrl.Should().BeNull();
    }

    [Fact]
    public void Ship_trackingUrl_too_long_throws_ArgumentException()
    {
        var order = BuildAcceptedOrder();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        var tooLong = new string('a', Order.MaxShippingCarrierTrackingUrlLength + 1);

        var act = () => order.Ship(clock, "ref-1", 7, trackingUrl: tooLong);

        act.Should().Throw<ArgumentException>();
    }
}
