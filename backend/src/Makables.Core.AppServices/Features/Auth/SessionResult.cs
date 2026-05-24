namespace Makables.Core.AppServices.Features.Auth;

/// <summary>
/// Result of a successful Login / Refresh / Register-with-confirmed-email.
/// Carries the access JWT (short-lived, returned in JSON to the client)
/// plus the raw refresh token (the caller's controller is responsible for
/// shipping it as an HttpOnly + Secure + SameSite=Strict cookie per ADR
/// 0012 §Refresh token).
/// </summary>
public sealed record SessionResult(
    string UserId,
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);
