using FluentAssertions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Orders;
using NSubstitute;

namespace Makables.Tests.Domain.Orders;

/// <summary>
/// T-0105 red-first pins for the reshaped <see cref="Order"/> refund
/// surface (user-locked Q1: full + partial refunds accumulating into
/// <c>RefundedAmountMinor</c>; state flips to
/// <see cref="OrderState.Refunded"/> only when cumulative == total).
///
/// <para>
/// Pure predicate + mutator split per T-0105 §C:
/// <c>ValidateRefund(long, bool)</c> never mutates;
/// <c>Refund(IClock, long, bool)</c> calls it then accumulates. The
/// handler pre-flights via the same predicate BEFORE the Comgate call
/// (locked decision A.5 — provider-first), so this single source of
/// truth is the money-safety gate.
/// </para>
/// </summary>
public class OrderRefundTests
{
    private const long Total = 32900;

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
        totalAmountMinor: Total,
        currency: "CZK",
        vatRateBp: 2100,
        shippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
        zasilkovnaPickupPointId: "branch-79",
        countryCode: "cz");

    /// <summary>Happy-path drive to <paramref name="target"/> (linear states).</summary>
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

    // === 1. Remaining-amount cap ===

    [Fact]
    public void ValidateRefund_rejects_amount_exceeding_remaining()
    {
        var o = OrderInState(OrderState.Paid);
        o.Refund(FixedClock(), 10000, acknowledgePostPayout: false).IsSuccess.Should().BeTrue();

        var remaining = o.RemainingRefundableMinor;
        remaining.Should().Be(Total - 10000);

        var result = o.ValidateRefund(remaining + 1, acknowledgePostPayout: false);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.PaymentRefundAmountExceedsRemaining);
        // Pure predicate — no mutation on rejection.
        o.RefundedAmountMinor.Should().Be(10000);
        o.State.Should().Be(OrderState.Paid);
    }

    // === 2. Non-positive amount is a programmer error (Validator owns user input) ===

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ValidateRefund_throws_on_non_positive_amount(long amountMinor)
    {
        var o = OrderInState(OrderState.Paid);
        var act = () => o.ValidateRefund(amountMinor, acknowledgePostPayout: false);
        act.Should().Throw<ArgumentException>();
    }

    // === 3. State gate: Paid/Accepted/Shipped/Delivered/Completed only ===

    [Fact]
    public void ValidateRefund_rejects_invalid_states()
    {
        // PendingPayment — no money captured yet.
        var pending = ValidDefaults();
        pending.ValidateRefund(100, false).Error!.Code
            .Should().Be(BusinessErrorMessage.PaymentRefundInvalidState);

        // Cancelled — terminal.
        var cancelled = OrderInState(OrderState.Paid);
        cancelled.Cancel(FixedClock());
        cancelled.ValidateRefund(100, false).Error!.Code
            .Should().Be(BusinessErrorMessage.PaymentRefundInvalidState);

        // Disputed — T-0106 restores PreDisputeState first, then refunds
        // (T-0105 user decision Q2: Disputed is NOT refundable directly).
        var disputed = OrderInState(OrderState.Shipped);
        disputed.OpenDispute(FixedClock());
        disputed.ValidateRefund(100, false).Error!.Code
            .Should().Be(BusinessErrorMessage.PaymentRefundInvalidState);

        // Refunded — terminal (handler maps a re-fire to Silent Success
        // BEFORE reaching the predicate; the predicate itself refuses).
        var refunded = OrderInState(OrderState.Paid);
        refunded.Refund(FixedClock(), Total, false).IsSuccess.Should().BeTrue();
        refunded.ValidateRefund(100, false).Error!.Code
            .Should().Be(BusinessErrorMessage.PaymentRefundInvalidState);
    }

    // === 4. Completed requires the post-payout acknowledgement (Q5) ===

    [Fact]
    public void ValidateRefund_on_Completed_requires_acknowledgement()
    {
        var o = OrderInState(OrderState.Completed);

        var refused = o.ValidateRefund(100, acknowledgePostPayout: false);
        refused.IsSuccess.Should().BeFalse();
        refused.Error!.Code.Should().Be(BusinessErrorMessage.PaymentRefundPostPayoutAckRequired);

        o.ValidateRefund(100, acknowledgePostPayout: true).IsSuccess.Should().BeTrue();
    }

    // === 5. Partial refund accumulates with NO state change (Q1) ===

    [Fact]
    public void Refund_partial_accumulates_without_state_change()
    {
        var o = OrderInState(OrderState.Paid);

        var result = o.Refund(FixedClock(), 5000, acknowledgePostPayout: false);

        result.IsSuccess.Should().BeTrue();
        o.RefundedAmountMinor.Should().Be(5000);
        o.RemainingRefundableMinor.Should().Be(Total - 5000);
        o.State.Should().Be(OrderState.Paid, "a partially-refunded order is still a live order");
        o.RefundedAt.Should().BeNull();
    }

    // === 6. Full refund flips state + stamps RefundedAt ===

    [Fact]
    public void Refund_full_transitions_to_Refunded_with_timestamp()
    {
        var o = OrderInState(OrderState.Delivered);

        var result = o.Refund(FixedClock(), Total, acknowledgePostPayout: false);

        result.IsSuccess.Should().BeTrue();
        o.RefundedAmountMinor.Should().Be(Total);
        o.RemainingRefundableMinor.Should().Be(0);
        o.State.Should().Be(OrderState.Refunded);
        o.RefundedAt.Should().Be(Now);
    }

    // === 7. Two partials summing to total close the order ===

    [Fact]
    public void Refund_two_partials_summing_to_total_transitions_to_Refunded()
    {
        var o = OrderInState(OrderState.Paid);

        o.Refund(FixedClock(), 12900, acknowledgePostPayout: false).IsSuccess.Should().BeTrue();
        o.State.Should().Be(OrderState.Paid);
        o.RefundedAt.Should().BeNull();

        var second = o.Refund(FixedClock(), Total - 12900, acknowledgePostPayout: false);

        second.IsSuccess.Should().BeTrue();
        o.RefundedAmountMinor.Should().Be(Total);
        o.State.Should().Be(OrderState.Refunded);
        o.RefundedAt.Should().Be(Now);
    }
}
