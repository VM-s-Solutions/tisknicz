using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Makables.Core.Domain.Identity;
using Microsoft.Extensions.Options;

namespace Makables.Infra.Common.Auth;

/// <summary>
/// HMAC-SHA256 implementation of <see cref="IOAuthStateSigner"/>.
/// Per reviewer T-0026 BLOCKERs B-1 / B-2:
///   - <b>B-1 (domain separation)</b>: the HMAC key is HKDF-derived
///     from the JWT signing key with a dedicated context label, so a
///     JWT can never be mistaken for an OAuth state or vice versa.
///   - <b>B-2 (state binding)</b>: the signed payload carries the
///     <c>RedirectUri</c> and an SHA-256 hash of the browser's
///     anti-CSRF cookie value. The verifier requires both to match
///     the values presented at the callback.
///
/// State shape: <c>{payload-b64url}.{hmac-b64url}</c>;
/// <see cref="TryVerify"/> validates the signature in constant time
/// before parsing.
///
/// State lifetime: <see cref="StateLifetime"/> (default 10 minutes) —
/// matches the OAuth ecosystem norm.
/// </summary>
public sealed class OAuthStateSigner : IOAuthStateSigner
{
    /// <summary>Maximum age of a state before it's considered stale.</summary>
    public static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);

    /// <summary>
    /// HKDF context label that separates the OAuth-state sub-key from
    /// every other use of the JWT signing key. Bumping the version
    /// suffix invalidates all live states (rotation lever).
    /// </summary>
    private static readonly byte[] HkdfInfo = Encoding.ASCII.GetBytes("makables-oauth-state-v1");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly byte[] _stateKey;

    public OAuthStateSigner(IOptions<JwtOptions> jwtOptions)
    {
        ArgumentNullException.ThrowIfNull(jwtOptions);
        var keyBase64 = jwtOptions.Value.SigningKeyBase64;
        if (string.IsNullOrWhiteSpace(keyBase64))
            throw new InvalidOperationException("Jwt:SigningKeyBase64 is not configured; cannot sign OAuth state.");

        var jwtKey = Convert.FromBase64String(keyBase64);
        if (jwtKey.Length < 32)
            throw new InvalidOperationException("Jwt signing key must be at least 32 bytes for OAuth state signing.");

        // HKDF-Expand under a dedicated info string. The JWT signer
        // never uses this derived key, and the state signer never uses
        // the raw JWT key — domain-separated.
        _stateKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm: jwtKey, outputLength: 32, salt: null, info: HkdfInfo);
    }

    public string Sign(string audience, string redirectUri, string csrfCookieValue, string nonce, DateTimeOffset issuedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);
        ArgumentException.ThrowIfNullOrWhiteSpace(redirectUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(csrfCookieValue);
        ArgumentException.ThrowIfNullOrWhiteSpace(nonce);

        var payload = new OAuthStatePayload(
            Audience: audience,
            RedirectUri: redirectUri,
            CsrfCookieHash: HashCookie(csrfCookieValue),
            Nonce: nonce,
            IssuedAt: issuedAt);

        var payloadJson = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        var payloadB64 = ToBase64Url(payloadJson);
        var signature = HMACSHA256.HashData(_stateKey, Encoding.ASCII.GetBytes(payloadB64));
        return $"{payloadB64}.{ToBase64Url(signature)}";
    }

    public OAuthStatePayload? TryVerify(
        string signedState,
        string redirectUri,
        string csrfCookieValue,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(signedState)) return null;
        if (string.IsNullOrWhiteSpace(redirectUri)) return null;
        if (string.IsNullOrWhiteSpace(csrfCookieValue)) return null;

        var dot = signedState.IndexOf('.');
        if (dot <= 0 || dot >= signedState.Length - 1) return null;

        var payloadB64 = signedState[..dot];
        var signatureB64 = signedState[(dot + 1)..];

        var expected = HMACSHA256.HashData(_stateKey, Encoding.ASCII.GetBytes(payloadB64));
        byte[] provided;
        try
        {
            provided = FromBase64Url(signatureB64);
        }
        catch (FormatException)
        {
            return null;
        }

        // Constant-time compare — no timing channel between
        // "signature has the right length but wrong bytes" and "signature
        // is the wrong length."
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

        // Stale-window check.
        if (now - payload.IssuedAt > StateLifetime) return null;

        // Nonce sanity (the signer always sets it; defensive against a
        // crafted-payload bypass).
        if (string.IsNullOrWhiteSpace(payload.Nonce)) return null;

        // Redirect-URI binding (B-2 fix). Exact ordinal compare — Google
        // requires the callback's redirect_uri to byte-match the one
        // sent at start, so an exact compare here surfaces a swap
        // before we call Google's exchange.
        if (!string.Equals(payload.RedirectUri, redirectUri, StringComparison.Ordinal)) return null;

        // CSRF-cookie binding (B-2 fix). Hash the presented cookie
        // value and compare in constant time to the hash baked into
        // the state at sign time.
        var presentedHash = HashCookie(csrfCookieValue);
        if (!FixedTimeEqualsString(presentedHash, payload.CsrfCookieHash)) return null;

        return payload;
    }

    private static string HashCookie(string cookieValue)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(cookieValue));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool FixedTimeEqualsString(string a, string b)
    {
        if (a.Length != b.Length) return false;
        var aBytes = Encoding.ASCII.GetBytes(a);
        var bBytes = Encoding.ASCII.GetBytes(b);
        return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
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
