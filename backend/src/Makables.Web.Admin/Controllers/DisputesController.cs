using Asp.Versioning;
using Makables.Config.Controllers;
using Makables.Core.AppServices.Features.Orders;
using Makables.Core.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Makables.Web.Admin.Controllers;

/// <summary>
/// Admin-host dispute endpoints keyed by dispute id (as opposed to
/// <c>OrdersController</c>'s order-id-keyed dispute open/resolve actions).
/// First home for T-0146's reverse-shipment admin actions —
/// "Vygenerovat vratkový štítek" + the admin-on-behalf-of-maker "mark
/// received" acknowledgment.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/disputes")]
[Authorize]
public sealed class DisputesController : MakablesApiController
{
    /// <summary>
    /// Generate the reverse Zásilkovna return-to-maker shipment for an
    /// open dispute in a return-warranting category (T-0146 AC-1). Idempotent
    /// re-run against an already-labeled dispute is Silent Success.
    /// </summary>
    [HttpPost("{disputeId}/return-label")]
    [ProducesResponseType(typeof(GenerateReturnLabel.GenerateReturnLabelResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GenerateReturnLabelAction(string disputeId, CancellationToken ct) =>
        HandleResult(await Mediator.Send(new GenerateReturnLabel.Command(disputeId), ct));

    /// <summary>
    /// Admin acknowledges — on the maker's behalf — that the returned
    /// item was received (T-0146 AC-5). No automated carrier-status sync
    /// for the reverse leg; this is the only way the acknowledgment lands
    /// short of the maker's own <c>MarkDisputeReturnReceivedByMaker</c>.
    /// </summary>
    [HttpPost("{disputeId}/return-label/mark-received")]
    [ProducesResponseType(typeof(MarkDisputeReturnReceivedByAdmin.MarkDisputeReturnReceivedByAdminResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> MarkReturnReceivedAction(string disputeId, CancellationToken ct) =>
        HandleResult(await Mediator.Send(new MarkDisputeReturnReceivedByAdmin.Command(disputeId), ct));
}
