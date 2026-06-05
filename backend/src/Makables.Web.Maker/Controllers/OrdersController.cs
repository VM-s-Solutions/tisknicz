using Asp.Versioning;
using Makables.Config.Controllers;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Storage;
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
    IUserSessionProvider session) : MakablesApiController
{
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
