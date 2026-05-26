using Makables.Core.AppServices.Features.Auth;
using Makables.Core.Domain.Identity;
using Microsoft.AspNetCore.Http;

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

    public static string AccessCookieName(string audience) => $"{AccessCookiePrefix}{audience}";
    public static string RefreshCookieName(string audience) => $"{RefreshCookiePrefix}{audience}";

    public static void SetSessionCookies(HttpResponse response, string audience, SessionResult session)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(session);

        response.Cookies.Append(
            AccessCookieName(audience),
            session.AccessToken,
            BuildOptions(session.AccessTokenExpiresAt));

        response.Cookies.Append(
            RefreshCookieName(audience),
            session.RefreshToken,
            BuildOptions(session.RefreshTokenExpiresAt));
    }

    public static void ClearSessionCookies(HttpResponse response, string audience)
    {
        ArgumentNullException.ThrowIfNull(response);
        var expired = new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
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

    private static CookieOptions BuildOptions(DateTimeOffset expiresAt) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        Expires = expiresAt,
    };
}
