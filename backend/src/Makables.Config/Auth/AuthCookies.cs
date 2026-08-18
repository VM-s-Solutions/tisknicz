using Makables.Core.AppServices.Features.Auth;
using Makables.Core.Domain.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Makables.Config.Auth;

/// <summary>
/// Helpers for shipping the access + refresh tokens as HttpOnly cookies
/// per ADR 0012 §"Refresh token". Cookie names match the frontend
/// conventions in <c>frontend/src/lib/auth/session.ts</c>
/// (<c>makables_access_{audience}</c> and <c>makables_refresh_{audience}</c>).
///
/// <para>
/// Both cookies are <c>HttpOnly</c>, <c>Secure</c>,
/// <c>SameSite=Strict</c>. The access cookie expires when the JWT
/// expires; the refresh cookie expires when the rotated family expires.
/// The cookie path is <c>/</c> so the same cookie covers every API route.
/// </para>
/// </summary>
public static class AuthCookies
{
    public const string AccessCookiePrefix = "makables_access_";
    public const string RefreshCookiePrefix = "makables_refresh_";

    /// <summary>
    /// Anti-CSRF cookie name for the OAuth start/callback round trip
    /// (Google T-0026/T-0035, Apple T-0139). The <c>__Host-</c> prefix
    /// requires <c>Secure=true</c>, <c>Path=/</c>, and no <c>Domain</c>
    /// attribute — enforced by <see cref="SetOAuthCsrfCookie"/> below.
    /// </summary>
    public const string OAuthCsrfCookieName = "__Host-makables_oauth_csrf";

    public static string AccessCookieName(string audience) => $"{AccessCookiePrefix}{audience}";
    public static string RefreshCookieName(string audience) => $"{RefreshCookiePrefix}{audience}";

    public static void SetSessionCookies(HttpResponse response, string audience, SessionResult session)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(session);

        response.Cookies.Append(
            AccessCookieName(audience),
            session.AccessToken,
            BuildOptions(response, session.AccessTokenExpiresAt));

        response.Cookies.Append(
            RefreshCookieName(audience),
            session.RefreshToken,
            BuildOptions(response, session.RefreshTokenExpiresAt));
    }

    public static void ClearSessionCookies(HttpResponse response, string audience)
    {
        ArgumentNullException.ThrowIfNull(response);
        var expired = new CookieOptions
        {
            HttpOnly = true,
            Secure = UseSecureCookies(response),
            SameSite = SameSiteMode.Strict,
            Path = "/",
            Expires = DateTimeOffset.UnixEpoch,
        };
        response.Cookies.Append(AccessCookieName(audience), string.Empty, expired);
        response.Cookies.Append(RefreshCookieName(audience), string.Empty, expired);
    }

    public static string? ReadRefreshCookie(HttpRequest request, string audience)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Cookies.TryGetValue(RefreshCookieName(audience), out var v) ? v : null;
    }

    /// <summary>
    /// Sets the OAuth anti-CSRF cookie minted by <c>StartGoogleOAuth</c> /
    /// <c>StartAppleOAuth</c>. Short-lived (10 minutes — matches
    /// <c>OAuthStateSigner.StateLifetime</c>). <c>SameSite=None</c> (with
    /// <c>Secure=true</c>, required by the spec for <c>None</c>) —
    /// Apple's callback is a cross-site top-level <b>POST</b>
    /// (<c>response_mode=form_post</c>), and browsers do not send
    /// <c>Lax</c> cookies on cross-site POST navigations (Safari never
    /// applies Chrome's temporary "Lax+POST" grace-period shim). Reviewer
    /// T-0139 finding: with <c>Lax</c>, this cookie silently fails to
    /// arrive at <c>/apple/callback</c>, and every Apple login would fail
    /// closed with <c>AuthOAuthInvalidState</c>. Do not revert this to
    /// <c>Lax</c> — it looks like the "safer default" but breaks the
    /// exact browser (Safari) this feature exists for.
    /// </summary>
    public static void SetOAuthCsrfCookie(HttpResponse response, string csrfCookieValue)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentException.ThrowIfNullOrWhiteSpace(csrfCookieValue);

        response.Cookies.Append(OAuthCsrfCookieName, csrfCookieValue, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.None,
            Path = "/",
            Expires = DateTimeOffset.UtcNow.AddMinutes(10),
        });
    }

    public static string? ReadOAuthCsrfCookie(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Cookies.TryGetValue(OAuthCsrfCookieName, out var v) ? v : null;
    }

    public static void ClearOAuthCsrfCookie(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        response.Cookies.Append(OAuthCsrfCookieName, string.Empty, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Expires = DateTimeOffset.UnixEpoch,
        });
    }

    private static CookieOptions BuildOptions(HttpResponse response, DateTimeOffset expiresAt) => new()
    {
        HttpOnly = true,
        Secure = UseSecureCookies(response),
        SameSite = SameSiteMode.Strict,
        Path = "/",
        Expires = expiresAt,
    };

    /// <summary>
    /// Whether the session cookies carry the <c>Secure</c> attribute.
    ///
    /// <para>
    /// Always <c>true</c> outside Development, regardless of the scheme
    /// the request arrived on, so a TLS-terminating reverse proxy (where
    /// <see cref="HttpRequest.IsHttps"/> is <c>false</c> on the inner hop)
    /// can never silently downgrade a production cookie. The relaxation
    /// is therefore provably unreachable in production per CLAUDE.md §6.
    /// </para>
    ///
    /// <para>
    /// In Development over plain <c>http://localhost</c> it is <c>false</c>:
    /// Safari refuses to store a <c>Secure</c> cookie on an insecure
    /// localhost origin (Chrome and Firefox treat localhost as trustworthy
    /// and store it either way). With the attribute always set, login
    /// answered <c>200</c> but the session cookie was silently dropped, so
    /// the app stayed logged out and the user re-submitted the form again
    /// and again. Development over HTTPS keeps <c>Secure</c>.
    /// </para>
    /// </summary>
    private static bool UseSecureCookies(HttpResponse response)
    {
        var environment = response.HttpContext.RequestServices
            .GetService<IHostEnvironment>();

        return environment is null
            || !environment.IsDevelopment()
            || response.HttpContext.Request.IsHttps;
    }
}
