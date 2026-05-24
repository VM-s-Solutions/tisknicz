using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Makables.Core.Domain.Identity;
using Microsoft.Extensions.Options;

namespace Makables.Infra.Common.Auth;

/// <summary>
/// HMAC-SHA256 implementation of <see cref="IOAuthStateSigner"/>.
/// Reuses the existing JWT signing key (from <see cref="JwtOptions.SigningKeyBase64"/>)
/// so we don't introduce a second secret. The state is a URL-safe
/// base64 of <c>{payload-json}.{hmac-hex}</c>; <see cref="TryVerify"/>
/// validates the signature in constant time before parsing.
///
/// State lifetime: <see cref="StateLifetime"/> (default 10 minutes) —
/// matches the OAuth ecosystem norm. A state older than that is
/// rejected even if otherwise valid.
/// </summary>
public sealed class OAuthStateSigner : IOAuthStateSigner
{
    /// <summary>Maximum age of a state before it's considered stale.</summary>
    public static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly byte[] _key;

    public OAuthStateSigner(IOptions<JwtOptions> jwtOptions)
    {
        ArgumentNullException.ThrowIfNull(jwtOptions);
        var keyBase64 = jwtOptions.Value.SigningKeyBase64;
        if (string.IsNullOrWhiteSpace(keyBase64))
            throw new InvalidOperationException("Jwt:SigningKeyBase64 is not configured; cannot sign OAuth state.");
        _key = Convert.FromBase64String(keyBase64);
        if (_key.Length < 32)
            throw new InvalidOperationException("Jwt signing key must be at least 32 bytes for OAuth state signing.");
    }

    public string Sign(OAuthStatePayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var payloadJson = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        var payloadB64 = ToBase64Url(payloadJson);
        var signature = HMACSHA256.HashData(_key, Encoding.ASCII.GetBytes(payloadB64));
        return $"{payloadB64}.{ToBase64Url(signature)}";
    }

    public OAuthStatePayload? TryVerify(string signedState, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(signedState)) return null;

        var dot = signedState.IndexOf('.');
        if (dot <= 0 || dot >= signedState.Length - 1) return null;

        var payloadB64 = signedState[..dot];
        var signatureB64 = signedState[(dot + 1)..];

        var expected = HMACSHA256.HashData(_key, Encoding.ASCII.GetBytes(payloadB64));
        byte[] provided;
        try
        {
            provided = FromBase64Url(signatureB64);
        }
        catch (FormatException)
        {
            return null;
        }

        // Constant-time comparison — no timing channel between
        // "signature is the right length but wrong bytes" and
        // "signature is the wrong length."
        if (!CryptographicOperations.FixedTimeEquals(expected, provided)) return null;

        OAuthStatePayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<OAuthStatePayload>(FromBase64Url(payloadB64), JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }

        if (payload is null) return null;
        if (now - payload.IssuedAt > StateLifetime) return null;
        if (string.IsNullOrWhiteSpace(payload.Nonce)) return null;

        return payload;
    }

    private static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static byte[] FromBase64Url(string s)
    {
        var padded = s.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        return Convert.FromBase64String(padded);
    }
}
