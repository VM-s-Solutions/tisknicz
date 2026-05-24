namespace Makables.Infra.Common.Auth;

/// <summary>
/// JWT issuer parameters per ADR 0012 §JWT. The signing key is loaded from
/// configuration (Key Vault in production, GitHub-secret-driven env var in
/// CI, in-memory IConfiguration in tests). 32 bytes minimum so HS256 has
/// at least the same security margin as its output.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>JWT <c>iss</c> claim. e.g. <c>https://makables.cz</c>.</summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>
    /// HMAC signing key (base64). MUST be ≥ 32 bytes after decoding;
    /// shorter keys are rejected at validation time.
    /// </summary>
    public string SigningKeyBase64 { get; set; } = string.Empty;

    /// <summary>Access-token lifetime. ADR default 15 min.</summary>
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Key id claim (<c>kid</c>) — future-proofing for rotation. Single
    /// active key at launch; rotation lives in the next ADR.
    /// </summary>
    public string KeyId { get; set; } = "k1";
}
