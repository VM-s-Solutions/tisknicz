using Asp.Versioning;
using Makables.Config.Controllers;
using Makables.Core.AppServices.Features.OrderMessages;
using Makables.Core.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Makables.Web.Customer.Controllers;

/// <summary>
/// Customer-host endpoints for the two-party order-message thread
/// (T-0079, US-customer-0014). Per ADR 0013 + T-0082 precedent the
/// feature surface is compile-time per-audience — these endpoints
/// dispatch the customer-side commands only; the maker-side mirror
/// commands ship on the Maker host.
///
/// <para>
/// The IDOR shield is the WHERE-predicate baked into the scoped
/// repository / queries. A customer JWT literally cannot post or read
/// another customer's thread because the SQL never selects nor inserts
/// against the row.
/// </para>
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/orders/{orderId}/messages")]
[Authorize]
public sealed class OrderMessagesController : MakablesApiController
{
    public sealed record PostOrderMessageRequest(string Body);

    /// <summary>
    /// Paged customer-scoped read of the thread. Page size clamped to
    /// <see cref="GetCustomerOrderMessages.MaxPageSize"/> = 50. Sorted
    /// <c>CreatedAt DESC</c>. Cross-tenant probes return an empty page,
    /// NOT 404, matching the T-0080 list-empty contract.
    /// </summary>
    [HttpGet("")]
    [ProducesResponseType(typeof(GetCustomerOrderMessages.GetCustomerOrderMessagesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(
        string orderId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = GetCustomerOrderMessages.DefaultPageSize,
        CancellationToken ct = default) =>
        HandleResult(await Mediator.Send(
            new GetCustomerOrderMessages.Query(orderId, page, pageSize), ct));

    /// <summary>
    /// Post a new message on the customer's own order. Body ∈ [1, 2000]
    /// chars. Triggers the 5-min debounced
    /// <see cref="OrderMessagePostedMakerEmail"/> outbox row.
    /// </summary>
    [HttpPost("")]
    [ProducesResponseType(typeof(PostCustomerOrderMessage.PostCustomerOrderMessageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Post(
        string orderId,
        [FromBody] PostOrderMessageRequest body,
        CancellationToken ct = default) =>
        HandleResult(await Mediator.Send(
            new PostCustomerOrderMessage.Command(orderId, body.Body), ct));

    /// <summary>
    /// Mark every unread maker-authored message as read. Idempotent: a
    /// second call after the first returns <c>{ MarkedCount: 0 }</c>;
    /// the denormalized counter on <c>orders.customer_unread_message_count</c>
    /// stays at 0. Also clears the customer's pending-debounce pointer
    /// so the maker's next post fires immediately.
    /// </summary>
    [HttpPost("mark-read")]
    [ProducesResponseType(typeof(MarkCustomerOrderMessagesAsRead.MarkCustomerOrderMessagesAsReadResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkRead(string orderId, CancellationToken ct = default) =>
        HandleResult(await Mediator.Send(
            new MarkCustomerOrderMessagesAsRead.Command(orderId), ct));
}
