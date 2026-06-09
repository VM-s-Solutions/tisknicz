using Asp.Versioning;
using Makables.Config.Controllers;
using Makables.Core.AppServices.Features.Orders;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Orders.Sorting;
using Makables.Core.Domain.Storage;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Makables.Web.Maker.Controllers;

/// <summary>
/// Maker-host order endpoints (US-maker-0010). T-0064 introduces this
/// controller with the single <see cref="DownloadAttachment"/> action so
/// the maker can read customer-uploaded spec sheets before accepting +
/// shipping. Subsequent tickets (T-0071 accept, T-0072 ship, T-0081 list)
/// add their actions to this same controller.
///
/// <para>
/// Per ADR 0005 / patterns §A.16 — JSON-only, audience-bound. A customer
/// JWT cannot reach this surface (audience policy in
/// <c>AddMakablesAuth</c>); an admin JWT can. No email-confirmed
/// middleware on the Maker host — every maker user passes through the
/// admin verification gate at registration time instead.
/// </para>
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/orders")]
[Authorize]
public sealed class OrdersController(
    IOrderRepository orders,
    IMakerRepository makers,
    IBlobStorageClient blobs,
    IUserSessionProvider session,
    IMediator mediator) : MakablesApiController
{
    /// <summary>
    /// Paged maker order list (T-0081 / US-maker-0005). Default sort is
    /// newest-first; filter by <see cref="OrderState"/> and / or a
    /// <c>CreatedAt</c> date range; page size clamped to
    /// <see cref="GetMakerOrders.MaxPageSize"/>.
    ///
    /// <para>
    /// The owning maker is resolved from the JWT inside the handler; there
    /// is no maker-id query parameter. Cross-maker probes surface as an
    /// empty page (TotalCount = 0). Customer email is NEVER on the wire
    /// per T-0081 §A.2.
    /// </para>
    /// </summary>
    [HttpGet("")]
    [ProducesResponseType(typeof(GetMakerOrders.GetMakerOrdersResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = GetMakerOrders.DefaultPageSize,
        [FromQuery] OrderState? state = null,
        [FromQuery] DateTimeOffset? dateFrom = null,
        [FromQuery] DateTimeOffset? dateTo = null,
        [FromQuery] OrderSort sort = OrderSort.CreatedAtDesc,
        CancellationToken ct = default) =>
        HandleResult(await mediator.Send(
            new GetMakerOrders.Query(page, pageSize, state, dateFrom, dateTo, sort), ct));

    /// <summary>
    /// Maker accepts a Paid order. Transitions to Accepted and enqueues
    /// the customer-notification outbox event atomically per ADR 0014.
    /// T-0071 (US-maker-0006).
    /// </summary>
    [HttpPost("{orderId}/accept")]
    [ProducesResponseType(typeof(AcceptOrder.AcceptOrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Accept(string orderId, CancellationToken ct) =>
        HandleResult(await mediator.Send(new AcceptOrder.Command(orderId), ct));

    /// <summary>
    /// Maker confirms shipment of an Accepted Zásilkovna order. Calls
    /// the Packeta adapter to create the shipment, stamps the carrier
    /// ref + tracking URL onto the Order, transitions Accepted → Shipped,
    /// and atomically enqueues 2 outbox events (customer email +
    /// generate-label). T-0072 (US-maker-0007).
    /// </summary>
    [HttpPost("{orderId}/ship")]
    [ProducesResponseType(typeof(ShipOrder.ShipOrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Ship(string orderId, CancellationToken ct) =>
        HandleResult(await mediator.Send(new ShipOrder.Command(orderId), ct));

    /// <summary>
    /// Maker confirms in-person handover of an Accepted PersonalPickup
    /// order. Transitions Accepted → Shipped with no carrier call + null
    /// tracking URL; enqueues a single customer shipped-email event.
    /// T-0073 (US-maker-0008).
    /// </summary>
    [HttpPost("{orderId}/handover")]
    [ProducesResponseType(typeof(HandOverOrder.HandOverOrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> HandOver(string orderId, CancellationToken ct) =>
        HandleResult(await mediator.Send(new HandOverOrder.Command(orderId), ct));

    /// <summary>
    /// Streaming download of a customer-uploaded order attachment. Same
    /// body as the customer-host equivalent except the ownership scope
    /// is <see cref="IOrderRepository.GetAttachmentForMakerAsync"/> —
    /// the maker assigned to the order can read every attachment on it;
    /// an unassigned maker gets <c>404</c>. T-0064 AC-12.
    /// </summary>
    [HttpGet("{orderId}/attachments/{attachmentId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadAttachment(
        string orderId, string attachmentId, CancellationToken ct)
    {
        var userId = session.GetUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(Error.Unauthorized());
        }

        // Resolve the maker for this user; a user without a maker row is
        // a customer who somehow got a maker-audience token — that's a
        // 404 in this context (no maker → no orders).
        var maker = await makers.GetByUserIdAsync(userId, ct);
        if (maker is null)
        {
            return NotFound(Error.NotFound("attachmentId", BusinessErrorMessage.OrderAttachmentNotFound));
        }

        var attachment = await orders.GetAttachmentForMakerAsync(orderId, attachmentId, maker.Id, ct);
        if (attachment is null)
        {
            return NotFound(Error.NotFound("attachmentId", BusinessErrorMessage.OrderAttachmentNotFound));
        }

        var result = await blobs.DownloadAsync(BlobContainer.OrderAttachments, attachment.BlobPath, ct);
        if (!result.IsSuccess)
        {
            // blob-deleted-but-row-remains edge case (e.g. GDPR purge).
            return NotFound(Error.NotFound("attachmentId", BusinessErrorMessage.OrderAttachmentNotFound));
        }

        var download = result.Value!;

        // Private files; intermediaries must not cache. Force-download
        // via Content-Disposition. Identical cache policy to the
        // customer host — attachments don't change shape between hosts.
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.ContentDisposition =
            $"attachment; filename=\"{EscapeFilenameForHeader(attachment.OriginalFilename)}\"";

        if (!string.IsNullOrEmpty(download.ETag))
        {
            Response.Headers.ETag = download.ETag;

            var ifNoneMatch = Request.Headers.IfNoneMatch.ToString();
            if (!string.IsNullOrEmpty(ifNoneMatch) && ETagMatches(ifNoneMatch, download.ETag))
            {
                await download.Content.DisposeAsync();
                return StatusCode(StatusCodes.Status304NotModified);
            }
        }

        return File(download.Content, download.ContentType, enableRangeProcessing: true);
    }

    private static string EscapeFilenameForHeader(string sanitized) =>
        sanitized.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static bool ETagMatches(string ifNoneMatchHeader, string etag)
    {
        if (ifNoneMatchHeader.Trim() == "*") return true;
        foreach (var candidate in ifNoneMatchHeader.Split(','))
        {
            if (string.Equals(candidate.Trim(), etag, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }
}
