using Asp.Versioning;
using Makables.Config.Controllers;
using Makables.Core.AppServices.Features.Admin;
using Makables.Core.AppServices.Features.Outbox;
using Makables.Core.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Makables.Web.Admin.Controllers;

/// <summary>
/// Admin outbox triage endpoints (T-0109 / US-admin-0014). Force-retry
/// nudges a stalled row back into the due set (the sweep re-publishes);
/// acknowledge silences it permanently. <c>[Authorize]</c> under the admin
/// audience (ADR 0013); both commands implement <c>IAdminAuditableCommand</c>
/// so the before/after JSONB + reason ride the pipeline (ADR 0014).
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/outbox-events")]
[Authorize]
public sealed class OutboxEventsController : MakablesApiController
{
    /// <summary>Request body for <see cref="Acknowledge"/>. The id rides the route.</summary>
    public sealed record AcknowledgeOutboxEventRequest(string Reason);

    /// <summary>
    /// Count stalled outbox events (T-0126 / Q-0027) — the retry ladder
    /// exhausted (<c>ProcessedAt == null AND NextRetryAt == null AND
    /// LastErrorKind != None</c>). Backs the admin overview's stalled-outbox KPI
    /// tile + the US-admin-0002 AC-2 banner. Read-only, admin-audience only
    /// (ADR 0013). No params; empty set → <c>{ count: 0 }</c>, never 404.
    /// </summary>
    [HttpGet("stalled/count")]
    [ProducesResponseType(typeof(GetStalledOutboxCount.GetStalledOutboxCountResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> StalledCount(CancellationToken ct) =>
        HandleResult(await Mediator.Send(new GetStalledOutboxCount.Query(), ct));

    /// <summary>
    /// Paged stalled-outbox triage list (T-0127 / Q-0029). Same EXACT stalled
    /// predicate as <see cref="StalledCount"/> (<c>ProcessedAt == null AND
    /// NextRetryAt == null AND LastErrorKind != None</c>) so the list and the
    /// KPI tile agree. The triage page browses + retries / acknowledges by
    /// VISIBLE id. Read-only, admin-audience only (ADR 0013); empty set →
    /// <c>PagedData</c> with <c>TotalCount = 0</c>, never 404.
    /// </summary>
    [HttpGet("stalled")]
    [ProducesResponseType(typeof(GetStalledOutboxEvents.GetStalledOutboxEventsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Stalled(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default) =>
        HandleResult(await Mediator.Send(new GetStalledOutboxEvents.Query(page, pageSize), ct));

    /// <summary>
    /// Force-retry a stalled event — one-shot "try now". 409
    /// <c>outbox.alreadyProcessed</c> if the row already drained.
    /// </summary>
    [HttpPost("{id}/retry")]
    [ProducesResponseType(typeof(RetryOutboxEvent.RetryOutboxEventResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Retry(string id, CancellationToken ct) =>
        HandleResult(await Mediator.Send(new RetryOutboxEvent.Command(id), ct));

    /// <summary>
    /// Acknowledge a stalled event — terminal, never retried. Re-acknowledge
    /// is a benign Silent-Success (200).
    /// </summary>
    [HttpPost("{id}/acknowledge")]
    [ProducesResponseType(typeof(AcknowledgeOutboxEvent.AcknowledgeOutboxEventResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Acknowledge(
        string id, [FromBody] AcknowledgeOutboxEventRequest request, CancellationToken ct) =>
        HandleResult(await Mediator.Send(new AcknowledgeOutboxEvent.Command(id, request.Reason), ct));
}
