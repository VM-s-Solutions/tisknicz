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
/// Transition an <see cref="Order"/> from
/// <see cref="OrderState.PendingPayment"/> to
/// <see cref="OrderState.Cancelled"/> after the 24h payment-retry
/// window elapses without a Comgate webhook confirming payment.
/// T-0083 (US-customer-0010 AC-3).
///
/// <para>
/// Dispatched by <c>CancelExpiredPendingPaymentOrdersFunction</c> per
/// row of the projection-only id stream
/// (<see cref="IOrderRepository.GetExpiredPendingPaymentUnscopedReadOnlyAsync"/>).
/// The Function context has no user identity — the unscoped lookup is
/// mandated. <see cref="OrderCancellationSource.AutoExpiry"/> is stamped
/// on the order so dispute trails + admin reporting can distinguish
/// auto-cancelled rows from customer (T-0105) / admin (T-0107) sources.
/// </para>
///
/// <para>
/// <b>Silent Success contract</b> per locked decision §C handler step 2:
/// if the loaded order is no longer in
/// <see cref="OrderState.PendingPayment"/> (customer-pays-mid-flight
/// race, double-cancel race, already-Cancelled re-fire), the handler
/// returns <see cref="BusinessResult.Success"/> WITHOUT mutation and
/// WITHOUT re-emitting the outbox event. Mirrors T-0076's
/// already-Delivered silent-Success contract.
/// </para>
///
/// <para>
/// <b>Atomicity.</b> The state transition + the outbox row commit in one
/// UoW transaction per ADR 0014. The handler NEVER calls
/// <c>SaveChangesAsync</c>.
/// </para>
/// </summary>
public static class CancelExpiredOrder
{
    public sealed record Command(string OrderId)
        : ICommand<CancelExpiredOrderResponse>;

    public sealed record CancelExpiredOrderResponse(string OrderId, OrderState State);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.OrderId)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(40).WithErrorCode(BusinessErrorMessage.MaxLength);
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
        : IRequestHandler<Command, BusinessResult<CancelExpiredOrderResponse>>
    {
        public async Task<BusinessResult<CancelExpiredOrderResponse>> Handle(
            Command command, CancellationToken cancellationToken)
        {
            // Step 1: Unscoped lookup (no user identity in Function context).
            var order = await orders.GetByIdUnscopedAsync(command.OrderId, cancellationToken);
            if (order is null)
            {
                return BusinessResult.Failure<CancelExpiredOrderResponse>(
                    Error.NotFound("orderId", BusinessErrorMessage.OrderNotFound));
            }

            // Step 2: Silent Success on wrong-state (already-cancelled,
            // customer-pays-mid-flight, etc.). Returns Success without
            // re-emitting outbox or re-mutating state.
            if (order.State != OrderState.PendingPayment)
            {
                logger.LogInformation(
                    "CancelExpiredOrder: order {OrderId} no longer in PendingPayment " +
                    "(current state = {State}); idempotent skip.",
                    order.Id, order.State);
                return BusinessResult.Success(
                    new CancelExpiredOrderResponse(order.Id, order.State));
            }

            // Step 3: Transition via the aggregate. AutoExpiry source
            // stamps Order.CancellationSource for dispute trails.
            var transitionResult = order.Cancel(clock, OrderCancellationSource.AutoExpiry);
            if (!transitionResult.IsSuccess)
            {
                // Belt-and-braces — the Silent Success guard above already
                // covered non-PendingPayment; this branch reflects a
                // state-graph drift that wasn't anticipated.
                return BusinessResult.Failure<CancelExpiredOrderResponse>(transitionResult.Error!);
            }

            // Step 4: Resolve customer + language + enqueue customer email.
            var customer = await users.GetByIdAsync(order.CustomerUserId, cancellationToken);
            if (customer is null)
            {
                logger.LogCritical(
                    "CancelExpiredOrder: customer user {UserId} not found for order {OrderId}. " +
                    "FK invariant violation — refusing to commit.",
                    order.CustomerUserId, order.Id);
                return BusinessResult.Failure<CancelExpiredOrderResponse>(
                    Error.NotFound("customerUserId", BusinessErrorMessage.OrderCustomerUserMissing));
            }
            var customerLanguage = await languageResolver.ResolveForUserAsync(customer, cancellationToken);
            var urls = publicAppUrls.Value;
            var payload = new OrderCancelledCustomerEmailPayload(
                OrderId: order.Id,
                OrderNumber: order.OrderNumber,
                Email: order.ContactEmail,
                ContactName: order.ContactName,
                Reason: OrderCancellationSource.AutoExpiry,
                LanguageCode: customerLanguage,
                ActionUrl: $"{urls.WebBaseUrl.TrimEnd('/')}/objednavka/{order.Id}");
            outbox.Enqueue(
                aggregateId: order.Id,
                eventType: OutboxEventTypes.OrderCancelledCustomerEmail,
                payloadJson: JsonSerializer.Serialize(payload));

            // Step 5: UoW pipeline commits state transition + outbox row atomically.
            return BusinessResult.Success(
                new CancelExpiredOrderResponse(order.Id, order.State));
        }
    }
}
