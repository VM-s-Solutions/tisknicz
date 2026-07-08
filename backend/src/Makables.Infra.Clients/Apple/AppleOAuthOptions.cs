namespace Makables.Infra.Clients.Apple;

/// <summary>
/// Configuration for the Apple OAuth client. Per ADR 0026 / T-0139.
/// Bound from <c>Auth:Apple</c>. Unlike Google, Apple has no static
/// client secret — <see cref="TeamId"/> / <see cref="KeyId"/> /
/// <see cref="PrivateKeyPem"/> feed <see cref="AppleClientSecretSigner"/>,
/// which mints a fresh ES256 JWT per token-exchange call.
///
/// <see cref="AuthorizationEndpoint"/>, <see cref="TokenEndpoint"/> and
/// <see cref="JwksEndpoint"/> default to Apple's published URLs but are
/// configurable so integration tests can point at a stub (mirrors
/// <see cref="Google.GoogleOAuthOptions"/> reviewer T-0026 CQ M-5).
///
/// Missing config (empty <see cref="ClientId"/> / <see cref="TeamId"/> /
/// <see cref="KeyId"/> / <see cref="PrivateKeyPem"/>) is NOT
/// <c>ValidateOnStart</c> here — per T-0139 Technical notes, the Apple
/// button/feature must fail closed (client secret signing fails at
/// first use) rather than crash hosts that haven't onboarded the Apple
/// Developer "Services ID" yet.
/// </summary>
public sealed class AppleOAuthOptions
{
    public const string SectionName = "Auth:Apple";

    /// <summary>Default scope set requested at start.</summary>
    public const string DefaultScopes = "name email";

    /// <summary>Apple "Services ID" (the OAuth client id), e.g. <c>cz.makables.web</c>.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Apple Developer Team ID — the JWT client secret's <c>iss</c> claim.</summary>
    public string TeamId { get; set; } = string.Empty;

    /// <summary>Key ID of the P-256 private key registered with Apple — the JWT's <c>kid</c> header.</summary>
    public string KeyId { get; set; } = string.Empty;

    /// <summary>
    /// PEM-encoded P-256 (ES256) private key issued by Apple (the
    /// <c>.p8</c> file contents). From Key Vault — never logged.
    /// </summary>
    public string PrivateKeyPem { get; set; } = string.Empty;

    /// <summary>
    /// Space-separated scopes requested at start. Apple requires
    /// requesting <c>name</c> to receive the one-time <c>user</c> field
    /// on first authorization. The handler still rechecks
    /// <c>email_verified</c> after the token exchange.
    /// </summary>
    public string Scopes { get; set; } = DefaultScopes;

    /// <summary>Apple authorization endpoint. Override only for tests / staging.</summary>
    public string AuthorizationEndpoint { get; set; } = "https://appleid.apple.com/auth/authorize";

    /// <summary>Apple token-exchange endpoint. Override only for tests / staging.</summary>
    public string TokenEndpoint { get; set; } = "https://appleid.apple.com/auth/token";

    /// <summary>Apple's JWKS endpoint for id_token signature verification. Override only for tests / staging.</summary>
    public string JwksEndpoint { get; set; } = "https://appleid.apple.com/auth/keys";

    /// <summary>
    /// Lifetime of the minted client-secret JWT. Apple allows up to 6
    /// months; per ADR 0026 we mint on demand with a short expiry —
    /// no caching, no rotation job.
    /// </summary>
    public TimeSpan ClientSecretLifetime { get; set; } = TimeSpan.FromMinutes(15);
}
