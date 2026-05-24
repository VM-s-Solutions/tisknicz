namespace Makables.Core.Domain.Identity;

/// <summary>
/// HMAC-signed state token for the Google OAuth authorization-code
/// flow. Per ADR 0012 §Google OAuth: "Audience query parameter is signed
/// in the state to prevent role escalation."
///
/// Threat model: the user starts the flow at
/// <c>/auth/google/start?audience=customer</c>; the backend redirects
/// to Google; Google calls us back with the state we minted. Without
/// signing, the user could edit the audience between start and
/// callback (or replay a state harvested from another flow). The
/// signed state carries the audience + a nonce + an issued-at
/// timestamp; the callback verifies all three.
///
/// Implementation lives in <c>Makables.Infra.Common.Auth.OAuthStateSigner</c>
/// and reuses the HMAC signing key from <c>JwtOptions</c>.
/// </summary>
public interface IOAuthStateSigner
{
    /// <summary>
    /// Mint a signed state for an OAuth start. The returned string is
    /// opaque to the caller; only <see cref="TryVerify"/> can read it.
    /// </summary>
    string Sign(OAuthStatePayload payload);

    /// <summary>
    /// Verify a state returned by the OAuth provider. Returns the
    /// payload on success. Returns <c>null</c> for any tampering,
    /// stale-timestamp, or signature-mismatch failure — callers MUST
    /// treat null as "reject the request."
    /// </summary>
    OAuthStatePayload? TryVerify(string signedState, DateTimeOffset now);
}

/// <summary>
/// Contents of a signed OAuth state. The audience anchors the role the
/// new session will run as; the nonce makes each state unique; the
/// issued-at timestamp lets us reject stales (default 10 min window).
/// </summary>
public sealed record OAuthStatePayload(
    string Audience,
    string Nonce,
    DateTimeOffset IssuedAt);
