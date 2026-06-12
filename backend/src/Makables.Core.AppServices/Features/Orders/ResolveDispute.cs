using System.Text.Json;
using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.AppServices.Common;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Outbox;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Makables.Core.AppServices.Features.Orders;

/// <summary>
/// Admin resolves an open dispute (T-0106 / US-admin-0011 AC-2).
/// Sequencing per §C.3: restore the order's <c>PreDisputeState</c>,
/// mark the <see cref="Dispute"/> row resolved (outcome + customer-visible
/// notes + timestamp), enqueue the customer notification, THEN apply the
/// outcome edge from the restored state:
///
/// <list type="bullet">
///   <item><description><see cref="DisputeResolutionOutcome.Refunded"/> —
///     nested <c>mediator.Send(RefundOrder.Command)</c> for the FULL
///     remaining amount (the dispute lane never issues partials, Q1).
///     The T-0105 pipeline runs in-scope: its UoW commit flushes the
///     shared DbContext, landing the resolution mutations + the refund
///     atomically. A refund failure (e.g. Comgate window expired)
///     propagates and the dispute stays OPEN — nothing commits.</description></item>
///   <item><description><see cref="DisputeResolutionOutcome.Cancelled"/> —
///     <c>order.Cancel(clock, Admin)</c>; only reachable when the restored
///     state is Paid/Accepted. From Shipped/Delivered the entity's
///     allow-list refuses (<c>order.invalidTransition</c>) and the
///     transaction rolls back — the error steers the admin to the
///     Refunded outcome (AC-10).</description></item>
///   <item><description><see cref="DisputeResolutionOutcome.Resumed"/> —
///     nothing further; the order proceeds as if undisputed
///     (AutoDeliverAt is deliberately NOT extended, §C.10).</description></item>
/// </list>
///
/// <para>
/// Re-resolve is LOUD (<c>409 order.dispute.notOpen</c>), not
/// Silent-Success — a silently "succeeding" second resolve with a
/// different outcome would mask an admin race (§C.4 / Alternatives G).
/// </para>
/// </summary>
public static class ResolveDispute
{
    public sealed record Command(
        string OrderId,
        DisputeResolutionOutcome Outcome,
        string ResolutionNotes)
        : ICommand<ResolveDisputeResponse>, IAdminAuditableCommand
    {
        public string ActionCode => "order.dispute.resolve";
        public string TargetEntity => "order";
        public string TargetId => OrderId;
        public string? Notes => ResolutionNotes;
    }

