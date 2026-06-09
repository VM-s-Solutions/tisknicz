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
/// Transition an <see cref="Order"/> from <see cref="OrderState.Shipped"/>
/// to <see cref="OrderState.Delivered"/>. <b>Single canonical writer</b>
/// for all three delivery-close callers per the bundle:
/// <list type="bullet">
///   <item><description><b>T-0076 customer-confirm endpoint</b> —
///     <c>POST /api/v1/customer/orders/{orderId}/deliver</c> with
///     <see cref="OrderDeliverySource.Customer"/>. IDOR-scoped via
///     <see cref="IUserSessionProvider.GetUserId"/>.</description></item>
///   <item><description><b>T-0077 auto-deliver Function</b> — dispatched
///     when <c>AutoDeliverAt</c> elapses without confirmation, with
///     <see cref="OrderDeliverySource.Auto"/>.</description></item>
///   <item><description><b>T-0078 Packeta status-sync Function</b> —
///     dispatched when Packeta reports Delivered, with
///     <see cref="OrderDeliverySource.Carrier"/> and the carrier's
///     authoritative <c>DeliveredAt</c> timestamp via
///     <see cref="Command.DeliveredAtOverride"/>.</description></item>
/// </list>
///
/// <para>
/// <b>Lookup branch.</b> When <c>Source == Customer</c> the handler reads
/// <see cref="IUserSessionProvider.GetUserId"/> and scopes via
/// <see cref="IOrderRepository.GetByIdForCustomerAsync"/> (IDOR shield).
/// When <c>Source == Auto</c> or <c>Source == Carrier</c> the Function
/// context has no user identity so the handler scopes via
/// <see cref="IOrderRepository.GetByIdUnscopedAsync"/>. Both lookups
/// return a tracked instance; the UoW pipeline commits the state
/// transition + outbox row atomically per ADR 0014.
/// </para>
///
/// <para>
/// <b>Silent Success on already-Delivered re-call</b> per T-0076 locked
/// decision A.3. When the loaded order is already in
/// <see cref="OrderState.Delivered"/> the handler returns
/// <see cref="BusinessResult.Success"/> WITHOUT mutating the entity and
/// WITHOUT enqueuing the outbox event. Detected by reading the order
/// BEFORE calling <see cref="Order.MarkAsDelivered"/> (so the silent
/// branch is testable and surfaces no spurious 409s in the customer +
/// carrier race). For any OTHER non-Shipped state (Accepted, Cancelled,
/// etc.) the standard <see cref="BusinessErrorMessage.OrderInvalidTransition"/>
/// failure surfaces as 409.
/// </para>
///
/// <para>
/// <b>Atomicity.</b> The order mutation (State → Delivered, DeliveredAt,
/// DeliverySource) AND the single <see cref="OutboxEventTypes.OrderDeliveredCustomerEmail"/>
/// outbox row commit in one Postgres transaction via
/// <c>UnitOfWorkPipelineBehavior</c> per ADR 0014. The handler NEVER
/// calls <c>SaveChangesAsync</c>.
/// </para>
/// </summary>
public static class MarkOrderDelivered
{
    /// <summary>
    /// Mark an order delivered by id.
    /// </summary>
    /// <param name="OrderId">Target order.</param>
    /// <param name="Source">Which caller is driving the transition.</param>
    /// <param name="DeliveredAtOverride">
    /// Optional authoritative timestamp from the carrier (T-0078 only).
    /// Null → <see cref="IClock.UtcNow"/> wins (T-0076 customer +
    /// T-0077 auto paths always pass null).
    /// </param>
    public sealed record Command(
        string OrderId,
        OrderDeliverySource Source,
        DateTimeOffset? DeliveredAtOverride = null) : ICommand<MarkOrderDeliveredResponse>;

