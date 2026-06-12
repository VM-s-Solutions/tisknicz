using Makables.Core.Domain.Common;

namespace Makables.Core.Domain.Orders;

/// <summary>
/// Which existing domain method an allowed manual transition routes to.
/// T-0107 §C: there is NO generic state setter — every permitted pair
/// dispatches a semantic method so its side effects (timestamps,
/// sources, set-once guards) cannot be skipped.
/// </summary>
public enum ManualOrderTransitionRoute : short
{
    /// <summary>Blocked decisions carry no route.</summary>
    None = 0,

    /// <summary>Same-state target — Silent-Success, no mutation.</summary>
    NoOp = 1,

    /// <summary>PendingPayment → Paid via <c>Order.MarkAsPaid(clock, existing ref)</c> (lost-webhook recovery).</summary>
    MarkAsPaid = 2,

    /// <summary>PendingPayment → Cancelled via <c>Order.Cancel(clock, Admin)</c> (manual expiry).</summary>
    Cancel = 3,

    /// <summary>Paid → Accepted via <c>Order.Accept(clock)</c> (redo after un-accept / maker unreachable).</summary>
    Accept = 4,

    /// <summary>Accepted → Paid via <c>Order.RevertAcceptance(clock)</c> (undo maker mis-click).</summary>
    RevertAcceptance = 5,

    /// <summary>Shipped → Delivered via <c>Order.MarkAsDelivered(clock, AdminManual)</c> (carrier-blind delivery).</summary>
    MarkAsDelivered = 6,
}

/// <summary>
/// The strict allow-list for the T-0107 admin manual state change
/// (user-locked Q4). Pure Domain logic — lives next to <see cref="Order"/>
/// so the entire transition surface (entity edges + manual-tool policy)
/// is reviewable in one folder, and stays dependency-free for the
/// table-driven matrix tests.
///
/// <para>
/// <b>Deterministic precedence (§C):</b>
/// (1) <c>to == from</c> → Allowed <see cref="ManualOrderTransitionRoute.NoOp"/>;
/// (2) from <see cref="OrderState.Refunded"/> → <c>notAllowed</c> (terminal, never out);
/// (3) from <see cref="OrderState.Disputed"/> → <c>useResolveDispute</c> (T-0106);
/// (4) to <see cref="OrderState.Refunded"/> → <c>useRefundOrder</c> (T-0105 — money must move first);
/// (5) to <see cref="OrderState.Disputed"/> → <c>useOpenDispute</c> (T-0106 — needs a Dispute row);
/// (6) (Delivered → Completed) → <c>useMarkPayoutBatchCompleted</c> (T-0103 — Completed means payout settled);
/// (7) (Paid|Accepted → Cancelled) → <c>useRefundOrder</c> (cancelling captured money strands customer funds);
/// (8) the five allow-list pairs (a Paid target additionally requires an
///     existing <c>PaymentProviderRef</c> — without one, marking Paid
///     fabricates revenue with no provider trail and permanently breaks
///     the refund path);
/// (9) everything else → <c>notAllowed</c>.
/// </para>
///
/// <para>
/// Every blocked transition that has a sanctioned command names it in
/// the error code (US-admin-0010 AC-2). Per the entity's authorisation
/// note: the state-graph EDGES live on <see cref="Order"/> (guards stay
/// as defence-in-depth); this policy is the who-may-take-which-edge
/// table for the admin manual tool.
/// </para>
/// </summary>
public static class ManualOrderTransitionPolicy
{
    /// <summary>
    /// Outcome of <see cref="Evaluate"/>: Allowed + a route, or Blocked +
    /// the <see cref="BusinessErrorMessage"/> code naming the sanctioned
    /// command (where one exists).
    /// </summary>
    public sealed record Decision(
        bool IsAllowed,
        ManualOrderTransitionRoute Route,
        string? BlockedCode)
    {
        internal static Decision Allowed(ManualOrderTransitionRoute route) =>
            new(true, route, null);

        internal static Decision Blocked(string code) =>
            new(false, ManualOrderTransitionRoute.None, code);
    }

    /// <summary>
    /// Classify a manual transition request. Total over the full
    /// <c>OrderState × OrderState</c> matrix — every pair gets a decision
    /// (the exhaustive test fails on an unclassified future enum value).
    /// </summary>
    public static Decision Evaluate(OrderState from, OrderState to, bool hasPaymentProviderRef)
    {
        // (1) Same-state target = Silent-Success no-op (T-0067/T-0076
        // idempotency precedent) — evaluated before the terminal-state
        // rules so a double-submit on any state stays a 200.
        if (to == from)
            return Decision.Allowed(ManualOrderTransitionRoute.NoOp);

        // (2) Refunded is terminal — never out.
        if (from == OrderState.Refunded)
            return Decision.Blocked(BusinessErrorMessage.OrderManualTransitionNotAllowed);

        // (3) Disputed resolution is T-0106's job.
        if (from == OrderState.Disputed)
            return Decision.Blocked(BusinessErrorMessage.OrderManualTransitionUseResolveDispute);

        // (4) Into Refunded only via RefundOrder — money moves before state.
        if (to == OrderState.Refunded)
            return Decision.Blocked(BusinessErrorMessage.OrderManualTransitionUseRefundOrder);

        // (5) Into Disputed only via OpenDispute — needs the Dispute row.
        if (to == OrderState.Disputed)
            return Decision.Blocked(BusinessErrorMessage.OrderManualTransitionUseOpenDispute);

        // (6) Completed means "maker payout settled" — faking it corrupts
        // payout reporting; T-0103 owns the edge.
        if (from == OrderState.Delivered && to == OrderState.Completed)
            return Decision.Blocked(BusinessErrorMessage.OrderManualTransitionUseMarkPayoutBatchCompleted);

        // (7) Money is captured — cancelling without refunding strands
        // customer funds.
        if (from is OrderState.Paid or OrderState.Accepted && to == OrderState.Cancelled)
            return Decision.Blocked(BusinessErrorMessage.OrderManualTransitionUseRefundOrder);

        // (8) The five documented manual-recovery pairs.
        switch (from, to)
        {
            case (OrderState.PendingPayment, OrderState.Paid):
                return hasPaymentProviderRef
                    ? Decision.Allowed(ManualOrderTransitionRoute.MarkAsPaid)
                    : Decision.Blocked(BusinessErrorMessage.OrderManualTransitionPaidRequiresProviderRef);

            case (OrderState.PendingPayment, OrderState.Cancelled):
                return Decision.Allowed(ManualOrderTransitionRoute.Cancel);

            case (OrderState.Paid, OrderState.Accepted):
                return Decision.Allowed(ManualOrderTransitionRoute.Accept);

            case (OrderState.Accepted, OrderState.Paid):
                return hasPaymentProviderRef
                    ? Decision.Allowed(ManualOrderTransitionRoute.RevertAcceptance)
                    : Decision.Blocked(BusinessErrorMessage.OrderManualTransitionPaidRequiresProviderRef);

            case (OrderState.Shipped, OrderState.Delivered):
                return Decision.Allowed(ManualOrderTransitionRoute.MarkAsDelivered);

            // (9) Everything else is blocked with the generic code.
            default:
                return Decision.Blocked(BusinessErrorMessage.OrderManualTransitionNotAllowed);
        }
    }
}
