using Asp.Versioning;
using Makables.Config.Controllers;
using Makables.Core.AppServices.Features.Orders;
using Makables.Core.AppServices.Features.Payouts;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Invoices;
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
    IInvoiceRepository invoices,
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
    /// Maker order detail (T-0082 / US-maker-0010). Lifecycle snapshot
    /// + price breakdown including the maker's net payout + customer
    /// contact name + phone (never email) + carrier ref + inline
    /// attachments + invoice PDF URL. Cross-maker probes return 404.
    /// </summary>
    [HttpGet("{orderId}")]
    [ProducesResponseType(typeof(GetMakerOrderDetails.GetMakerOrderDetailsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(string orderId, CancellationToken ct) =>
        HandleResult(await mediator.Send(new GetMakerOrderDetails.Query(orderId), ct));

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
    /// Maker REFUSES a paid order they cannot fulfil (T-0181 / Q-0041).
    /// Paid-only and only within
    /// <c>CountryConfiguration.MakerRefusalWindowHours</c> of
    /// <c>PaidAt</c> — past that the refusal goes through admin support
    /// (409 <c>order.refusalWindowExpired</c>), which is the pre-T-0181
    /// status quo. Refunds the full remaining amount through the payment
    /// provider BEFORE mutating the order, then cancels it with
    /// <c>CancellationSource = Maker</c> and notifies the customer.
    /// Re-calling on an already-cancelled order is a 200 (Silent Success)
    /// with no second refund.
    /// </summary>
    [HttpPost("{orderId}/refuse")]
    [ProducesResponseType(typeof(RefuseOrder.RefuseOrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Refuse(
        string orderId, [FromBody] RefuseOrderRequest body, CancellationToken ct) =>
        HandleResult(await mediator.Send(new RefuseOrder.Command(orderId, body.Reason), ct));

    /// <summary>Body for <see cref="Refuse"/> — the maker must say why.</summary>
    public sealed record RefuseOrderRequest(string Reason);

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

    /// <summary>Request body for <see cref="OpenDisputeAction"/>. The order id rides the route.</summary>
    public sealed record OpenMakerDisputeRequest(DisputeCategory Category, string Description);

    /// <summary>
    /// Maker opens a dispute on an order assigned to them (T-0106 /
    /// US-admin-0011 — mirror of the customer endpoint with the maker
    /// ownership scope). Carrier-reserved categories are rejected (400);
    /// re-opening an already-disputed order returns the existing
    /// dispute's id (200, Silent Success).
    /// </summary>
    [HttpPost("{orderId}/dispute")]
    [ProducesResponseType(typeof(OpenMakerDispute.OpenMakerDisputeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> OpenDisputeAction(
        string orderId, [FromBody] OpenMakerDisputeRequest request, CancellationToken ct) =>
        HandleResult(await mediator.Send(
            new OpenMakerDispute.Command(orderId, request.Category, request.Description), ct));

    /// <summary>
    /// Maker-scoped, read-only outbox audit trail for one of the maker's
    /// orders (T-0112 / US-maker-0017): event type + a derived delivery status
    /// + timestamp only. NO payload internals (customer PII), no error code, no
    /// retry (admin-only). A cross-maker / unknown order id returns an EMPTY
    /// page (IDOR shield in the projection — not 403, not an oracle).
    /// </summary>
    [HttpGet("{orderId}/events")]
    [ProducesResponseType(typeof(GetMakerOutboxEventsForOrder.GetMakerOutboxEventsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Events(
        string orderId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = GetMakerOutboxEventsForOrder.DefaultPageSize,
        CancellationToken ct = default) =>
        HandleResult(await mediator.Send(
            new GetMakerOutboxEventsForOrder.Query(orderId, page, pageSize), ct));

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

    /// <summary>
    /// Streaming download of the order's invoice PDF (T-0088 /
    /// US-maker-0010). Same body as the customer-host equivalent except
    /// the ownership scope is the T-0075 maker-resolution prefix
    /// (session → maker row → <see cref="IOrderRepository.GetByIdForMakerReadOnlyAsync"/>).
    /// The route is the literal string T-0082's maker detail projection
    /// emits as <c>InvoicePdfUrl</c> — changing it is forbidden per
    /// T-0088 §A.1. Controller-direct per ADR 0014 §"Handler-free read
    /// paths".
    ///
    /// <para>
    /// Cache policy mirrors <see cref="DownloadAttachment"/> —
    /// <c>private, no-store</c> + ETag/304: invoices carry recipient PII,
    /// NOT the label's <c>public, immutable</c> family.
    /// </para>
    /// </summary>
    [HttpGet("{orderId}/invoice")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadInvoice(string orderId, CancellationToken ct)
    {
        var userId = session.GetUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(Error.Unauthorized());
        }

        // Resolve the maker for this user; a user without a maker row is
        // a customer who somehow got a maker-audience token — 404 in this
        // context (no maker → no orders), and the order lookup is never
        // performed (T-0088 AC-6).
        var maker = await makers.GetByUserIdAsync(userId, ct);
        if (maker is null)
        {
            return NotFound(Error.NotFound("orderId", BusinessErrorMessage.OrderNotFound));
        }

        // Ownership-scoped read-only order load. IDOR-shielded — null for
        // unknown ids AND cross-maker ids; same 404 shape (no existence
        // oracle). Read-only variant per ADR 0025.
        var order = await orders.GetByIdForMakerReadOnlyAsync(orderId, maker.Id, ct);
        if (order is null)
        {
            return NotFound(Error.NotFound("orderId", BusinessErrorMessage.OrderNotFound));
        }

        // GetByOrderIdReadOnlyAsync is documented Unscoped — safe ONLY
        // after the ownership-scoped order load above (T-0088 risk note).
        // Read-only variant per ADR 0025 (Gate 8 B2): this action never
        // mutates the Invoice. No Invoice row yet OR null PdfBlobPath
        // both mean the T-0068b render pipeline hasn't finished:
        // invoice.notYetRendered.
        var invoice = await invoices.GetByOrderIdReadOnlyAsync(orderId, ct);
        if (invoice is null || string.IsNullOrEmpty(invoice.PdfBlobPath))
        {
            return NotFound(Error.NotFound("invoice", BusinessErrorMessage.InvoiceNotYetRendered));
        }

        // PdfBlobPath is container-relative, used verbatim against the
        // Invoices container. No path construction here.
        var result = await blobs.DownloadAsync(BlobContainer.Invoices, invoice.PdfBlobPath, ct);
        if (!result.IsSuccess)
        {
            // Blob-purged-but-row-remains race — retryable 404 per the
            // invoice.notYetRendered i18n copy. T-0088 Option F rationale.
            return NotFound(Error.NotFound("invoice", BusinessErrorMessage.InvoiceNotYetRendered));
        }

        var download = result.Value!;

        // Recipient-PII document; intermediaries must not cache.
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.ContentDisposition =
            $"attachment; filename=\"faktura-{EscapeFilenameForHeader(invoice.InvoiceNumber)}.pdf\"";

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

        // Range processing off per T-0075 rationale (small platform
        // artifact; range support is attack surface for zero benefit).
        return File(download.Content, "application/pdf", enableRangeProcessing: false);
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
