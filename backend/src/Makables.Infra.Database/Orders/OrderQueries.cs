using Makables.Core.Domain.Common;
using Makables.Core.Domain.Invoices;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Orders.Queries;
using Makables.Core.Domain.Orders.Sorting;
using Makables.Core.Domain.Products;
using Microsoft.EntityFrameworkCore;

namespace Makables.Infra.Database.Orders;

/// <summary>
/// EF Core <see cref="IOrderQueries"/> impl. Every method is
/// <c>AsNoTracking</c> + <c>IgnoreAutoIncludes</c> and projects straight
/// into a DTO — no <see cref="Order"/> aggregate is materialized. Per
/// ADR 0023.
///
/// <para>
/// Soft-deleted rows are hidden by the global
/// <see cref="Common.Auditable"/> query filter on <see cref="Order"/>;
/// no <c>IgnoreQueryFilters</c> is needed (read-side reads should not
/// resurrect deactivated rows).
/// </para>
///
/// <para>
/// The <c>Maker</c> + <c>Product</c> joins go through the DbContext's
/// per-entity <see cref="DbSet{TEntity}"/> instead of EF navigation
/// properties (the <see cref="Order"/> entity intentionally does not
/// carry navigations — per ADR 0013 / 0023, the aggregate stays
/// lightweight and projection queries opt in to joins explicitly here).
/// </para>
/// </summary>
public sealed class OrderQueries(MakablesDbContext db) : IOrderQueries
{
    public async Task<PagedData<CustomerOrderListItemDto>> GetCustomerOrdersPagedAsync(
        string customerId,
        OrderFilter filter,
        OrderSort sort,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(filter);

        if (string.IsNullOrWhiteSpace(customerId))
        {
            return PagedData<CustomerOrderListItemDto>.Empty(page, pageSize);
        }

        // Base predicate IS the IDOR shield (T-0080 §A locked decision):
        // a customer cannot select another customer's row because the
        // SQL never references it.
        var baseQuery = db.Set<Order>()
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Where(o => o.CustomerUserId == customerId);

        baseQuery = ApplyFilter(baseQuery, filter);

        var totalCount = await baseQuery.CountAsync(ct);
        if (totalCount == 0)
        {
            return PagedData<CustomerOrderListItemDto>.Empty(page, pageSize);
        }

        var ordered = ApplySort(baseQuery, sort);

        // LEFT JOIN to maker for MakerName (Maker.CompanyName is the
        // canonical business label — the ARES-verified entity name).
        // LEFT JOIN to product for ProductTitle (null for custom orders
        // where o.ProductId is null). The projection lists every column
        // explicitly so EF translates to a tight SELECT … FROM orders
        // LEFT JOIN makers LEFT JOIN products … LIMIT @pageSize OFFSET …
        // rather than materializing entity rows.
        var items = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new CustomerOrderListItemDto(
                o.Id,
                o.OrderNumber,
                o.State,
                o.TotalAmountMinor,
                o.Currency,
                o.CreatedAt,
                db.Set<Maker>()
                    .Where(m => m.Id == o.MakerId)
                    .Select(m => m.CompanyName)
                    .FirstOrDefault() ?? string.Empty,
                o.ProductId == null
                    ? null
                    : db.Set<Product>()
                        .Where(p => p.Id == o.ProductId)
                        .Select(p => p.Title)
                        .FirstOrDefault(),
                // T-0079: denormalized unread count from the order row.
                o.CustomerUnreadMessageCount))
            .ToListAsync(ct);

