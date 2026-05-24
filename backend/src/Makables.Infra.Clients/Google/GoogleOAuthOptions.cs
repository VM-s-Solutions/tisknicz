namespace Makables.Infra.Clients.Google;

/// <summary>
/// Configuration for the Google OAuth client. Per ADR 0012 §Google OAuth.
/// Bound from <c>Auth:Google</c>. The client id is public (it appears in
/// the redirect URL); the client secret comes from Key Vault.
///
/// <see cref="AuthorizationEndpoint"/> and <see cref="TokenEndpoint"/>
/// default to Google's published URLs but are configurable so
/// integration tests can point at a WireMock stub (reviewer T-0026
/// code-quality M-5).
/// </summary>
public sealed class GoogleOAuthOptions
{
    public const string SectionName = "Auth:Google";

    /// <summary>Default scope set requested at start.</summary>
    public const string DefaultScopes = "openid email profile";

    /// <summary>OAuth 2.0 client id (public).</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>OAuth 2.0 client secret (from Key Vault).</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Space-separated scopes requested at start. Default
    /// (<see cref="DefaultScopes"/>) requests only what we need to
    /// confirm identity: openid + email + profile. The handler still
    /// rechecks <c>email_verified</c> after the token exchange.
    /// </summary>
    public string Scopes { get; set; } = DefaultScopes;

    /// <summary>
    /// Google authorization endpoint. Override only for tests / staging.
    /// </summary>
    public string AuthorizationEndpoint { get; set; } = "https://accounts.google.com/o/oauth2/v2/auth";

    /// <summary>
    /// Google token-exchange endpoint. Override only for tests / staging.
    /// </summary>
    public string TokenEndpoint { get; set; } = "https://oauth2.googleapis.com/token";
}
