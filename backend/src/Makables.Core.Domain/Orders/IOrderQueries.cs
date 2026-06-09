using Makables.Core.Domain.Common;
using Makables.Core.Domain.Orders.Queries;
using Makables.Core.Domain.Orders.Sorting;

namespace Makables.Core.Domain.Orders;

/// <summary>
/// Read-side projection seam for the order surface (ADR 0023). Split from
/// the write-scoped <see cref="IOrderRepository"/> per the bundle's
/// CQRS-on-orders convention (T-0080 ships the interface + customer-list
/// method; T-0081 adds the maker-list method; T-0082 adds the per-audience
/// detail methods).
///
/// <para>
/// Every method is owner-scoped at the SQL level — the implementation
/// bakes the IDOR predicate (<c>CustomerUserId == customerId</c> or
/// <c>MakerId == makerId</c>) into the EF <c>Where</c> clause. Callers
/// MUST resolve the scoping id from the authenticated principal
/// (<see cref="IUserSessionProvider"/>); the queries trust the id they're
/// handed. Cross-tenant probes surface as empty results (paged) or
/// <c>null</c> (detail) — never as an oracle that distinguishes
/// "doesn't exist" from "not yours".
/// </para>
///
/// <para>
/// Projections are <c>AsNoTracking</c> + <c>IgnoreAutoIncludes</c> and
/// materialize directly into DTOs — no <see cref="Order"/> aggregate is
/// constructed. Soft-deleted rows are hidden by the global
/// <see cref="Common.Auditable"/> query filter (no
/// <c>IgnoreQueryFilters</c> call).
/// </para>
/// </summary>
public interface IOrderQueries
{
    /// <summary>
    /// Paged customer-scoped order list (T-0080). Predicate
    /// <c>o.CustomerUserId == customerId</c>. Returns an empty page (not
    /// null, not 404) when the customer has no matching orders.
    ///
    /// <para>
    /// <see cref="OrderFilter.State"/> / <see cref="OrderFilter.DateRangeStart"/>
    /// / <see cref="OrderFilter.DateRangeEnd"/> apply conditionally — the
    /// caller passes whichever subset the request carried.
    /// </para>
    /// </summary>
    Task<PagedData<CustomerOrderListItemDto>> GetCustomerOrdersPagedAsync(
        string customerId,
        OrderFilter filter,
        OrderSort sort,
        int page,
        int pageSize,
        CancellationToken ct);

    /// <summary>
    /// Paged maker-scoped order list (T-0081). Predicate
    /// <c>o.MakerId == makerId</c>. Returns an empty page when the maker
    /// has no matching orders.
    ///
    /// <para>
    /// Implementation MUST NOT reference <c>o.ContactEmail</c> anywhere
    /// in the projection — T-0081 §A.2 GDPR data-minimization lock. The
    /// <see cref="MakerOrderListItemDto.UnreadMessageCount"/> field is
    /// populated as <c>null</c> at T-0081 (forward-compat slot for T-0079).
    /// </para>
    /// </summary>
    Task<PagedData<MakerOrderListItemDto>> GetMakerOrdersPagedAsync(
        string makerId,
        OrderFilter filter,
        OrderSort sort,
        int page,
        int pageSize,
        CancellationToken ct);
}
