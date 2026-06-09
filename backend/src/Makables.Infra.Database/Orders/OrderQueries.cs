using Makables.Core.Domain.Common;
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
                        .FirstOrDefault()))
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
        // reference to o.ContactEmail. UnreadMessageCount is reserved
        // null for T-0079.
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
                (int?)null)) // UnreadMessageCount — T-0079
            .ToListAsync(ct);

        return new PagedData<MakerOrderListItemDto>(items, page, pageSize, totalCount);
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
