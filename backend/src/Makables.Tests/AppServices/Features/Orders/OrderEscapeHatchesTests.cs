using FluentAssertions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Orders;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Orders;

/// <summary>
/// T-0181 / Q-0041 — the domain half of the order escape hatches.
///
/// <para>
/// The user's answer (2026-08-22) supplied the TIME BOUND; their
/// 2026-06-03 decision supplied the ROLES. The aggregate owns the
/// transition and the money record; the WINDOW deliberately lives in the
/// command layer (it is a tunable <c>CountryConfiguration</c> policy, not
/// an invariant), so it is not asserted here.
/// </para>
/// </summary>
public sealed class OrderEscapeHatchesTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);

    private static IClock Clock()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        return clock;
    }

    private readonly IClock _clock = Clock();

    /// <summary>Mirrors the AcceptOrderHandlerTests factory.</summary>
    private static Order NewOrder() => Order.Create(
        id: "order-1", orderNumber: "M-CZ-20260042",
        customerUserId: "user-1", makerId: "maker-1", productId: "prod-1",
        contactName: "Anna", contactEmail: "anna@example.cz", contactPhone: "+420",
        productPriceAmountMinor: 50000, shippingPriceAmountMinor: 7900,
        platformFeeAmountMinor: 7500, makerPayoutAmountMinor: 50400,
        totalAmountMinor: 57900, currency: "CZK", vatRateBp: 2100,
        shippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
        zasilkovnaPickupPointId: "pp-42", countryCode: "CZ");

    private Order PaidOrder()
    {
        var order = NewOrder();
        order.MarkAsPaid(_clock, "tx-1");
        return order;
    }

    [Fact]
    public void RefuseByMaker_cancels_the_order_and_records_the_money()
    {
        var order = PaidOrder();
        var total = order.TotalAmountMinor;

        var result = order.RefuseByMaker(_clock, total);

        result.IsSuccess.Should().BeTrue();
        // Cancelled, NOT Refunded: the user asked for the order to be
        // cancelled, and a full Refund() would have landed it in Refunded.
        order.State.Should().Be(OrderState.Cancelled);
        order.CancelledAt.Should().Be(Now);
        order.RefundedAmountMinor.Should().Be(total, "the money must be recorded, not just returned");
    }

    [Fact]
    public void RefuseByMaker_stamps_Maker_so_a_dispute_trail_can_tell_who_refused()
    {
        var order = PaidOrder();

        order.RefuseByMaker(_clock, order.TotalAmountMinor);

        order.CancellationSource.Should().Be(OrderCancellationSource.Maker,
            "'the maker refused' must never read as 'the platform intervened'");
    }

    [Theory]
    [InlineData(OrderState.PendingPayment)]
    [InlineData(OrderState.Accepted)]
    public void RefuseByMaker_is_Paid_only(OrderState from)
    {
        var order = NewOrder();
        if (from == OrderState.Accepted)
        {
            order.MarkAsPaid(_clock, "ref", "card", null);
            order.Accept(_clock);
        }

        var result = order.RefuseByMaker(_clock, order.TotalAmountMinor);

        result.IsSuccess.Should().BeFalse(
            "Accepted means the maker already took the job on — backing out there is admin-mediated");
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderInvalidTransition);
        order.State.Should().Be(from);
    }

    [Fact]
    public void RefuseByMaker_refuses_to_refund_more_than_remains()
    {
        var order = PaidOrder();

        var result = order.RefuseByMaker(_clock, order.TotalAmountMinor + 1);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.PaymentRefundAmountExceedsRemaining);
        order.State.Should().Be(OrderState.Paid, "a refused mutation must change nothing");
        order.RefundedAmountMinor.Should().Be(0);
    }

    [Fact]
    public void RefuseByMaker_rejects_a_negative_amount()
    {
        var order = PaidOrder();

        var act = () => order.RefuseByMaker(_clock, -1);

        act.Should().Throw<ArgumentException>("a negative refund is programmer error, not user error");
    }

    [Fact]
    public void Customer_cancel_from_PendingPayment_moves_no_money()
    {
        var order = NewOrder();

        var result = order.Cancel(_clock, OrderCancellationSource.Customer);

        result.IsSuccess.Should().BeTrue();
        order.State.Should().Be(OrderState.Cancelled);
        order.CancellationSource.Should().Be(OrderCancellationSource.Customer);
        order.RefundedAmountMinor.Should().Be(0, "nothing was captured on an unpaid order");
    }
}
