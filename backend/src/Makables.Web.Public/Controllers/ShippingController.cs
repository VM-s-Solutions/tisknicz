using Asp.Versioning;
using Makables.Config.Controllers;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Shipping;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Makables.Web.Public.Controllers;

/// <summary>
/// Public-host shipping endpoints. T-0070 ships the widget-config endpoint
/// the customer checkout page hits to discover the carrier-specific widget
/// configuration (script URL + public key + locale + country).
///
/// <para>
/// Anonymous + IP-rate-limited. The endpoint runs BEFORE customer sign-in
/// on the checkout flow (anonymous checkout is supported), so it cannot
/// require auth. Per-IP rate limit (100/min) blocks scrapers without
/// hurting legit checkout traffic.
/// </para>
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/public/shipping")]
[AllowAnonymous]
public sealed class ShippingController(
    IShippingCarrierFactory carrierFactory) : MakablesApiController
{
    /// <summary>
    /// Returns the carrier-specific widget configuration for the given
    /// country + locale. Sets <c>Cache-Control: public, max-age=3600</c>
    /// (1h) on success so customer checkout pages don't hammer the
    /// backend — config is static for hours per T-0070 locked decision A.3.
    /// </summary>
    [HttpGet("widget-config")]
    [EnableRateLimiting("shipping-widget-config")]
    [ProducesResponseType(typeof(PickupPointWidgetConfig), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetWidgetConfig(
        [FromQuery] string country = "CZ",
        [FromQuery] string locale = "cs-CZ",
        CancellationToken cancellationToken = default)
    {
        var carrierResult = await carrierFactory.ResolveAsync(country, cancellationToken);
        if (!carrierResult.IsSuccess)
        {
            return HandleResult(carrierResult);
        }

        var config = carrierResult.Value!.WidgetConfig(locale, country);
        Response.Headers.CacheControl = "public, max-age=3600";
        return Ok(config);
    }
}
