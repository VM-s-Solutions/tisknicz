using Asp.Versioning;
using Makables.Config.Controllers;
using Makables.Core.AppServices.Features.Orders;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Orders.Validators;
using Makables.Core.Domain.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Makables.Web.Customer.Controllers;

/// <summary>
/// Customer-host order endpoints (US-customer-0010 / US-customer-0011).
/// First controller on the Customer host — sets the convention for
/// every Phase-4 customer endpoint that follows (T-0064 attachments,
/// T-0080 list, T-0082 detail, T-0083 cancel).
///
/// <para>
/// Per ADR 0005 / patterns §A.16 — JSON-only, audience-bound. A
/// maker JWT cannot reach this surface (audience policy in
/// <c>AddMakablesAuth</c>); an admin JWT can. The email-confirmed gate
/// runs as host middleware (<c>RequireEmailConfirmedMiddleware</c>) so
/// every authenticated endpoint here inherits the 403 path without
/// per-action plumbing.
/// </para>
///
/// <para>
/// <b>Adapter discipline.</b> Comgate session creation is NOT in
/// CreateOrder per user decision Q1 — the frontend navigates to
/// <c>/objednavka/&lt;orderId&gt;</c> after a successful POST and
/// triggers T-0065's <c>CreatePaymentSession</c> from that page. The
/// order persists in <see cref="OrderState.PendingPayment"/> in the
/// meantime; if Comgate is down the customer can retry inside the 24-hour
/// window (US-customer-0010 AC-3).
/// </para>
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/orders")]
[Authorize]
public sealed class OrdersController(
    IOrderRepository orders,
    IBlobStorageClient blobs,
    IUserSessionProvider session,
    IIdGenerator ids) : MakablesApiController
{
    public sealed record CreateOrderRequest(
        string ProductId,
        int Quantity,
        ShippingMethod ShippingMethod,
        string? ZasilkovnaPickupPointId,
        string CustomerName,
        string CustomerEmail,
        string CustomerPhone,
        string? CustomerNotes);

    // Controller-level wrapper to dodge the OpenAPI schema-name collision
    // pattern from ProductController.cs:49-58. Every Features/*/Xxx.Response
    // would emit as "Response" and NSwag picks whichever wins the
    // collision; wrapping into a unique top-level shape gives the spec a
    // stable schema name (CreateOrderResponse) without touching the CQRS
    // nesting convention.
    public sealed record CreateOrderResponse(
        string OrderId,
        string OrderNumber,
        long TotalPriceMinor,
        string Currency);

    // Same schema-collision dodge for the attachment upload action — the
    // handler's nested AddOrderAttachment.Response would conflict with
    // every other "Response" in the spec. T-0064.
    public sealed record UploadOrderAttachmentResponse(
        string AttachmentId,
        string OriginalFilename,
        long SizeBytes,
        DateTimeOffset UploadedOn);

    // Same schema-collision dodge for the payment-session action. T-0065.
    public sealed record CreatePaymentSessionResponse(
        string PaymentProviderRef,
        string RedirectUrl);

    /// <summary>
    /// Create a customer order in <see cref="OrderState.PendingPayment"/>.
    /// Returns the four fields the frontend uses to navigate to the
    /// order page and trigger T-0065's payment-session creation.
    ///
    /// <para>
    /// 401 is declared via <see cref="ProducesResponseTypeAttribute"/>
    /// for OpenAPI completeness even though, on the normal controller
    /// path, the framework's authentication challenge can short-circuit
    /// before the handler runs. The handler still has an Unauthorized
    /// backstop for non-controller callers (e.g. a future cron), which
    /// returns a typed <see cref="Error"/>.
    /// </para>
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(CreateOrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] CreateOrderRequest body, CancellationToken ct)
    {
        var result = await Mediator.Send(new CreateOrder.Command(
            ProductId: body.ProductId,
            Quantity: body.Quantity,
            ShippingMethod: body.ShippingMethod,
            ZasilkovnaPickupPointId: body.ZasilkovnaPickupPointId,
            CustomerName: body.CustomerName,
            CustomerEmail: body.CustomerEmail,
            CustomerPhone: body.CustomerPhone,
            CustomerNotes: body.CustomerNotes), ct);

        // Project the handler's nested Response into the controller-level
        // shape so the OpenAPI schema gets a unique top-level name (see
        // CreateOrderResponse remark above).
        return result.IsSuccess
            ? HandleResult(BusinessResult.Success(new CreateOrderResponse(
                result.Value!.OrderId,
                result.Value.OrderNumber,
                result.Value.TotalPriceMinor,
                result.Value.Currency)))
            : HandleResult(BusinessResult.Failure<CreateOrderResponse>(result.Error!));
    }

    /// <summary>
    /// Create (or re-use) a Comgate payment session for an order in
    /// <see cref="OrderState.PendingPayment"/>. Returns the redirect URL
    /// the frontend navigates the customer to and the provider's session
    /// reference (Comgate <c>transId</c>). T-0065 / US-customer-0010 AC-2.
    ///
    /// <para>
    /// Per user decision Q1 the 24h retry window is handled inside the
    /// handler: a second call within the window with the existing Comgate
    /// session still <see cref="Payments.PaymentState.Pending"/> /
    /// <see cref="Payments.PaymentState.Authorized"/> returns the same
    /// cached URL without a new Comgate roundtrip.
    /// </para>
    /// </summary>
    [HttpPost("{orderId}/payment-session")]
    [ProducesResponseType(typeof(CreatePaymentSessionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> CreatePaymentSession(string orderId, CancellationToken ct)
    {
        var result = await Mediator.Send(new CreatePaymentSession.Command(orderId), ct);

        // Project the handler's nested Response into the controller-level
        // shape so the OpenAPI schema gets a unique top-level name.
        return result.IsSuccess
            ? HandleResult(BusinessResult.Success(new CreatePaymentSessionResponse(
                result.Value!.PaymentProviderRef,
                result.Value.RedirectUrl)))
            : HandleResult(BusinessResult.Failure<CreatePaymentSessionResponse>(result.Error!));
    }

    /// <summary>
    /// Upload a customer-provided attachment (PDF / JPEG / PNG / WebP,
    /// ≤ 10 MiB) onto an order in <see cref="OrderState.PendingPayment"/>
    /// / <see cref="OrderState.Paid"/> / <see cref="OrderState.Accepted"/>.
    /// Mirrors <c>ProductController.UploadImage</c> step-by-step.
    /// T-0064.
    /// </summary>
    [HttpPost("{orderId}/attachments")]
    [RequestSizeLimit(OrderAttachmentValidator.MaxSizeBytes + 4096)]  // file + small multipart overhead
    [ProducesResponseType(typeof(UploadOrderAttachmentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UploadAttachment(
        string orderId, IFormFile file, CancellationToken ct)
    {
        // Bare IFormFile parameter — multipart schema rewritten to the
        // canonical { type: "string", format: "binary" } by the T-0049c
        // OpenAPI operation transformer. The defensive null / zero-length
        // check below stays: spec-level `required: ["file"]` informs the
        // client, but the server still enforces the runtime contract.
        if (file is null || file.Length == 0)
        {
            return BadRequest(Error.Validation("file", BusinessErrorMessage.FileInvalid));
        }

        var userId = session.GetUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(Error.Unauthorized());
        }

        // Order ownership pre-check. IDOR-shielded — null both for unknown
        // ids AND cross-customer ids; surfaces as 404 per AC-2.
        var order = await orders.GetByIdForCustomerAsync(orderId, userId, ct);
        if (order is null)
        {
            return NotFound(Error.NotFound("orderId", BusinessErrorMessage.OrderNotFound));
        }

        // State + count fast-path. Both are re-checked under the UoW
        // transaction in AddOrderAttachment.Handler (race defence).
        if (!order.AllowsAttachmentUpload())
        {
            return Conflict(Error.Conflict("order", BusinessErrorMessage.OrderStateForbidsAttachment));
        }
        if (order.Attachments.Count >= Order.MaxAttachmentCount)
        {
            return Conflict(Error.Conflict("attachments", BusinessErrorMessage.OrderAttachmentLimitReached));
        }

        // Buffer the header bytes for the magic-byte sniff.
        await using var stream = file.OpenReadStream();
        var header = new byte[OrderAttachmentValidator.RequiredHeaderBytes];
        var read = await ReadAtLeastAsync(stream, header, ct);

        var validation = OrderAttachmentValidator.Validate(
            file.ContentType, file.Length, header.AsSpan(0, read));
        if (validation != OrderAttachmentValidator.Result.Valid)
        {
            var code = validation switch
            {
                OrderAttachmentValidator.Result.TooLarge => BusinessErrorMessage.FileTooLarge,
                OrderAttachmentValidator.Result.UnsupportedType => BusinessErrorMessage.FileUnsupportedType,
                _ => BusinessErrorMessage.FileInvalid,  // MagicByteMismatch → generic "invalid"
            };
            return BadRequest(Error.Validation("file", code));
        }

        // Sanitize the customer-supplied filename for display only.
        // Strip path separators / control chars / null bytes; trim; cap
        // to the column length. The blob path is built from a fresh ulid
        // and never uses this value.
        var sanitizedFilename = SanitizeFilename(file.FileName);

        // Build the blob path: {country}/orders/{orderId}/{ulid}.{ext}.
        // Random id in the filename prevents collisions + guessing.
        var ext = OrderAttachmentValidator.ExtensionFor(file.ContentType);
        var filename = $"{ids.Next()}.{ext}";
        var blobPath = $"{order.CountryCode.ToLowerInvariant()}/orders/{orderId}/{filename}";

        // Re-open the stream from the start (we consumed the header bytes).
        await using var uploadStream = file.OpenReadStream();
        var upload = await blobs.UploadAsync(
            BlobContainer.OrderAttachments, blobPath, uploadStream, file.ContentType, ct);
        if (!upload.IsSuccess)
        {
            return HandleResult(upload);
        }

        // Attach the blob path to the aggregate. If the attach fails —
        // wrong owner (NotFound), state-gate (Conflict), count (Conflict)
        // — the blob we just uploaded would be orphaned. Best-effort
        // delete it on failure so a rejected upload leaves no residue.
        // Mirrors ProductController.cs:228-232 / T-0041 Copilot review.
        var attach = await Mediator.Send(new AddOrderAttachment.Command(
            OrderId: orderId,
            BlobPath: blobPath,
            OriginalFilename: sanitizedFilename,
            ContentType: file.ContentType,
            SizeBytes: file.Length), ct);
        if (!attach.IsSuccess)
        {
            await blobs.DeleteAsync(BlobContainer.OrderAttachments, blobPath, ct);
            return HandleResult(BusinessResult.Failure<UploadOrderAttachmentResponse>(attach.Error!));
        }

        // Project the handler's nested Response into the controller-level
        // shape so the OpenAPI schema gets a unique top-level name.
        return HandleResult(BusinessResult.Success(new UploadOrderAttachmentResponse(
            AttachmentId: attach.Value!.AttachmentId,
            OriginalFilename: attach.Value.OriginalFilename,
            SizeBytes: attach.Value.SizeBytes,
            UploadedOn: attach.Value.UploadedOn)));
    }

    /// <summary>
    /// Streaming download of a customer-uploaded order attachment.
    /// Different cache policy from <c>ProductImageController</c>:
    /// <c>private, no-store</c> because the bytes belong to the
    /// customer and a logged-out request must miss the cache and 401.
    /// Conditional GET via <c>ETag</c> / <c>If-None-Match</c> mirrors the
    /// product-image precedent. T-0064 AC-10/AC-11.
    /// </summary>
    [HttpGet("{orderId}/attachments/{attachmentId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadAttachment(
        string orderId, string attachmentId, CancellationToken ct)
    {
        var userId = session.GetUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(Error.Unauthorized());
        }

        var attachment = await orders.GetAttachmentForCustomerAsync(orderId, attachmentId, userId, ct);
        if (attachment is null)
        {
            return NotFound(Error.NotFound("attachmentId", BusinessErrorMessage.OrderAttachmentNotFound));
        }

        var result = await blobs.DownloadAsync(BlobContainer.OrderAttachments, attachment.BlobPath, ct);
        if (!result.IsSuccess)
        {
            // Covers the rare blob-deleted-but-row-remains case (e.g. a
            // GDPR purge that wiped the blob first). Surfaces as 404 with
            // the attachment-shaped code so the i18n key resolves.
            return NotFound(Error.NotFound("attachmentId", BusinessErrorMessage.OrderAttachmentNotFound));
        }

        var download = result.Value!;

        // Private files; intermediaries must not cache. Force-download via
        // Content-Disposition so the browser treats a JPEG attachment the
        // same as a PDF (the user uploaded a spec sheet, they expect to
        // save it). T-0064 §"Technical notes".
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.ContentDisposition =
            $"attachment; filename=\"{EscapeFilenameForHeader(attachment.OriginalFilename)}\"";

        if (!string.IsNullOrEmpty(download.ETag))
        {
            Response.Headers.ETag = download.ETag;

            // Conditional GET: if the client's cached ETag matches, skip
            // the body and 304. Must dispose the download stream here —
            // a 304 has no body, so nothing else will. Match any of the
            // (possibly comma-separated) If-None-Match values.
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
    /// Strip path separators, control chars, null bytes, and surrounding
    /// whitespace from the customer-supplied filename. Truncate to the
    /// column length. Unicode is preserved (Czech filenames such as
    /// <c>Příloha.pdf</c> survive). Per T-0064 §"Original filename
    /// sanitization".
    /// </summary>
    private static string SanitizeFilename(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "attachment";
        }

        var cleaned = new System.Text.StringBuilder(raw.Length);
        foreach (var ch in raw)
        {
            if (ch is '/' or '\\' or ':' or '\0') continue;
            if (ch < 0x20) continue;  // control chars
            cleaned.Append(ch);
        }
        var trimmed = cleaned.ToString().Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return "attachment";
        }
        if (trimmed.Length > OrderAttachment.MaxOriginalFilenameLength)
        {
            trimmed = trimmed[..OrderAttachment.MaxOriginalFilenameLength];
        }
        return trimmed;
    }

    /// <summary>
    /// Escape any double-quote / backslash in the filename so the
    /// quoted-string form of <c>Content-Disposition</c> stays parseable.
    /// Sanitize-step already stripped path separators + control chars, so
    /// only the quote-string escaping is needed here.
    /// </summary>
    private static string EscapeFilenameForHeader(string sanitized) =>
        sanitized.Replace("\\", "\\\\").Replace("\"", "\\\"");

    /// <summary>
    /// Match an <c>If-None-Match</c> header against the blob's current
    /// <c>ETag</c>. Same shape as
    /// <c>ProductImageController.ETagMatches</c>.
    /// </summary>
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

    /// <summary>
    /// Read up to <paramref name="buffer"/>.Length bytes, tolerating
    /// short reads (a stream may return fewer bytes per call). Returns
    /// the number actually read — fewer than the buffer length only at
    /// genuine end-of-stream. Same helper as
    /// <c>ProductController.ReadAtLeastAsync</c>.
    /// </summary>
    private static async Task<int> ReadAtLeastAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(total), ct);
            if (n == 0) break;
            total += n;
        }
        return total;
    }
}
