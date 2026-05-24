using System.Security.Cryptography;
using System.Text;

namespace Makables.Core.Domain.Identity;

/// <summary>
/// Generator + hasher for opaque single-use tokens shared by every Phase-2
/// email-bound auth flow (refresh tokens, magic link, email confirmation,
/// password reset). Per ADR 0012: 32 bytes CSPRNG, URL-safe base64,
/// SHA-256 hex hash persisted server-side.
///
/// Pure stateless helper — kept in Core.Domain because it has no
/// third-party deps (ADR 0001).
/// </summary>
public static class OpaqueTokenFactory
{
    /// <summary>
    /// Mint a fresh (raw, hash) pair. 32 bytes of CSPRNG → URL-safe
    /// base64 (no padding) for the raw value, plus its SHA-256 hex hash
    /// as the server-side lookup key. The raw value lives in the channel
    /// shipped to the user (cookie for refresh; email body for the
    /// single-use flows); the hash is what hits persistence.
    /// </summary>
    public static (string Raw, string Hash) GenerateUrlSafe32()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var raw = Convert.ToBase64String(bytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return (raw, Sha256Hex(raw));
    }

    /// <summary>
    /// Hex-lowercase SHA-256 of <paramref name="rawToken"/>. 64 characters.
    /// </summary>
    public static string Sha256Hex(string rawToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawToken);
        // ASCII is correct: every Makables opaque token is URL-safe base64
        // (ASCII alphabet) so UTF-8 and ASCII produce identical bytes;
        // ASCII is faster.
        var bytes = Encoding.ASCII.GetBytes(rawToken);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
