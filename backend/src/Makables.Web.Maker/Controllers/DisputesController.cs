using Asp.Versioning;
using Makables.Config.Controllers;
using Makables.Core.AppServices.Features.Orders;
using Makables.Core.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Makables.Web.Maker.Controllers;

/// <summary>
/// Maker-host dispute endpoints (T-0146). The maker's own "mark received"
/// acknowledgment for a reverse-shipment return — the AC-5 counterpart to
/// the admin's <c>DisputesController.MarkReturnReceivedAction</c>.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/disputes")]
[Authorize]
public sealed class DisputesController : MakablesApiController
{
    /// <summary>
    /// Maker acknowledges receiving the customer's returned item (AC-5).
    /// Owner-scoped (IDOR shield, AC-7) — a cross-maker dispute id 404s.
    /// </summary>
    [HttpPost("{disputeId}/return-label/mark-received")]
    [ProducesResponseType(typeof(MarkDisputeReturnReceivedByMaker.MarkDisputeReturnReceivedByMakerResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> MarkReturnReceivedAction(string disputeId, CancellationToken ct) =>
        HandleResult(await Mediator.Send(new MarkDisputeReturnReceivedByMaker.Command(disputeId), ct));
}
