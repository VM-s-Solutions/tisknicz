using Makables.Core.Domain.Common;
using Makables.Core.Domain.Payouts.Queries;

namespace Makables.Core.Domain.Payouts;

/// <summary>
/// Read-side projection seam for the maker money surface (ADR 0023, T-0112).
/// A dedicated interface (not an extension of <c>IOrderQueries</c>) because the
/// list/detail reads are <see cref="PayoutBatch"/>-shaped, not Order-shaped;
/// the outbox-events method co-locates here because it shares the same
/// maker-money read bundle + <c>makerId</c>-scoping discipline.
///
/// <para>
/// Every method is owner-scoped at the SQL level — the implementation bakes
/// the maker IDOR predicate into the EF <c>Where</c>. Callers MUST resolve
/// <c>makerId</c> from the authenticated principal
/// (<see cref="IUserSessionProvider"/> + <c>IMakerRepository.GetByUserIdAsync</c>);
/// the queries trust the id they're handed. Cross-tenant probes surface as an
/// empty page (list / events) or <c>null</c> (detail) — never as an oracle
/// that distinguishes "doesn't exist" from "not yours".
/// </para>
///
/// <para>
/// Projections are <c>AsNoTracking</c> + <c>IgnoreAutoIncludes</c> and
/// materialize directly into DTOs. The per-maker batch total is the SUM of
/// THIS maker's <c>Order.MakerPayoutAmountMinor</c> — never the cross-maker
/// <see cref="PayoutBatch.TotalAmountMinor"/>. The operator CSV is admin-only
/// and is NEVER surfaced here (cross-maker PII, Q4). The outbox-events
/// projection NEVER references <c>OutboxEvent.PayloadJson</c> (it embeds
/// customer PII). Soft-deleted rows are hidden by the global Auditable filter
/// (no <c>IgnoreQueryFilters</c>).
/// </para>
/// </summary>
public interface IPayoutQueries
{
    /// <summary>
    /// Paged maker payout-batch list: the per-maker slice
    /// (<c>SUM</c>/<c>COUNT</c> over this maker's claimed orders) joined to the
    /// batch header + this maker's Fee-invoice id. Sorted
    /// <c>CompletedAt DESC, BatchNumber DESC</c>. Empty page when the maker has
    /// no batches.
    /// </summary>
    Task<PagedData<MakerPayoutListItemDto>> GetMakerPayoutsPagedAsync(
        string makerId, int page, int pageSize, CancellationToken ct);

    /// <summary>
    /// Maker payout-batch detail with the per-order breakdown (this maker's
    /// claimed orders only). Returns <c>null</c> for an unknown OR cross-maker
    /// batch id (same shape — no oracle).
    /// </summary>
    Task<MakerPayoutDetailDto?> GetMakerPayoutDetailAsync(
        string makerId, string batchId, CancellationToken ct);

    /// <summary>
    /// Paged, read-only, payload-free outbox audit trail for one of this
    /// maker's orders (US-maker-0017): event type + a derived
    /// <see cref="OutboxDeliveryStatus"/> + timestamp only. A cross-maker /
    /// unknown order id yields an empty page (IDOR shield in the projection).
    /// NEVER surfaces <c>PayloadJson</c> / <c>LastErrorCode</c> / retry.
    /// </summary>
    Task<PagedData<MakerOutboxEventDto>> GetMakerOutboxEventsForOrderAsync(
        string makerId, string orderId, int page, int pageSize, CancellationToken ct);
}
