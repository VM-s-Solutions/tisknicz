namespace Makables.Core.Domain.Identity;

/// <summary>
/// External boundary for Apple's OAuth 2.0 authorization-code flow
/// ("Sign in with Apple"). Implementation in
/// <c>Makables.Infra.Clients.Apple</c> per ADR 0026 / T-0139.
///
/// Mirrors <see cref="IGoogleOAuthClient"/> exactly in shape; the two
/// real deltas from Google (ES256 JWT client secret, <c>form_post</c>
/// callback contract) are isolated inside the implementation and the
/// controller action, not this interface.
///
///   - <see cref="BuildAuthorizationUrl"/>: produce the URL the user is
///     redirected to so Apple can collect consent. Pure local
///     computation — no HTTP, deterministic given the same arguments.
///   - <see cref="ExchangeCodeAsync"/>: trade the authorization code
///     returned by Apple for tokens, then unpack the verified profile
///     claims we care about. The one-time <c>user</c> form field
///     (name, present only on first authorization) is passed in
///     separately by the caller since it arrives alongside — not
///     inside — the authorization code at the callback.
/// </summary>
public interface IAppleOAuthClient
{
    /// <summary>
    /// Build the redirect URL for the start of the OAuth flow.
    /// <paramref name="signedState"/> is the HMAC-signed audience bundle
    /// produced by <see cref="IOAuthStateSigner"/> — Apple passes it
    /// back unchanged (as a form field, not a query param, per the
    /// <c>form_post</c> response mode) so the callback handler can
    /// verify it.
    /// </summary>
    string BuildAuthorizationUrl(string signedState, string redirectUri);

    /// <summary>
    /// Exchange the authorization <paramref name="code"/> for tokens and
    /// resolve the profile. The implementation MUST verify the ID
    /// token's signature (against Apple's JWKS) + issuer + audience +
    /// expiry before returning. <paramref name="userFieldJson"/> is the
    /// raw JSON of Apple's one-time <c>user</c> form field when present
    /// (first authorization only) — pass <c>null</c> on repeat logins
    /// where Apple omits it.
    /// </summary>
    Task<AppleProfile> ExchangeCodeAsync(
        string code,
        string redirectUri,
        string? userFieldJson,
        CancellationToken cancellationToken);
}

/// <summary>
/// Verified Apple identity. <see cref="Sub"/> is Apple's stable unique
/// user id; <see cref="EmailVerified"/> tells us whether Apple has
/// confirmed the email (we treat <c>EmailVerified=false</c> as an auth
/// failure, mirroring <see cref="GoogleProfile"/>). <see cref="Name"/>
/// is populated only when Apple's one-time <c>user</c> field carried a
/// name — i.e. on the first authorization for this (app, Apple ID)
/// pair; <c>null</c> on every subsequent login. <see cref="IsPrivateEmail"/>
/// surfaces Apple's "Hide My Email" relay flag; informational only, not
/// used in any decision.
/// </summary>
public sealed record AppleProfile(
    string Sub,
    string Email,
    bool EmailVerified,
    string? Name,
    bool IsPrivateEmail);
