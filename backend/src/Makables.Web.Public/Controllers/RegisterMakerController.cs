using Asp.Versioning;
using Makables.Config.Controllers;
using Makables.Core.AppServices.Features.Maker;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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
}
