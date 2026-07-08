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
    /// T-0146. Load a dispute by id without ownership scoping. <b>Admin
    /// host only</b> per ADR 0013. Tracked — <c>GenerateReturnLabel</c> /
    /// admin <c>MarkReturnReceived</c> mutate the returned entity.
    /// </summary>
    Task<Dispute?> GetByIdUnscopedAsync(string disputeId, CancellationToken cancellationToken);

    /// <summary>
    /// T-0146. Read-only unscoped variant for the Function context
    /// (<c>FetchAndStoreReturnLabel</c>, no caller principal). Mirrors
    /// <c>IOrderRepository.GetByIdUnscopedReadOnlyAsync</c>.
    /// </summary>
    Task<Dispute?> GetByIdUnscopedReadOnlyAsync(string disputeId, CancellationToken cancellationToken);

    /// <summary>
    /// T-0146. Load a dispute owned (via its parent order) by
    /// <paramref name="customerUserId"/>, read-only. Returns <c>null</c>
    /// for unknown ids OR ids belonging to another customer's order —
    /// same IDOR-leak-resistant shape as
    /// <c>IOrderRepository.GetByIdForCustomerReadOnlyAsync</c> (AC-7).
    /// Backs the customer-host return-label download endpoint.
    /// </summary>
    Task<Dispute?> GetByIdForCustomerReadOnlyAsync(
        string disputeId, string customerUserId, CancellationToken cancellationToken);

    /// <summary>
    /// T-0146. Load a dispute owned (via its parent order) by
    /// <paramref name="makerId"/>. Returns <c>null</c> for unknown ids OR
    /// ids belonging to another maker's order (same IDOR shield). Tracked
    /// — the maker's <c>MarkReturnReceived</c> command mutates. Backs
    /// AC-7's maker-side symmetric 404.
    /// </summary>
    Task<Dispute?> GetByIdForMakerAsync(
        string disputeId, string makerId, CancellationToken cancellationToken);
}
