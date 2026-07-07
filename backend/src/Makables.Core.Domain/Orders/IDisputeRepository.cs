namespace Makables.Core.Domain.Orders;

/// <summary>
/// Persistence access for <see cref="Dispute"/>. Minimal surface at MVP
/// (T-0106): the write path adds rows; the only read is the open-dispute
/// lookup that backs the Silent-Success re-open contract (§C.4) and the
/// resolve handler. List/read models land with T-0118's admin UI.
///
/// <para>
/// No <c>UpdateAsync</c> — EF Core change-tracking on the returned
/// aggregate handles mutation (<see cref="Dispute.Resolve"/>) and the
/// <c>UnitOfWorkPipelineBehavior</c> commits. No <c>DeleteAsync</c> —
/// disputes are an adjudication record; soft delete only via
/// <see cref="Common.Auditable.MarkDeactivated"/> if ever needed.
/// </para>
/// </summary>
public interface IDisputeRepository
{
    /// <summary>Track <paramref name="dispute"/> as a pending insert.</summary>
    Task AddAsync(Dispute dispute, CancellationToken cancellationToken);

    /// <summary>
    /// Load the order's OPEN dispute (<c>ResolvedAt == null</c>), or null
    /// when none is open. At most one row exists per the partial unique
    /// index <c>UNIQUE (order_id) WHERE resolved_at IS NULL</c>. Tracked —
    /// the resolve handler mutates the returned entity.
    /// </summary>
    Task<Dispute?> GetOpenByOrderIdAsync(string orderId, CancellationToken cancellationToken);

    /// <summary>
    /// Load a single dispute by id, unscoped. Tracked — the T-0145
    /// <c>EscalateDispute.Handler</c> (Function-dispatched, no caller
    /// principal) mutates the returned entity via
    /// <see cref="Dispute.TryMarkAutoEscalated"/>.
    /// </summary>
    Task<Dispute?> GetByIdUnscopedAsync(string disputeId, CancellationToken cancellationToken);

    /// <summary>
    /// Projection-only stream of <see cref="Dispute.Id"/> values past the
    /// T-0145 7-day maker-response window: <c>ResolvedAt IS NULL AND
    /// Source == Customer AND AutoEscalatedAt IS NULL AND CreatedAt &lt;
    /// asOf - 7 days</c>. Unscoped + read-only (<c>AsNoTracking</c>) — the
    /// daily sweep Function has no user identity and only needs the id to
    /// dispatch <c>EscalateDispute.Command</c> per row, which re-checks
    /// the maker-reply-since guard (Technical notes) and the idempotency
    /// flag against a freshly tracked read. Mirrors
    /// <see cref="IOrderRepository.GetAutoDeliverableUnscopedReadOnlyAsync"/>
    /// (T-0077) — the predicate IS the claim, so a partial-run failure
    /// simply retries next sweep.
    /// </summary>
    IAsyncEnumerable<string> GetAutoEscalationCandidateIdsUnscopedReadOnlyAsync(
        DateTimeOffset asOf,
        CancellationToken cancellationToken);
}
