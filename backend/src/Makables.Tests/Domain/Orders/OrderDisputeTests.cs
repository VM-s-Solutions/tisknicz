using FluentAssertions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Orders;
using NSubstitute;

namespace Makables.Tests.Domain.Orders;

/// <summary>
/// T-0106 red-first pins for the dispute parenthesis-state (patterns
/// §A.22): the disputable-states allow-list change (Paid/Accepted in,
/// Completed OUT — §C.1), <c>PreDisputeState</c> preservation on open,
/// <c>ResolveDispute</c> restore semantics, and the <see cref="Dispute"/>
/// child entity's resolve invariants (Q2).
///
/// <para>
/// REWRITES the T-0060 pins formerly in <c>OrderTests.cs</c> (old
/// allow-list Shipped/Delivered/Completed) — the only production caller
/// of <c>OpenDispute</c> today is the T-0078 stub, which never invoked
/// it; the rewrite is safe per T-0106 Risk §1.
/// </para>
/// </summary>
public class OrderDisputeTests
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

    // === 1. Disputable allow-list = Paid | Accepted | Shipped | Delivered (§C.1) ===

    [Theory]
    [InlineData(OrderState.Paid)]
    [InlineData(OrderState.Accepted)]
    [InlineData(OrderState.Shipped)]
    [InlineData(OrderState.Delivered)]
    public void OpenDispute_allowed_from_disputable_states(OrderState from)
    {
        var o = OrderInState(from);

        var result = o.OpenDispute(FixedClock());

        result.IsSuccess.Should().BeTrue();
        o.State.Should().Be(OrderState.Disputed);
        o.PreDisputeState.Should().Be(from, "the parenthesis state stores where the order was");
        o.DisputedAt.Should().Be(Now);
    }

    // === 2. Non-disputable states refuse without mutation ===

    [Theory]
    [InlineData(OrderState.PendingPayment)]
    [InlineData(OrderState.Cancelled)]
    [InlineData(OrderState.Refunded)]
    [InlineData(OrderState.Completed)]
    [InlineData(OrderState.Disputed)]
    public void OpenDispute_refused_from_non_disputable_states(OrderState from)
    {
        var o = from switch
        {
            OrderState.PendingPayment => ValidDefaults(),
            OrderState.Cancelled => Cancelled(),
            OrderState.Refunded => Refunded(),
            OrderState.Completed => OrderInState(OrderState.Completed),
            OrderState.Disputed => Disputed(OrderState.Shipped),
            _ => throw new ArgumentOutOfRangeException(nameof(from)),
        };
        var stateBefore = o.State;
        var preDisputeBefore = o.PreDisputeState;
        var disputedAtBefore = o.DisputedAt;

        var result = o.OpenDispute(FixedClock());

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderInvalidTransition);
        o.State.Should().Be(stateBefore);
        o.PreDisputeState.Should().Be(preDisputeBefore);
        o.DisputedAt.Should().Be(disputedAtBefore);

        static Order Cancelled()
        {
            var c = OrderInState(OrderState.Paid);
            c.Cancel(FixedClock());
            return c;
        }
        static Order Refunded()
        {
            var r = OrderInState(OrderState.Paid);
            r.Refund(FixedClock(), 32900, acknowledgePostPayout: false);
            return r;
        }
    }

    // === 3. Resolve restores the pre-dispute state and clears the pointer ===

    [Fact]
    public void ResolveDispute_restores_state_and_clears_PreDisputeState()
    {
        var o = Disputed(OrderState.Shipped);

        var result = o.ResolveDispute(FixedClock(), OrderState.Shipped);

        result.IsSuccess.Should().BeTrue();
        o.State.Should().Be(OrderState.Shipped);
        o.PreDisputeState.Should().BeNull("the restore pointer is cleared on resolve");
        o.DisputedAt.Should().Be(Now, "DisputedAt is a historical marker, kept like PaidAt/ShippedAt");
    }

    // === 4. Resolve refused when not Disputed ===

    [Theory]
    [InlineData(OrderState.PendingPayment)]
    [InlineData(OrderState.Paid)]
    [InlineData(OrderState.Accepted)]
    [InlineData(OrderState.Shipped)]
    [InlineData(OrderState.Delivered)]
    [InlineData(OrderState.Completed)]
    public void ResolveDispute_refused_when_not_disputed(OrderState from)
    {
        var o = OrderInState(from);
        var stateBefore = o.State;

        var result = o.ResolveDispute(FixedClock(), OrderState.Shipped);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderInvalidTransition);
        o.State.Should().Be(stateBefore);
    }

    // === Invariant: PreDisputeState non-null ⇔ State == Disputed (§A.22 rule 2) ===

    [Fact]
    public void PreDisputeState_is_null_outside_the_detour()
    {
        var o = OrderInState(OrderState.Delivered);
        o.PreDisputeState.Should().BeNull();

        o.OpenDispute(FixedClock());
        o.PreDisputeState.Should().Be(OrderState.Delivered);

        o.ResolveDispute(FixedClock(), OrderState.Delivered);
        o.PreDisputeState.Should().BeNull();
    }

    // === 5. Dispute child entity: resolve invariants ===

    [Fact]
    public void Dispute_Resolve_sets_outcome_notes_timestamp_and_refuses_double_resolve()
    {
        var d = Dispute.Open(
            id: "dsp-1",
            orderId: "ord-1",
            category: DisputeCategory.DamagedItem,
            description: "  Rozbitá váza při doručení.  ",
            source: DisputeSource.Customer,
            countryCode: "CZ");

        d.OrderId.Should().Be("ord-1");
        d.Description.Should().Be("Rozbitá váza při doručení.", "factory trims the description");
        d.ResolvedAt.Should().BeNull("a fresh dispute is OPEN");
        d.ResolutionOutcome.Should().BeNull();
        d.ResolutionNotes.Should().BeNull();

        var first = d.Resolve(FixedClock(), DisputeResolutionOutcome.Resumed, "Vyřešeno dohodou.");

        first.IsSuccess.Should().BeTrue();
        d.ResolutionOutcome.Should().Be(DisputeResolutionOutcome.Resumed);
        d.ResolutionNotes.Should().Be("Vyřešeno dohodou.");
        d.ResolvedAt.Should().Be(Now);

        var second = d.Resolve(FixedClock(), DisputeResolutionOutcome.Refunded, "Druhý pokus.");

        second.IsSuccess.Should().BeFalse();
        second.Error!.Code.Should().Be(BusinessErrorMessage.OrderDisputeNotOpen);
        d.ResolutionOutcome.Should().Be(DisputeResolutionOutcome.Resumed, "the first resolution is immutable");
        d.ResolutionNotes.Should().Be("Vyřešeno dohodou.");
    }

    [Fact]
    public void Dispute_Open_rejects_missing_or_oversize_description()
    {
        var blank = () => Dispute.Open("dsp-1", "ord-1", DisputeCategory.Other, "   ",
            DisputeSource.Customer, "CZ");
        blank.Should().Throw<ArgumentException>();

        var oversize = () => Dispute.Open("dsp-1", "ord-1", DisputeCategory.Other,
            new string('x', Dispute.MaxDescriptionLength + 1), DisputeSource.Customer, "CZ");
        oversize.Should().Throw<ArgumentException>();
    }

    // === Helpers ===

    private static Order Disputed(OrderState origin)
    {
        var o = OrderInState(origin);
        o.OpenDispute(FixedClock());
        return o;
    }
}
