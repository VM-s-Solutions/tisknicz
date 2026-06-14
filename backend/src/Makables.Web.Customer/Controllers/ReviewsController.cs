using Asp.Versioning;
using Makables.Config.Controllers;
using Makables.Core.AppServices.Features.Reviews;
using Makables.Core.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Makables.Web.Customer.Controllers;

/// <summary>
/// Customer-host review endpoints (T-0100, US-customer-0015). Per ADR 0013
/// + T-0079/T-0082 precedent the feature surface is compile-time
/// per-audience — only the customer-side <c>SubmitReview</c> + the two
/// customer dashboard reads ship here; <c>RespondToReview</c> lives on the
/// Maker host. The IDOR shield is the WHERE-predicate baked into the
/// scoped order/review reads. <c>[Authorize]</c> under the customer
/// audience.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}")]
[Authorize]
public sealed class ReviewsController : MakablesApiController
{
    public sealed record SubmitReviewRequest(short Rating, string? Body);

    /// <summary>
    /// Submit a review for one of the customer's own Delivered/Completed
    /// orders. <c>rating ∈ [1,5]</c>; <c>body</c> optional ≤1000 chars. A
    /// cross-tenant / unknown order returns 404 (<c>order.notFound</c>); a
    /// second review for the same order returns the <c>review.alreadyExists</c>
    /// conflict; a pre-delivery order returns <c>review.orderNotDelivered</c>.
    /// On success the maker's rating is recomputed atomically.
    /// </summary>
    [HttpPost("orders/{orderId}/review")]
    [ProducesResponseType(typeof(SubmitReview.SubmitReviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Submit(
        string orderId,
        [FromBody] SubmitReviewRequest body,
        CancellationToken ct = default) =>
        HandleResult(await Mediator.Send(
            new SubmitReview.Command(orderId, body.Rating, body.Body), ct));

    /// <summary>
    /// The customer's Delivered/Completed orders with no active review yet
    /// — the "leave a review" CTA list.
    /// </summary>
    [HttpGet("reviews/reviewable-orders")]
    [ProducesResponseType(typeof(GetCustomerReviewableOrders.GetCustomerReviewableOrdersResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ReviewableOrders(CancellationToken ct = default) =>
        HandleResult(await Mediator.Send(new GetCustomerReviewableOrders.Query(), ct));

    /// <summary>The reviews the customer themselves submitted (read-back).</summary>
    [HttpGet("reviews/mine")]
    [ProducesResponseType(typeof(GetCustomerSubmittedReviews.GetCustomerSubmittedReviewsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Mine(CancellationToken ct = default) =>
        HandleResult(await Mediator.Send(new GetCustomerSubmittedReviews.Query(), ct));
}
