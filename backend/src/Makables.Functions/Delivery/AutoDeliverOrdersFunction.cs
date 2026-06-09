using Makables.Core.AppServices.Features.Orders;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Orders;
using MediatR;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Makables.Functions.Delivery;

/// <summary>
/// Timer-triggered Function that auto-transitions Shipped orders to
/// Delivered once <see cref="Order.AutoDeliverAt"/> has elapsed without
/// an explicit customer confirmation or carrier-status sync arriving
/// first. T-0077 (US-customer-0013).
///
/// <para>
/// Per ADR 0020: thin MediatR-dispatch wrapper. No business logic in
/// the Function — the writer (T-0076 <see cref="MarkOrderDelivered"/>)
/// owns the state transition, the Silent Success contract handles the
/// already-Delivered race, and the per-Order failure-isolation pattern
/// (fail-continue + per-row Warning log) keeps one bad row from
/// stalling the whole nightly run.
/// </para>
///
/// <para>
/// <b>Schedule:</b> daily 08:00 UTC (= 09:00 CET / 10:00 CEST). Morning
/// landing aligns with working hours so the customer notification lands
/// in their inbox at a respectful time, not overnight. T-0077 §C.
/// </para>
///
/// <para>
/// <b>Stateless re-fetch on partial-run failure</b> per T-0029
/// precedent: the predicate IS the claim. Rows that succeed transition
/// out of the <c>State == Shipped</c> filter and no longer appear;
/// rows that failed are still <c>Shipped + AutoDeliverAt-expired</c>
/// and get retried automatically on the next sweep.
/// </para>
/// </summary>
public sealed class AutoDeliverOrdersFunction(
    IOrderRepository orderRepository,
    ISender mediator,
    IClock clock,
    ILogger<AutoDeliverOrdersFunction> logger)
{
    public const string FunctionName = "AutoDeliverOrders";

    [Function(FunctionName)]
    public async Task RunAsync(
        [TimerTrigger("%AutoDeliverOrders:Schedule%")] TimerInfo timer,
        CancellationToken cancellationToken)
    {
        var asOf = clock.UtcNow;
        var claimed = 0;
        var dispatched = 0;
        var failed = 0;

        // Materialize the streaming projection BEFORE the per-row mediator.Send
        // loop. Q-0008 + Gate 8 BLOCKER fold: Npgsql does NOT support MARS
        // (Multiple Active Result Sets). The downstream MarkOrderDelivered.Handler
        // re-uses the same scoped DbContext to load + mutate the tracked Order.
        // If we kept this as `await foreach`, the open NpgsqlDataReader from the
        // sweep would race the handler's connection use and throw
        // NpgsqlOperationInProgressException. At MVP volume (~10-200 rows/run,
        // daily cadence) the eager-list cost is sub-millisecond + sub-MB. If
        // production volume grows, Q-0008 is the architect-led posture (per-row
        // IServiceScope or materialised IReadOnlyList contract).
        var orderIds = await orderRepository
            .GetAutoDeliverableUnscopedReadOnlyAsync(asOf, cancellationToken)
            .ToListAsync(cancellationToken);

        foreach (var orderId in orderIds)
        {
            claimed++;
            try
            {
                var result = await mediator.Send(
                    new MarkOrderDelivered.Command(orderId, OrderDeliverySource.Auto),
                    cancellationToken);
                if (result.IsSuccess)
                {
                    // T-0076 Silent Success contract: an already-Delivered
                    // race (customer or T-0078 carrier-sync hit first) still
                    // returns Success here; we count it as dispatched.
                    dispatched++;
                }
                else
                {
                    failed++;
                    logger.LogWarning(
                        "AutoDeliverOrders: MarkOrderDelivered failed for order {OrderId}: {Code}",
                        orderId, result.Error!.Code);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Fail-continue per row. One bad order MUST NOT stall the
                // rest of tonight's expirations. The failed row stays in
                // the predicate and gets retried on the next sweep.
                failed++;
                logger.LogWarning(ex,
                    "AutoDeliverOrders: unexpected exception for order {OrderId}",
                    orderId);
            }
        }

        logger.LogInformation(
            "AutoDeliverOrders completed: claimed {Claimed} orders, dispatched {Dispatched}, failed {Failed}",
            claimed, dispatched, failed);
    }
}
