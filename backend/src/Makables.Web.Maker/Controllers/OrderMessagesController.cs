using Asp.Versioning;
using Makables.Config.Controllers;
using Makables.Core.AppServices.Features.OrderMessages;
using Makables.Core.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Makables.Web.Maker.Controllers;

/// <summary>
/// Maker-host endpoints for the two-party order-message thread (T-0079,
/// US-maker-0011). Symmetric to the Customer host; compile-time
/// per-audience IDOR shield per ADR 0013.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/orders/{orderId}/messages")]
[Authorize]
public sealed class OrderMessagesController : MakablesApiController
{
    public sealed record PostOrderMessageRequest(string Body);

    [HttpGet("")]
    [ProducesResponseType(typeof(GetMakerOrderMessages.GetMakerOrderMessagesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(
        string orderId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = GetMakerOrderMessages.DefaultPageSize,
        CancellationToken ct = default) =>
        HandleResult(await Mediator.Send(
            new GetMakerOrderMessages.Query(orderId, page, pageSize), ct));

    [HttpPost("")]
    [ProducesResponseType(typeof(PostMakerOrderMessage.PostMakerOrderMessageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Post(
        string orderId,
        [FromBody] PostOrderMessageRequest body,
        CancellationToken ct = default) =>
        HandleResult(await Mediator.Send(
            new PostMakerOrderMessage.Command(orderId, body.Body), ct));

    [HttpPost("mark-read")]
    [ProducesResponseType(typeof(MarkMakerOrderMessagesAsRead.MarkMakerOrderMessagesAsReadResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkRead(string orderId, CancellationToken ct = default) =>
        HandleResult(await Mediator.Send(
            new MarkMakerOrderMessagesAsRead.Command(orderId), ct));
}
