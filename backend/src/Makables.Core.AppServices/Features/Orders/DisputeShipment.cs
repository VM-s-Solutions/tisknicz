using System.Text.Json;
using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.AppServices.Common;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Outbox;
using Makables.Core.Domain.Shipping;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Makables.Core.AppServices.Features.Orders;

/// <summary>
/// <b>T-0078 STUB.</b> Flags an order for dispute based on a
/// carrier-sourced terminal state (Packeta Returned / Failed).
/// Emits a single <see cref="OutboxEventTypes.OrderDisputedCarrierSourced"/>
/// outbox event + logs Warning. <b>NO Order state mutation at MVP.</b>
///
/// <para>
/// T-0078 owns DETECTING the dispute-worthy carrier statuses; T-0106
/// will wire what HAPPENS next — the real <c>OrderState.Disputed</c>
/// transition + customer "your shipment is disputed" email + admin
/// "dispute opened" email. The stub gives ops day-one visibility into
/// dispute volume + character via the outbox row + the Warning log;
/// T-0106's dispute domain design isn't locked yet so we deliberately
/// leave Order.State untouched until then.
/// </para>
///
/// <para>
/// <b>Atomicity.</b> The single outbox row commits under the UoW
/// pipeline per ADR 0014. The handler NEVER calls
/// <c>SaveChangesAsync</c>. Repeat invocations against the same
/// (OrderId, Reason) emit a second outbox row — intentional MVP
/// behaviour so visible duplicates signal that the dispute keeps
/// re-firing across sweeps; T-0106 will resolve this via the
/// state-graph transition (a Disputed order short-circuits in the
/// T-0078 Function's Shipped predicate).
/// </para>
/// </summary>
public static class DisputeShipment
{
    /// <summary>
    /// Flag an order for dispute by id + reason.
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
        IOutbox outbox,
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

            // Step 2: Warning log with structured context — ops triage
            // surface during the T-0078-to-T-0106 window.
            logger.LogWarning(
                "DisputeShipment STUB: order {OrderId} flagged for dispute " +
                "(reason: {Reason}, source: carrier-sourced, carrierRef: {CarrierRef}). " +
                "T-0106 will wire the real Disputed state transition + customer + admin email.",
                order.Id, command.Reason, order.ShippingCarrierRef);

            // Step 3: Map the dispute reason to the carrier state that
            // produced it. Audit-friendly so T-0106 admin processing can
            // distinguish Returned vs Failed even after DisputeReason
            // semantics evolve.
            var carrierState = command.Reason switch
            {
                DisputeReason.CarrierReturned => ShipmentState.Returned,
                DisputeReason.CarrierFailed => ShipmentState.Failed,
                _ => ShipmentState.Failed,  // defensive default — future Reasons land in T-0106
            };

            // Step 4: Enqueue the dispute outbox event. NOT email-routed
            // at MVP (intentionally absent from IsEmailSend) — the
            // OutboxDispatcher's "unrouted" branch logs it visibly until
            // T-0106 wires the consumer Function.
            var payload = new OrderDisputedCarrierSourcedPayload(
                OrderId: order.Id,
                Reason: command.Reason,
                CarrierState: carrierState);
            outbox.Enqueue(
                aggregateId: order.Id,
                eventType: OutboxEventTypes.OrderDisputedCarrierSourced,
                payloadJson: JsonSerializer.Serialize(payload));

            // Step 5: NO Order state mutation at MVP. Order stays in
            // Shipped. T-0106 will own the Disputed transition once the
            // state-graph design is locked.
            return BusinessResult.Success(new DisputeShipmentResponse(order.Id, command.Reason));
        }
    }
}
