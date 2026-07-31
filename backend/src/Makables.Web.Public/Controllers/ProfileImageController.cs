using Asp.Versioning;
using Makables.Config.Controllers;
using Makables.Core.Domain.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Makables.Web.Public.Controllers;

/// <summary>
/// Public-host streaming endpoints for profile imagery — maker logos and
/// user avatars — per ADR 0011 §"All access through the backend — no
/// direct browser → blob links". Anonymous + ETag-cached, mirroring
/// <see cref="ProductImageController"/>.
///
/// <para>
/// Both are catalog-facing: a logo heads the maker card and profile, an
/// avatar sits beside the reviews its owner wrote. Anonymous access is
/// therefore the point, not an oversight — but note it means any avatar
/// URL is fetchable by anyone who has it. Nothing here enumerates or
/// leaks those URLs: a path only reaches a client through a DTO that
/// already decided the image should be visible, and the ULID filename
/// makes a path unguessable from the owner id alone.
/// </para>
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/files")]
[AllowAnonymous]
public sealed class ProfileImageController(IBlobStorageClient blobs) : MakablesApiController
{
    [HttpGet("makers/{country}/{makerId}/{filename}")]
    public Task<IActionResult> GetMakerLogo(string country, string makerId, string filename, CancellationToken ct) =>
        StreamAsync($"{country.ToLowerInvariant()}/makers/{makerId}/{filename}", ct);

    [HttpGet("avatars/{country}/{userId}/{filename}")]
    public Task<IActionResult> GetAvatar(string country, string userId, string filename, CancellationToken ct) =>
        StreamAsync($"{country.ToLowerInvariant()}/avatars/{userId}/{filename}", ct);

    /// <summary>
    /// Stream a blob from the <c>profile-images</c> container with the
    /// same caching contract as product images. Path traversal is
    /// rejected by the blob adapter (no <c>.</c> / <c>..</c> / empty
    /// segments); a missing blob surfaces as 404.
    /// </summary>
    private async Task<IActionResult> StreamAsync(string blobPath, CancellationToken ct)
    {
        var result = await blobs.DownloadAsync(BlobContainer.ProfileImages, blobPath, ct);
        if (!result.IsSuccess)
        {
            return HandleResult(result);
        }

        var download = result.Value!;

        // Day-cached with the blob's strong ETag. Safe to cache
        // aggressively despite being mutable content: replacing an image
        // writes a NEW ULID filename, so the URL itself changes and no
        // client is ever served a stale body under a live URL.
        Response.Headers.CacheControl = "public, max-age=86400";
        if (!string.IsNullOrEmpty(download.ETag))
        {
            Response.Headers.ETag = download.ETag;

            // Conditional GET: a 304 has no body, so the download stream
            // must be disposed here — nothing downstream will.
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
