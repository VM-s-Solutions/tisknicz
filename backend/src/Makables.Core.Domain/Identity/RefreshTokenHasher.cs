using System.Security.Cryptography;
using System.Text;

namespace Makables.Core.Domain.Identity;

/// <summary>
/// SHA-256 of a raw refresh token, encoded as lowercase hex (64 chars).
/// Per ADR 0012 §Refresh token: only the hash is persisted server-side;
/// the raw token lives in the HttpOnly cookie shipped to the client.
///
/// Plain static helper because SHA-256 is stateless and there's no
/// reason to push it through DI.
/// </summary>
public static class RefreshTokenHasher
{
    public static string Sha256Hex(string rawToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawToken);
        var bytes = Encoding.UTF8.GetBytes(rawToken);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
