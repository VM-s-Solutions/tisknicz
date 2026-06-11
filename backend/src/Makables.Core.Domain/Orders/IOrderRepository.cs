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
    /// Read-only maker-scoped variant of <see cref="GetByIdForMakerAsync"/>.
    /// Same IDOR shield + return shape, but applies <c>.AsNoTracking()</c>
    /// so EF Core skips change-tracking and the snapshot allocation. Use
    /// this when the handler does NOT call methods on the returned
    /// <see cref="Order"/> — saves change-tracking overhead per
    /// ADR 0025 §Performance expectations item 2.
    ///
    /// <para>
    /// Read-only callers (T-0075 maker-host label download) only inspect
    /// <see cref="Order.ShippingCarrierRef"/> + <see cref="Common.Auditable.CountryCode"/>
    /// and verify maker ownership; mutating callers
    /// (<c>AcceptOrder</c> / <c>ShipOrder</c> / <c>HandOverOrder</c> /
    /// <c>MarkOrderPaid</c>) keep using the tracked
    /// <see cref="GetByIdForMakerAsync"/>.
    /// </para>
    /// </summary>
    Task<Order?> GetByIdForMakerReadOnlyAsync(string orderId, string makerId, CancellationToken cancellationToken);

    /// <summary>
    /// Read-only customer-scoped variant of <see cref="GetByIdForCustomerAsync"/>.
    /// Same IDOR shield + return shape (null for unknown ids AND
    /// cross-customer ids), but applies <c>.AsNoTracking()</c> so EF Core
    /// skips change-tracking and the snapshot allocation. Use this when
    /// the caller does NOT call methods on the returned <see cref="Order"/>
    /// — saves change-tracking overhead per ADR 0025 §Performance
    /// expectations item 2. Mirrors <see cref="GetByIdForMakerReadOnlyAsync"/>.
    ///
    /// <para>
    /// Read-only callers (T-0088 customer-host invoice download) only
    /// verify customer ownership/existence before an unscoped child
    /// lookup; mutating callers (T-0076 mark delivered, T-0083 cancel)
    /// keep using the tracked <see cref="GetByIdForCustomerAsync"/>.
    /// </para>
    /// </summary>
    Task<Order?> GetByIdForCustomerReadOnlyAsync(string orderId, string customerUserId, CancellationToken cancellationToken);

    /// <summary>
    /// Load a single order without ownership scoping. <b>Admin host
    /// only</b> per ADR 0013. Used by admin lookups (T-0107 manual
    /// state change, T-0105 refund) and GDPR reconciliation (T-0110).
    /// Tracked.
    /// <para>
    /// Calls <c>.IgnoreQueryFilters()</c> internally so soft-deleted
    /// rows are returned. This is intentional: admin reconciliation
    /// paths legitimately need to see deactivated orders. Callers that
    /// must hide soft-deleted rows should use the owner-scoped
    /// <see cref="GetByIdForCustomerAsync"/> or
    /// <see cref="GetByIdForMakerAsync"/>.
    /// </para>
    /// </summary>
    Task<Order?> GetByIdUnscopedAsync(string orderId, CancellationToken cancellationToken);

    /// <summary>
    /// Read-only variant of <see cref="GetByIdUnscopedAsync"/>. Applies
    /// <c>.IgnoreQueryFilters()</c> (admin/reconciliation rows still
    /// visible) and <c>.AsNoTracking()</c> so EF Core skips change-tracking
    /// and the snapshot allocation. Use this when the handler does NOT
    /// call methods on the returned <see cref="Order"/> — saves
    /// change-tracking overhead per ADR 0025 §Performance expectations
    /// item 2.
    ///
    /// <para>
    /// Read-only callers (T-0074 <c>FetchAndStoreShippingLabel</c>) only
    /// inspect <see cref="Order.ShippingCarrierRef"/> +
    /// <see cref="Common.Auditable.CountryCode"/>; mutating callers
    /// keep using the tracked <see cref="GetByIdUnscopedAsync"/>.
    /// </para>
    /// </summary>
    Task<Order?> GetByIdUnscopedReadOnlyAsync(string orderId, CancellationToken cancellationToken);

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

    /// <summary>
    /// Projection-only stream of <see cref="Order.Id"/> values for orders
    /// that have crossed their auto-delivery window. Unscoped + read-only
    /// (<c>AsNoTracking</c>): the T-0077 Function context has no user
    /// identity and only needs the id to dispatch
    /// <c>MarkOrderDelivered.Command</c> per row.
    ///
    /// <para>
    /// Predicate: <c>State == Shipped AND AutoDeliverAt != null AND
    /// AutoDeliverAt &lt; asOf</c>. Soft-deleted rows excluded via the
    /// global query filter (auto-deliver MUST NOT resurrect deactivated
    /// orders). Stream materializes one row at a time — keeps memory
    /// flat under any batch size. Ordered by <c>AutoDeliverAt</c>
    /// ascending (oldest expirations first). T-0077.
    /// </para>
    /// </summary>
    IAsyncEnumerable<string> GetAutoDeliverableUnscopedReadOnlyAsync(
        DateTimeOffset asOf,
        CancellationToken cancellationToken);

    /// <summary>
    /// Stream of <see cref="Order"/> aggregates for the T-0078 Packeta
    /// status-sync sweep. Predicate:
    /// <c>State == Shipped AND ShippingMethod == ZasilkovnaPickupPoint
    /// AND ShippingCarrierRef != null</c>. PersonalPickup orders are
    /// skipped (no carrier ref to query); they rely on T-0076 + T-0077
    /// for delivery close.
    ///
    /// <para>
    /// Full Order projection (not just id) because the Function reads
    /// <see cref="Order.ShippingMethod"/>, <see cref="Order.ShippingCarrierRef"/>,
    /// <see cref="Common.Auditable.CountryCode"/>, and
    /// <see cref="Order.State"/> at decision time. Unscoped + read-only
    /// (<c>AsNoTracking</c>) — mutating dispatch happens through
    /// MediatR Commands which re-load via the tracked path. T-0078.
    /// </para>
    /// </summary>
    IAsyncEnumerable<Order> GetCarrierSyncableUnscopedReadOnlyAsync(
        CancellationToken cancellationToken);

    /// <summary>
    /// Projection-only stream of <see cref="Order.Id"/> values for orders
    /// in <see cref="OrderState.PendingPayment"/> whose
    /// <see cref="Common.Auditable.CreatedAt"/> is older than
    /// <c>asOf - 24h</c>. Unscoped + read-only — the T-0083 Function
    /// context has no user identity and only needs the id to dispatch
    /// <c>CancelExpiredOrder.Command</c> per row.
    ///
    /// <para>
    /// Soft-deleted rows excluded via the global query filter
    /// (auto-cancel MUST NOT resurrect deactivated orders).
    /// <c>AsNoTracking</c>; ORDER BY <c>CreatedAt ASC</c> (oldest
    /// expirations first — mirrors T-0077's AutoDeliverAt ascending sort).
    /// Stream materializes one row at a time so memory stays flat under
    /// any batch size. T-0083 §C.
    /// </para>
    /// </summary>
    IAsyncEnumerable<string> GetExpiredPendingPaymentUnscopedReadOnlyAsync(
        DateTimeOffset asOf,
        CancellationToken cancellationToken);

    /// <summary>Track <paramref name="order"/> as a pending insert.</summary>
    Task AddAsync(Order order, CancellationToken cancellationToken);

    /// <summary>
    /// Load a single <see cref="OrderAttachment"/> belonging to an order
    /// owned by <paramref name="customerUserId"/>. Returns <c>null</c>
    /// when any of <paramref name="orderId"/> / <paramref name="attachmentId"/>
    /// is unknown OR when the order is owned by another customer — same
    /// IDOR-leak-resistant shape as <see cref="GetByIdForCustomerAsync"/>.
    /// Backs the customer-host attachment download endpoint (T-0064).
    /// Read-only — no tracking needed.
    /// </summary>
    Task<OrderAttachment?> GetAttachmentForCustomerAsync(
        string orderId,
        string attachmentId,
        string customerUserId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Load a single <see cref="OrderAttachment"/> belonging to an order
    /// assigned to <paramref name="makerId"/>. Returns <c>null</c> for
    /// unknown ids or cross-maker ids (same IDOR shield). Backs the
    /// maker-host attachment download endpoint (T-0064).
    /// </summary>
    Task<OrderAttachment?> GetAttachmentForMakerAsync(
        string orderId,
        string attachmentId,
        string makerId,
        CancellationToken cancellationToken);
}
