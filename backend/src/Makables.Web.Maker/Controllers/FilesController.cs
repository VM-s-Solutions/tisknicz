using Asp.Versioning;
using Makables.Config.Controllers;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Invoices;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Shipping;
using Makables.Core.Domain.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Makables.Web.Maker.Controllers;

/// <summary>
/// Maker-host file endpoints. T-0075 ships the shipping-label download
/// endpoint: <c>GET /api/v1/maker/files/orders/{orderId}/label</c>.
///
/// <para>
/// Cache → Packeta fallback. Steady state (after T-0074's queue Function
/// has stored the blob) is a blob hit; first request after Ship may race
/// the queue Function and hit the live-Packeta fallback path with a
/// fire-and-forget cache-fill.
/// </para>
///
/// <para>
/// Cache-Control: <c>public, max-age=31536000, immutable</c> on 200 OK
/// responses per T-0075 locked decision A.3 — labels are deterministic
/// and order-id-keyed. Error responses (404 / 503) are uncached.
/// </para>
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/maker/files")]
[Authorize]
public sealed class FilesController(
    IOrderRepository orders,
    IInvoiceRepository invoices,
    IMakerRepository makers,
    IBlobStorageClient blobs,
    IShippingCarrierFactory carrierFactory,
    IUserSessionProvider session,
    ILogger<FilesController> logger) : MakablesApiController
{
    /// <summary>
    /// Stream the shipping-label PDF for a maker-owned Zásilkovna order.
    /// Returns 404 for ownership mismatches, missing orders, missing
    /// carrier ref (personal-pickup), or Packeta permanent failures.
    /// Returns 503 + <c>Retry-After: 60</c> for transient Packeta
    /// failures during the live-fallback path.
    /// </summary>
    [HttpGet("orders/{orderId}/label")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetShippingLabel(string orderId, CancellationToken ct)
    {
        // Step 1: Session.
        var userId = session.GetUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(Error.Unauthorized());
        }

        // Step 2: Maker resolution.
        var maker = await makers.GetByUserIdAsync(userId, ct);
        if (maker is null)
        {
            return NotFound(Error.NotFound("orderId", BusinessErrorMessage.OrderNotFound));
        }

        // Step 3: Owner-scoped order load. Read-only variant: this
        // endpoint only inspects ShippingCarrierRef + CountryCode and
        // verifies maker ownership; it never mutates the Order, so the
        // AsNoTracking variant saves change-tracking overhead per
        // ADR 0025 §Performance expectations item 2.
        var order = await orders.GetByIdForMakerReadOnlyAsync(orderId, maker.Id, ct);
        if (order is null)
        {
            return NotFound(Error.NotFound("orderId", BusinessErrorMessage.OrderNotFound));
        }

        // Step 4: Verify the order has a carrier ref. Personal-pickup +
        // pre-T-0072 orders return null — same IDOR-resistant 404.
        if (string.IsNullOrEmpty(order.ShippingCarrierRef))
        {
            return NotFound(Error.NotFound("orderId", BusinessErrorMessage.OrderNotFound));
        }

        // Step 5: Compute the deterministic blob path.
        var cc = order.CountryCode.ToLowerInvariant();
        var path = $"{cc}/orders/{order.Id}/label.pdf";

        // Step 6: Cache check.
        var existsResult = await blobs.ExistsAsync(BlobContainer.Invoices, path, ct);
        if (existsResult.IsSuccess && existsResult.Value)
        {
            var downloadResult = await blobs.DownloadAsync(BlobContainer.Invoices, path, ct);
            if (downloadResult.IsSuccess)
            {
                Response.Headers.CacheControl = "public, max-age=31536000, immutable";
                return File(downloadResult.Value!.Content, "application/pdf");
            }
            // Blob row missing despite Exists=true (rare race) — fall through.
        }

        // Step 7: Fallback — live Packeta fetch.
        var carrierResult = await carrierFactory.ResolveAsync(order.CountryCode, ct);
        if (!carrierResult.IsSuccess)
        {
            return MapShippingErrorToResponse(carrierResult.Error!);
        }

        var labelResult = await carrierResult.Value!.GetLabelPdfAsync(order.ShippingCarrierRef, ct);
        if (!labelResult.IsSuccess)
        {
            return MapShippingErrorToResponse(labelResult.Error!);
        }

        // Step 8: Buffer the Packeta stream so we can both stream to the
        // maker AND fire-and-forget cache-fill against a separate copy.
        var buffer = new MemoryStream();
        await using (var packetaStream = labelResult.Value!)
        {
            await packetaStream.CopyToAsync(buffer, ct);
        }
        buffer.Position = 0;

        // Snapshot the underlying byte[] for the background upload —
        // zero-copy via MemoryStream over the same buffer. The response
        // writer reads `buffer` from Position=0→Length when ASP.NET
        // pumps the response; the background Task.Run executes
        // asynchronously and uses its own Position cursor on a
        // separate MemoryStream over the shared byte[]. The Task
        // closure keeps the array reachable until upload completes.
        var sharedArray = buffer.GetBuffer();
        var sharedLength = (int)buffer.Length;
        var orderIdForLog = order.Id;
        _ = Task.Run(async () =>
        {
            try
            {
                using var uploadBuffer = new MemoryStream(
                    sharedArray, 0, sharedLength,
                    writable: false, publiclyVisible: true);
                var uploadResult = await blobs.UploadAsync(
                    BlobContainer.Invoices, path, uploadBuffer, "application/pdf",
                    CancellationToken.None);
                if (!uploadResult.IsSuccess)
                {
                    logger.LogWarning(
                        "Background label cache-fill failed for order {OrderId}: {ErrorCode}",
                        orderIdForLog, uploadResult.Error!.Code);
                }
                else
                {
                    logger.LogInformation(
                        "Background label cache-fill succeeded for order {OrderId}",
                        orderIdForLog);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Background label cache-fill threw for order {OrderId}", orderIdForLog);
            }
        });

        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        return File(buffer, "application/pdf");
    }

    /// <summary>
    /// Stream the maker's Fee-invoice PDF (T-0112a / US-maker-0013):
    /// <c>GET /api/v1/maker/files/invoices/{invoiceId}</c>. Controller-direct
    /// per ADR 0014 §"Handler-free read paths" (sibling to
    /// <see cref="GetShippingLabel"/>). The lookup predicate IS the IDOR shield
    /// (<c>i.Id == invoiceId &amp;&amp; i.MakerId == makerId</c>); a cross-maker
    /// or unknown id, AND a Customer-invoice id via this Fee route, all 404
    /// <c>order.notFound</c> — no existence oracle. A null
    /// <c>PdfBlobPath</c> / blob-miss is 404 <c>invoice.notYetRendered</c>.
    ///
    /// <para>
    /// Cache policy <c>private, no-store</c> + ETag/304 — Fee invoices carry
    /// recipient PII (maker name, IČO/DIČ, address); NOT the label's
    /// <c>public, immutable</c> family.
    /// </para>
    /// </summary>
    [HttpGet("invoices/{invoiceId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadFeeInvoice(string invoiceId, CancellationToken ct)
    {
        // Step 1: session.
        var userId = session.GetUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(Error.Unauthorized());
        }

        // Step 2: maker resolution. A maker-audience user with no maker row
        // 404s order.notFound and the invoice lookup is never performed
        // (T-0088 AC-6 parity).
        var maker = await makers.GetByUserIdAsync(userId, ct);
        if (maker is null)
        {
            return NotFound(Error.NotFound("invoiceId", BusinessErrorMessage.OrderNotFound));
        }

        // Step 3: ownership-scoped read-only invoice load. Null for unknown ids
        // AND cross-maker ids — same 404 shape (no IDOR oracle).
        var invoice = await invoices.GetForMakerReadOnlyAsync(invoiceId, maker.Id, ct);
        if (invoice is null)
        {
            return NotFound(Error.NotFound("invoiceId", BusinessErrorMessage.OrderNotFound));
        }

        // Step 4: route-purpose Fee gate — a maker-owned Customer invoice via
        // this Fee route gets the same not-found shape (no leak that a Customer
        // invoice with that id exists). Fires BEFORE the blob read.
        if (invoice.Type != InvoiceType.Fee)
        {
            return NotFound(Error.NotFound("invoiceId", BusinessErrorMessage.OrderNotFound));
        }

        // Step 5: not-yet-rendered (PdfBlobPath null) → retryable 404.
        if (string.IsNullOrEmpty(invoice.PdfBlobPath))
        {
            return NotFound(Error.NotFound("invoice", BusinessErrorMessage.InvoiceNotYetRendered));
        }

        // Step 6: stream from the private Invoices container (PdfBlobPath is
        // container-relative; used verbatim per ADR 0011).
        var result = await blobs.DownloadAsync(BlobContainer.Invoices, invoice.PdfBlobPath, ct);
        if (!result.IsSuccess)
        {
            // Blob-purged-but-row-remains race — retryable 404.
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

        // Range processing off per T-0075/T-0088 rationale (small platform
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

    private IActionResult MapShippingErrorToResponse(Error error)
    {
        // Permanent → 404 (carrier lost the label permanently — treat as
        // not-found from the maker's POV per T-0075 locked B).
        if (error.Type == ErrorType.Permanent)
        {
            return NotFound(Error.NotFound("orderId", BusinessErrorMessage.OrderNotFound));
        }
        // Transient / Configuration / Unknown → 503 + Retry-After: 60.
        if (error.Type is ErrorType.Transient or ErrorType.Configuration)
        {
            Response.Headers["Retry-After"] = "60";
        }
        return StatusCode(StatusCodes.Status503ServiceUnavailable,
            Error.Transient(BusinessErrorMessage.ShippingCarrierUnavailable));
    }
}
