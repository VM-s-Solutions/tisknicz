using Asp.Versioning;
using Makables.Config.Controllers;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Invoices;
using Makables.Core.Domain.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Makables.Web.Admin.Controllers;

/// <summary>
/// Admin-host invoice file endpoints (T-0126 / Q-0026). Streams the rendered
/// invoice PDF for ANY invoice by id — the admin is the privileged actor, so
/// unlike the customer/maker download surfaces (T-0088 / T-0112a) there is no
/// order/maker owner to scope to: the lookup is <b>Unscoped by id</b> via
/// <see cref="IInvoiceRepository.GetByIdUnscopedReadOnlyAsync"/> (ADR 0013
/// §"Unscoped escape hatch is admin-host only").
///
/// <para>
/// Controller-direct streaming per the T-0088 precedent (ADR 0014 §"Handler-free
/// read paths"): a passthrough read with no validation rule and no transaction
/// does not justify a MediatR feature. Mirrors the customer/maker download
/// shape exactly — only the ownership lookup is swapped for the Unscoped read.
/// </para>
///
/// <para>
/// Cache policy <c>private, no-store</c> + ETag/304 — invoices carry recipient
/// PII (name, address, tax ids), so a logged-out request must miss every cache
/// and 401 (T-0064/T-0088 PII policy). <c>[Authorize]</c> under the admin
/// audience (ADR 0013): a customer/maker JWT cannot replay here.
/// </para>
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin-invoices")]
[Authorize]
public sealed class AdminInvoicesController(
    IInvoiceRepository invoices,
    IBlobStorageClient blobs) : MakablesApiController
{
    /// <summary>
    /// Stream the rendered invoice PDF for any invoice by id (T-0126 / Q-0026 /
    /// US-admin-0012 AC-2). 404 <c>invoice.notYetRendered</c> for an unknown id,
    /// a null <c>PdfBlobPath</c> (the T-0068b render pipeline hasn't finished),
    /// or a blob-purged-but-row-remains race — exactly as T-0088 (no new error
    /// code). 304 on a matching <c>If-None-Match</c>.
    /// </summary>
    [HttpGet("{invoiceId}/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadInvoice(string invoiceId, CancellationToken ct)
    {
        // Unscoped read-only lookup by id (ADR 0013/0025). Admin sees ANY
        // invoice — no owner predicate. A null row OR a row whose PdfBlobPath
        // is still null both mean the render pipeline hasn't produced a PDF:
        // invoice.notYetRendered (reused from T-0069, exactly as T-0088). The
        // admin-host audience is the security control for this Unscoped read.
        var invoice = await invoices.GetByIdUnscopedReadOnlyAsync(invoiceId, ct);
        if (invoice is null || string.IsNullOrEmpty(invoice.PdfBlobPath))
        {
            return NotFound(Error.NotFound("invoice", BusinessErrorMessage.InvoiceNotYetRendered));
        }

        // PdfBlobPath is container-relative and used verbatim against the
        // private Invoices container (the IEmailSendService precedent). No
        // path construction here.
        var result = await blobs.DownloadAsync(BlobContainer.Invoices, invoice.PdfBlobPath, ct);
        if (!result.IsSuccess)
        {
            // Blob-purged-but-row-remains race — honest transient-shaped 404;
            // the invoice.notYetRendered copy describes a retryable state
            // (T-0088 Option F rationale).
            return NotFound(Error.NotFound("invoice", BusinessErrorMessage.InvoiceNotYetRendered));
        }

        var download = result.Value!;

        // Recipient-PII document; intermediaries must not cache. Invoice
        // numbers are platform-generated (FV-CZ-NNNNNNNN) but run through the
        // header escape defensively anyway.
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

        // Range processing off per T-0075/T-0088 rationale: invoices are small
        // platform artifacts; range support is attack surface for zero benefit.
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
