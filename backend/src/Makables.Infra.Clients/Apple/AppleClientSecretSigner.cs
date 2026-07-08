using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Makables.Infra.Clients.Apple;

/// <summary>
/// Mints Apple's ES256 JWT "client secret" per ADR 0026 / T-0139 AC-3:
/// <c>iss</c>=<see cref="AppleOAuthOptions.TeamId"/>,
/// <c>sub</c>=<see cref="AppleOAuthOptions.ClientId"/>,
/// <c>aud</c>=<c>https://appleid.apple.com</c>, <c>kid</c>=
/// <see cref="AppleOAuthOptions.KeyId"/> (JWT header), signed with the
/// P-256 private key from Key Vault. Minted fresh per call — no
/// caching, no rotation job (Apple allows up to 6 months; we choose a
/// short 15-minute default lifetime since the secret is used exactly
/// once, immediately, for the token exchange).
///
/// Reuses the same <see cref="Microsoft.IdentityModel.JsonWebTokens.JsonWebTokenHandler"/>
/// signing primitive as <see cref="Makables.Infra.Common.Auth.JwtIssuer"/>
/// (same NuGet family already in the solution) rather than adding a new
/// JWT library — the only difference is the signing algorithm (ES256 +
/// an <see cref="ECDsaSecurityKey"/> instead of HS256 + a symmetric key).
/// </summary>
public sealed class AppleClientSecretSigner(IOptions<AppleOAuthOptions> options)
{
    private const string AppleAudience = "https://appleid.apple.com";

    // JsonWebTokenHandler is thread-safe per Microsoft docs — one shared
    // instance avoids allocation on every call, mirroring JwtIssuer.
    private static readonly JsonWebTokenHandler TokenHandler = new();

    /// <summary>
    /// Mint a fresh ES256 client-secret JWT for immediate, single use in
    /// a token-exchange call. Throws <see cref="AppleOAuthException"/>
    /// if the P-256 private key is malformed or configuration is
    /// missing — this is a Configuration-class failure the caller
    /// should surface distinctly (T-0139 scope note on
    /// <c>AuthOAuthAppleClientSecretSigningFailed</c>).
    /// </summary>
    public string Mint(DateTimeOffset now)
    {
        var opts = options.Value;
        if (string.IsNullOrWhiteSpace(opts.TeamId) || string.IsNullOrWhiteSpace(opts.ClientId)
            || string.IsNullOrWhiteSpace(opts.KeyId) || string.IsNullOrWhiteSpace(opts.PrivateKeyPem))
        {
            throw new AppleOAuthException(
                "Auth:Apple:TeamId / ClientId / KeyId / PrivateKeyPem are not fully configured.");
        }

        ECDsa ecdsa;
        try
        {
            ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(opts.PrivateKeyPem);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
        {
            throw new AppleOAuthException("Apple private key (PrivateKeyPem) failed to import.", ex);
        }

        using (ecdsa)
        {
            var key = new ECDsaSecurityKey(ecdsa) { KeyId = opts.KeyId };
            var signingCredentials = new SigningCredentials(key, SecurityAlgorithms.EcdsaSha256)
            {
                // Disable IdentityModel's signature-provider cache. It
                // keys cached providers by (KeyId, algorithm) — since
                // every call mints against the SAME configured KeyId,
                // an un-cached, freshly-disposed ECDsa from a PRIOR call
                // would otherwise be reused by a later call, throwing
                // ObjectDisposedException. Each Mint() call is single-use
                // by design (no caching wanted at this layer either).
                CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false },
            };

            var expiresAt = now + opts.ClientSecretLifetime;

            var descriptor = new SecurityTokenDescriptor
            {
                Issuer = opts.TeamId,
                Audience = AppleAudience,
                Subject = new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, opts.ClientId)]),
                IssuedAt = now.UtcDateTime,
                NotBefore = now.UtcDateTime,
                Expires = expiresAt.UtcDateTime,
                SigningCredentials = signingCredentials,
            };

            return TokenHandler.CreateToken(descriptor);
        }
    }
}
