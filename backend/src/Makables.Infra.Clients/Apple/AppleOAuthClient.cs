using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Makables.Core.Domain.Identity;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Makables.Infra.Clients.Apple;

/// <summary>
/// Server-side OAuth-2.0 authorization-code flow against Apple ("Sign
/// in with Apple"). Per ADR 0026 / T-0139.
///
/// <see cref="BuildAuthorizationUrl"/> is local computation only — it
/// requests <c>response_mode=form_post</c>, which is why the matching
/// controller action MUST be a POST reading form fields (the real HTTP
/// contract delta from Google's GET callback).
///
/// <see cref="ExchangeCodeAsync"/> mints a fresh ES256 client-secret JWT
/// via <see cref="AppleClientSecretSigner"/>, POSTs to Apple's token
/// endpoint, and verifies the returned <c>id_token</c> against Apple's
/// JWKS (cached in-process — Apple's signing keys rotate rarely).
///
/// HttpClient is injected via <see cref="IHttpClientFactory"/> per
/// CLAUDE.md (no direct HttpClient outside Infra.Clients). Failures
/// surface as <see cref="AppleOAuthException"/>; the use-case layer
/// catches them and maps to
/// <see cref="Makables.Core.Domain.Common.BusinessResult"/> failures.
/// </summary>
public sealed class AppleOAuthClient(
    IHttpClientFactory httpClientFactory,
    IOptions<AppleOAuthOptions> options,
    AppleClientSecretSigner clientSecretSigner,
    IMemoryCache memoryCache,
    ILogger<AppleOAuthClient> logger) : IAppleOAuthClient
{
    /// <summary>Named HttpClient registered by the wiring extension.</summary>
    public const string HttpClientName = "Makables.Infra.Clients.Apple";

    private const string AppleIssuer = "https://appleid.apple.com";
    private const string JwksCacheKey = "apple-oauth-jwks";
    private static readonly TimeSpan JwksCacheTtl = TimeSpan.FromHours(24);

    private static readonly JsonWebTokenHandler TokenHandler = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public string BuildAuthorizationUrl(string signedState, string redirectUri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signedState);
        ArgumentException.ThrowIfNullOrWhiteSpace(redirectUri);

        var opts = options.Value;
        var query = new[]
        {
            ("client_id", opts.ClientId),
            ("redirect_uri", redirectUri),
            ("response_type", "code"),
            ("scope", opts.Scopes),
            ("state", signedState),
            // Apple's web flow requires form_post — the callback
            // controller action must be [HttpPost] reading [FromForm]
            // fields, not [FromQuery]. This is the real HTTP-contract
            // delta from Google's GET callback (ADR 0026).
            ("response_mode", "form_post"),
        };

        var qs = string.Join("&", query.Select(p => $"{Uri.EscapeDataString(p.Item1)}={Uri.EscapeDataString(p.Item2)}"));
        return $"{opts.AuthorizationEndpoint}?{qs}";
    }

    public async Task<AppleProfile> ExchangeCodeAsync(
        string code,
        string redirectUri,
        string? userFieldJson,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(redirectUri);

        var opts = options.Value;
        if (string.IsNullOrWhiteSpace(opts.ClientId))
            throw new AppleOAuthException("Auth:Apple:ClientId is not configured.");

        // Minted fresh, used exactly once, then discarded. Signing
        // failures (missing/malformed key config) surface as
        // AppleOAuthException, same failure family as an exchange error.
        var clientSecret = clientSecretSigner.Mint(DateTimeOffset.UtcNow);

        var http = httpClientFactory.CreateClient(HttpClientName);

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = opts.ClientId,
            ["client_secret"] = clientSecret,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code",
        });

        using var response = await http.PostAsync(opts.TokenEndpoint, form, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning("Apple token exchange failed: {Status} {Body}", (int)response.StatusCode, body);
            throw new AppleOAuthException($"Apple rejected the authorization code (HTTP {(int)response.StatusCode}).");
        }

        var tokenPayload = await response.Content.ReadFromJsonAsync<AppleTokenResponse>(cancellationToken: cancellationToken)
            ?? throw new AppleOAuthException("Apple token endpoint returned no body.");

        if (string.IsNullOrWhiteSpace(tokenPayload.IdToken))
            throw new AppleOAuthException("Apple token response had no id_token.");

        var claims = await ValidateIdTokenAsync(tokenPayload.IdToken, opts.ClientId, cancellationToken);

        if (string.IsNullOrWhiteSpace(claims.Sub) || string.IsNullOrWhiteSpace(claims.Email))
            throw new AppleOAuthException("Apple ID token missing sub or email.");

        // The one-time `user` field carries the name only on first
        // authorization for this (app, Apple ID) pair. Absent on every
        // subsequent login — AppleProfile.Name is null in that case.
        var name = TryExtractNameFromUserField(userFieldJson);

        return new AppleProfile(
            Sub: claims.Sub,
            Email: claims.Email,
            EmailVerified: claims.EmailVerified,
            Name: name,
            IsPrivateEmail: claims.IsPrivateEmail);
    }

    /// <summary>
    /// Validates the id_token's signature against Apple's JWKS, issuer,
    /// audience, and expiry, then extracts the claims we care about
    /// directly from the decoded payload JSON (not the ClaimsIdentity)
    /// so the <c>email_verified</c> string-vs-bool quirk (Apple
    /// sometimes serializes it as the string <c>"false"</c>/<c>"true"</c>
    /// rather than a JSON boolean — ADR 0026, T-0139 AC-4) is handled
    /// explicitly rather than relying on claim-value stringification.
    /// </summary>
    private async Task<AppleIdTokenClaims> ValidateIdTokenAsync(string idToken, string clientId, CancellationToken ct)
    {
        var jwks = await GetJwksAsync(ct);

        TokenValidationResult validationResult;
        try
        {
            validationResult = await TokenHandler.ValidateTokenAsync(idToken, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = AppleIssuer,
                ValidateAudience = true,
                ValidAudience = clientId,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = jwks.Keys
                    .Select(k => JsonWebKeyConverter.TryConvertToSecurityKey(k, out var key) ? key : null)
                    .Where(k => k is not null)!,
                ClockSkew = TimeSpan.FromSeconds(30),
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new AppleOAuthException("Apple ID token failed validation.", ex);
        }

        if (!validationResult.IsValid)
        {
            throw new AppleOAuthException("Apple ID token failed validation.", validationResult.Exception);
        }

        // Re-decode the raw payload for the fields with quirky wire
        // shapes rather than trusting ClaimsIdentity stringification.
        var jsonWebToken = new JsonWebToken(idToken);
        using var payloadDoc = JsonDocument.Parse(jsonWebToken.EncodedPayload is { Length: > 0 }
            ? Base64UrlDecode(jsonWebToken.EncodedPayload)
            : "{}"u8.ToArray());
        var root = payloadDoc.RootElement;

        var sub = root.TryGetProperty("sub", out var subEl) ? subEl.GetString() : null;
        var email = root.TryGetProperty("email", out var emailEl) ? emailEl.GetString() : null;
        var emailVerified = ParseFlexibleBool(root, "email_verified");
        var isPrivateEmail = ParseFlexibleBool(root, "is_private_email");

        return new AppleIdTokenClaims(sub, email, emailVerified, isPrivateEmail);
    }

    /// <summary>
    /// Apple documents <c>email_verified</c> / <c>is_private_email</c>
    /// as sometimes appearing as the JSON string <c>"true"</c>/<c>"false"</c>
    /// instead of a native JSON boolean. Handles both shapes; missing or
    /// unparseable values default to <c>false</c> (fail closed).
    /// </summary>
    private static bool ParseFlexibleBool(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element)) return false;

        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => string.Equals(element.GetString(), "true", StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    private static byte[] Base64UrlDecode(string s)
    {
        var padded = s.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        return Convert.FromBase64String(padded);
    }

    /// <summary>
    /// Extracts <c>name.firstName</c> + <c>name.lastName</c> from
    /// Apple's one-time <c>user</c> form field JSON, e.g.
    /// <c>{"name":{"firstName":"Anna","lastName":"Nováková"},"email":"..."}</c>.
    /// Returns <c>null</c> when the field is absent (repeat login) or
    /// carries no name.
    /// </summary>
    private static string? TryExtractNameFromUserField(string? userFieldJson)
    {
        if (string.IsNullOrWhiteSpace(userFieldJson)) return null;

        try
        {
            using var doc = JsonDocument.Parse(userFieldJson);
            if (!doc.RootElement.TryGetProperty("name", out var nameEl)) return null;

            var first = nameEl.TryGetProperty("firstName", out var f) ? f.GetString() : null;
            var last = nameEl.TryGetProperty("lastName", out var l) ? l.GetString() : null;
            var full = string.Join(' ', new[] { first, last }.Where(s => !string.IsNullOrWhiteSpace(s)));
            return string.IsNullOrWhiteSpace(full) ? null : full;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<JsonWebKeySet> GetJwksAsync(CancellationToken ct)
    {
        if (memoryCache.TryGetValue<JsonWebKeySet>(JwksCacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        var http = httpClientFactory.CreateClient(HttpClientName);
        var opts = options.Value;

        string json;
        try
        {
            json = await http.GetStringAsync(opts.JwksEndpoint, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new AppleOAuthException("Failed to fetch Apple's JWKS.", ex);
        }

        JsonWebKeySet jwks;
        try
        {
            jwks = new JsonWebKeySet(json);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            throw new AppleOAuthException("Apple's JWKS response could not be parsed.", ex);
        }

        memoryCache.Set(JwksCacheKey, jwks, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = JwksCacheTtl,
        });

        return jwks;
    }

    private sealed record AppleTokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("id_token")] string? IdToken,
        [property: JsonPropertyName("token_type")] string? TokenType,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);

    private readonly record struct AppleIdTokenClaims(
        string? Sub,
        string? Email,
        bool EmailVerified,
        bool IsPrivateEmail);
}