    /// <summary>
    /// <b>Globally-unique name</b> per the post-PR-#38 NSwag convention.
    /// </summary>
    public sealed record ResolveDisputeResponse(
        string OrderId,
        OrderState State,
        DisputeResolutionOutcome Outcome);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.OrderId)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(40).WithErrorCode(BusinessErrorMessage.MaxLength);

            RuleFor(c => c.Outcome)
                .IsInEnum().WithErrorCode(BusinessErrorMessage.InvalidEnumValue);

            // Customer-VISIBLE (§C.7) — rendered in the resolve email.
            RuleFor(c => c.ResolutionNotes)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(Dispute.MaxResolutionNotesLength).WithErrorCode(BusinessErrorMessage.MaxLength);
        }
    }

    public sealed class Handler(
        IOrderRepository orders,
        IDisputeRepository disputes,
        ISender mediator,
        IUserRepository users,
        IOutbox outbox,
        IClock clock,
        IUserSessionProvider session,
        ILanguageResolver languageResolver,
        IOptions<PublicAppUrlsOptions> publicAppUrls,
        ILogger<Handler> logger)
        : IRequestHandler<Command, BusinessResult<ResolveDisputeResponse>>
    {
        public async Task<BusinessResult<ResolveDisputeResponse>> Handle(
            Command command, CancellationToken cancellationToken)
        {
            // Step 1: Fail-closed session check (VerifyMaker precedent).
            if (string.IsNullOrEmpty(session.GetUserId()))
            {
                return BusinessResult.Failure<ResolveDisputeResponse>(Error.Unauthorized());
            }

            // Step 2: Tracked unscoped load (admin host only, ADR 0013).
            var order = await orders.GetByIdUnscopedAsync(command.OrderId, cancellationToken);
            if (order is null)
            {
                return BusinessResult.Failure<ResolveDisputeResponse>(
                    Error.NotFound("orderId", BusinessErrorMessage.OrderNotFound));
            }

            // Step 3: Loud 409 when there is nothing open to resolve.
            if (order.State != OrderState.Disputed)
            {
                return BusinessResult.Failure<ResolveDisputeResponse>(
                    Error.Conflict("state", BusinessErrorMessage.OrderDisputeNotOpen));
            }

            // Step 4: The open dispute row. Defensive null — a Disputed
            // order without an open row violates the partial-unique-index
            // invariant.
            var dispute = await disputes.GetOpenByOrderIdAsync(order.Id, cancellationToken);
            if (dispute is null)
            {
                logger.LogCritical(
                    "ResolveDispute: order {OrderId} is Disputed but has NO open dispute row. " +
                    "Invariant violation — refusing to resolve.",
                    order.Id);
                return BusinessResult.Failure<ResolveDisputeResponse>(
                    Error.Conflict("dispute", BusinessErrorMessage.OrderDisputeNotOpen));
            }

            // Step 5: Restore the parenthesis state. PreDisputeState is
            // entity-guaranteed non-null while Disputed (§A.22 rule 2).
            var restore = order.ResolveDispute(clock, order.PreDisputeState!.Value);
            if (!restore.IsSuccess)
            {
                return BusinessResult.Failure<ResolveDisputeResponse>(restore.Error!);
            }

            // Step 6: Mark the dispute row resolved.
            var resolved = dispute.Resolve(clock, command.Outcome, command.ResolutionNotes);
            if (!resolved.IsSuccess)
            {
                return BusinessResult.Failure<ResolveDisputeResponse>(resolved.Error!);
            }

            // Step 7: Customer notification (enrichment-at-enqueue). FK
            // violation refuses the commit — CancelExpiredOrder precedent.
            var customer = await users.GetByIdAsync(order.CustomerUserId, cancellationToken);
            if (customer is null)
            {
                logger.LogCritical(
                    "ResolveDispute: customer user {UserId} not found for order {OrderId}. " +
                    "FK invariant violation — refusing to commit.",
                    order.CustomerUserId, order.Id);
                return BusinessResult.Failure<ResolveDisputeResponse>(
                    Error.NotFound("customerUserId", BusinessErrorMessage.OrderCustomerUserMissing));
            }
            var customerLanguage = await languageResolver.ResolveForUserAsync(customer, cancellationToken);
            var payload = new OrderDisputeResolvedCustomerEmailPayload(
                OrderId: order.Id,
                OrderNumber: order.OrderNumber,
                Email: order.ContactEmail,
                ContactName: order.ContactName,
                Outcome: command.Outcome,
                ResolutionNotes: dispute.ResolutionNotes!,
                LanguageCode: customerLanguage,
                ActionUrl: $"{publicAppUrls.Value.WebBaseUrl.TrimEnd('/')}/objednavka/{order.Id}");
            outbox.Enqueue(
                aggregateId: order.Id,
                eventType: OutboxEventTypes.OrderDisputeResolvedCustomerEmail,
                payloadJson: JsonSerializer.Serialize(payload));

            // Step 8: Outcome edge from the restored state.
            switch (command.Outcome)
            {
                case DisputeResolutionOutcome.Refunded:
                {
                    // Full remaining amount — the dispute lane never issues
                    // partials (Q1). The nested T-0105 pipeline pre-flights,
                    // calls Comgate, mutates, audits, and its UoW commit
                    // flushes the shared DbContext (resolution + refund land
                    // atomically). Failure → dispute stays OPEN (Risk §4).
                    var refundResult = await mediator.Send(
                        new RefundOrder.Command(
                            order.Id,
                            order.RemainingRefundableMinor,
                            command.ResolutionNotes,
                            AcknowledgePostPayout: false),
                        cancellationToken);
                    if (!refundResult.IsSuccess)
                    {
                        return BusinessResult.Failure<ResolveDisputeResponse>(refundResult.Error!);
                    }
                    break;
                }
                case DisputeResolutionOutcome.Cancelled:
                {
                    // Only reachable from a Paid/Accepted restore; the
                    // entity's allow-list refuses Shipped/Delivered and the
                    // whole resolution rolls back (AC-10).
                    var cancelResult = order.Cancel(clock, OrderCancellationSource.Admin);
                    if (!cancelResult.IsSuccess)
                    {
                        return BusinessResult.Failure<ResolveDisputeResponse>(cancelResult.Error!);
                    }
                    break;
                }
                case DisputeResolutionOutcome.Resumed:
                default:
                    // Resumed: the order proceeds from the restored state.
                    break;
            }

            // Step 9: UoW commits restore + dispute row + outbox + audit
            // atomically (the Refunded branch already flushed; the outer
            // commit is a safe no-op for unchanged entries).
            return BusinessResult.Success(
                new ResolveDisputeResponse(order.Id, order.State, command.Outcome));
        }
    }
}
