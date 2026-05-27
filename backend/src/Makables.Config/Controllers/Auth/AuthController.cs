using Asp.Versioning;
using Makables.Config.Auth;
using Makables.Core.AppServices.Features.Auth;
using Makables.Core.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Makables.Config.Controllers.Auth;

/// <summary>
/// HTTP surface for the auth use cases per ADR 0012. The controller is a
/// thin mapping layer: each endpoint extracts the host audience + caller
/// metadata (IP / User-Agent) from <see cref="HttpContext"/>, dispatches
/// the matching MediatR command, and ships the session result (where
/// applicable) as HttpOnly cookies via <see cref="AuthCookies"/>.
///
/// <para>
/// The controller lives in <c>Makables.Config</c> so every Web host
/// picks it up via the shared MVC application part. Audience isolation
/// is enforced by JWT validation (<c>AddMakablesAuth</c>); the anonymous
/// endpoints below (<c>register</c>, <c>login</c>, <c>request-*</c>,
/// <c>confirm-*</c>, <c>consume-magic-link</c>) are intentionally
/// reachable on every host that wires the controller — the host
/// audience (<see cref="IHostAudience"/>) determines which JWT the
/// resulting session targets.
/// </para>
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/auth")]
public sealed class AuthController(IHostAudience hostAudience) : MakablesApiController
{
    public sealed record RegisterRequest(string Email, string Password, string FullName, string CountryCodePrimary);
    public sealed record LoginRequest(string Email, string Password);
    public sealed record ConfirmEmailRequest(string Token);
    public sealed record RequestPasswordResetRequest(string Email);
    public sealed record ConfirmPasswordResetRequest(string Token, string NewPassword);
    public sealed record RequestMagicLinkRequest(string Email);
    public sealed record ConsumeMagicLinkRequest(string Token);

    /// <summary>Register a customer account. Maker registration goes through <c>/api/v1/makers/register</c> on the Public host.</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register([FromBody] RegisterRequest body, CancellationToken ct)
    {
        var result = await Mediator.Send(new Register.Command(
            Email: body.Email,
            Password: body.Password,
            FullName: body.FullName,
            CountryCodePrimary: body.CountryCodePrimary,
            Role: UserRole.Customer), ct);
        return HandleResult(result);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest body, CancellationToken ct)
    {
        var result = await Mediator.Send(new Login.Command(
            Email: body.Email,
            Password: body.Password,
            Audience: hostAudience.Value,
            UserAgent: NormalizedUserAgent(),
            IpAddress: HttpContext.Connection.RemoteIpAddress?.ToString()), ct);

        if (result.IsSuccess && result.Value is not null)
        {
            AuthCookies.SetSessionCookies(Response, hostAudience.Value, result.Value);
        }
        return HandleResult(result);
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var refreshToken = AuthCookies.ReadRefreshCookie(Request, hostAudience.Value);
        try
        {
            if (!string.IsNullOrEmpty(refreshToken))
            {
                // Logout is idempotent — failures (e.g. token already revoked)
                // are not propagated; the cookies are still cleared below.
                _ = await Mediator.Send(new Logout.Command(refreshToken), ct);
            }
        }
        finally
        {
            // T-0035 sec reviewer m4: cookies MUST be cleared regardless of
            // command outcome so an infra exception doesn't leave the user
            // half-logged-in.
            AuthCookies.ClearSessionCookies(Response, hostAudience.Value);
        }
        return NoContent();
    }

    private string? NormalizedUserAgent()
    {
        var ua = Request.Headers.UserAgent.ToString();
        return string.IsNullOrEmpty(ua) ? null : ua;
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        var refreshToken = AuthCookies.ReadRefreshCookie(Request, hostAudience.Value);
        if (string.IsNullOrEmpty(refreshToken))
        {
            return Unauthorized();
        }

        var result = await Mediator.Send(new Refresh.Command(
            RawRefreshToken: refreshToken,
            Audience: hostAudience.Value,
            UserAgent: NormalizedUserAgent(),
            IpAddress: HttpContext.Connection.RemoteIpAddress?.ToString()), ct);

        if (result.IsSuccess && result.Value is not null)
        {
            AuthCookies.SetSessionCookies(Response, hostAudience.Value, result.Value);
        }
        else
        {
            // Refresh failed (rotation, expiry, revocation) — clear the
            // stale cookies so the next request lands on /login cleanly.
            AuthCookies.ClearSessionCookies(Response, hostAudience.Value);
        }
        return HandleResult(result);
    }

    [HttpPost("confirm-email")]
    [AllowAnonymous]
    public async Task<IActionResult> ConfirmEmail([FromBody] ConfirmEmailRequest body, CancellationToken ct)
    {
        var result = await Mediator.Send(new ConfirmEmail.Command(body.Token), ct);
        return HandleResult(result);
    }

    [HttpPost("request-password-reset")]
    [AllowAnonymous]
    public async Task<IActionResult> RequestPasswordReset([FromBody] RequestPasswordResetRequest body, CancellationToken ct)
    {
        var result = await Mediator.Send(new RequestPasswordReset.Command(
            Email: body.Email,
            IpAddress: HttpContext.Connection.RemoteIpAddress?.ToString()), ct);
        return HandleResult(result);
    }

    [HttpPost("confirm-password-reset")]
    [AllowAnonymous]
    public async Task<IActionResult> ConfirmPasswordReset([FromBody] ConfirmPasswordResetRequest body, CancellationToken ct)
    {
        var result = await Mediator.Send(new ConfirmPasswordReset.Command(
            RawToken: body.Token,
            NewPassword: body.NewPassword), ct);
        return HandleResult(result);
    }

    [HttpPost("request-magic-link")]
    [AllowAnonymous]
    public async Task<IActionResult> RequestMagicLink([FromBody] RequestMagicLinkRequest body, CancellationToken ct)
    {
        var result = await Mediator.Send(new RequestMagicLink.Command(
            Email: body.Email,
            IpAddress: HttpContext.Connection.RemoteIpAddress?.ToString()), ct);
        return HandleResult(result);
    }

    [HttpPost("consume-magic-link")]
    [AllowAnonymous]
    public async Task<IActionResult> ConsumeMagicLink([FromBody] ConsumeMagicLinkRequest body, CancellationToken ct)
    {
        var result = await Mediator.Send(new ConsumeMagicLink.Command(
            RawToken: body.Token,
            Audience: hostAudience.Value,
            UserAgent: NormalizedUserAgent(),
            IpAddress: HttpContext.Connection.RemoteIpAddress?.ToString()), ct);

        if (result.IsSuccess && result.Value is not null)
        {
            AuthCookies.SetSessionCookies(Response, hostAudience.Value, result.Value);
        }
        return HandleResult(result);
    }
}
