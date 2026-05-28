using Asp.Versioning;
using Makables.Config.Controllers;
using Makables.Core.Domain.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
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
        Response.Headers.CacheControl = "public, max-age=86400";
        if (!string.IsNullOrEmpty(download.ETag))
        {
            Response.Headers.ETag = download.ETag;

            // Conditional GET: if the client's cached ETag matches, skip
            // the body and 304. Must dispose the download stream here —
            // a 304 has no body, so nothing else will (T-0041 Copilot
            // review). Match any of the (possibly comma-separated)
            // If-None-Match values.
            var ifNoneMatch = Request.Headers.IfNoneMatch.ToString();
            if (!string.IsNullOrEmpty(ifNoneMatch) && ETagMatches(ifNoneMatch, download.ETag))
            {
                await download.Content.DisposeAsync();
                return StatusCode(StatusCodes.Status304NotModified);
            }
        }

        return File(download.Content, download.ContentType, enableRangeProcessing: true);
    }

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
