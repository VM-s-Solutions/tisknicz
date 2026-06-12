using Asp.Versioning;
using Makables.Config.Controllers;
using Makables.Core.AppServices.Features.Orders;
using Makables.Core.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Makables.Web.Admin.Controllers;

/// <summary>
/// Admin-host order endpoints. <b>First controller on the Admin host</b>
/// (T-0105) — sets the convention for every admin endpoint that follows
/// (T-0106 dispute open/resolve, T-0107 manual state change, T-0118 UI
/// read models): one-liner Mediator dispatch, <c>[Authorize]</c> under
/// the admin audience (ADR 0013 — a customer/maker JWT cannot replay
/// here), all write commands implement <c>IAdminAuditableCommand</c> so
/// the before/after JSONB audit rides the pipeline (ADR 0014).
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/orders")]
[Authorize]
public sealed class OrdersController : MakablesApiController
{
    /// <summary>Request body for <see cref="Refund"/>. The order id rides the route.</summary>
    public sealed record RefundOrderRequest(long AmountMinor, string Reason, bool AcknowledgePostPayout);

    /// <summary>
    /// Refund an order — full or partial (T-0105 / US-admin-0008).
    /// Money moves at the provider FIRST; the cumulative
    /// <c>refunded_amount_minor</c> accumulates and the order transitions
    /// to <c>Refunded</c> only when it reaches the total. Refunding a
    /// Completed (paid-out) order requires <c>acknowledgePostPayout</c>.
    /// </summary>
    [HttpPost("{orderId}/refund")]
    [ProducesResponseType(typeof(RefundOrder.RefundOrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Refund(
        string orderId, [FromBody] RefundOrderRequest request, CancellationToken ct) =>
        HandleResult(await Mediator.Send(
            new RefundOrder.Command(orderId, request.AmountMinor, request.Reason, request.AcknowledgePostPayout),
            ct));
}
