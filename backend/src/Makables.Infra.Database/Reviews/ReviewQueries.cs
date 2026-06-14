using Makables.Core.Domain.Common;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Reviews;
using Makables.Core.Domain.Reviews.Queries;
using Microsoft.EntityFrameworkCore;

namespace Makables.Infra.Database.Reviews;

/// <summary>
/// EF Core <see cref="IReviewQueries"/> impl (T-0100, ADR 0023). Every
/// method is <c>AsNoTracking</c> + <c>IgnoreAutoIncludes</c> and projects
/// straight into the read DTOs. The audience-scope predicate
/// (<c>customer_user_id</c> / <c>maker_id</c>) is baked into the SQL
/// <c>WHERE</c> — the IDOR shield at the read layer. Soft-deleted rows
/// (orders, makers, reviews) are hidden by the global query filter.
/// </summary>
public sealed class ReviewQueries(MakablesDbContext db) : IReviewQueries
{
    public async Task<IReadOnlyList<ReviewableOrderDto>> GetCustomerReviewableOrdersAsync(
        string customerUserId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(customerUserId))
            return Array.Empty<ReviewableOrderDto>();

        // Orders the customer owns in Delivered/Completed with NO active
        // review (left-anti-join on reviews.order_id). The maker company
        // name is fetched via a scalar subquery; soft-deleted makers
        // surface as null → empty string fallback.
        return await db.Set<Order>()
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Where(o => o.CustomerUserId == customerUserId
                     && (o.State == OrderState.Delivered || o.State == OrderState.Completed)
                     && !db.Set<Review>().Any(r => r.OrderId == o.Id))
            .OrderByDescending(o => o.DeliveredAt)
            .ThenByDescending(o => o.Id)
            .Select(o => new ReviewableOrderDto(
                o.Id,
                o.OrderNumber,
                o.MakerId,
                db.Set<Maker>().Where(mk => mk.Id == o.MakerId).Select(mk => mk.CompanyName).FirstOrDefault() ?? string.Empty,
                o.DeliveredAt ?? o.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<SubmittedReviewDto>> GetCustomerSubmittedReviewsAsync(
        string customerUserId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(customerUserId))
            return Array.Empty<SubmittedReviewDto>();

        return await db.Set<Review>()
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Where(r => r.CustomerUserId == customerUserId)
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.Id)
            .Select(r => new SubmittedReviewDto(
                r.Id,
                r.OrderId,
                r.MakerId,
                db.Set<Maker>().Where(mk => mk.Id == r.MakerId).Select(mk => mk.CompanyName).FirstOrDefault() ?? string.Empty,
                r.Rating,
                r.Body,
                r.MakerReply,
                r.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<PagedData<MakerReceivedReviewDto>> GetMakerReceivedReviewsPagedAsync(
        string makerId, int page, int pageSize, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(makerId))
            return PagedData<MakerReceivedReviewDto>.Empty(page, pageSize);

        // maker_id predicate IS the IDOR shield. Backed by the
        // (maker_id, created_at DESC) index; no customer identity in the
        // projection (GDPR data-minimization §C.7).
        var baseQuery = db.Set<Review>()
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Where(r => r.MakerId == makerId);

        var totalCount = await baseQuery.CountAsync(ct);
        if (totalCount == 0)
            return PagedData<MakerReceivedReviewDto>.Empty(page, pageSize);

        var items = await baseQuery
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new MakerReceivedReviewDto(
                r.Id,
                r.OrderId,
                db.Set<Order>().Where(o => o.Id == r.OrderId).Select(o => o.OrderNumber).FirstOrDefault() ?? string.Empty,
                r.Rating,
                r.Body,
                r.MakerReply,
                r.MakerReplyAt,
                r.CreatedAt))
            .ToListAsync(ct);

        return new PagedData<MakerReceivedReviewDto>(items, page, pageSize, totalCount);
    }
}
