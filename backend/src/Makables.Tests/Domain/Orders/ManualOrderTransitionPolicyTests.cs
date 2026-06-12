using FluentAssertions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Orders;

namespace Makables.Tests.Domain.Orders;

/// <summary>
/// T-0107 red-first, table-driven pins for
/// <see cref="ManualOrderTransitionPolicy"/> — the strict allow-list the
/// admin manual state-change tool enforces (user-locked Q4). The policy
/// is pure Domain logic: deterministic precedence, no mocks, full
/// <c>OrderState × OrderState</c> matrix coverage so a future enum value
/// fails test 9 until explicitly classified.
///
/// <para>
/// Precedence per T-0107 §C: (1) same-state NoOp; (2) from Refunded →
/// notAllowed; (3) from Disputed → useResolveDispute; (4) to Refunded →
/// useRefundOrder; (5) to Disputed → useOpenDispute; (6) Delivered →
/// Completed → useMarkPayoutBatchCompleted; (7) Paid|Accepted →
/// Cancelled → useRefundOrder; (8) allow-list pair (Paid target needs a
/// provider ref); (9) everything else → notAllowed.
/// </para>
/// </summary>
public class ManualOrderTransitionPolicyTests
{
    private static readonly OrderState[] AllStates =
        Enum.GetValues<OrderState>();

    // === 1. The five allowed pairs route to the right domain method ===

    [Theory]
    [InlineData(OrderState.PendingPayment, OrderState.Paid, ManualOrderTransitionRoute.MarkAsPaid)]
    [InlineData(OrderState.PendingPayment, OrderState.Cancelled, ManualOrderTransitionRoute.Cancel)]
    [InlineData(OrderState.Paid, OrderState.Accepted, ManualOrderTransitionRoute.Accept)]
    [InlineData(OrderState.Accepted, OrderState.Paid, ManualOrderTransitionRoute.RevertAcceptance)]
    [InlineData(OrderState.Shipped, OrderState.Delivered, ManualOrderTransitionRoute.MarkAsDelivered)]
    public void Allowed_pairs_route_correctly(
        OrderState from, OrderState to, ManualOrderTransitionRoute expectedRoute)
    {
        var decision = ManualOrderTransitionPolicy.Evaluate(from, to, hasPaymentProviderRef: true);

        decision.IsAllowed.Should().BeTrue();
        decision.Route.Should().Be(expectedRoute);
        decision.BlockedCode.Should().BeNull();
    }

    // === Same-state target = Silent-Success NoOp (precedence rule 1) ===

    [Fact]
    public void Same_state_target_is_allowed_NoOp_for_every_state()
    {
        foreach (var state in AllStates)
        {
            var decision = ManualOrderTransitionPolicy.Evaluate(state, state, hasPaymentProviderRef: false);
            decision.IsAllowed.Should().BeTrue($"({state} → {state}) is the idempotent no-op");
            decision.Route.Should().Be(ManualOrderTransitionRoute.NoOp);
        }
    }

    // === 2. → Refunded is always RefundOrder's job (Q4 interlock with T-0105) ===

    [Theory]
    [InlineData(OrderState.PendingPayment)]
    [InlineData(OrderState.Paid)]
    [InlineData(OrderState.Accepted)]
    [InlineData(OrderState.Shipped)]
    [InlineData(OrderState.Delivered)]
    [InlineData(OrderState.Completed)]
    [InlineData(OrderState.Cancelled)]
    public void Target_Refunded_blocked_useRefundOrder_from_every_state(OrderState from)
    {
        var decision = ManualOrderTransitionPolicy.Evaluate(
            from, OrderState.Refunded, hasPaymentProviderRef: true);

        decision.IsAllowed.Should().BeFalse();
        decision.BlockedCode.Should().Be(BusinessErrorMessage.OrderManualTransitionUseRefundOrder);
    }

    // === 3. → Disputed is always OpenDispute's job (T-0106) ===

    [Theory]
    [InlineData(OrderState.PendingPayment)]
    [InlineData(OrderState.Paid)]
    [InlineData(OrderState.Accepted)]
    [InlineData(OrderState.Shipped)]
    [InlineData(OrderState.Delivered)]
    [InlineData(OrderState.Completed)]
    [InlineData(OrderState.Cancelled)]
    public void Target_Disputed_blocked_useOpenDispute_from_every_state(OrderState from)
    {
        var decision = ManualOrderTransitionPolicy.Evaluate(
            from, OrderState.Disputed, hasPaymentProviderRef: true);

        decision.IsAllowed.Should().BeFalse();
        decision.BlockedCode.Should().Be(BusinessErrorMessage.OrderManualTransitionUseOpenDispute);
    }

