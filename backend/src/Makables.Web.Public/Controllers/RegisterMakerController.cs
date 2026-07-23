using Asp.Versioning;
using Makables.Config.Controllers;
using Makables.Config.Extensions;
using Makables.Core.AppServices.Features.Maker;
using Makables.Core.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Makables.Web.Public.Controllers;

/// <summary>
/// Public-host endpoint that owns the maker onboarding flow (T-0033).
/// Lives in <c>Web.Public</c> rather than <c>Config</c> so it is reachable
/// ONLY from the Public host — registration is the one moment a Maker
/// account exists before any audience-specific JWT has been issued, so
/// the route must not appear on the customer/maker/admin hosts.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/makers")]
public sealed class RegisterMakerController : MakablesApiController
{
    public sealed record RegisterMakerRequest(
        string Email,
        string Password,
        string FullName,
        string CountryCodePrimary,
        string RegistrationNumber);

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register([FromBody] RegisterMakerRequest body, CancellationToken ct)
    {
        var result = await Mediator.Send(new RegisterMaker.Command(
            Email: body.Email,
            Password: body.Password,
            FullName: body.FullName,
            CountryCodePrimary: body.CountryCodePrimary,
            RegistrationNumber: body.RegistrationNumber), ct);
        return HandleResult(result);
    }

    /// <summary>
    /// Anonymous IČO → company preview for the registration form (T-0159,
    /// business decision Q4: prefill from ARES, user confirms). Read-only
    /// UX helper — registration re-runs the authoritative lookup. Rides
    /// the tight per-IP "auth" rate-limit bucket (T-0136): it is an
    /// anonymous enumeration-adjacent surface, and the client debounces
    /// to at most one call per typed IČO.
    /// </summary>
    [HttpGet("registry-preview")]
    [AllowAnonymous]
    [EnableRateLimiting(MakablesRateLimitingExtensions.AuthPolicyName)]
    [ProducesResponseType(typeof(LookupCompanyPreview.LookupCompanyPreviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RegistryPreview(
        [FromQuery] string registrationNumber,
        [FromQuery] string countryCode,
        CancellationToken ct)
    {
        var result = await Mediator.Send(new LookupCompanyPreview.Query(
            RegistrationNumber: registrationNumber?.Trim() ?? string.Empty,
            CountryCode: countryCode?.Trim().ToUpperInvariant() ?? string.Empty), ct);
        return HandleResult(result);
    }
}
