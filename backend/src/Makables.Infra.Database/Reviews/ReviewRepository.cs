using Makables.Core.Domain.Reviews;
using Microsoft.EntityFrameworkCore;

namespace Makables.Infra.Database.Reviews;

/// <summary>
/// EF Core <see cref="IReviewRepository"/> impl (T-0100). Write-side per
/// ADR 0013 — <see cref="GetByIdForMakerAsync"/> bakes the maker IDOR
/// predicate into the SQL. Soft-delete filtering is automatic via the
/// global query filter on <c>Auditable.IsActive</c>; no explicit
/// <c>.Where(r =&gt; r.IsActive)</c> below.
/// </summary>
public sealed class ReviewRepository(MakablesDbContext db) : IReviewRepository
{
    public Task AddAsync(Review review, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(review);
        return db.Set<Review>().AddAsync(review, cancellationToken).AsTask();
    }

    public Task<Review?> GetByIdForMakerAsync(string reviewId, string makerId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reviewId) || string.IsNullOrWhiteSpace(makerId))
            return Task.FromResult<Review?>(null);

        // The maker-owns predicate IS the IDOR shield: a cross-tenant
        // probe (reply to a review on another maker's order) selects no
        // row → null → 404 at the handler, no existence leak.
        return db.Set<Review>()
            .FirstOrDefaultAsync(r => r.Id == reviewId && r.MakerId == makerId, cancellationToken);
    }

    public Task<bool> ExistsForOrderAsync(string orderId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(orderId))
            return Task.FromResult(false);

        // Active-only via the global filter — a soft-deleted review does
        // NOT block a new one (the partial unique index frees the slot).
        return db.Set<Review>()
            .AnyAsync(r => r.OrderId == orderId, cancellationToken);
    }

    public async Task<(int Count, double AverageStars)> GetMakerRatingAggregateAsync(
        string makerId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(makerId))
            return (0, 0d);

        // COUNT(*) + AVG(rating) in ONE SQL round-trip over the maker's
        // ACTIVE reviews (global filter). The recompute folds the result
        // into basis points and writes via Maker.RecomputeRating. AVG over
        // zero rows would yield NULL in SQL — project a defensive 0.
        var aggregate = await db.Set<Review>()
            .Where(r => r.MakerId == makerId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Count = g.Count(),
                Average = (double?)g.Average(r => (double)r.Rating),
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (aggregate is null)
            return (0, 0d);

        return (aggregate.Count, aggregate.Average ?? 0d);
    }

    public async Task<(int Count, double AverageStars)> GetProductRatingAggregateAsync(
        string productId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(productId))
            return (0, 0d);

        // Same one-round-trip COUNT + AVG shape as the maker aggregate,
        // keyed on the denormalized product_id (null for custom orders,
        // which therefore never enter any product aggregate).
        var aggregate = await db.Set<Review>()
            .Where(r => r.ProductId == productId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Count = g.Count(),
                Average = (double?)g.Average(r => (double)r.Rating),
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (aggregate is null)
            return (0, 0d);

        return (aggregate.Count, aggregate.Average ?? 0d);
    }
}
