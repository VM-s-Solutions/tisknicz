using System.Text.Json;
using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.AppServices.Common;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Outbox;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Makables.Core.AppServices.Features.Orders;

/// <summary>
/// Personal-pickup HandOverOrder command — symmetric counterpart to
/// <see cref="ShipOrder"/>. T-0073 (US-maker-0008).
///
/// <para>
/// Maker hands the finished item to the customer in person (workshop
/// pickup, market stall) and clicks "Předáno zákazníkovi". The handler
/// transitions <see cref="OrderState.Accepted"/> →
/// <see cref="OrderState.Shipped"/> with NO carrier call + null tracking
/// URL, and enqueues a single
/// <see cref="OutboxEventTypes.OrderShippedCustomerEmail"/> event with
/// <c>TrackingUrl = null</c>. The unified SendGrid template conditionally
/// renders the tracking-URL row.
/// </para>
///
/// <para>
/// Both writers (T-0072 + T-0073) use the same 7-day auto-deliver window
/// per T-0070 locked decision A.4.
/// </para>
/// </summary>
public static class HandOverOrder
{
    public sealed record Command(string OrderId) : ICommand<Response>;

    public sealed record Response(string OrderId);

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
        IMakerRepository makers,
        IUserSessionProvider session,
        IOutbox outbox,
        IClock clock,
        ILanguageResolver languageResolver,
        IOptions<PublicAppUrlsOptions> publicAppUrls,
        ILogger<Handler> logger) : IRequestHandler<Command, BusinessResult<Response>>
    {
        public async Task<BusinessResult<Response>> Handle(
            Command command, CancellationToken cancellationToken)
        {
            // Step 1: Resolve maker session.
            var userId = session.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return BusinessResult.Failure<Response>(Error.Unauthorized());
            }

            // Step 2: Resolve maker for the authenticated user.
            var maker = await makers.GetByUserIdAsync(userId, cancellationToken);
            if (maker is null)
            {
                return BusinessResult.Failure<Response>(
                    Error.NotFound("orderId", BusinessErrorMessage.OrderNotFound));
            }

            // Step 3: Owner-scoped order load.
            var order = await orders.GetByIdForMakerAsync(
                command.OrderId, maker.Id, cancellationToken);
            if (order is null)
            {
                return BusinessResult.Failure<Response>(
                    Error.NotFound("orderId", BusinessErrorMessage.OrderNotFound));
            }

            // Step 4: Assert shipping method eligibility. PersonalPickup only;
            // Zásilkovna orders route to ShipOrder.
            if (order.ShippingMethod != ShippingMethod.PersonalPickup)
            {
                return BusinessResult.Failure<Response>(
                    Error.Validation("shippingMethod", BusinessErrorMessage.ShippingMethodNotEligible));
            }

            // Step 5: State transition. Null carrier ref + null tracking URL.
            var transitionResult = order.Ship(
                clock,
                shippingCarrierRef: null,
                autoDeliverWindowDays: 7,
                trackingUrl: null);
            if (!transitionResult.IsSuccess)
            {
                return BusinessResult.Failure<Response>(transitionResult.Error!);
            }

            // Step 6: Resolve customer language + build payload (TrackingUrl null).
            var customer = await users.GetByIdAsync(order.CustomerUserId, cancellationToken);
            if (customer is null)
            {
                logger.LogCritical(
                    "HandOverOrder: customer user {UserId} not found for order {OrderId}. " +
                    "FK invariant violation — refusing to commit.",
                    order.CustomerUserId, order.Id);
                return BusinessResult.Failure<Response>(
                    Error.NotFound("customerUserId", BusinessErrorMessage.OrderCustomerUserMissing));
            }
            var customerLanguage = await languageResolver.ResolveForUserAsync(customer, cancellationToken);
            var urls = publicAppUrls.Value;
            var payload = new OrderShippedCustomerEmailPayload(
                OrderId: order.Id,
                OrderNumber: order.OrderNumber,
                Email: order.ContactEmail,
                ContactName: order.ContactName,
                LanguageCode: customerLanguage,
                ActionUrl: $"{urls.WebBaseUrl.TrimEnd('/')}/objednavka/{order.Id}",
                TrackingUrl: null);

            // Step 7: Enqueue a SINGLE outbox event — no shipping.generate.label
            // event for personal-pickup (no Packeta call, no label, no carrier ref).
            // This is the T-0072-vs-T-0073 distinguishing constraint.
            outbox.Enqueue(
                aggregateId: order.Id,
                eventType: OutboxEventTypes.OrderShippedCustomerEmail,
                payloadJson: JsonSerializer.Serialize(payload));

            // Step 8: No SaveChangesAsync — UoW pipeline commits atomically.
            return BusinessResult.Success(new Response(order.Id));
        }
    }
}
