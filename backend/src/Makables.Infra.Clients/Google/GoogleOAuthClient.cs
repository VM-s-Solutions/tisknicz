using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Google.Apis.Auth;
using Makables.Core.Domain.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Makables.Infra.Clients.Google;

/// <summary>
/// Server-side OAuth-2.0 authorization-code flow against Google. Per
/// ADR 0012 §Google OAuth.
///
/// <see cref="BuildAuthorizationUrl"/> is local computation only.
/// <see cref="ExchangeCodeAsync"/> POSTs to Google's token endpoint and
/// verifies the returned ID token against Google's JWKS via
/// <c>GoogleJsonWebSignature.ValidateAsync</c> — that step covers
/// signature, audience-equals-client-id, expiry, and issuer checks.
///
/// HttpClient is injected via <see cref="IHttpClientFactory"/> per
/// CLAUDE.md (no direct HttpClient outside Infra.Clients). Failures
/// surface as exceptions; the use-case layer catches them and maps to
/// <see cref="Makables.Core.Domain.Common.BusinessResult"/> failures.
/// </summary>
public sealed class GoogleOAuthClient(
    IHttpClientFactory httpClientFactory,
    IOptions<GoogleOAuthOptions> options,
    ILogger<GoogleOAuthClient> logger) : IGoogleOAuthClient
{
    private const string AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";

    /// <summary>Named HttpClient registered by the wiring extension.</summary>
    public const string HttpClientName = "Makables.Infra.Clients.Google";

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
            ("access_type", "online"),
            ("prompt", "select_account"),
            // PKCE is not strictly required for confidential clients
            // (we have a client secret) but Google recommends it.
            // Deferred — adds an extra exchange table; revisit before
            // launch.
        };

        var qs = string.Join("&", query.Select(p => $"{Uri.EscapeDataString(p.Item1)}={Uri.EscapeDataString(p.Item2)}"));
        return $"{AuthorizationEndpoint}?{qs}";
    }

    public async Task<GoogleProfile> ExchangeCodeAsync(string code, string redirectUri, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(redirectUri);

        var opts = options.Value;
        if (string.IsNullOrWhiteSpace(opts.ClientId) || string.IsNullOrWhiteSpace(opts.ClientSecret))
            throw new InvalidOperationException("Auth:Google:ClientId / ClientSecret are not configured.");

        var http = httpClientFactory.CreateClient(HttpClientName);

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = opts.ClientId,
            ["client_secret"] = opts.ClientSecret,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code",
        });

        using var response = await http.PostAsync(TokenEndpoint, form, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning("Google token exchange failed: {Status} {Body}", (int)response.StatusCode, body);
            throw new GoogleOAuthException($"Google rejected the authorization code (HTTP {(int)response.StatusCode}).");
        }

        var tokenPayload = await response.Content.ReadFromJsonAsync<GoogleTokenResponse>(cancellationToken: cancellationToken)
            ?? throw new GoogleOAuthException("Google token endpoint returned no body.");

        if (string.IsNullOrWhiteSpace(tokenPayload.IdToken))
            throw new GoogleOAuthException("Google token response had no id_token.");

        // ValidateAsync checks signature against Google's JWKS, expiry,
        // and the audience (which must equal our ClientId).
        GoogleJsonWebSignature.Payload payload;
        try
        {
            payload = await GoogleJsonWebSignature.ValidateAsync(tokenPayload.IdToken,
                new GoogleJsonWebSignature.ValidationSettings { Audience = [opts.ClientId] });
        }
        catch (InvalidJwtException ex)
        {
            logger.LogWarning(ex, "Google ID token validation failed.");
            throw new GoogleOAuthException("Google ID token failed validation.", ex);
        }

        if (string.IsNullOrWhiteSpace(payload.Subject) || string.IsNullOrWhiteSpace(payload.Email))
            throw new GoogleOAuthException("Google ID token missing sub or email.");

        return new GoogleProfile(
            Sub: payload.Subject,
            Email: payload.Email,
            EmailVerified: payload.EmailVerified,
            Name: payload.Name);
    }

    private sealed record GoogleTokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("id_token")] string? IdToken,
        [property: JsonPropertyName("token_type")] string? TokenType,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}

/// <summary>
/// Thrown by <see cref="GoogleOAuthClient"/> for any unrecoverable
/// failure (network error, invalid code, ID-token validation failure).
/// Caught by the use-case layer which maps to a
/// <see cref="Makables.Core.Domain.Common.BusinessResult"/> failure.
/// </summary>
public sealed class GoogleOAuthException : Exception
{
    public GoogleOAuthException(string message) : base(message) { }
    public GoogleOAuthException(string message, Exception inner) : base(message, inner) { }
}