    /// <summary>
    /// Response carrying the order id + final state. <b>Globally-unique
    /// name</b> (not nested <c>Response</c>) to avoid the NSwag TS class
    /// collision documented at T-0070-T-0075 CI fix.
    /// </summary>
    public sealed record MarkOrderDeliveredResponse(string OrderId, OrderState State);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.OrderId)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(40).WithErrorCode(BusinessErrorMessage.MaxLength);

            RuleFor(c => c.Source)
                .IsInEnum().WithErrorCode(BusinessErrorMessage.Required);
        }
    }

    public sealed class Handler(
        IOrderRepository orders,
        IUserRepository users,
        IUserSessionProvider session,
        IOutbox outbox,
        IClock clock,
        ILanguageResolver languageResolver,
        IOptions<PublicAppUrlsOptions> publicAppUrls,
        ILogger<Handler> logger) : IRequestHandler<Command, BusinessResult<MarkOrderDeliveredResponse>>
    {
        public async Task<BusinessResult<MarkOrderDeliveredResponse>> Handle(
            Command command, CancellationToken cancellationToken)
        {
            // Step 1: Load the order. Customer path is IDOR-scoped via the
            // session id; Function paths (Auto / Carrier) have no caller
            // identity so they go through the unscoped lookup.
            Order? order;
            if (command.Source == OrderDeliverySource.Customer)
            {
                var customerUserId = session.GetUserId();
                if (string.IsNullOrEmpty(customerUserId))
                {
                    return BusinessResult.Failure<MarkOrderDeliveredResponse>(Error.Unauthorized());
                }
                order = await orders.GetByIdForCustomerAsync(
                    command.OrderId, customerUserId, cancellationToken);
            }
            else
            {
                order = await orders.GetByIdUnscopedAsync(
                    command.OrderId, cancellationToken);
            }

            if (order is null)
            {
                return BusinessResult.Failure<MarkOrderDeliveredResponse>(
                    Error.NotFound("orderId", BusinessErrorMessage.OrderNotFound));
            }

            // Step 2: Already-Delivered Silent Success guard (T-0076 A.3).
            // The customer + T-0078 carrier-sync race is the expected
            // concurrent path; the second writer must succeed silently
            // without re-emitting the email outbox event or overwriting
            // the first writer's DeliverySource.
            if (order.State == OrderState.Delivered)
            {
                logger.LogInformation(
                    "MarkOrderDelivered: order {OrderId} already Delivered (idempotent skip, source={Source}, existingSource={ExistingSource}).",
                    order.Id, command.Source, order.DeliverySource);
                return BusinessResult.Success(
                    new MarkOrderDeliveredResponse(order.Id, order.State));
            }

            // Step 3: State transition via the aggregate. Any non-Shipped,
            // non-Delivered state (Accepted, Cancelled, Paid, …) surfaces
            // as Conflict + OrderInvalidTransition — the customer endpoint
            // maps that to 409.
            var transitionResult = order.MarkAsDelivered(
                clock, command.Source, command.DeliveredAtOverride);
            if (!transitionResult.IsSuccess)
            {
                return BusinessResult.Failure<MarkOrderDeliveredResponse>(transitionResult.Error!);
            }

            // Step 4: Resolve customer + language + build payload.
            var customer = await users.GetByIdAsync(order.CustomerUserId, cancellationToken);
            if (customer is null)
            {
                logger.LogCritical(
                    "MarkOrderDelivered: customer user {UserId} not found for order {OrderId}. " +
                    "FK invariant violation — refusing to commit.",
                    order.CustomerUserId, order.Id);
                return BusinessResult.Failure<MarkOrderDeliveredResponse>(
                    Error.NotFound("customerUserId", BusinessErrorMessage.OrderCustomerUserMissing));
            }
            var customerLanguage = await languageResolver.ResolveForUserAsync(customer, cancellationToken);
            var urls = publicAppUrls.Value;

            // Step 5: Enqueue the customer delivered-email event. No
            // attachment — the invoice rode the OrderPaidCustomerEmail
            // per T-0069 locked decision 10.
            var payload = new OrderDeliveredCustomerEmailPayload(
                OrderId: order.Id,
                OrderNumber: order.OrderNumber,
                Email: order.ContactEmail,
                ContactName: order.ContactName,
                LanguageCode: customerLanguage,
                ActionUrl: $"{urls.WebBaseUrl.TrimEnd('/')}/objednavka/{order.Id}");
            outbox.Enqueue(
                aggregateId: order.Id,
                eventType: OutboxEventTypes.OrderDeliveredCustomerEmail,
                payloadJson: JsonSerializer.Serialize(payload));

            // Step 6: No SaveChangesAsync — UoW pipeline commits the
            // order mutation + outbox row atomically per ADR 0014.
            return BusinessResult.Success(
                new MarkOrderDeliveredResponse(order.Id, order.State));
        }
    }
}
