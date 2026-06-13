using Makables.Core.Domain.Invoices;
using Makables.Core.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace Makables.Infra.Database.Invoices;

/// <summary>
/// EF Core <see cref="IInvoiceRepository"/> impl per ADR 0013. Tracked
/// reads on every <c>GetByIdFor*</c> so T-0068b's
/// <c>IInvoiceService.IssueAsync</c> + the future
/// <c>AttachPdfBlobPath</c> call site (set-once mutation) can mutate the
/// returned aggregate inside the surrounding command's UoW. Read-only
/// callers (T-0088 invoice download) use the paired
/// <c>*ReadOnlyAsync</c> variants which add <c>.AsNoTracking()</c> per
/// ADR 0025 §Performance expectations item 2 (OrderRepository pattern).
///
/// <para>
/// Soft-delete filtering is automatic via
/// <c>MakablesDbContext.ApplySoftDeleteQueryFilters</c> — no explicit
/// <c>.Where(i =&gt; i.IsActive)</c> in any method below. Admin paths
/// needing soft-deleted rows add <c>.IgnoreQueryFilters()</c> explicitly
/// with a comment.
/// </para>
/// </summary>
public sealed class InvoiceRepository(MakablesDbContext db) : IInvoiceRepository
{
    public IQueryable<Invoice> ForCustomer(string customerUserId)
    {
        if (string.IsNullOrWhiteSpace(customerUserId))
        {
            // Defensive: an empty session id should yield no rows, not
            // every row. Mirrors OrderRepository.ForCustomer.
            return db.Set<Invoice>().Where(i => false);
        }

        // Customer invoices link to an Order; the join via OrderId →
        // Order.CustomerUserId is the IDOR-safe scoping per role doc.
        // Fee invoices (PayoutBatchId path) never match a customer scope
        // — they are platform-to-maker documents, not customer-facing.
        var customerOrderIds = db.Set<Order>()
            .Where(o => o.CustomerUserId == customerUserId)
            .Select(o => o.Id);
        return db.Set<Invoice>()
            .Where(i => i.OrderId != null && customerOrderIds.Contains(i.OrderId));
    }

    public IQueryable<Invoice> ForMaker(string makerId)
    {
        if (string.IsNullOrWhiteSpace(makerId))
        {
            return db.Set<Invoice>().Where(i => false);
        }

        // T-0068a Customer-only: the denormalised MakerId column is
        // populated from Order.MakerId at issue time, so a single column
        // filter is sufficient and matches the
        // (maker_id, created_at DESC) composite index. T-0101 will
        // extend this to ALSO include Fee invoices via the PayoutBatch
        // → MakerId join once payout_batches lands; the absent Fee path
        // is the known gap documented on IInvoiceRepository.ForMaker.
        return db.Set<Invoice>().Where(i => i.MakerId == makerId);
    }

    public IQueryable<Invoice> Unscoped() => db.Set<Invoice>();

    public Task<Invoice?> GetByIdForCustomerAsync(
        string invoiceId,
        string customerUserId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(invoiceId) || string.IsNullOrWhiteSpace(customerUserId))
            return Task.FromResult<Invoice?>(null);

        return ForCustomer(customerUserId)
            .FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);
    }

    public Task<Invoice?> GetByIdForMakerAsync(
        string invoiceId,
        string makerId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(invoiceId) || string.IsNullOrWhiteSpace(makerId))
            return Task.FromResult<Invoice?>(null);

        return db.Set<Invoice>()
            .FirstOrDefaultAsync(
                i => i.Id == invoiceId && i.MakerId == makerId,
                cancellationToken);
    }

    public Task<Invoice?> GetForMakerReadOnlyAsync(
        string invoiceId,
        string makerId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(invoiceId) || string.IsNullOrWhiteSpace(makerId))
            return Task.FromResult<Invoice?>(null);

        // Read-only mirror of GetByIdForMakerAsync — T-0112a Fee-invoice
        // download only reads (Type + PdfBlobPath + InvoiceNumber); ADR 0025
        // §Performance item 2. Same i.Id == invoiceId && i.MakerId == makerId
        // IDOR shield, same global soft-delete filter (no IgnoreQueryFilters).
        return db.Set<Invoice>()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                i => i.Id == invoiceId && i.MakerId == makerId,
                cancellationToken);
    }

    public Task<Invoice?> GetByIdUnscopedAsync(string invoiceId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(invoiceId))
            return Task.FromResult<Invoice?>(null);

        // IgnoreQueryFilters: admin + GDPR reconciliation lookup, same
        // policy as IOrderRepository.GetByIdUnscopedAsync.
        return db.Set<Invoice>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);
    }

    public Task<Invoice?> GetByInvoiceNumberAsync(string invoiceNumber, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(invoiceNumber))
            return Task.FromResult<Invoice?>(null);

        var trimmed = invoiceNumber.Trim();
        return db.Set<Invoice>()
            .FirstOrDefaultAsync(i => i.InvoiceNumber == trimmed, cancellationToken);
    }

    public Task<Invoice?> GetByOrderIdAsync(string orderId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(orderId))
            return Task.FromResult<Invoice?>(null);

        // T-0068b idempotency check: MarkOrderPaid webhook re-deliveries
        // must find the existing invoice (if any) rather than allocating
        // a second number. Soft-deleted invoices are correctly excluded
        // here — a re-issuance after GDPR anonymisation gets a NEW
        // number from the sequence (the original number is consumed
        // forever in the gap-free sense; that is fine because the
        // numbering scope is platform-wide per locked decision 1).
        return db.Set<Invoice>()
            .FirstOrDefaultAsync(i => i.OrderId == orderId, cancellationToken);
    }

    public Task<Invoice?> GetByOrderIdReadOnlyAsync(string orderId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(orderId))
            return Task.FromResult<Invoice?>(null);

        // AsNoTracking: read-only callers (T-0088 invoice download on
        // both hosts) only inspect PdfBlobPath + InvoiceNumber after the
        // ownership-scoped order load; they never call methods on the
        // returned aggregate. Skipping change-tracking + the snapshot
        // allocation per ADR 0025 §Performance expectations item 2.
        // Mirrors OrderRepository.GetByIdForCustomerReadOnlyAsync. The
        // T-0068b idempotency caller keeps the tracked GetByOrderIdAsync.
        return db.Set<Invoice>()
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.OrderId == orderId, cancellationToken);
    }

    public Task<Invoice?> GetByIdAsync(string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
            return Task.FromResult<Invoice?>(null);

        // Tracked — the EmailSendService fee-invoice branch only reads, but
        // keeping the tracked path mirrors the other GetBy* lookups; the
        // global soft-delete filter applies.
        return db.Set<Invoice>()
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Invoice>> GetByPayoutBatchIdAsync(
        string payoutBatchId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payoutBatchId))
            return [];

        // Tracked — the artifact service's re-entrancy path calls
        // AttachPdfBlobPath on the returned Fee invoices when re-rendering a
        // maker whose PdfBlobPath is still null. Global soft-delete filter
        // applies.
        return await db.Set<Invoice>()
            .Where(i => i.PayoutBatchId == payoutBatchId)
            .ToListAsync(cancellationToken);
    }

    public Task AddAsync(Invoice invoice, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        db.Set<Invoice>().Add(invoice);
        return Task.CompletedTask;
    }
}
