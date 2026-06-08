using Asp.Versioning;
using Makables.Config.Controllers;
using Makables.Core.Domain.Common;
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

        // Step 3: Owner-scoped order load.
        var order = await orders.GetByIdForMakerAsync(orderId, maker.Id, ct);
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

        // Snapshot bytes for the background upload — a second MemoryStream
        // so the response writer + background uploader don't race on Position.
        var uploadBytes = buffer.ToArray();
        var orderIdForLog = order.Id;
        _ = Task.Run(async () =>
        {
            try
            {
                using var uploadBuffer = new MemoryStream(uploadBytes, writable: false);
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
