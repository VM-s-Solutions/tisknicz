namespace Makables.Core.Domain.Invoices;

/// <summary>
/// Persistence access for <see cref="Invoice"/>. Surface shape mirrors
/// <see cref="Orders.IOrderRepository"/> per ADR 0013 §"Country and
/// ownership scoping": scoped read methods
/// (<see cref="ForCustomer"/> / <see cref="ForMaker"/>) for audience
/// hosts, an <see cref="Unscoped"/> escape hatch for admin only, plus a
/// pair of documented "no caller principal" lookups
/// (<see cref="GetByInvoiceNumberAsync"/> for admin search,
/// <see cref="GetByOrderIdAsync"/> for T-0068b's idempotency check on
/// <c>MarkOrderPaid</c> webhook retries).
///
/// <para>
/// <b>No <c>UpdateAsync</c>, no <c>DeleteAsync</c>.</b> Per role/invoice.md:
/// invoices are never modified after issuance — errata flow through
/// credit-notes (post-MVP). The only mutation is
/// <see cref="Invoice.AttachPdfBlobPath"/> (set-once), which EF Core
/// change-tracking on the returned aggregate handles automatically. GDPR
/// hard-delete goes through <c>DeleteUserPermanently</c> (T-0110) — soft
/// delete on the invoice anonymises but the legal record remains.
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
public interface IInvoiceRepository
{
    /// <summary>
    /// Customer-scoped queryable. Joins via
    /// <c>Order.CustomerUserId == customerUserId</c> for Customer
    /// invoices. Caller composes further <c>.Where</c> / <c>.OrderBy</c>
    /// as needed.
    ///
    /// <para>
    /// <b>IDOR warning.</b> The caller MUST resolve
    /// <paramref name="customerUserId"/> from the authenticated
    /// principal (<c>IUserSessionProvider.GetUserId()</c>), NEVER from a
    /// request body or path segment.
    /// </para>
    /// </summary>
    IQueryable<Invoice> ForCustomer(string customerUserId);

    /// <summary>
    /// Maker-scoped queryable. At T-0068a this returns ONLY Customer
    /// invoices that belong to orders assigned to
    /// <paramref name="makerId"/> (filter on the denormalised
    /// <see cref="Invoice.MakerId"/> column populated at issue time from
    /// <c>Order.MakerId</c>).
    ///
    /// <para>
    /// <b>TODO (T-0101).</b> When the <c>payout_batches</c> table lands,
    /// extend this method to also surface <see cref="InvoiceType.Fee"/>
    /// invoices targeting the maker via the PayoutBatch → MakerId join.
    /// At T-0068a/b the maker-host UI only displays Customer invoices;
    /// the Fee-side is intentionally omitted (rather than stubbed with
    /// <c>NotImplementedException</c>) per CLAUDE.md "no mocks during
    /// build phase" — the absent join is a known gap, not a silent skip.
    /// </para>
    ///
    /// <para>
    /// <b>IDOR warning.</b> The caller MUST resolve
    /// <paramref name="makerId"/> from the authenticated principal +
    /// <see cref="Makers.IMakerRepository.GetByUserIdAsync"/>, NEVER from
    /// a request param.
    /// </para>
    /// </summary>
    IQueryable<Invoice> ForMaker(string makerId);

    /// <summary>
    /// Unscoped queryable. <b>Admin host only</b> per ADR 0013. The
    /// global soft-delete filter still applies; admin reconciliation
    /// views add <c>.IgnoreQueryFilters()</c> explicitly.
    /// </summary>
    IQueryable<Invoice> Unscoped();

    /// <summary>
    /// Load a single invoice owned by a customer (via the Order link).
    /// Returns <c>null</c> for unknown ids OR ids owned by another
    /// customer — same IDOR-leak-resistant shape as
    /// <see cref="Orders.IOrderRepository.GetByIdForCustomerAsync"/>.
    /// Tracked instance.
    /// </summary>
    Task<Invoice?> GetByIdForCustomerAsync(string invoiceId, string customerUserId, CancellationToken cancellationToken);

    /// <summary>
    /// Load a single invoice owned by a maker (via the denormalised
    /// <see cref="Invoice.MakerId"/> column). Returns <c>null</c> for
    /// unknown / cross-maker ids (same IDOR shield). Tracked.
    /// </summary>
    Task<Invoice?> GetByIdForMakerAsync(string invoiceId, string makerId, CancellationToken cancellationToken);

    /// <summary>
    /// Load a single invoice without ownership scoping. <b>Admin host
    /// only</b> per ADR 0013. Used by admin lookups and GDPR
    /// reconciliation. Tracked.
    /// <para>
    /// Calls <c>.IgnoreQueryFilters()</c> internally so soft-deleted /
    /// anonymised rows are returned — admin reconciliation paths
    /// legitimately need to see them.
    /// </para>
    /// </summary>
    Task<Invoice?> GetByIdUnscopedAsync(string invoiceId, CancellationToken cancellationToken);

    /// <summary>
    /// Look up an invoice by its customer-facing number
    /// (<c>FV-CZ-20260001</c>). <b>Unscoped</b> — backs admin search and
    /// reconciliation. The global soft-delete filter still applies;
    /// admin views opt out as needed.
    /// </summary>
    Task<Invoice?> GetByInvoiceNumberAsync(string invoiceNumber, CancellationToken cancellationToken);

    /// <summary>
    /// Look up the Customer invoice for a given order. <b>Unscoped</b> —
    /// backs T-0068b's <c>IInvoiceService.IssueAsync</c> idempotency
    /// check: a webhook re-delivery of <c>MarkOrderPaid</c> for an order
    /// that already has an invoice must return the existing row rather
    /// than allocating a second number. Returns <c>null</c> for orders
    /// with no invoice yet (or with a soft-deleted invoice — the global
    /// filter applies, which is the right behaviour for re-issuance
    /// after GDPR anonymisation per T-0068a locked decision).
    /// </summary>
    Task<Invoice?> GetByOrderIdAsync(string orderId, CancellationToken cancellationToken);

    /// <summary>Track <paramref name="invoice"/> as a pending insert.</summary>
    Task AddAsync(Invoice invoice, CancellationToken cancellationToken);
}
