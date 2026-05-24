namespace Makables.Infra.Clients.Google;

/// <summary>
/// Configuration for the Google OAuth client. Per ADR 0012 §Google OAuth.
/// Bound from <c>Auth:Google</c>. The client id is public (it appears in
/// the redirect URL); the client secret comes from Key Vault.
/// </summary>
public sealed class GoogleOAuthOptions
{
    public const string SectionName = "Auth:Google";

    /// <summary>OAuth 2.0 client id (public).</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>OAuth 2.0 client secret (from Key Vault).</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Comma-or-space-separated scopes requested at start. Default
    /// requests only what we need to confirm identity: openid + email +
    /// profile. The handler still rechecks <c>email_verified</c> after
    /// the token exchange.
    /// </summary>
    public string Scopes { get; set; } = "openid email profile";
}
