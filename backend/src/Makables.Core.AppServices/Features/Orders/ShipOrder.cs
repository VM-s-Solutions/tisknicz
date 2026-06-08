using System.Text.Json;
using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.AppServices.Common;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Outbox;
using Makables.Core.Domain.Shipping;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Makables.Core.AppServices.Features.Orders;

/// <summary>
/// Zásilkovna ShipOrder command — first writer of the shipping seam.
/// T-0072 (US-maker-0007).
///
/// <para>
/// Maker presses <b>"Odeslat"</b> on an Accepted Zásilkovna order; the
/// handler calls the Packeta adapter to create the shipment, stamps the
/// carrier ref + tracking URL onto the Order row, transitions
/// <see cref="OrderState.Accepted"/> → <see cref="OrderState.Shipped"/>,
/// and atomically enqueues 2 outbox events:
/// <list type="bullet">
///   <item><see cref="OutboxEventTypes.OrderShippedCustomerEmail"/> — customer notification with tracking URL.</item>
///   <item><see cref="OutboxEventTypes.ShippingGenerateLabel"/> — T-0074 GenerateLabelFunction picks it up.</item>
/// </list>
/// Both events ride on a single UoW transaction per ADR 0014 per locked
/// decision A.1.
/// </para>
///
/// <para>
/// <b>PersonalPickup orders</b> route to <see cref="HandOverOrder"/>
/// instead. The eligibility assertion here ensures wrong-endpoint calls
/// surface as <see cref="BusinessErrorMessage.ShippingMethodNotEligible"/>.
/// </para>
/// </summary>
public static class ShipOrder
{
    public sealed record Command(string OrderId) : ICommand<Response>;

    public sealed record Response(string OrderId, string CarrierRef, string TrackingUrl);

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
        IShippingCarrierFactory shippingCarrierFactory,
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

            // Step 3: Owner-scoped order load (IDOR shield per ADR 0013).
            var order = await orders.GetByIdForMakerAsync(
                command.OrderId, maker.Id, cancellationToken);
            if (order is null)
            {
                return BusinessResult.Failure<Response>(
                    Error.NotFound("orderId", BusinessErrorMessage.OrderNotFound));
            }

            // Step 4: Assert shipping method eligibility. Zásilkovna only;
            // PersonalPickup orders route to HandOverOrder.
            if (order.ShippingMethod != ShippingMethod.ZasilkovnaPickupPoint)
            {
                return BusinessResult.Failure<Response>(
                    Error.Validation("shippingMethod", BusinessErrorMessage.ShippingMethodNotEligible));
            }

            // Step 5: Resolve carrier via factory (keyed-services pattern).
            var carrierResult = await shippingCarrierFactory.ResolveAsync(order.CountryCode, cancellationToken);
            if (!carrierResult.IsSuccess)
            {
                return BusinessResult.Failure<Response>(carrierResult.Error!);
            }

            // Step 6: Create the shipment at Packeta. Carrier-side error
            // classification propagates verbatim (Transient → maker retry,
            // Permanent → admin investigation, Configuration → ops alert).
            var shipmentResult = await carrierResult.Value!.CreateShipmentAsync(order, cancellationToken);
            if (!shipmentResult.IsSuccess)
            {
                return BusinessResult.Failure<Response>(shipmentResult.Error!);
            }
            var shipment = shipmentResult.Value!;

            // Step 7: State transition via the aggregate. Order.Ship sets
            // ShippingCarrierRef + ShippingCarrierTrackingUrl + AutoDeliverAt
            // under the set-once invariants.
            var transitionResult = order.Ship(
                clock,
                shipment.CarrierRef,
                autoDeliverWindowDays: 7,
                trackingUrl: shipment.TrackingUrl);
            if (!transitionResult.IsSuccess)
            {
                return BusinessResult.Failure<Response>(transitionResult.Error!);
            }

            // Step 8: Resolve customer language + build customer payload.
            var customer = await users.GetByIdAsync(order.CustomerUserId, cancellationToken);
            if (customer is null)
            {
                logger.LogCritical(
                    "ShipOrder: customer user {UserId} not found for order {OrderId}. " +
                    "FK invariant violation — refusing to commit.",
                    order.CustomerUserId, order.Id);
                return BusinessResult.Failure<Response>(
                    Error.NotFound("customerUserId", BusinessErrorMessage.OrderCustomerUserMissing));
            }
            var customerLanguage = await languageResolver.ResolveForUserAsync(customer, cancellationToken);
            var urls = publicAppUrls.Value;

            // Step 9: Enqueue the customer shipped-email event.
            var customerPayload = new OrderShippedCustomerEmailPayload(
                OrderId: order.Id,
                OrderNumber: order.OrderNumber,
                Email: order.ContactEmail,
                ContactName: order.ContactName,
                LanguageCode: customerLanguage,
                ActionUrl: $"{urls.WebBaseUrl.TrimEnd('/')}/objednavka/{order.Id}",
                TrackingUrl: shipment.TrackingUrl);
            outbox.Enqueue(
                aggregateId: order.Id,
                eventType: OutboxEventTypes.OrderShippedCustomerEmail,
                payloadJson: JsonSerializer.Serialize(customerPayload));

            // Step 10: Enqueue the generate-label event. T-0074's Function
            // picks it up off the generate-label queue and stores the PDF
            // in blob storage at invoices/{cc}/orders/{orderId}/label.pdf.
            var labelPayload = new GenerateLabelOutboxPayload(order.Id);
            outbox.Enqueue(
                aggregateId: order.Id,
                eventType: OutboxEventTypes.ShippingGenerateLabel,
                payloadJson: JsonSerializer.Serialize(labelPayload));

            // Step 11: No SaveChangesAsync — UoW pipeline commits the
            // Order mutation + both outbox rows atomically per ADR 0014.
            return BusinessResult.Success(new Response(
                OrderId: order.Id,
                CarrierRef: shipment.CarrierRef,
                TrackingUrl: shipment.TrackingUrl));
        }
    }
}
