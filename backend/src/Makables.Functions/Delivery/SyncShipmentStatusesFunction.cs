using Makables.Core.AppServices.Features.Orders;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Shipping;
using MediatR;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Makables.Functions.Delivery;

/// <summary>
/// Timer-triggered Function that polls Packeta every 6h for all
/// <c>Shipped + ZasilkovnaPickupPoint + ShippingCarrierRef</c>
/// orders, then dispatches a MediatR Command per the carrier's
/// reported state:
/// <list type="bullet">
///   <item><description><see cref="ShipmentState.Delivered"/> →
///     <c>MarkOrderDelivered.Command(orderId, Carrier, DeliveredAt)</c>
///     with the carrier's authoritative timestamp.</description></item>
///   <item><description><see cref="ShipmentState.Returned"/> →
///     <c>DisputeShipment.Command(orderId, CarrierReturned)</c>.</description></item>
///   <item><description><see cref="ShipmentState.Failed"/> →
///     <c>DisputeShipment.Command(orderId, CarrierFailed)</c>.</description></item>
///   <item><description><see cref="ShipmentState.Created"/> /
///     <see cref="ShipmentState.InTransit"/> → no-op (Debug log).</description></item>
/// </list>
/// T-0078 (US-customer-0013).
///
/// <para>
/// Per ADR 0020: thin MediatR-dispatch wrapper. Polly resilience on
/// the Packeta side is already wired at T-0070. Per-row fail-continue
/// so one carrier hiccup does NOT stall the rest of the sweep.
/// Structured end-of-sweep summary log. <b>Schedule:</b> every 6h
/// starting 00:00 UTC.
/// </para>
///
/// <para>
/// The already-Delivered race (customer or T-0077 hit first) is
/// handled by T-0076's Silent Success contract — the
/// <see cref="MarkOrderDelivered"/> Command returns Success no-op
/// without re-emitting the email event or overwriting the first
/// writer's <see cref="OrderDeliverySource"/>.
/// </para>
/// </summary>
public sealed class SyncShipmentStatusesFunction(
    IOrderRepository orderRepository,
    IShippingCarrierFactory carrierFactory,
    ISender mediator,
    ILogger<SyncShipmentStatusesFunction> logger)
{
    public const string FunctionName = "SyncShipmentStatuses";

    [Function(FunctionName)]
    public async Task RunAsync(
        [TimerTrigger("%SyncShipmentStatuses:Schedule%")] TimerInfo timer,
        CancellationToken cancellationToken)
    {
        var synced = 0;
        var delivered = 0;
        var disputed = 0;
        var failed = 0;

        await foreach (var order in orderRepository
            .GetCarrierSyncableUnscopedReadOnlyAsync(cancellationToken)
            .WithCancellation(cancellationToken))
        {
            synced++;
            try
            {
                var carrierResult = await carrierFactory.ResolveAsync(order.CountryCode, cancellationToken);
                if (!carrierResult.IsSuccess)
                {
                    logger.LogCritical(
                        "SyncShipmentStatuses: carrier resolve failed for order {OrderId} (cc={CountryCode}): {Code}",
                        order.Id, order.CountryCode, carrierResult.Error!.Code);
                    failed++;
                    continue;
                }

                var statusResult = await carrierResult.Value!.GetStatusAsync(
                    order.ShippingCarrierRef!, cancellationToken);
                if (!statusResult.IsSuccess)
                {
                    logger.LogWarning(
                        "SyncShipmentStatuses: GetStatusAsync failed for order {OrderId} " +
                        "(carrierRef={CarrierRef}): {Code} — retrying next sweep",
                        order.Id, order.ShippingCarrierRef, statusResult.Error!.Code);
                    failed++;
                    continue;
                }

                var status = statusResult.Value!;
                switch (status.State)
                {
                    case ShipmentState.Delivered:
                        if (status.DeliveredAt is null)
                        {
                            logger.LogWarning(
                                "SyncShipmentStatuses: Packeta reported Delivered without timestamp " +
                                "for order {OrderId} — using clock.UtcNow fallback",
                                order.Id);
                        }
                        var deliverResult = await mediator.Send(
                            new MarkOrderDelivered.Command(
                                order.Id, OrderDeliverySource.Carrier, status.DeliveredAt),
                            cancellationToken);
                        if (deliverResult.IsSuccess)
                        {
                            delivered++;
                        }
                        else
                        {
                            failed++;
                            logger.LogWarning(
                                "SyncShipmentStatuses: MarkOrderDelivered failed for order {OrderId}: {Code}",
                                order.Id, deliverResult.Error!.Code);
                        }
                        break;

                    case ShipmentState.Returned:
                        var returnResult = await mediator.Send(
                            new DisputeShipment.Command(order.Id, DisputeReason.CarrierReturned),
                            cancellationToken);
                        if (returnResult.IsSuccess)
                        {
                            disputed++;
                        }
                        else
                        {
                            failed++;
                            logger.LogWarning(
                                "SyncShipmentStatuses: DisputeShipment(Returned) failed for order {OrderId}: {Code}",
                                order.Id, returnResult.Error!.Code);
                        }
                        break;

                    case ShipmentState.Failed:
                        var failResult = await mediator.Send(
                            new DisputeShipment.Command(order.Id, DisputeReason.CarrierFailed),
                            cancellationToken);
                        if (failResult.IsSuccess)
                        {
                            disputed++;
                        }
                        else
                        {
                            failed++;
                            logger.LogWarning(
                                "SyncShipmentStatuses: DisputeShipment(Failed) failed for order {OrderId}: {Code}",
                                order.Id, failResult.Error!.Code);
                        }
                        break;

                    case ShipmentState.Created:
                    case ShipmentState.InTransit:
                        logger.LogDebug(
                            "SyncShipmentStatuses: order {OrderId} still in transit (state={State})",
                            order.Id, status.State);
                        break;

                    default:
                        // Defensive: a future Packeta state addition not
                        // yet mapped onto ShipmentState should NOT
                        // dispatch any Command — let the next ticket
                        // extend the switch explicitly.
                        logger.LogWarning(
                            "SyncShipmentStatuses: unknown ShipmentState for order {OrderId}: {State}",
                            order.Id, status.State);
                        break;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Per-row fail-continue. One bad order MUST NOT stall the
                // rest of the sweep. The row stays Shipped and gets
                // retried on the next 6h tick automatically.
                failed++;
                logger.LogError(ex,
                    "SyncShipmentStatuses: unexpected error processing order {OrderId}",
                    order.Id);
            }
        }

        logger.LogInformation(
            "SyncShipmentStatuses completed: synced {Synced}, delivered {Delivered}, disputed {Disputed}, failed {Failed}",
            synced, delivered, disputed, failed);
    }
}
