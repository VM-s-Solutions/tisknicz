using Makables.Core.AppServices.Features.Orders;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Orders;
using MediatR;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Makables.Functions.Disputes;

/// <summary>
/// Timer-triggered Function that fires the T-0145 7-day maker-response
/// auto-escalation: for every OPEN, customer-sourced
/// <see cref="Dispute"/> past <see cref="Dispute.ResponseWindowDays"/>
/// with no maker reply since, dispatches <see cref="EscalateDispute.Command"/>
/// to enqueue the <c>dispute.autoEscalated.adminEmail</c> notification
/// exactly once. This sweep NEVER resolves the dispute or sanctions the
/// maker (Alternatives Considered Option B) — notification only.
///
/// <para>
/// Mirrors <see cref="Delivery.AutoDeliverOrdersFunction"/> (T-0077)
/// verbatim per ADR 0020: thin MediatR-dispatch wrapper, no business
/// logic in the Function — <see cref="EscalateDispute"/>'s handler owns
/// every guard (resolved / already-escalated / maker-replied), and the
/// per-dispute failure-isolation pattern (fail-continue + per-row
/// Warning log) keeps one bad row from stalling the whole run.
/// </para>
///
/// <para>
/// <b>Schedule:</b> daily 09:00 UTC — offset from T-0077's 08:00 UTC
/// AutoDeliver and T-0083's 02:00 UTC auto-cancel so the daily sweeps
/// spread load across the morning.
/// </para>
///
/// <para>
/// <b>Stateless re-fetch on partial-run failure</b> per T-0077/T-0083
/// precedent: the predicate IS the claim. Escalated rows stamp
/// <see cref="Dispute.AutoEscalatedAt"/> and drop out of the
/// <c>AutoEscalatedAt IS NULL</c> filter; rows that failed are still
/// candidates and retry automatically on the next sweep.
/// </para>
/// </summary>
public sealed class DisputeAutoEscalationFunction(
    IDisputeRepository disputeRepository,
    ISender mediator,
    IClock clock,
    ILogger<DisputeAutoEscalationFunction> logger)
{
    public const string FunctionName = "DisputeAutoEscalation";

    [Function(FunctionName)]
    public async Task RunAsync(
        [TimerTrigger("%DisputeAutoEscalation:Schedule%")] TimerInfo timer,
        CancellationToken cancellationToken)
    {
        var asOf = clock.UtcNow;
        var claimed = 0;
        var dispatched = 0;
        var failed = 0;

        // Materialize the streaming projection BEFORE the per-row mediator.Send
        // loop — same Npgsql-MARS constraint as T-0077/T-0083: the downstream
        // EscalateDispute.Handler re-uses the same scoped DbContext to load +
        // mutate the tracked Dispute. At MVP volume the eager-list cost is
        // negligible.
        var disputeIds = await disputeRepository
            .GetAutoEscalationCandidateIdsUnscopedReadOnlyAsync(asOf, cancellationToken)
            .ToListAsync(cancellationToken);

        foreach (var disputeId in disputeIds)
        {
            claimed++;
            try
            {
                var result = await mediator.Send(
                    new EscalateDispute.Command(disputeId),
                    cancellationToken);
                if (result.IsSuccess)
                {
                    // Every guard (resolved / already-escalated / maker-
                    // replied / missing row) resolves to a Silent Success
                    // no-op inside the handler — counted as dispatched.
                    dispatched++;
                }
                else
                {
                    failed++;
                    logger.LogWarning(
                        "DisputeAutoEscalation: EscalateDispute failed for dispute {DisputeId}: {Code}",
                        disputeId, result.Error!.Code);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Fail-continue per row. One bad dispute MUST NOT stall the
                // rest of today's escalations. The failed row stays in the
                // predicate and gets retried on the next sweep.
                failed++;
                logger.LogWarning(ex,
                    "DisputeAutoEscalation: unexpected exception for dispute {DisputeId}",
                    disputeId);
            }
        }

        logger.LogInformation(
            "DisputeAutoEscalation completed: claimed {Claimed} disputes, dispatched {Dispatched}, failed {Failed}",
            claimed, dispatched, failed);
    }
}
