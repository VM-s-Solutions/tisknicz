using Makables.Core.Domain.Admin;
using Makables.Core.Domain.Auditing;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Invoices;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Payouts;
using Makables.Core.Domain.Products;
using Microsoft.EntityFrameworkCore;

namespace Makables.Infra.Database.Admin;

/// <summary>
/// EF Core <see cref="IAdminQueries"/> impl (T-0111). Composes over the
/// repositories' <c>Unscoped()</c> queryables — the documented admin
/// escape hatch (ADR 0013) — so the "admin reads everything" intent is
/// self-documenting. Every method is <c>AsNoTracking</c> +
/// <c>IgnoreAutoIncludes</c>, projects straight into a DTO, and uses two
/// round-trips (count + page) per the T-0080 precedent.
/// </summary>
public sealed class AdminQueries(
    MakablesDbContext db,
    IOrderRepository orders,
    IInvoiceRepository invoices) : IAdminQueries
{
    public async Task<PagedData<AdminOrderListItemDto>> GetAllOrdersPagedAsync(
        AdminOrderFilter filter, int page, int pageSize, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var baseQuery = orders.Unscoped()
            .AsNoTracking()
            .IgnoreAutoIncludes()
            // IgnoreQueryFilters: the admin reconciliation view MUST surface
            // soft-deleted + anonymised orders (a T-0110 GDPR erasure leaves
            // anonymised rows visible so admin can confirm the erasure ran
            // and reconcile payouts). The single, commented opt-out — the
            // invoice + audit queries below deliberately do NOT ignore filters.
            .IgnoreQueryFilters();

        if (filter.State.HasValue)
            baseQuery = baseQuery.Where(o => o.State == filter.State.Value);
        if (!string.IsNullOrEmpty(filter.CountryCode))
            baseQuery = baseQuery.Where(o => o.CountryCode == filter.CountryCode);
        if (!string.IsNullOrEmpty(filter.MakerId))
            baseQuery = baseQuery.Where(o => o.MakerId == filter.MakerId);
        // T-0127 per-user in-flight signal (Q-0024): exact FK match on the
        // order's customer. The delete-user panel filters this + an in-flight
        // state; a non-empty page proactively pre-disables the destructive
        // button (the backend T-0110 erase gate stays authoritative).
        if (!string.IsNullOrEmpty(filter.CustomerUserId))
            baseQuery = baseQuery.Where(o => o.CustomerUserId == filter.CustomerUserId);
        if (!string.IsNullOrEmpty(filter.CustomerEmail))
        {
            var pattern = $"%{filter.CustomerEmail}%";
            baseQuery = baseQuery.Where(o => EF.Functions.ILike(o.ContactEmail, pattern));
        }

        var totalCount = await baseQuery.CountAsync(ct);
        if (totalCount == 0)
            return PagedData<AdminOrderListItemDto>.Empty(page, pageSize);

        var items = await baseQuery
            .OrderByDescending(o => o.CreatedAt)
            .ThenByDescending(o => o.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new AdminOrderListItemDto(
                o.Id,
                o.OrderNumber,
                o.State,
                o.CountryCode,
                o.TotalAmountMinor,
                o.Currency,
                o.CreatedAt,
                o.MakerId,
                db.Set<Maker>().IgnoreQueryFilters()
                    .Where(m => m.Id == o.MakerId)
                    .Select(m => m.CompanyName)
                    .FirstOrDefault() ?? string.Empty,
                o.ContactEmail,
                o.ProductId == null
                    ? null
                    : db.Set<Product>().IgnoreQueryFilters()
                        .Where(p => p.Id == o.ProductId)
                        .Select(p => p.Title)
                        .FirstOrDefault(),
                o.IsActive))
            .ToListAsync(ct);

        return new PagedData<AdminOrderListItemDto>(items, page, pageSize, totalCount);
    }

    public async Task<PagedData<AdminInvoiceListItemDto>> GetAllInvoicesPagedAsync(
        AdminInvoiceFilter filter, int page, int pageSize, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(filter);

        // NO IgnoreQueryFilters: invoices are never soft-deleted
        // post-issuance (role/invoice.md); the global filter stays.
        var baseQuery = invoices.Unscoped()
            .AsNoTracking()
            .IgnoreAutoIncludes();

        if (filter.Type.HasValue)
            baseQuery = baseQuery.Where(i => i.Type == filter.Type.Value);
        if (!string.IsNullOrEmpty(filter.CountryCode))
            baseQuery = baseQuery.Where(i => i.CountryCode == filter.CountryCode);
        if (!string.IsNullOrEmpty(filter.Recipient))
        {
            var pattern = $"%{filter.Recipient}%";
            baseQuery = baseQuery.Where(i => EF.Functions.ILike(i.RecipientName, pattern));
        }
        if (filter.DateRangeStart.HasValue)
            baseQuery = baseQuery.Where(i => i.CreatedAt >= filter.DateRangeStart.Value);
        if (filter.DateRangeEnd.HasValue)
            baseQuery = baseQuery.Where(i => i.CreatedAt <= filter.DateRangeEnd.Value);

        var totalCount = await baseQuery.CountAsync(ct);
        if (totalCount == 0)
            return PagedData<AdminInvoiceListItemDto>.Empty(page, pageSize);

        var items = await baseQuery
            .OrderByDescending(i => i.CreatedAt)
            .ThenByDescending(i => i.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(i => new AdminInvoiceListItemDto(
                i.Id,
                i.InvoiceNumber,
                i.Type,
                i.CountryCode,
                i.RecipientName,
                i.AmountWithVatMinor,
                i.Currency,
                i.CreatedAt,
                i.OrderId,
                i.PayoutBatchId))
            .ToListAsync(ct);

        return new PagedData<AdminInvoiceListItemDto>(items, page, pageSize, totalCount);
    }

    public async Task<PagedData<AdminAuditLogItemDto>> GetAdminAuditLogPagedAsync(
        AdminAuditLogFilter filter, int page, int pageSize, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(filter);

        // AdminAuditLogEntry is append-only and NOT Auditable — there is no
        // soft-delete query filter to ignore.
        var baseQuery = db.Set<AdminAuditLogEntry>().AsNoTracking();

        if (!string.IsNullOrEmpty(filter.AdminUserId))
            baseQuery = baseQuery.Where(a => a.AdminUserId == filter.AdminUserId);
        if (!string.IsNullOrEmpty(filter.ActionCode))
            baseQuery = baseQuery.Where(a => a.ActionCode == filter.ActionCode);
        if (!string.IsNullOrEmpty(filter.TargetEntity))
            baseQuery = baseQuery.Where(a => a.TargetEntity == filter.TargetEntity);
        if (filter.DateRangeStart.HasValue)
            baseQuery = baseQuery.Where(a => a.CreatedAt >= filter.DateRangeStart.Value);
        if (filter.DateRangeEnd.HasValue)
            baseQuery = baseQuery.Where(a => a.CreatedAt <= filter.DateRangeEnd.Value);

        var totalCount = await baseQuery.CountAsync(ct);
        if (totalCount == 0)
            return PagedData<AdminAuditLogItemDto>.Empty(page, pageSize);

        var items = await baseQuery
            .OrderByDescending(a => a.CreatedAt)
            .ThenByDescending(a => a.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AdminAuditLogItemDto(
                a.Id,
                a.AdminUserId,
                a.ActionCode,
                a.TargetEntity,
                a.TargetId,
                a.Notes,
                a.IpAddress,
                a.CreatedAt))
            .ToListAsync(ct);

        return new PagedData<AdminAuditLogItemDto>(items, page, pageSize, totalCount);
    }

    public async Task<AdminOrderDetailDto?> GetOrderDetailAsync(string orderId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(orderId))
            return null;

        // Unscoped + IgnoreQueryFilters (admin reconciliation rows surface) +
        // AsNoTracking (pure read, no mutation). Maker name / product title
        // resolved via correlated subqueries (Order has no EF navigation to
        // Maker/Product) — same pattern as GetAllOrdersPagedAsync above. NO
        // GDPR redaction: admin is privileged (CustomerEmail + full contact
        // snapshot carried).
        return await orders.Unscoped()
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .IgnoreQueryFilters()
            .Where(o => o.Id == orderId)
            .Select(o => new AdminOrderDetailDto(
                o.Id,
                o.OrderNumber,
                o.State,
                o.CountryCode,
                o.MakerId,
                db.Set<Maker>().IgnoreQueryFilters()
                    .Where(m => m.Id == o.MakerId)
                    .Select(m => m.CompanyName)
                    .FirstOrDefault() ?? string.Empty,
                o.ProductId,
                o.ProductId == null
                    ? null
                    : db.Set<Product>().IgnoreQueryFilters()
                        .Where(p => p.Id == o.ProductId)
                        .Select(p => p.Title)
                        .FirstOrDefault(),
                o.CustomerUserId,
                o.ContactEmail,
                o.ContactName,
                o.ContactPhone,
                o.CustomerNotes,
                o.ProductPriceAmountMinor,
                o.ShippingPriceAmountMinor,
                o.PlatformFeeAmountMinor,
                o.MakerPayoutAmountMinor,
                o.TotalAmountMinor,
                o.RefundedAmountMinor,
                o.Currency,
                o.VatRateBp,
                o.ShippingMethod,
                o.ZasilkovnaPickupPointId,
                o.ShippingCarrierRef,
                o.ShippingCarrierTrackingUrl,
                o.PaymentProviderRef,
                o.PaymentMethod,
                o.PayoutBatchId,
                o.CreatedAt,
                o.PaidAt,
                o.AcceptedAt,
                o.ShippedAt,
                o.DeliveredAt,
                o.CompletedAt,
                o.CancelledAt,
                o.RefundedAt,
                o.DisputedAt,
                o.IsActive,
                db.Set<Dispute>()
                    .Where(d => d.OrderId == o.Id && d.ResolvedAt == null)
                    .Select(d => d.Id)
                    .FirstOrDefault(),
                db.Set<Dispute>()
                    .Where(d => d.OrderId == o.Id && d.ResolvedAt == null)
                    .Select(d => (DisputeCategory?)d.Category)
                    .FirstOrDefault(),
                db.Set<Dispute>()
                    .Any(d => d.OrderId == o.Id && d.ResolvedAt == null && d.ReturnCarrierRef != null)))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<PagedData<AdminPayoutBatchListItemDto>> GetPayoutBatchesPagedAsync(
        int page, int pageSize, CancellationToken ct)
    {
        // Cross-maker browse list (T-0127). The batch aggregate has no
        // Unscoped() queryable on its repo, so read the DbSet directly —
        // admin-host only per ADR 0013 (the host audience is the security
        // boundary). AsNoTracking; the global soft-delete filter applies (a
        // batch is never destroyed, so the count is exact). NO
        // IgnoreQueryFilters — there are no soft-deleted batches to surface.
        var baseQuery = db.Set<PayoutBatch>()
            .AsNoTracking()
            .IgnoreAutoIncludes();

        var totalCount = await baseQuery.CountAsync(ct);
        if (totalCount == 0)
            return PagedData<AdminPayoutBatchListItemDto>.Empty(page, pageSize);

        var items = await baseQuery
            .OrderByDescending(b => b.CreatedAt)
            .ThenByDescending(b => b.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(b => new AdminPayoutBatchListItemDto(
                b.Id,
                b.BatchNumber,
                b.State,
                b.TotalAmountMinor,
                b.Currency,
                b.OrderCount,
                b.MakerCount,
                b.CreatedAt,
                b.CompletedAt))
            .ToListAsync(ct);

        return new PagedData<AdminPayoutBatchListItemDto>(items, page, pageSize, totalCount);
    }
}
