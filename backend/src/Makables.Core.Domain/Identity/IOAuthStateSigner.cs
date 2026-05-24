namespace Makables.Core.Domain.Identity;

/// <summary>
/// HMAC-signed state token for the Google OAuth authorization-code
/// flow. Per ADR 0012 §Google OAuth + reviewer T-0026 BLOCKERs B-1 / B-2.
///
/// Threat model: the user starts the flow at
/// <c>/auth/google/start?audience=customer</c>; the backend redirects
/// to Google; Google calls us back with the state we minted. Without
/// signing, the user could edit the audience between start and
/// callback. Without context binding, an attacker who captured a state
/// could trick a victim's browser into completing the flow under the
/// attacker's chosen audience (OAuth login-CSRF). The signed state
/// therefore binds the audience, the redirect URI, a hash of an
/// HttpOnly anti-CSRF cookie value, a nonce, and an issued-at
/// timestamp. The callback verifies all five.
///
/// The signing key is derived (HKDF) from the JWT signing key with a
/// dedicated context label so a JWT can never be misinterpreted as a
/// state or vice versa — reviewer B-1 fix.
///
/// Implementation lives in <c>Makables.Infra.Common.Auth.OAuthStateSigner</c>.
/// </summary>
public interface IOAuthStateSigner
{
    /// <summary>
    /// Mint a signed state for an OAuth start. The returned string is
    /// opaque to the caller; only <see cref="TryVerify"/> can read it.
    /// The CSRF cookie value is hashed (SHA-256) and bound into the
    /// state so the raw cookie value never appears in the URL.
    /// </summary>
    string Sign(string audience, string redirectUri, string csrfCookieValue, string nonce, DateTimeOffset issuedAt);

    /// <summary>
    /// Verify a state returned by the OAuth provider. Returns the
    /// payload on success. Returns <c>null</c> for any tampering,
    /// stale-timestamp, redirect-uri mismatch, csrf-cookie mismatch,
    /// or signature-mismatch failure — callers MUST treat null as
    /// "reject the request."
    /// </summary>
    /// <param name="signedState">The signed state returned by Google.</param>
    /// <param name="redirectUri">The redirect URI presented to the callback. MUST equal the one bound into the state at sign time.</param>
    /// <param name="csrfCookieValue">The value of the anti-CSRF cookie the browser presented on the callback. MUST equal the one bound into the state at sign time (compared by SHA-256 hash so the cookie value itself never appears in the URL state).</param>
    /// <param name="now">Current time for stale-window check.</param>
    OAuthStatePayload? TryVerify(string signedState, string redirectUri, string csrfCookieValue, DateTimeOffset now);
}

/// <summary>
/// Contents of a signed OAuth state. Per ADR 0012 + reviewer T-0026 B-2.
///   - <see cref="Audience"/>: the role the new session will run as.
///   - <see cref="RedirectUri"/>: bound so an attacker can't swap it
///     between start and callback.
///   - <see cref="CsrfCookieHash"/>: SHA-256 hex of an HttpOnly cookie
///     value set on the browser at start; verified on callback.
///     Prevents capturing-a-state-and-replaying-into-a-victim-browser
///     (the attacker can't forge the victim's cookie).
///   - <see cref="Nonce"/>: random per state.
///   - <see cref="IssuedAt"/>: stale-window enforcement.
/// </summary>
public sealed record OAuthStatePayload(
    string Audience,
    string RedirectUri,
    string CsrfCookieHash,
    string Nonce,
    DateTimeOffset IssuedAt);
