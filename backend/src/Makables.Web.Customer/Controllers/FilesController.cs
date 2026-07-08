using Asp.Versioning;
using Makables.Config.Controllers;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Shipping;
using Makables.Core.Domain.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Makables.Web.Customer.Controllers;

/// <summary>
/// Customer-host file endpoints. T-0146 ships the return-label download
/// endpoint: <c>GET /api/v1/customer/files/disputes/{disputeId}/return-label</c>
/// — customer-side mirror of the maker-host T-0075
/// <c>FilesController.GetShippingLabel</c>, pointed at the dispute-scoped
/// reverse-shipment blob path instead of the order-scoped forward one.
///
/// <para>
/// Cache → Packeta fallback, same shape as the forward download: a blob
/// hit streams straight through; a miss (race with the T-0074-pattern
/// queue Function, or the Function hasn't run yet) falls back to a live
/// Packeta call with a fire-and-forget cache-fill.
/// </para>
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/customer/files")]
[Authorize]
public sealed class FilesController(
    IDisputeRepository disputes,
    IBlobStorageClient blobs,
    IShippingCarrierFactory carrierFactory,
    IUserSessionProvider session,
    ILogger<FilesController> logger) : MakablesApiController
{
    /// <summary>
    /// Stream the reverse-shipment ("vratkový štítek") label PDF for a
    /// customer-owned dispute. 404 for ownership mismatches, missing
    /// disputes, or a dispute with no return label yet (AC-7 IDOR
    /// shield — same not-found shape for all three). 503 + Retry-After
    /// for transient Packeta failures during the live-fallback path.
    /// </summary>
    [HttpGet("disputes/{disputeId}/return-label")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetReturnLabel(string disputeId, CancellationToken ct)
    {
        // Step 1: session.
        var userId = session.GetUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(Error.Unauthorized());
        }

        // Step 2: owner-scoped read-only dispute load (IDOR shield, AC-7).
        var dispute = await disputes.GetByIdForCustomerReadOnlyAsync(disputeId, userId, ct);
        if (dispute is null)
        {
            return NotFound(Error.NotFound("disputeId", BusinessErrorMessage.OrderDisputeNotFound));
        }

        // Step 3: no return label yet — same not-found shape (no IDOR
        // oracle distinguishing "not yours" from "not generated yet").
        if (string.IsNullOrEmpty(dispute.ReturnCarrierRef))
        {
            return NotFound(Error.NotFound("disputeId", BusinessErrorMessage.OrderDisputeNotFound));
        }

        // Step 4: deterministic dispute-scoped blob path.
        var cc = dispute.CountryCode.ToLowerInvariant();
        var path = $"{cc}/disputes/{dispute.Id}/return-label.pdf";

        // Step 5: cache check.
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

        // Step 6: fallback — live Packeta fetch.
        var carrierResult = await carrierFactory.ResolveAsync(dispute.CountryCode, ct);
        if (!carrierResult.IsSuccess)
        {
            return MapShippingErrorToResponse(carrierResult.Error!);
        }

        var labelResult = await carrierResult.Value!.GetLabelPdfAsync(dispute.ReturnCarrierRef, ct);
        if (!labelResult.IsSuccess)
        {
            return MapShippingErrorToResponse(labelResult.Error!);
        }

        // Step 7: buffer so we can both stream to the customer AND
        // fire-and-forget cache-fill against a separate copy over the
        // same buffer (T-0075 pattern verbatim).
        var buffer = new MemoryStream();
        await using (var packetaStream = labelResult.Value!)
        {
            await packetaStream.CopyToAsync(buffer, ct);
        }
        buffer.Position = 0;

        var sharedArray = buffer.GetBuffer();
        var sharedLength = (int)buffer.Length;
        var disputeIdForLog = dispute.Id;
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
                        "Background return-label cache-fill failed for dispute {DisputeId}: {ErrorCode}",
                        disputeIdForLog, uploadResult.Error!.Code);
                }
                else
                {
                    logger.LogInformation(
                        "Background return-label cache-fill succeeded for dispute {DisputeId}",
                        disputeIdForLog);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Background return-label cache-fill threw for dispute {DisputeId}", disputeIdForLog);
            }
        });

        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        return File(buffer, "application/pdf");
    }

    private IActionResult MapShippingErrorToResponse(Error error)
    {
        if (error.Type == ErrorType.Permanent)
        {
            return NotFound(Error.NotFound("disputeId", BusinessErrorMessage.OrderDisputeNotFound));
        }
        if (error.Type is ErrorType.Transient or ErrorType.Configuration)
        {
            Response.Headers["Retry-After"] = "60";
        }
        return StatusCode(StatusCodes.Status503ServiceUnavailable,
            Error.Transient(BusinessErrorMessage.ShippingCarrierUnavailable));
    }
}
