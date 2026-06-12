using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Orders;
using MediatR;

namespace Makables.Core.AppServices.Features.Orders;

/// <summary>
/// Admin escape hatch for stuck orders (T-0107 / US-admin-0010): lost
/// Comgate webhook, maker mis-click on Accept, carrier-blind delivery,
/// manual pending-payment expiry. Deliberately NOT a free-form state
/// setter — <see cref="ManualOrderTransitionPolicy"/> enforces the
/// user-locked Q4 strict allow-list and every blocked transition with a
/// sanctioned command names it in the error code (AC-2). Allowed pairs
/// route to the existing semantic domain methods so timestamps, sources
/// and set-once guards are never skipped.
///
/// <para>
/// Mandatory ≥10-char reason (the cheap forensic record — the audit
/// JSONB shows WHAT changed, the reason records WHY); audited via
/// <see cref="IAdminAuditableCommand"/>. Same-state target is Silent
/// Success (no mutation; the audit row records identical before/after
/// with the reason). No outbox events, no emails (PM default — manual
/// fixes are exception handling; the admin coordinates via the
/// order-message thread).
/// </para>
/// </summary>
public static class ChangeOrderStateManually
{
    public sealed record Command(string OrderId, OrderState TargetState, string Reason)
        : ICommand<ChangeOrderStateManuallyResponse>, IAdminAuditableCommand
    {
        public string ActionCode => "order.manualStateChange";
        public string TargetEntity => "order";
        public string TargetId => OrderId;
        public string? Notes => Reason;
    }

    /// <summary>
    /// <b>Globally-unique name</b> per the post-PR-#38 NSwag convention.
    /// Returns the post-transition state.
    /// </summary>
    public sealed record ChangeOrderStateManuallyResponse(OrderState State);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.OrderId)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(40).WithErrorCode(BusinessErrorMessage.MaxLength);

            RuleFor(c => c.TargetState)
                .IsInEnum().WithErrorCode(BusinessErrorMessage.InvalidEnumValue);

            // MinimumLength(10) forces a real sentence, not "fix"; max is
            // the audit-notes column width (VerifyMaker m-3 precedent).
            RuleFor(c => c.Reason)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MinimumLength(10).WithErrorCode(BusinessErrorMessage.MinLength)
                .MaximumLength(2000).WithErrorCode(BusinessErrorMessage.MaxLength);
        }
    }

    public sealed class Handler(
        IOrderRepository orders,
        IUserSessionProvider session,
        IClock clock)
        : IRequestHandler<Command, BusinessResult<ChangeOrderStateManuallyResponse>>
    {
        public async Task<BusinessResult<ChangeOrderStateManuallyResponse>> Handle(
            Command command, CancellationToken cancellationToken)
        {
            // Step 1: Fail-closed session check (VerifyMaker / T-0034
            // precedent) — never attribute a privileged state change to
            // "system" in the audit log.
            if (string.IsNullOrEmpty(session.GetUserId()))
            {
                return BusinessResult.Failure<ChangeOrderStateManuallyResponse>(Error.Unauthorized());
            }

            // Step 2: Tracked unscoped load (admin host only, ADR 0013).
            var order = await orders.GetByIdUnscopedAsync(command.OrderId, cancellationToken);
            if (order is null)
            {
                return BusinessResult.Failure<ChangeOrderStateManuallyResponse>(
                    Error.NotFound("orderId", BusinessErrorMessage.OrderNotFound));
            }

            // Step 3: The policy classifies the pair (same-state → NoOp).
            var decision = ManualOrderTransitionPolicy.Evaluate(
                order.State, command.TargetState, order.PaymentProviderRef is not null);
            if (!decision.IsAllowed)
            {
                return BusinessResult.Failure<ChangeOrderStateManuallyResponse>(
                    Error.Conflict("state", decision.BlockedCode!));
            }

            // Step 4: Route to the semantic domain method (§A.2 table).
            // NoOp = Silent Success — the audit row still records the
            // identical before/after with the reason.
            var transition = decision.Route switch
            {
                ManualOrderTransitionRoute.NoOp => BusinessResult.Success(),
                // The matching-ref set-once guard passes; PaidAt = now —
                // there is no authoritative provider timestamp on the
                // manual path (§C). The policy guaranteed the ref exists.
                ManualOrderTransitionRoute.MarkAsPaid =>
                    order.MarkAsPaid(clock, order.PaymentProviderRef!),
                ManualOrderTransitionRoute.Cancel =>
                    order.Cancel(clock, OrderCancellationSource.Admin),
                ManualOrderTransitionRoute.Accept => order.Accept(clock),
                ManualOrderTransitionRoute.RevertAcceptance => order.RevertAcceptance(clock),
                ManualOrderTransitionRoute.MarkAsDelivered =>
                    order.MarkAsDelivered(clock, OrderDeliverySource.AdminManual),
                _ => BusinessResult.Failure(
                    Error.Conflict("state", BusinessErrorMessage.OrderManualTransitionNotAllowed)),
            };
            if (!transition.IsSuccess)
            {
                // Defence-in-depth: unreachable while the policy and the
                // entity guards agree; surfaces as Conflict if they drift.
                return BusinessResult.Failure<ChangeOrderStateManuallyResponse>(transition.Error!);
            }

            // Step 5: UoW commits mutation + audit row atomically. No
            // outbox, no emails (PM default).
            return BusinessResult.Success(new ChangeOrderStateManuallyResponse(order.State));
        }
    }
}
