using Asp.Versioning;
using Makables.Config.Extensions;
using Makables.Core.Domain.Addresses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Makables.Config.Controllers.Addresses;

/// <summary>
/// Backend proxy for Mapbox autocomplete per ADR 0010 §"Frontend
/// integration". The frontend never calls Mapbox directly — every
/// autocomplete query goes through this endpoint so we can:
///
/// <list type="bullet">
///   <item><description>Keep the Mapbox token server-side only.</description></item>
///   <item><description>Rate-limit per authenticated user (or per IP for unauth) via the
///     <c>addresses-autocomplete</c> partitioned policy. ADR 0010
///     §"Compliance" + ADR 0023 (NFRs).</description></item>
///   <item><description>Wrap Mapbox failures in <see cref="Core.Domain.Common.BusinessResult{T}"/>
///     so the frontend sees the canonical error code shape, not Mapbox-specific HTTP statuses.</description></item>
/// </list>
///
/// The controller lives in <c>Makables.Config</c> so both Customer +
/// Maker hosts pick it up via the shared MVC application part. Routes
/// are versioned via the existing <c>AddMakablesApiVersioning</c>
/// wiring (URL-path versioning: <c>/api/v1/addresses/autocomplete</c>).
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/addresses")]
[Authorize]
[EnableRateLimiting(MakablesRateLimitingExtensions.AddressesAutocompletePolicyName)]
public sealed class AddressAutocompleteController(IAddressGeocoder geocoder) : MakablesApiController
{
    /// <summary>
    /// Returns up to <c>Mapbox:AutocompleteLimit</c> address suggestions
    /// matching <paramref name="q"/>, scoped to <paramref name="country"/>
    /// (ISO 3166-1 alpha-2, e.g. <c>CZ</c>). Authenticated callers get
    /// 20/min; unauthenticated callers (currently blocked by
    /// <c>[Authorize]</c>, kept for future opening) would get 5/min/IP.
    /// </summary>
    [HttpGet("autocomplete")]
    public async Task<IActionResult> Autocomplete(
        [FromQuery(Name = "q")] string q,
        [FromQuery(Name = "country")] string country,
        CancellationToken cancellationToken)
    {
        var result = await geocoder.AutocompleteAsync(q, country, cancellationToken);
        return HandleResult(result);
    }
}
