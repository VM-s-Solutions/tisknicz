using Makables.Core.Domain.Common;
using Makables.Core.Domain.Reviews.Queries;

namespace Makables.Core.Domain.Reviews;

/// <summary>
/// Read-side projection seam for reviews (T-0100, ADR 0023). Split from
/// the write-scoped <see cref="IReviewRepository"/>; mirrors the
/// <c>IOrderQueries</c> shape. Every method is <c>AsNoTracking</c> +
/// <c>IgnoreAutoIncludes</c> and materializes straight into DTOs — no
/// <see cref="Review"/> aggregate is constructed.
///
/// <para>
/// Each method bakes the audience predicate
/// (<c>customer_user_id</c> / <c>maker_id</c>) into the EF <c>Where</c> —
/// the IDOR shield at the read layer. Callers MUST resolve the scoping id
/// from the authenticated principal (<see cref="IUserSessionProvider"/>).
/// Cross-tenant probes surface as empty results, never as an oracle.
/// Soft-deleted rows are hidden by the global <see cref="Common.Auditable"/>
/// query filter.
/// </para>
/// </summary>
public interface IReviewQueries
{
    /// <summary>
    /// Orders owned by <paramref name="customerUserId"/> in state ∈
    /// <c>{Delivered, Completed}</c> with NO active review (left-anti-join
    /// on <c>reviews.order_id</c>). Powers the "leave a review" CTA list.
    /// Small set per customer — no paging.
    /// </summary>
    Task<IReadOnlyList<ReviewableOrderDto>> GetCustomerReviewableOrdersAsync(
        string customerUserId,
        CancellationToken ct);

    /// <summary>
    /// The reviews <paramref name="customerUserId"/> has themselves
    /// submitted (rating + body + maker reply). Small set — no paging.
    /// </summary>
    Task<IReadOnlyList<SubmittedReviewDto>> GetCustomerSubmittedReviewsAsync(
        string customerUserId,
        CancellationToken ct);

    /// <summary>
    /// The maker-dashboard "reviews about me" list for
    /// <paramref name="makerId"/>. Paginated 20/page. Sort
    /// <c>CreatedOn DESC</c>, tiebreak <c>Id DESC</c>. Backed by the
    /// <c>(maker_id, created_on DESC)</c> index.
    /// </summary>
    Task<PagedData<MakerReceivedReviewDto>> GetMakerReceivedReviewsPagedAsync(
        string makerId,
        int page,
        int pageSize,
        CancellationToken ct);
}
