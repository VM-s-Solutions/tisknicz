using Asp.Versioning;
using Makables.Config.Controllers;
using Makables.Core.AppServices.Features.Reviews;
using Makables.Core.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Makables.Web.Maker.Controllers;

/// <summary>
/// Maker-host review endpoints (T-0100, US-maker-0014). Compile-time
/// per-audience per ADR 0013 — only the maker-side <c>RespondToReview</c>
/// + the maker dashboard list ship here. The IDOR shield is the maker-owns
/// predicate baked into <see cref="Core.Domain.Reviews.IReviewRepository.GetByIdForMakerAsync"/>;
/// a cross-tenant reply returns 404 (<c>review.notFound</c>), no existence
/// leak. <c>[Authorize]</c> under the maker audience.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/reviews")]
[Authorize]
public sealed class ReviewsController : MakablesApiController
{
    public sealed record RespondToReviewRequest(string Reply);

    /// <summary>
    /// Paged "reviews about me" list, sorted <c>CreatedAt DESC</c>,
    /// 20/page. Cross-tenant probes return an empty page (no leak).
    /// </summary>
    [HttpGet("")]
    [ProducesResponseType(typeof(GetMakerReceivedReviews.GetMakerReceivedReviewsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = GetMakerReceivedReviews.DefaultPageSize,
        CancellationToken ct = default) =>
        HandleResult(await Mediator.Send(new GetMakerReceivedReviews.Query(page, pageSize), ct));

    /// <summary>
    /// Attach (or overwrite) the single public reply to a review on one of
    /// the maker's own orders. <c>reply ∈ [1, 500]</c> chars. A cross-tenant
    /// / unknown review returns 404 (<c>review.notFound</c>); re-submitting
    /// OVERWRITES the prior reply (one reply per review).
    /// </summary>
    [HttpPost("{reviewId}/reply")]
    [ProducesResponseType(typeof(RespondToReview.RespondToReviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Reply(
        string reviewId,
        [FromBody] RespondToReviewRequest body,
        CancellationToken ct = default) =>
        HandleResult(await Mediator.Send(
            new RespondToReview.Command(reviewId, body.Reply), ct));
}
