namespace Makables.Core.Domain.Identity;

/// <summary>
/// JWT minting boundary. Per ADR 0012 §JWT structure:
///   - HS256, 15 min TTL.
///   - Claims: sub, email, role, country_code, aud, iss, iat, exp, jti.
///   - Audience MUST equal one of: <c>customer</c> / <c>maker</c> / <c>admin</c>;
///     each Web host accepts only its own audience (plus <c>admin</c>) so a
///     customer JWT cannot be replayed against the maker or admin API.
/// </summary>
public interface IJwtIssuer
{
    /// <summary>
    /// Issue a short-lived access JWT for <paramref name="user"/> bound to
    /// <paramref name="audience"/>. The <paramref name="now"/> parameter
    /// drives <c>iat</c> / <c>exp</c> so tests are deterministic.
    /// </summary>
    AccessToken Issue(User user, string audience, DateTimeOffset now);
}

/// <summary>
/// Result of <see cref="IJwtIssuer.Issue"/>. Records the raw token + its
/// expiry so the caller can ship the expiry to the client (the JWT itself
/// carries it too, but having it on the wrapper avoids parsing).
/// </summary>
public readonly record struct AccessToken(string Token, DateTimeOffset ExpiresAt, string Jti);
