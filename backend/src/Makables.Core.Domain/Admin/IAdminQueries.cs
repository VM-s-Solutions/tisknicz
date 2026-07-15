using Makables.Core.Domain.Common;

namespace Makables.Core.Domain.Admin;

/// <summary>
/// Read-side projection seam for the admin cross-tenant views (T-0111,
/// ADR 0023). UNLIKE the owner-scoped <see cref="Orders.IOrderQueries"/>
/// (which bakes an IDOR predicate into every query), these reads compose
/// over <see cref="Orders.IOrderRepository.Unscoped"/> /
/// <see cref="Invoices.IInvoiceRepository.Unscoped"/> — admin is the
/// privileged actor and sees EVERY tenant (ADR 0013 §"the Unscoped escape
/// hatch is admin-host only"). The boundary is the admin-audience JWT on
/// the <c>Web.Admin</c> host, not a SQL WHERE clause; the Reviewer rejects
/// any call reaching these from a non-admin host.
///
/// <para>
/// Every method is <c>AsNoTracking</c> + <c>IgnoreAutoIncludes</c>,
/// projects straight into a DTO (no aggregate materialised), and uses two
/// round-trips (count + page) per the T-0080 precedent. Reads are NOT
/// audited (ADR 0014 audits admin WRITES only).
/// </para>
/// </summary>
public interface IAdminQueries
{
    /// <summary>
    /// Paged cross-tenant order list (US-admin-0009). Applies
    /// <c>.IgnoreQueryFilters()</c> so soft-deleted / anonymised T-0110
    /// reconciliation rows surface (the only one of the three that does).
    /// Sorted <c>CreatedAt DESC</c>.
    /// </summary>
    Task<PagedData<AdminOrderListItemDto>> GetAllOrdersPagedAsync(
        AdminOrderFilter filter, int page, int pageSize, CancellationToken ct);

    /// <summary>
    /// Paged cross-tenant invoice list (US-admin-0012). Keeps the global
    /// soft-delete filter (invoices are never soft-deleted post-issuance).
    /// Sorted <c>CreatedAt DESC</c>.
    /// </summary>
    Task<PagedData<AdminInvoiceListItemDto>> GetAllInvoicesPagedAsync(
        AdminInvoiceFilter filter, int page, int pageSize, CancellationToken ct);

    /// <summary>
    /// Paged admin audit log (US-admin-0015). Reads the append-only
    /// <c>AdminAuditLogEntry</c> set (not <c>Auditable</c> — no filter to
    /// ignore). List DTO omits before/after JSONB. Sorted
    /// <c>CreatedAt DESC</c>.
    /// </summary>
    Task<PagedData<AdminAuditLogItemDto>> GetAdminAuditLogPagedAsync(
        AdminAuditLogFilter filter, int page, int pageSize, CancellationToken ct);

    /// <summary>
    /// Single privileged order header (T-0127 / Q-0024). Composes over
    /// <c>IOrderRepository.Unscoped()</c> + <c>.IgnoreQueryFilters()</c>
    /// (soft-deleted / anonymised rows surface for reconciliation),
    /// <c>AsNoTracking</c>, projected to <see cref="AdminOrderDetailDto"/>.
    /// NO GDPR redaction — admin is privileged (carries
    /// <c>CustomerEmail</c> + the full contact snapshot). Returns
    /// <c>null</c> for unknown / inactive ids (the handler maps it to
    /// <c>order.notFound</c>).
    /// </summary>
    Task<AdminOrderDetailDto?> GetOrderDetailAsync(string orderId, CancellationToken ct);

    /// <summary>
    /// Paged cross-tenant maker list (T-0119b / US-admin-0003..0005).
    /// Ignores the soft-delete filter so deactivated makers surface
    /// (reconciliation + audit context). Joins User (account email) +
    /// Address (city). Sorted <c>CreatedAt DESC</c>.
    /// </summary>
    Task<PagedData<AdminMakerListItemDto>> GetAllMakersPagedAsync(
        AdminMakerFilter filter, int page, int pageSize, CancellationToken ct);

    /// <summary>
    /// Single privileged maker header (T-0119b). Ignores the soft-delete
    /// filter (a deactivated maker's detail stays reachable). Returns
    /// <c>null</c> for unknown ids (the handler maps to
    /// <c>maker.notFound</c>).
    /// </summary>
    Task<AdminMakerDetailDto?> GetMakerDetailAsync(string makerId, CancellationToken ct);

    /// <summary>
    /// Paged cross-maker payout-batch list (T-0127 / Q-0029). Reads the
    /// payout-batch DbSet directly, <c>AsNoTracking</c> (the batch repo
    /// exposes no Unscoped queryable). Spans every maker (the admin browse
    /// list, NOT the per-maker T-0112 surface). The global soft-delete filter
    /// applies (a batch is never destroyed). Sorted <c>CreatedAt DESC</c>.
    /// </summary>
    Task<PagedData<AdminPayoutBatchListItemDto>> GetPayoutBatchesPagedAsync(
        int page, int pageSize, CancellationToken ct);
}
