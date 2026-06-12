using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.AppServices.Common;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Outbox;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Makables.Core.AppServices.Features.Orders;

/// <summary>
/// Carrier-sourced dispute open (T-0078 detection → T-0106 real flow).
/// Dispatched by <c>SyncShipmentStatusesFunction</c> when Packeta reports
/// a <c>Returned</c> / <c>Failed</c> terminal state. The T-0078 stub
/// behaviour (log + unrouted outbox event, NO state mutation) is REWIRED
/// here to the real dispute domain: Shipped → Disputed (stamping
/// <c>PreDisputeState</c>), a <see cref="Dispute"/> row with
/// <see cref="DisputeSource.Carrier"/> and the §C.5 category mapping,
/// and the admin-notification outbox event.
///
/// <para>
/// The <see cref="Command"/> / <see cref="DisputeShipmentResponse"/>
/// shapes are KEPT verbatim (§C.5) so the Function call sites and its
/// unit tests stay untouched — <see cref="DisputeReason"/> remains the
/// two-value carrier WIRE enum; <see cref="DisputeCategory"/> is the
/// domain axis.
/// </para>
///
/// <para>
/// <b>Idempotency:</b> a re-fire against the now-Disputed order is
/// Silent-Success with no new rows and no second outbox emission — this
/// kills the stub's intentional repeat-emission across sweeps (the
/// state flip also drops the order out of the carrier sweep's
/// <c>State == Shipped</c> predicate by definition, AC-11).
/// </para>
/// </summary>
public static class DisputeShipment
{
    /// <summary>
    /// Flag an order for dispute by id + carrier-wire reason.
    /// </summary>
    public sealed record Command(string OrderId, DisputeReason Reason)
        : ICommand<DisputeShipmentResponse>;

    /// <summary>
    /// <b>Globally-unique name</b> (not nested <c>Response</c>) per the
    /// T-0070-T-0075 CI convention — avoids the NSwag TS class collision.
    /// </summary>
    public sealed record DisputeShipmentResponse(string OrderId, DisputeReason Reason);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.OrderId)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(40).WithErrorCode(BusinessErrorMessage.MaxLength);

            RuleFor(c => c.Reason)
                .IsInEnum().WithErrorCode(BusinessErrorMessage.Required);
        }
    }

    public sealed class Handler(
        IOrderRepository orders,
        IDisputeRepository disputes,
        IOutbox outbox,
        IClock clock,
        IIdGenerator ids,
        ILanguageResolver languageResolver,
        IOptions<PublicAppUrlsOptions> publicAppUrls,
        ILogger<Handler> logger) : IRequestHandler<Command, BusinessResult<DisputeShipmentResponse>>
    {
        public async Task<BusinessResult<DisputeShipmentResponse>> Handle(
            Command command, CancellationToken cancellationToken)
        {
            // Step 1: Load the order. Function context (T-0078) has no
            // user identity → unscoped lookup is the correct shape.
            var order = await orders.GetByIdUnscopedAsync(command.OrderId, cancellationToken);
            if (order is null)
            {
                return BusinessResult.Failure<DisputeShipmentResponse>(
                    Error.NotFound("orderId", BusinessErrorMessage.OrderNotFound));
            }

            // Step 2: Silent-Success on a re-fire against the now-Disputed
            // order (§C.4) — no new rows, no second outbox emission.
            if (order.State == OrderState.Disputed)
            {
                logger.LogInformation(
                    "DisputeShipment: order {OrderId} already Disputed; idempotent skip (reason: {Reason}).",
                    order.Id, command.Reason);
                return BusinessResult.Success(new DisputeShipmentResponse(order.Id, command.Reason));
            }

            // Step 3: Parenthesis-state flip — stamps PreDisputeState
            // (Shipped on the carrier path).
            var transition = order.OpenDispute(clock);
            if (!transition.IsSuccess)
            {
                return BusinessResult.Failure<DisputeShipmentResponse>(transition.Error!);
            }

            // Step 4: Ops triage surface — kept from the T-0078 stub.
            logger.LogWarning(
                "DisputeShipment: order {OrderId} disputed from carrier-sourced terminal state " +
                "(reason: {Reason}, carrierRef: {CarrierRef}).",
                order.Id, command.Reason, order.ShippingCarrierRef);

            // Step 5: Dispute row — §C.5 wire-enum → domain-enum mapping;
            // canned description carries the carrier ref as the evidence
            // pointer (admin follows up in Packeta's console).
            var category = command.Reason switch
            {
                DisputeReason.CarrierReturned => DisputeCategory.CarrierReturned,
                DisputeReason.CarrierFailed => DisputeCategory.CarrierFailed,
                _ => DisputeCategory.CarrierFailed, // defensive default for future wire values
            };
            var dispute = Dispute.Open(
                id: ids.Next(),
                orderId: order.Id,
                category: category,
                description:
                    $"Carrier-reported terminal shipment state: {command.Reason}. " +
                    $"Carrier ref: {order.ShippingCarrierRef ?? "(none)"}.",
                source: DisputeSource.Carrier,
                countryCode: order.CountryCode);
            await disputes.AddAsync(dispute, cancellationToken);

            // Step 6: Admin notification — shared payload builder so the
            // shape cannot drift between sources.
            await OpenCustomerDispute.Handler.EnqueueAdminEmailAsync(
                outbox, languageResolver, publicAppUrls.Value, order, dispute, cancellationToken);

            return BusinessResult.Success(new DisputeShipmentResponse(order.Id, command.Reason));
        }
    }
}
