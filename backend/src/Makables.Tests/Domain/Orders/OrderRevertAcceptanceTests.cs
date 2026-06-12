using FluentAssertions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Orders;
using NSubstitute;

namespace Makables.Tests.Domain.Orders;

/// <summary>
/// T-0107 red-first pins for <see cref="Order.RevertAcceptance"/> — the
/// one manual-tool edge with no pre-existing domain method (Accepted →
/// Paid, undo a maker mis-click). Dedicated semantic method per §C: a
/// generic state setter would miss the non-obvious cleanup (clear
/// <c>AcceptedAt</c> so a later re-accept stamps fresh).
/// </summary>
public class OrderRevertAcceptanceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-12T10:00:00Z");

    private static IClock FixedClock(DateTimeOffset? at = null)
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(at ?? Now);
        return clock;
    }

    private static Order ValidDefaults() => Order.Create(
        id: "ord-1",
        orderNumber: "CZ-2026-000001",
        customerUserId: "user-1",
        makerId: "maker-1",
        productId: "prod-1",
        contactName: "Jan Novák",
        contactEmail: "jan@example.cz",
        contactPhone: "+420777123456",
        productPriceAmountMinor: 25000,
        shippingPriceAmountMinor: 7900,
        platformFeeAmountMinor: 3290,
        makerPayoutAmountMinor: 29610,
        totalAmountMinor: 32900,
        currency: "CZK",
        vatRateBp: 2100,
        shippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
        zasilkovnaPickupPointId: "branch-79",
        countryCode: "cz");

    [Fact]
    public void RevertAcceptance_from_Accepted_clears_AcceptedAt_and_sets_Paid()
    {
        var o = ValidDefaults();
        o.MarkAsPaid(FixedClock(Now.AddDays(-1)), "tx-1");
        o.Accept(FixedClock());
        o.AcceptedAt.Should().NotBeNull();

        var result = o.RevertAcceptance(FixedClock());

        result.IsSuccess.Should().BeTrue();
        o.State.Should().Be(OrderState.Paid);
        o.AcceptedAt.Should().BeNull("a later re-accept must stamp a fresh timestamp");
        o.PaidAt.Should().Be(Now.AddDays(-1), "the original payment timestamp is untouched");
    }

    [Theory]
    [InlineData(OrderState.PendingPayment)]
    [InlineData(OrderState.Paid)]
    [InlineData(OrderState.Shipped)]
    [InlineData(OrderState.Delivered)]
    [InlineData(OrderState.Completed)]
    public void RevertAcceptance_refused_from_any_other_state(OrderState from)
    {
        var o = OrderInState(from);
        var stateBefore = o.State;
        var acceptedAtBefore = o.AcceptedAt;

        var result = o.RevertAcceptance(FixedClock());

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderInvalidTransition);
        o.State.Should().Be(stateBefore);
        o.AcceptedAt.Should().Be(acceptedAtBefore);
    }

    [Fact]
    public void RevertAcceptance_rejects_null_clock()
    {
        var o = OrderInState(OrderState.Accepted);
        var act = () => o.RevertAcceptance(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    private static Order OrderInState(OrderState target)
    {
        var o = ValidDefaults();
        if (target == OrderState.PendingPayment) return o;

        o.MarkAsPaid(FixedClock(), "tx-1");
        if (target == OrderState.Paid) return o;

        o.Accept(FixedClock());
        if (target == OrderState.Accepted) return o;

        o.Ship(FixedClock(), "PKT-1", 7);
        if (target == OrderState.Shipped) return o;

        o.MarkAsDelivered(FixedClock(), OrderDeliverySource.Auto);
        if (target == OrderState.Delivered) return o;

        o.Complete(FixedClock());
        if (target == OrderState.Completed) return o;

        throw new ArgumentOutOfRangeException(nameof(target), target, "Linear states only.");
    }
}
