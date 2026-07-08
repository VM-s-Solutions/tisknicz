using Asp.Versioning;
using Makables.Config.Auth;
using Makables.Config.Extensions;
using Makables.Core.AppServices.Features.Auth;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

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
// T-0136 (Q-0011): tight per-IP rate limit (10/min, no queue) across the
// brute-force / credential-stuffing / enumeration surface. Class-level so any
// NEW auth endpoint inherits the limit by default; composes under the global
// "default" envelope (the stricter wins). The two cookie-bearing,
// machine-triggered endpoints — `refresh` (the frontend auto-calls it on 401)
// and `logout` (must never fail-closed) — carry [DisableRateLimiting] so a
// shared-NAT office or a multi-tab session can't lock itself out; they still
// fall under the global per-host envelope (secops Gate-3 fold).
[EnableRateLimiting(MakablesRateLimitingExtensions.AuthPolicyName)]
public sealed class AuthController(IHostAudience hostAudience) : MakablesApiController
{
    public sealed record RegisterRequest(string Email, string Password, string FullName, string CountryCodePrimary);
    public sealed record LoginRequest(string Email, string Password);
    public sealed record ConfirmEmailRequest(string Token);
    public sealed record RequestPasswordResetRequest(string Email);
    public sealed record ConfirmPasswordResetRequest(string Token, string NewPassword);
    public sealed record RequestMagicLinkRequest(string Email);
    public sealed record ConsumeMagicLinkRequest(string Token);
    public sealed record StartAppleOAuthRequest(string RedirectUri);
    public sealed record StartAppleOAuthResponse(string AuthorizationUrl);

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
    // T-0136 secops fold: logout must never fail-closed (a 429 here strands a
    // user logged-in). Falls under the global per-host envelope only.
    [DisableRateLimiting]
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

    /// <summary>
    /// Begin the "Sign in with Apple" flow. Mirrors the audience-derived
    /// shape of the other anonymous auth endpoints on this controller
    /// (audience comes from <see cref="IHostAudience"/>, not the query
    /// string). Sets the OAuth anti-CSRF cookie before returning the
    /// authorization URL for the frontend to redirect the browser to.
    /// Per ADR 0026 / T-0139 AC-1.
    /// </summary>
    [HttpGet("apple/start")]
    [AllowAnonymous]
    public async Task<IActionResult> StartAppleOAuth([FromQuery] string redirectUri, CancellationToken ct)
    {
        var result = await Mediator.Send(new StartAppleOAuth.Command(hostAudience.Value, redirectUri), ct);

        if (result.IsSuccess && result.Value is not null)
        {
            AuthCookies.SetOAuthCsrfCookie(Response, result.Value.CsrfCookieValue);
            return HandleResult(BusinessResult.Success(
                new StartAppleOAuthResponse(result.Value.AuthorizationUrl)));
        }
        return HandleResult(result);
    }

    /// <summary>
    /// Apple's <c>response_mode=form_post</c> callback — Apple POSTs
    /// <c>code</c>/<c>state</c>/optional <c>user</c> as form fields, not
    /// query params. This action is deliberately <c>[HttpPost]</c> with
    /// <c>[FromForm]</c> binding; this is the one place the Apple flow's
    /// HTTP shape differs from a GET callback. Per ADR 0026 / T-0139
    /// AC-3, AC-6, AC-7.
    /// </summary>
    [HttpPost("apple/callback")]
    [AllowAnonymous]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> CompleteAppleOAuth(
        [FromForm] string code,
        [FromForm] string state,
        [FromForm] string? user,
        CancellationToken ct)
    {
        var csrfCookieValue = AuthCookies.ReadOAuthCsrfCookie(Request) ?? string.Empty;

        // Apple's form_post body carries only code/state/user — not
        // redirect_uri. The redirect URI bound into the signed state at
        // Start MUST match the URL Apple actually posted back to, so we
        // derive it from the current request rather than trusting a
        // caller-supplied value (which the state signer's exact-match
        // check would reject anyway if it disagreed).
        var redirectUri = $"{Request.Scheme}://{Request.Host}{Request.Path}";

        var result = await Mediator.Send(new CompleteAppleOAuth.Command(
            Code: code,
            State: state,
            RedirectUri: redirectUri,
            CsrfCookieValue: csrfCookieValue,
            UserFieldJson: user,
            UserAgent: NormalizedUserAgent(),
            IpAddress: HttpContext.Connection.RemoteIpAddress?.ToString()), ct);

        AuthCookies.ClearOAuthCsrfCookie(Response);

        if (result.IsSuccess && result.Value is not null)
        {
            AuthCookies.SetSessionCookies(Response, hostAudience.Value, result.Value);
        }
        return HandleResult(result);
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    // T-0136 secops fold: refresh is machine-triggered (the frontend auto-calls
    // it on 401) and carries an HttpOnly refresh cookie — not a credential-
    // guessing surface. Excluded from the tight per-IP auth bucket so a
    // multi-tab session / shared-NAT office can't lock itself out on legitimate
    // token rotation; still covered by the global per-host envelope.
    [DisableRateLimiting]
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
