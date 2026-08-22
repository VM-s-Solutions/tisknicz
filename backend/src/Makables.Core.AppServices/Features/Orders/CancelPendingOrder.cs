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
/// Customer cancels their own UNPAID order (T-0181, audit CUST-M3).
///
/// <para>
/// Until now an accidental or abandoned order offered only "Zaplatit";
/// the sole exit was the silent 24 h auto-expiry (T-0083), while the
/// order sat in the customer's list as "Čeká na platbu" with no
/// explanation of how to get rid of it.
/// </para>
///
/// <para>
/// <b>Scope is deliberately narrow (Q-0041).</b> The customer may cancel
/// from <see cref="OrderState.PendingPayment"/> ONLY — no money has moved
/// there, so there is no refund path and no time window. Cancelling a
/// PAID order is the maker's "refuse" action
/// (<see cref="RefuseOrder"/>), never the customer's: on made-to-order
/// goods that would return money after production may have started.
/// This mirrors the user-confirmed 2026-06-03 role decision.
/// </para>
///
/// <para>
/// <b>Silent Success</b> on an already-cancelled order (mirrors T-0076 /
/// T-0083): re-running the action produces no second transition and no
/// second outbox row, so a double-click or a stale tab is harmless.
/// </para>
/// </summary>
public static class CancelPendingOrder
{
    public sealed record Command(string OrderId, string CustomerUserId)
        : ICommand<CancelPendingOrderResponse>;

    /// <summary>Globally-unique name (NSwag convention).</summary>
    public sealed record CancelPendingOrderResponse(string OrderId, OrderState State);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.OrderId)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(40).WithErrorCode(BusinessErrorMessage.MaxLength);

            RuleFor(c => c.CustomerUserId)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required);
        }
    }

    public sealed class Handler(
        IOrderRepository orders,
        IUserRepository users,
        IOutbox outbox,
        IClock clock,
        ILanguageResolver languageResolver,
        IOptions<PublicAppUrlsOptions> publicAppUrls,
        ILogger<Handler> logger)
        : IRequestHandler<Command, BusinessResult<CancelPendingOrderResponse>>
    {
        public async Task<BusinessResult<CancelPendingOrderResponse>> Handle(
            Command command, CancellationToken cancellationToken)
        {
            // Ownership IS the scoped lookup (ADR 0013): another customer's
            // order resolves to null, so it is not-found rather than a 403
            // that would confirm the order exists.
            var order = await orders.GetByIdForCustomerAsync(
                command.OrderId, command.CustomerUserId, cancellationToken);
            if (order is null)
            {
                return BusinessResult.Failure<CancelPendingOrderResponse>(
                    Error.NotFound("orderId", BusinessErrorMessage.OrderNotFound));
            }

            // Idempotent re-run: already cancelled → Success, no second
            // outbox row, no second transition.
            if (order.State == OrderState.Cancelled)
            {
                logger.LogInformation(
                    "CancelPendingOrder: order {OrderId} already cancelled; idempotent skip.",
                    order.Id);
                return BusinessResult.Success(
                    new CancelPendingOrderResponse(order.Id, order.State));
            }

            // Anything past PendingPayment is money-touching and therefore
            // NOT the customer's to cancel — the aggregate would accept the
            // transition, so the role rule is enforced here on purpose.
            if (order.State != OrderState.PendingPayment)
            {
                return BusinessResult.Failure<CancelPendingOrderResponse>(
                    Error.Conflict("state", BusinessErrorMessage.OrderInvalidTransition));
            }

            var transitionResult = order.Cancel(clock, OrderCancellationSource.Customer);
            if (!transitionResult.IsSuccess)
            {
                return BusinessResult.Failure<CancelPendingOrderResponse>(transitionResult.Error!);
            }

            var customer = await users.GetByIdAsync(order.CustomerUserId, cancellationToken);
            if (customer is null)
            {
                logger.LogCritical(
                    "CancelPendingOrder: customer user {UserId} not found for order {OrderId}. " +
                    "FK invariant violation — refusing to commit.",
                    order.CustomerUserId, order.Id);
                return BusinessResult.Failure<CancelPendingOrderResponse>(
                    Error.NotFound("customerUserId", BusinessErrorMessage.OrderCustomerUserMissing));
            }

            var customerLanguage = await languageResolver.ResolveForUserAsync(customer, cancellationToken);
            var urls = publicAppUrls.Value;
            var payload = new OrderCancelledCustomerEmailPayload(
                OrderId: order.Id,
                OrderNumber: order.OrderNumber,
                Email: order.ContactEmail,
                ContactName: order.ContactName,
                Reason: OrderCancellationSource.Customer,
                LanguageCode: customerLanguage,
                ActionUrl: $"{urls.WebBaseUrl.TrimEnd('/')}/objednavka/{order.Id}");
            outbox.Enqueue(
                aggregateId: order.Id,
                eventType: OutboxEventTypes.OrderCancelledCustomerEmail,
                payloadJson: JsonSerializer.Serialize(payload));

            // The UoW pipeline commits the transition + the outbox row together.
            return BusinessResult.Success(
                new CancelPendingOrderResponse(order.Id, order.State));
        }
    }
}
