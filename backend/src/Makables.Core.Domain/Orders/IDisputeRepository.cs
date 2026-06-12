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
}
