using Asp.Versioning;
using Makables.Config.Controllers;
using Makables.Core.Domain.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Makables.Web.Public.Controllers;

/// <summary>
/// Public-host streaming endpoint for product images per ADR 0011
/// §"All access through the backend — no direct browser → blob links".
/// Anonymous + ETag-cached (the <c>product-images</c> container is the
/// one public-read container, but the browser still only ever sees a
/// backend URL so we keep a single access-control surface).
///
/// <para>
/// The country prefix is taken from the request so the blob path
/// reconstructs as <c>{country}/products/{productId}/{filename}</c>.
/// Path segments are validated by the blob adapter (no traversal); a
/// missing blob surfaces as 404.
/// </para>
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/files/products")]
[AllowAnonymous]
public sealed class ProductImageController(IBlobStorageClient blobs) : MakablesApiController
{
    [HttpGet("{country}/{productId}/{filename}")]
    public async Task<IActionResult> Get(string country, string productId, string filename, CancellationToken ct)
    {
        var blobPath = $"{country.ToLowerInvariant()}/products/{productId}/{filename}";
        var result = await blobs.DownloadAsync(BlobContainer.ProductImages, blobPath, ct);
        if (!result.IsSuccess)
        {
            return HandleResult(result);
        }

        var download = result.Value!;

        // Public, day-cached, with the blob's strong ETag so a repeat
        // fetch can 304. next/image caches at the edge on top of this
        // (ADR 0011 §"Caching").
        if (!string.IsNullOrEmpty(download.ETag))
        {
            Response.Headers.ETag = download.ETag;
        }
        Response.Headers.CacheControl = "public, max-age=86400";

        return File(download.Content, download.ContentType, enableRangeProcessing: true);
    }
}
