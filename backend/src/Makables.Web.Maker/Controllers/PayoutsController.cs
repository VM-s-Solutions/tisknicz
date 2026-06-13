using Asp.Versioning;
using Makables.Config.Controllers;
using Makables.Core.AppServices.Features.Payouts;
using Makables.Core.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Makables.Web.Maker.Controllers;

/// <summary>
/// Maker-host payout endpoints (T-0112 / US-maker-0012). <c>[Authorize]</c>
/// under the maker audience per ADR 0013 — a customer JWT cannot replay here.
/// The owning maker is resolved from the JWT inside the handler; there is no
/// maker-id parameter. The per-maker slice (not the cross-maker batch total)
/// and the maker's own Fee-invoice id are surfaced; the operator CSV is
/// admin-only and never reachable here.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/payout-batches")]
[Authorize]
public sealed class PayoutsController : MakablesApiController
{
    /// <summary>
    /// Paged maker payout-batch list, sorted <c>CompletedAt DESC,
    /// BatchNumber DESC</c>. Each row carries the per-maker total, order count,
    /// state, completed date, and the maker's Fee-invoice id (null until
    /// rendered). Empty page when the maker has no batches.
    /// </summary>
    [HttpGet("")]
    [ProducesResponseType(typeof(GetMakerPayouts.GetMakerPayoutsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = GetMakerPayouts.DefaultPageSize,
        CancellationToken ct = default) =>
        HandleResult(await Mediator.Send(new GetMakerPayouts.Query(page, pageSize), ct));

    /// <summary>
    /// Maker payout-batch detail: the batch summary + this maker's per-order
    /// breakdown (order #, product price, shipping, platform fee, net payout)
    /// + the Fee-invoice id. A cross-maker OR unknown batch id returns the SAME
    /// 404 (no enumeration oracle).
    /// </summary>
    [HttpGet("{batchId}")]
    [ProducesResponseType(typeof(GetMakerPayoutDetail.GetMakerPayoutDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Detail(string batchId, CancellationToken ct) =>
        HandleResult(await Mediator.Send(new GetMakerPayoutDetail.Query(batchId), ct));
}
