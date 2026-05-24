namespace Makables.Core.Domain.Identity;

/// <summary>
/// External boundary for Google's OAuth 2.0 authorization-code flow.
/// Implementation in <c>Makables.Infra.Clients.Google</c> per ADR 0012
/// §Google OAuth.
///
/// Two operations:
///   - <see cref="BuildAuthorizationUrl"/>: produce the URL the user is
///     redirected to so Google can collect consent. Pure local
///     computation — no HTTP, deterministic given the same arguments.
///   - <see cref="ExchangeCodeAsync"/>: trade the authorization code
///     returned by Google for an access token + ID token, then unpack
///     the verified profile claims we care about.
/// </summary>
public interface IGoogleOAuthClient
{
    /// <summary>
    /// Build the redirect URL for the start of the OAuth flow.
    /// <paramref name="signedState"/> is the HMAC-signed audience bundle
    /// produced by <see cref="IOAuthStateSigner"/> — Google passes it
    /// back unchanged so the callback handler can verify it.
    /// </summary>
    string BuildAuthorizationUrl(string signedState, string redirectUri);

    /// <summary>
    /// Exchange the authorization <paramref name="code"/> for tokens and
    /// resolve the profile. The implementation MUST verify the ID
    /// token's signature + audience + expiry before returning.
    /// </summary>
    Task<GoogleProfile> ExchangeCodeAsync(string code, string redirectUri, CancellationToken cancellationToken);
}

/// <summary>
/// Verified Google identity. <see cref="Sub"/> is Google's stable
/// unique user id; <see cref="EmailVerified"/> tells us whether Google
/// has confirmed the email (we treat <c>EmailVerified=false</c> as an
/// auth failure to avoid letting unverified-Google accounts bypass our
/// own confirmation flow).
/// </summary>
public sealed record GoogleProfile(
    string Sub,
    string Email,
    bool EmailVerified,
    string? Name);