    // === 4. Refunded is terminal — never out (precedence beats target rules) ===

    [Fact]
    public void From_Refunded_blocked_notAllowed_for_every_target()
    {
        foreach (var to in AllStates.Where(s => s != OrderState.Refunded))
        {
            var decision = ManualOrderTransitionPolicy.Evaluate(
                OrderState.Refunded, to, hasPaymentProviderRef: true);

            decision.IsAllowed.Should().BeFalse($"Refunded → {to} must be blocked");
            decision.BlockedCode.Should().Be(BusinessErrorMessage.OrderManualTransitionNotAllowed);
        }
    }

    // === 5. Disputed routes the admin to ResolveDispute ===

    [Fact]
    public void From_Disputed_blocked_useResolveDispute_for_every_target()
    {
        foreach (var to in AllStates.Where(s => s != OrderState.Disputed))
        {
            var decision = ManualOrderTransitionPolicy.Evaluate(
                OrderState.Disputed, to, hasPaymentProviderRef: true);

            decision.IsAllowed.Should().BeFalse($"Disputed → {to} must be blocked");
            decision.BlockedCode.Should().Be(BusinessErrorMessage.OrderManualTransitionUseResolveDispute);
        }
    }

    // === 6. → Paid without a provider ref fabricates revenue — blocked ===

    [Theory]
    [InlineData(OrderState.PendingPayment)]
    [InlineData(OrderState.Accepted)]
    public void Paid_target_without_providerRef_blocked_paidRequiresProviderRef(OrderState from)
    {
        var decision = ManualOrderTransitionPolicy.Evaluate(
            from, OrderState.Paid, hasPaymentProviderRef: false);

        decision.IsAllowed.Should().BeFalse();
        decision.BlockedCode.Should().Be(BusinessErrorMessage.OrderManualTransitionPaidRequiresProviderRef);
    }

    // === 7. Completed is the payout pipeline's terminal stamp (T-0103) ===

    [Fact]
    public void Delivered_to_Completed_blocked_useMarkPayoutBatchCompleted()
    {
        var decision = ManualOrderTransitionPolicy.Evaluate(
            OrderState.Delivered, OrderState.Completed, hasPaymentProviderRef: true);

        decision.IsAllowed.Should().BeFalse();
        decision.BlockedCode.Should().Be(
            BusinessErrorMessage.OrderManualTransitionUseMarkPayoutBatchCompleted);
    }

    // === 8. Cancelling captured money without a refund strands customer funds ===

    [Theory]
    [InlineData(OrderState.Paid)]
    [InlineData(OrderState.Accepted)]
    public void Paid_or_Accepted_to_Cancelled_blocked_useRefundOrder(OrderState from)
    {
        var decision = ManualOrderTransitionPolicy.Evaluate(
            from, OrderState.Cancelled, hasPaymentProviderRef: true);

        decision.IsAllowed.Should().BeFalse();
        decision.BlockedCode.Should().Be(BusinessErrorMessage.OrderManualTransitionUseRefundOrder);
    }

    // === 9. Exhaustive matrix: a future OrderState fails here until classified ===

    [Fact]
    public void Full_matrix_exhaustive_only_documented_pairs_are_allowed()
    {
        var allowedPairs = new HashSet<(OrderState From, OrderState To)>
        {
            (OrderState.PendingPayment, OrderState.Paid),
            (OrderState.PendingPayment, OrderState.Cancelled),
            (OrderState.Paid, OrderState.Accepted),
            (OrderState.Accepted, OrderState.Paid),
            (OrderState.Shipped, OrderState.Delivered),
        };

        foreach (var from in AllStates)
        foreach (var to in AllStates)
        {
            var decision = ManualOrderTransitionPolicy.Evaluate(from, to, hasPaymentProviderRef: true);

            decision.Should().NotBeNull($"every ({from} → {to}) pair must have a decision");

            var expectedAllowed = from == to || allowedPairs.Contains((from, to));
            decision.IsAllowed.Should().Be(expectedAllowed,
                $"({from} → {to}) — only the 5 documented pairs plus same-state NoOp are Allowed");

            if (decision.IsAllowed)
            {
                decision.Route.Should().NotBe(ManualOrderTransitionRoute.None,
                    $"({from} → {to}) — an allowed decision must carry a route");
                decision.BlockedCode.Should().BeNull();
            }
            else
            {
                decision.Route.Should().Be(ManualOrderTransitionRoute.None);
                decision.BlockedCode.Should().NotBeNullOrEmpty(
                    $"({from} → {to}) — a blocked decision must name its error code");
            }
        }
    }
}
