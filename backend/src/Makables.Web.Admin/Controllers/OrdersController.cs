using Asp.Versioning;
using Makables.Config.Controllers;
using Makables.Core.AppServices.Features.Orders;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Orders;
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

    /// <summary>Request body for <see cref="OpenDisputeAction"/>. The order id rides the route.</summary>
    public sealed record OpenAdminDisputeRequest(DisputeCategory Category, string Description);

    /// <summary>
    /// Admin opens a dispute on any order (T-0106) — e.g. transcribing a
    /// phone-reported problem. All six categories are accepted, including
    /// the carrier-reserved ones. Re-opening an already-disputed order
    /// returns the existing dispute's id (200, Silent Success).
    /// </summary>
    [HttpPost("{orderId}/dispute")]
    [ProducesResponseType(typeof(OpenDispute.OpenDisputeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> OpenDisputeAction(
        string orderId, [FromBody] OpenAdminDisputeRequest request, CancellationToken ct) =>
        HandleResult(await Mediator.Send(
            new OpenDispute.Command(orderId, request.Category, request.Description), ct));

    /// <summary>Request body for <see cref="ResolveDisputeAction"/>. The order id rides the route.</summary>
    public sealed record ResolveDisputeRequest(DisputeResolutionOutcome Outcome, string ResolutionNotes);

    /// <summary>
    /// Admin resolves the order's open dispute (T-0106 / US-admin-0011
    /// AC-2): restores the pre-dispute state, records outcome +
    /// customer-visible notes, notifies the customer, then applies the
    /// outcome edge (Refunded dispatches the T-0105 refund for the full
    /// remaining amount; Cancelled requires a Paid/Accepted restore;
    /// Resumed proceeds as-is). Re-resolve is a loud 409.
    /// </summary>
    [HttpPost("{orderId}/dispute/resolve")]
    [ProducesResponseType(typeof(ResolveDispute.ResolveDisputeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ResolveDisputeAction(
        string orderId, [FromBody] ResolveDisputeRequest request, CancellationToken ct) =>
        HandleResult(await Mediator.Send(
            new ResolveDispute.Command(orderId, request.Outcome, request.ResolutionNotes), ct));

    /// <summary>Request body for <see cref="ChangeState"/>. The order id rides the route.</summary>
    public sealed record ChangeOrderStateRequest(OrderState TargetState, string Reason);

    /// <summary>
    /// Manual state change — the admin escape hatch for stuck orders
    /// (T-0107 / US-admin-0010). Strict allow-list: only transitions with
    /// a defensible manual-recovery story are permitted; every blocked
    /// transition with a sanctioned command names it in the 409 error
    /// code (refund → RefundOrder, dispute → Open/ResolveDispute,
    /// completion → MarkPayoutBatchCompleted). Mandatory ≥10-char reason,
    /// recorded in the audit log.
    /// </summary>
    [HttpPost("{orderId}/state")]
    [ProducesResponseType(typeof(ChangeOrderStateManually.ChangeOrderStateManuallyResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ChangeState(
        string orderId, [FromBody] ChangeOrderStateRequest request, CancellationToken ct) =>
        HandleResult(await Mediator.Send(
            new ChangeOrderStateManually.Command(orderId, request.TargetState, request.Reason), ct));
}
