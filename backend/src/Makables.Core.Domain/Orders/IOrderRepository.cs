namespace Makables.Core.Domain.Orders;

/// <summary>
/// Persistence access for <see cref="Order"/>. Surface shape follows
/// ADR 0013 §"Country and ownership scoping": scoped read methods
/// (<see cref="ForCustomer"/> / <see cref="ForMaker"/>) for audience
/// hosts, an <see cref="Unscoped"/> escape hatch for admin only, and a
/// single documented exception (<see cref="GetByPaymentProviderRefAsync"/>)
/// for the webhook that has no caller principal.
///
/// <para>
/// <b>No <c>UpdateAsync</c></b> — EF Core change-tracking on the
/// returned aggregate handles mutations and the
/// <c>UnitOfWorkPipelineBehavior</c> commits at the end of the command.
/// <b>No <c>DeleteAsync</c></b> — soft delete via
/// <see cref="Common.Auditable.MarkDeactivated"/> is the only path;
/// GDPR hard-delete goes through <c>DeleteUserPermanently</c> (T-0110).
/// </para>
///
/// <para>
/// Active-row filtering is automatic via the global soft-delete query
/// filter on <see cref="Common.Auditable"/> (see
/// <c>MakablesDbContext.ApplySoftDeleteQueryFilters</c>). Admin paths
/// that legitimately need soft-deleted rows call
/// <c>.IgnoreQueryFilters()</c> explicitly with a comment.
/// </para>
/// </summary>
public interface IOrderRepository
{
    /// <summary>
    /// Customer-scoped queryable, filtered to
    /// <c>o =&gt; o.CustomerUserId == customerUserId</c>. The caller
    /// composes further <c>.Where</c> / <c>.OrderBy</c> as needed for
    /// list / detail queries.
    ///
    /// <para>
    /// <b>IDOR warning.</b> The caller MUST resolve
    /// <paramref name="customerUserId"/> from the authenticated
    /// principal (<c>IUserSessionProvider.GetUserId()</c>), NEVER from a
    /// request body or path segment. The repository does not check
    /// caller context.
    /// </para>
    /// </summary>
    IQueryable<Order> ForCustomer(string customerUserId);

    /// <summary>
    /// Maker-scoped queryable, filtered to
    /// <c>o =&gt; o.MakerId == makerId</c>. Backs the maker dashboard
    /// order list (T-0081).
    ///
    /// <para>
    /// <b>IDOR warning.</b> The caller MUST resolve
    /// <paramref name="makerId"/> from the authenticated principal +
    /// <see cref="Makers.IMakerRepository.GetByUserIdAsync"/>, NEVER from
    /// a request param. The repository does not check caller context.
    /// </para>
    /// </summary>
    IQueryable<Order> ForMaker(string makerId);

    /// <summary>
    /// Unscoped queryable. <b>Admin host only</b> per ADR 0013 —
    /// Reviewer rejects calls from <c>Web.Customer</c>, <c>Web.Maker</c>,
    /// or <c>Web.Public</c> (with the documented exception of
    /// <see cref="GetByPaymentProviderRefAsync"/>). Soft-deleted rows are
    /// still hidden by the global filter; admin reconciliation views
    /// add <c>.IgnoreQueryFilters()</c> explicitly.
    /// </summary>
    IQueryable<Order> Unscoped();

    /// <summary>
    /// Load a single order owned by <paramref name="customerUserId"/>.
    /// Returns <c>null</c> when the id is unknown OR owned by another
    /// customer — same shape so order ids aren't enumerable across
    /// customers (IDOR shield, same pattern as
    /// <see cref="Products.IMakerProductQueries.GetMyProductByIdAsync"/>).
    /// Tracked instance — customer commands (T-0076 mark delivered,
    /// T-0083 cancel) mutate the returned aggregate.
    /// </summary>
    Task<Order?> GetByIdForCustomerAsync(string orderId, string customerUserId, CancellationToken cancellationToken);

    /// <summary>
    /// Load a single order owned by <paramref name="makerId"/>. Returns
    /// <c>null</c> for unknown ids or cross-maker ids (same IDOR
    /// shield). Tracked — maker commands (T-0071 accept, T-0072 ship)
    /// mutate.
    /// </summary>
    Task<Order?> GetByIdForMakerAsync(string orderId, string makerId, CancellationToken cancellationToken);

    /// <summary>
    /// Load a single order without ownership scoping. <b>Admin host
    /// only</b> per ADR 0013. Used by admin lookups (T-0107 manual
    /// state change, T-0105 refund) and GDPR reconciliation. Tracked.
    /// </summary>
    Task<Order?> GetByIdUnscopedAsync(string orderId, CancellationToken cancellationToken);

    /// <summary>
    /// Look up an order by its payment provider reference (Comgate
    /// transaction id). <b>Unscoped</b> — this is the ONE legitimate
    /// non-admin caller of an unscoped lookup, justified by the Comgate
    /// webhook (T-0066) running on the Public host with no
    /// authenticated principal. The webhook controller already enforces
    /// IP allowlist + signature verification, so the scoping invariant
    /// is held by the network boundary instead of by application
    /// scoping.
    ///
    /// <para>
    /// Returns <c>null</c> for unknown refs AND for refs attached to
    /// soft-deleted orders — the global query filter applies, which is
    /// the right behaviour for webhook idempotency (a duplicate
    /// notification against a deleted order should be a no-op, not a
    /// 500). Reviewer checks no other host calls this method.
    /// </para>
    /// </summary>
    Task<Order?> GetByPaymentProviderRefAsync(string paymentProviderRef, CancellationToken cancellationToken);

    /// <summary>Track <paramref name="order"/> as a pending insert.</summary>
    Task AddAsync(Order order, CancellationToken cancellationToken);
}
