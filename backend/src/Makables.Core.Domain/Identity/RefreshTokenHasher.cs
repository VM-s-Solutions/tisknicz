using System.Security.Cryptography;
using System.Text;

namespace Makables.Core.Domain.Identity;

/// <summary>
/// Refresh-token raw + hashed value helpers. Per ADR 0012 §Refresh token:
/// only SHA-256(raw token) is persisted; the raw token lives in the
/// HttpOnly cookie shipped to the client.
///
/// Static helpers because they are stateless and pure. Kept in Core.Domain
/// because they have no third-party dependencies (per ADR 0001).
/// </summary>
public static class RefreshTokenHasher
{
    /// <summary>
    /// Hex-lowercase SHA-256 of <paramref name="rawToken"/>. 64 characters.
    /// </summary>
    public static string Sha256Hex(string rawToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawToken);
        // ASCII is correct: the raw token is URL-safe base64 (ASCII alphabet)
        // so UTF-8 and ASCII produce identical bytes; ASCII is faster.
        var bytes = Encoding.ASCII.GetBytes(rawToken);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Generate a fresh refresh token pair: 32 bytes of CSPRNG → URL-safe
    /// base64 (no padding) as the raw client value, plus its SHA-256
    /// hex hash as the server-side lookup key. Reviewer T-0022 MAJOR M-2 —
    /// centralized so the encoding / entropy size cannot drift between
    /// Login and Refresh.
    /// </summary>
    public static (string Raw, string Hash) GenerateNewPair()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var raw = Convert.ToBase64String(bytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return (raw, Sha256Hex(raw));
    }
}
