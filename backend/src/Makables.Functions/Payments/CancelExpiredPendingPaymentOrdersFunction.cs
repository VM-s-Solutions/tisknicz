using Makables.Core.AppServices.Features.Orders;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Orders;
using MediatR;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Makables.Functions.Payments;

/// <summary>
/// Timer-triggered Function that auto-cancels orders stuck in
/// <see cref="OrderState.PendingPayment"/> past the 24h Comgate retry
/// window (US-customer-0010 AC-3). T-0083.
///
/// <para>
/// Per ADR 0020: thin MediatR-dispatch wrapper. No business logic in
/// the Function — the writer
/// (<see cref="CancelExpiredOrder"/>) owns the state transition, the
/// Silent Success contract handles the customer-pays-mid-flight race,
/// and the per-Order failure-isolation pattern (fail-continue + per-row
/// Warning log) keeps one bad row from stalling the whole sweep.
/// Mirrors <see cref="Delivery.AutoDeliverOrdersFunction"/> verbatim.
/// </para>
///
/// <para>
/// <b>Schedule:</b> daily 02:00 UTC (= 03:00 CET / 04:00 CEST). Off-peak;
/// deliberately offset from T-0077's 08:00 UTC AutoDeliver so the two
/// cleanup jobs spread load across the day. T-0083 §A.4.
/// </para>
///
/// <para>
/// <b>Stateless re-fetch on partial-run failure</b> per T-0029 / T-0077
/// precedent: the predicate IS the claim. Rows that succeed transition
/// out of the <c>State == PendingPayment</c> filter and no longer appear;
/// rows that failed are still PendingPayment + expired and get retried
/// on the next sweep.
/// </para>
/// </summary>
public sealed class CancelExpiredPendingPaymentOrdersFunction(
    IOrderRepository orderRepository,
    ISender mediator,
    IClock clock,
    ILogger<CancelExpiredPendingPaymentOrdersFunction> logger)
{
    public const string FunctionName = "CancelExpiredPendingPaymentOrders";

    [Function(FunctionName)]
    public async Task RunAsync(
        [TimerTrigger("%CancelExpiredPendingPaymentOrders:Schedule%")] TimerInfo timer,
        CancellationToken cancellationToken)
    {
        var asOf = clock.UtcNow;
        var claimed = 0;
        var dispatched = 0;
        var failed = 0;

        // Materialize the streaming projection BEFORE the per-row mediator.Send
        // loop. Q-0008 + Gate 8 BLOCKER fold: Npgsql does NOT support MARS
        // (Multiple Active Result Sets). The downstream CancelExpiredOrder.Handler
        // re-uses the same scoped DbContext to load + mutate the tracked Order.
        // If we kept this as `await foreach`, the open NpgsqlDataReader from the
        // sweep would race the handler's connection use and throw
        // NpgsqlOperationInProgressException. At MVP volume (~10-200 rows/run,
        // daily cadence) the eager-list cost is sub-millisecond + sub-MB.
        var orderIds = await orderRepository
            .GetExpiredPendingPaymentUnscopedReadOnlyAsync(asOf, cancellationToken)
            .ToListAsync(cancellationToken);

        foreach (var orderId in orderIds)
        {
            claimed++;
            try
            {
                var result = await mediator.Send(
                    new CancelExpiredOrder.Command(orderId),
                    cancellationToken);
                if (result.IsSuccess)
                {
                    // Silent Success contract: an already-Cancelled race
                    // (T-0105 customer-cancel or T-0107 admin-cancel
                    // landed first) still returns Success here; we count
                    // it as dispatched.
                    dispatched++;
                }
                else
                {
                    failed++;
                    logger.LogWarning(
                        "CancelExpiredPendingPaymentOrders: CancelExpiredOrder failed for order {OrderId}: {Code}",
                        orderId, result.Error!.Code);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Fail-continue per row. One bad order MUST NOT stall the
                // rest of tonight's expirations.
                failed++;
                logger.LogWarning(ex,
                    "CancelExpiredPendingPaymentOrders: unexpected exception for order {OrderId}",
                    orderId);
            }
        }

        logger.LogInformation(
            "CancelExpiredPendingPaymentOrders completed: claimed {Claimed} orders, dispatched {Dispatched}, failed {Failed}",
            claimed, dispatched, failed);
    }
}
