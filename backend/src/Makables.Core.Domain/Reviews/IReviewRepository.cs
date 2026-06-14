namespace Makables.Core.Domain.Reviews;

/// <summary>
/// Write-side persistence for <see cref="Review"/> (T-0100, ADR 0013
/// scoping + ADR 0023 split from <see cref="IReviewQueries"/>). The
/// maker-scoped read bakes the IDOR predicate into the SQL; the caller
/// drives the unit-of-work — the <c>UnitOfWorkPipelineBehavior</c>
/// commits when the surrounding command succeeds. No
/// <c>SaveChangesAsync</c> here.
///
/// <para>
/// Active-row filtering is automatic via the global soft-delete query
/// filter on <see cref="Common.Auditable"/>; no explicit
/// <c>.Where(r =&gt; r.IsActive)</c> in the implementation.
/// </para>
/// </summary>
public interface IReviewRepository
{
    /// <summary>Track <paramref name="review"/> as a pending insert.</summary>
    Task AddAsync(Review review, CancellationToken cancellationToken);

    /// <summary>
    /// Load a single review owned by <paramref name="makerId"/> (the
    /// reviewed order's maker). Predicate
    /// <c>r.Id == reviewId AND r.MakerId == makerId</c> — the maker IDOR
    /// shield. Returns <c>null</c> for unknown ids AND cross-maker ids
    /// (same shape, no existence leak); cross-tenant → 404 at the handler.
    /// Tracked — the <c>RespondToReview</c> handler mutates via
    /// <see cref="Review.AddReply"/>.
    /// </summary>
    Task<Review?> GetByIdForMakerAsync(string reviewId, string makerId, CancellationToken cancellationToken);

    /// <summary>
    /// True when an ACTIVE review already exists for
    /// <paramref name="orderId"/> (the global filter excludes
    /// soft-deleted rows). Backs the <c>SubmitReview</c> eligibility
    /// second-leg; the partial unique index <c>(order_id) WHERE is_active</c>
    /// is the hard backstop for the concurrent-double-submit race.
    /// </summary>
    Task<bool> ExistsForOrderAsync(string orderId, CancellationToken cancellationToken);

    /// <summary>
    /// <c>COUNT(*)</c> + <c>AVG(rating)</c> over <paramref name="makerId"/>'s
    /// ACTIVE reviews, computed in SQL. Feeds the recompute-from-rows
    /// rating mutation (Q5). When <c>Count == 0</c>, <c>AverageStars</c>
    /// returns 0 (defensive — the recompute then stores 0 bp / 0 count).
    /// The aggregate MUST include the just-added review (the handler reads
    /// it after the new row is visible — flushed within the same UoW
    /// transaction); the <c>RatingRecomputeCorrectnessTests</c> pin the
    /// resulting stored values so an off-by-one cannot ship silently.
    /// </summary>
    Task<(int Count, double AverageStars)> GetMakerRatingAggregateAsync(string makerId, CancellationToken cancellationToken);
}