        return new PagedData<CustomerOrderListItemDto>(items, page, pageSize, totalCount);
    }

    public async Task<PagedData<MakerOrderListItemDto>> GetMakerOrdersPagedAsync(
        string makerId,
        OrderFilter filter,
        OrderSort sort,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(filter);

        if (string.IsNullOrWhiteSpace(makerId))
        {
            return PagedData<MakerOrderListItemDto>.Empty(page, pageSize);
        }

        // Predicate IS the IDOR shield: defence-in-depth alongside the
        // handler-layer makerId resolution from session → IMakerRepository.
        var baseQuery = db.Set<Order>()
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Where(o => o.MakerId == makerId);

        baseQuery = ApplyFilter(baseQuery, filter);

        var totalCount = await baseQuery.CountAsync(ct);
        if (totalCount == 0)
        {
            return PagedData<MakerOrderListItemDto>.Empty(page, pageSize);
        }

        var ordered = ApplySort(baseQuery, sort);

        // Customer email DELIBERATELY NOT projected — T-0081 §A.2 GDPR
        // data-minimization lock. The expression tree below carries no
        // reference to o.ContactEmail. T-0079 now populates
        // UnreadMessageCount from the denormalized maker_unread_message_count
        // column (was forward-compat null at T-0081 ship time).
        var items = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new MakerOrderListItemDto(
                o.Id,
                o.OrderNumber,
                o.State,
                o.TotalAmountMinor,
                o.MakerPayoutAmountMinor,
                o.Currency,
                o.CreatedAt,
                o.ContactName, // snapshotted contact NAME (NOT email)
                o.ShippingMethod,
                o.ProductId == null
                    ? null
                    : db.Set<Product>()
                        .Where(p => p.Id == o.ProductId)
                        .Select(p => p.Title)
                        .FirstOrDefault(),
                o.MakerUnreadMessageCount))  // T-0079
            .ToListAsync(ct);

        return new PagedData<MakerOrderListItemDto>(items, page, pageSize, totalCount);
    }

    public async Task<CustomerOrderDetailDto?> GetCustomerOrderDetailsAsync(
        string orderId,
        string customerUserId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(orderId) || string.IsNullOrWhiteSpace(customerUserId))
        {
            return null;
        }

        // Predicate IS the IDOR shield: a customer cannot read another
        // customer's row because the SQL never references it. Cross-
        // tenant probes surface as null — same shape as nonexistent so
        // there's no IDOR oracle.
        return await db.Set<Order>()
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Where(o => o.Id == orderId && o.CustomerUserId == customerUserId)
            .Select(o => new CustomerOrderDetailDto(
                o.Id,
                o.OrderNumber,
                o.State,
                o.PaidAt,
                o.AcceptedAt,
                o.ShippedAt,
                o.DeliveredAt,
                o.CancelledAt,
                o.TotalAmountMinor,
                o.ProductPriceAmountMinor,
                o.ShippingPriceAmountMinor,
                ComputeVatAmount(o.ProductPriceAmountMinor + o.ShippingPriceAmountMinor, o.VatRateBp),
                o.VatRateBp,
                o.Currency,
                o.ContactName,
                o.ContactPhone,
                db.Set<Maker>()
                    .Where(m => m.Id == o.MakerId)
                    .Select(m => m.CompanyName)
                    .FirstOrDefault() ?? string.Empty,
                o.ProductId == null
                    ? null
                    : db.Set<Product>()
                        .Where(p => p.Id == o.ProductId)
                        .Select(p => p.Title)
                        .FirstOrDefault(),
                o.ShippingMethod,
                o.ShippingCarrierTrackingUrl,
                db.Set<OrderAttachment>()
                    .Where(a => a.OrderId == o.Id)
                    .OrderBy(a => a.CreatedAt)
                    .Select(a => new OrderAttachmentSummaryDto(
                        a.Id,
                        a.OriginalFilename,
                        a.ContentType,
                        a.SizeBytes,
                        "/api/v1/orders/" + o.Id + "/attachments/" + a.Id))
                    .ToList(),
                db.Set<Invoice>()
                    .Where(i => i.OrderId == o.Id && i.PdfBlobPath != null)
                    .Select(i => "/api/v1/orders/" + o.Id + "/invoice")
                    .FirstOrDefault(),
                o.CreatedAt,
                o.UpdatedAt))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<MakerOrderDetailDto?> GetMakerOrderDetailsAsync(
        string orderId,
        string makerId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(orderId) || string.IsNullOrWhiteSpace(makerId))
        {
            return null;
        }

        // Predicate IS the IDOR shield. Customer email DELIBERATELY NOT
        // projected — T-0081 §A.2 GDPR data-minimization lock. The
        // expression tree below carries no reference to o.ContactEmail.
        return await db.Set<Order>()
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Where(o => o.Id == orderId && o.MakerId == makerId)
            .Select(o => new MakerOrderDetailDto(
                o.Id,
                o.OrderNumber,
                o.State,
                o.PaidAt,
                o.AcceptedAt,
                o.ShippedAt,
                o.DeliveredAt,
                o.CancelledAt,
                o.TotalAmountMinor,
                o.ProductPriceAmountMinor,
                o.ShippingPriceAmountMinor,
                ComputeVatAmount(o.ProductPriceAmountMinor + o.ShippingPriceAmountMinor, o.VatRateBp),
                o.VatRateBp,
                o.MakerPayoutAmountMinor,
                o.Currency,
                o.ContactName,  // contact NAME — never email
                o.ContactPhone,
                o.ProductId == null
                    ? null
                    : db.Set<Product>()
                        .Where(p => p.Id == o.ProductId)
                        .Select(p => p.Title)
                        .FirstOrDefault(),
                o.ShippingMethod,
                o.ShippingCarrierRef,
                o.ShippingCarrierTrackingUrl,
                o.ZasilkovnaPickupPointId,
                db.Set<OrderAttachment>()
                    .Where(a => a.OrderId == o.Id)
                    .OrderBy(a => a.CreatedAt)
                    .Select(a => new OrderAttachmentSummaryDto(
                        a.Id,
                        a.OriginalFilename,
                        a.ContentType,
                        a.SizeBytes,
                        "/api/v1/orders/" + o.Id + "/attachments/" + a.Id))
                    .ToList(),
                db.Set<Invoice>()
                    .Where(i => i.OrderId == o.Id && i.PdfBlobPath != null)
                    .Select(i => "/api/v1/orders/" + o.Id + "/invoice")
                    .FirstOrDefault(),
                o.CreatedAt,
                o.UpdatedAt))
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// The four "in flight" states — money or fulfilment is still in motion.
    /// Static so the predicate is a single source of truth. T-0108.
    /// </summary>
    private static readonly OrderState[] InFlightStates =
    [
        OrderState.PendingPayment,
        OrderState.Paid,
        OrderState.Accepted,
        OrderState.Shipped,
    ];

    public Task<int> CountInFlightByCountryAsync(string countryCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(countryCode))
        {
            return Task.FromResult(0);
        }

        // Unscoped (admin host only); soft-deleted rows excluded by the
        // global Auditable filter (no IgnoreQueryFilters).
        return db.Set<Order>()
            .AsNoTracking()
            .Where(o => o.CountryCode == countryCode && InFlightStates.Contains(o.State))
            .CountAsync(ct);
    }

    /// <summary>
    /// Compute the VAT-portion of the gross total in minor units, given
    /// the gross and the rate in basis points (2100 = 21%). The gross
    /// already includes VAT in our snapshot model (T-0060 / T-0061 +
    /// patterns §A.13); rearranging
    /// <c>gross = net + net * (rate / 10000)</c> gives
    /// <c>vat = gross - net = gross * rate / (10000 + rate)</c>. Integer
    /// rounding is half-up via the +half trick to match the platform's
    /// money-math convention.
    /// </summary>
    private static long ComputeVatAmount(long grossMinor, int vatRateBp)
    {
        if (vatRateBp <= 0)
        {
            return 0;
        }
        var denom = 10_000L + vatRateBp;
        var numerator = grossMinor * vatRateBp;
        // Half-up rounding.
        return (numerator + denom / 2) / denom;
    }

    private static IQueryable<Order> ApplyFilter(IQueryable<Order> q, OrderFilter filter)
    {
        if (filter.State.HasValue)
        {
            q = q.Where(o => o.State == filter.State.Value);
        }
        if (filter.DateRangeStart.HasValue)
        {
            q = q.Where(o => o.CreatedAt >= filter.DateRangeStart.Value);
        }
        if (filter.DateRangeEnd.HasValue)
        {
            q = q.Where(o => o.CreatedAt <= filter.DateRangeEnd.Value);
        }
        return q;
    }

    private static IOrderedQueryable<Order> ApplySort(IQueryable<Order> q, OrderSort sort) =>
        sort switch
        {
            // Tie-break on Id desc for stable pagination on identical
            // timestamp rows (ULIDs are lexicographically time-ordered).
            OrderSort.CreatedAtAsc =>
                q.OrderBy(o => o.CreatedAt).ThenBy(o => o.Id),
            OrderSort.TotalAmountDesc =>
                q.OrderByDescending(o => o.TotalAmountMinor).ThenByDescending(o => o.Id),
            OrderSort.TotalAmountAsc =>
                q.OrderBy(o => o.TotalAmountMinor).ThenByDescending(o => o.Id),
            OrderSort.StateAsc =>
                q.OrderBy(o => o.State).ThenByDescending(o => o.Id),
            _ => // CreatedAtDesc (default)
                q.OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id),
        };
}
