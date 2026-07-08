using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Makables.Infra.Clients.Apple;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;

namespace Makables.Tests.Infra.Clients.Apple;

/// <summary>
/// Closes the Gate-0/T-0139 QA coverage gap on <see cref="AppleOAuthClient"/>
/// (docs/test-plans/T-0139.md, gap 1): drives <c>ExchangeCodeAsync</c> end-to-
/// end against a stub token endpoint + JWKS so the private
/// <c>ParseFlexibleBool</c> logic (<c>email_verified</c> as JSON bool vs the
/// string <c>"true"</c>/<c>"false"</c> quirk Apple documents, ADR 0026 AC-4)
/// is exercised for real, not just asserted against a hand-built
/// <see cref="Makables.Core.Domain.Identity.AppleProfile"/>. Also covers
/// AC-3's remaining gap: the token-exchange POST body must carry the
/// freshly-minted <see cref="AppleClientSecretSigner"/> JWT as the
/// <c>client_secret</c> form field.
/// </summary>
public class AppleOAuthClientTests
{
    private const string ClientId = "cz.makables.web";
    private const string TeamId = "TEAMID1234";
    private const string KeyId = "test-key-1";
    private const string RedirectUri = "https://makables.cz/auth/apple/callback";
    private const string Sub = "001837.abcdef1234567890.1234";
    private const string Email = "anna@example.cz";

    private static string GenerateSignerPrivateKeyPem()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return ecdsa.ExportPkcs8PrivateKeyPem();
    }

    /// <summary>Generates an Apple-shaped id_token key pair + matching JWKS JSON.</summary>
    private static (ECDsa Key, string JwksJson) GenerateIdTokenKeyAndJwks()
    {
        var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = ecdsa.ExportParameters(false);
        var x = Base64UrlEncoder.Encode(parameters.Q.X);
        var y = Base64UrlEncoder.Encode(parameters.Q.Y);
        var jwks = $$"""
            {"keys":[{"kty":"EC","crv":"P-256","x":"{{x}}","y":"{{y}}","kid":"{{KeyId}}","use":"sig","alg":"ES256"}]}
            """;
        return (ecdsa, jwks);
    }

    private static string MintIdToken(
        ECDsa signingKey,
        object? emailVerified,
        object? isPrivateEmail = null,
        bool includeEmailVerified = true,
        DateTimeOffset? now = null)
    {
        var moment = now ?? DateTimeOffset.UtcNow;
        var key = new ECDsaSecurityKey(signingKey) { KeyId = KeyId };
        var creds = new SigningCredentials(key, SecurityAlgorithms.EcdsaSha256)
        {
            CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false },
        };

        var claims = new Dictionary<string, object>
        {
            ["sub"] = Sub,
            ["email"] = Email,
        };
        if (includeEmailVerified && emailVerified is not null)
        {
            claims["email_verified"] = emailVerified;
        }
        if (isPrivateEmail is not null)
        {
            claims["is_private_email"] = isPrivateEmail;
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = "https://appleid.apple.com",
            Audience = ClientId,
            IssuedAt = moment.UtcDateTime,
            NotBefore = moment.UtcDateTime,
            Expires = moment.UtcDateTime.AddMinutes(10),
            SigningCredentials = creds,
            Claims = claims,
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private static AppleOAuthClient BuildSut(
        StubHttpMessageHandler handler,
        out string signerPrivateKeyPem)
    {
        signerPrivateKeyPem = GenerateSignerPrivateKeyPem();

        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(AppleOAuthClient.HttpClientName).Returns(_ => new HttpClient(handler));

        var opts = Options.Create(new AppleOAuthOptions
        {
            ClientId = ClientId,
            TeamId = TeamId,
            KeyId = "signer-key",
            PrivateKeyPem = signerPrivateKeyPem,
            AuthorizationEndpoint = "https://appleid.apple.test/auth/authorize",
            TokenEndpoint = "https://appleid.apple.test/auth/token",
            JwksEndpoint = "https://appleid.apple.test/auth/keys",
        });

        var signer = new AppleClientSecretSigner(opts);
        var cache = new MemoryCache(new MemoryCacheOptions());

        return new AppleOAuthClient(factory, opts, signer, cache, NullLogger<AppleOAuthClient>.Instance);
    }

    private static HttpResponseMessage TokenResponse(string idToken) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    access_token = "at-1",
                    id_token = idToken,
                    token_type = "bearer",
                    expires_in = 3600,
                }),
                Encoding.UTF8,
                "application/json"),
        };

    // ---- email_verified string-vs-bool quirk (AC-4, ParseFlexibleBool) ----

    [Fact]
    public async Task Email_verified_as_JSON_bool_true_is_parsed_true()
    {
        var (key, jwks) = GenerateIdTokenKeyAndJwks();
        var handler = new StubHttpMessageHandler(jwks);
        var sut = BuildSut(handler, out _);
        handler.TokenResponseFactory = () => TokenResponse(MintIdToken(key, true));

        var profile = await sut.ExchangeCodeAsync("code", RedirectUri, null, CancellationToken.None);

        profile.EmailVerified.Should().BeTrue();
    }

    [Fact]
    public async Task Email_verified_as_JSON_bool_false_is_parsed_false()
    {
        var (key, jwks) = GenerateIdTokenKeyAndJwks();
        var handler = new StubHttpMessageHandler(jwks);
        var sut = BuildSut(handler, out _);
        handler.TokenResponseFactory = () => TokenResponse(MintIdToken(key, false));

        var profile = await sut.ExchangeCodeAsync("code", RedirectUri, null, CancellationToken.None);

        profile.EmailVerified.Should().BeFalse();
    }

    [Fact]
    public async Task Email_verified_as_the_string_true_is_parsed_true()
    {
        var (key, jwks) = GenerateIdTokenKeyAndJwks();
        var handler = new StubHttpMessageHandler(jwks);
        var sut = BuildSut(handler, out _);
        handler.TokenResponseFactory = () => TokenResponse(MintIdToken(key, "true"));

        var profile = await sut.ExchangeCodeAsync("code", RedirectUri, null, CancellationToken.None);

        profile.EmailVerified.Should().BeTrue();
    }

    [Fact]
    public async Task Email_verified_as_the_string_false_is_parsed_false()
    {
        // The exact quirk called out by ADR 0026 AC-4: Apple sometimes
        // serializes email_verified as the JSON string "false" rather
        // than a native boolean.
        var (key, jwks) = GenerateIdTokenKeyAndJwks();
        var handler = new StubHttpMessageHandler(jwks);
        var sut = BuildSut(handler, out _);
        handler.TokenResponseFactory = () => TokenResponse(MintIdToken(key, "false"));

        var profile = await sut.ExchangeCodeAsync("code", RedirectUri, null, CancellationToken.None);

        profile.EmailVerified.Should().BeFalse();
    }

    [Fact]
    public async Task Email_verified_as_string_TRUE_uppercase_is_parsed_true_case_insensitively()
    {
        var (key, jwks) = GenerateIdTokenKeyAndJwks();
        var handler = new StubHttpMessageHandler(jwks);
        var sut = BuildSut(handler, out _);
        handler.TokenResponseFactory = () => TokenResponse(MintIdToken(key, "TRUE"));

        var profile = await sut.ExchangeCodeAsync("code", RedirectUri, null, CancellationToken.None);

        profile.EmailVerified.Should().BeTrue();
    }

    [Fact]
    public async Task Email_verified_missing_entirely_defaults_to_false_fail_closed()
    {
        var (key, jwks) = GenerateIdTokenKeyAndJwks();
        var handler = new StubHttpMessageHandler(jwks);
        var sut = BuildSut(handler, out _);
        handler.TokenResponseFactory = () => TokenResponse(MintIdToken(key, null, includeEmailVerified: false));

        var profile = await sut.ExchangeCodeAsync("code", RedirectUri, null, CancellationToken.None);

        profile.EmailVerified.Should().BeFalse();
    }

    [Fact]
    public async Task Email_verified_as_an_unrecognized_string_value_defaults_to_false_fail_closed()
    {
        var (key, jwks) = GenerateIdTokenKeyAndJwks();
        var handler = new StubHttpMessageHandler(jwks);
        var sut = BuildSut(handler, out _);
        handler.TokenResponseFactory = () => TokenResponse(MintIdToken(key, "maybe"));

        var profile = await sut.ExchangeCodeAsync("code", RedirectUri, null, CancellationToken.None);

        profile.EmailVerified.Should().BeFalse();
    }

    [Fact]
    public async Task Is_private_email_string_true_uses_the_same_flexible_bool_parser()
    {
        var (key, jwks) = GenerateIdTokenKeyAndJwks();
        var handler = new StubHttpMessageHandler(jwks);
        var sut = BuildSut(handler, out _);
        handler.TokenResponseFactory = () => TokenResponse(MintIdToken(key, true, isPrivateEmail: "true"));

        var profile = await sut.ExchangeCodeAsync("code", RedirectUri, null, CancellationToken.None);

        profile.IsPrivateEmail.Should().BeTrue();
    }

    // ---- AC-3: exchange POST carries the minted JWT as client_secret ----

    [Fact]
    public async Task ExchangeCodeAsync_posts_a_freshly_minted_JWT_as_the_client_secret_form_field()
    {
        var (key, jwks) = GenerateIdTokenKeyAndJwks();
        var handler = new StubHttpMessageHandler(jwks);
        var sut = BuildSut(handler, out _);
        handler.TokenResponseFactory = () => TokenResponse(MintIdToken(key, true));

        await sut.ExchangeCodeAsync("auth-code-1", RedirectUri, null, CancellationToken.None);

        var body = handler.LastTokenRequestBody;
        body.Should().NotBeNull();
        var form = ParseFormUrlEncoded(body!);
        var clientSecret = form["client_secret"];
        clientSecret.Should().NotBeNullOrWhiteSpace();
        // Sufficient JWT-shape proof (three dot-separated base64url
        // segments) — the secret's own claims/header shape is already
        // pinned by AppleClientSecretSignerTests.
        clientSecret!.Split('.').Should().HaveCount(3);
        form["code"].Should().Be("auth-code-1");
        form["grant_type"].Should().Be("authorization_code");
        form["client_id"].Should().Be(ClientId);
        form["redirect_uri"].Should().Be(RedirectUri);
    }

    private static Dictionary<string, string> ParseFormUrlEncoded(string body) =>
        body.Split('&')
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(
                parts => Uri.UnescapeDataString(parts[0]),
                parts => Uri.UnescapeDataString(parts.Length > 1 ? parts[1] : string.Empty));

    // ---- stub HTTP handler routing token endpoint vs JWKS endpoint ----

    private sealed class StubHttpMessageHandler(string jwksJson) : HttpMessageHandler
    {
        public Func<HttpResponseMessage>? TokenResponseFactory { get; set; }
        public string? LastTokenRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsoluteUri.Contains("/auth/keys"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(jwksJson, Encoding.UTF8, "application/json"),
                };
            }

            if (request.RequestUri.AbsoluteUri.Contains("/auth/token"))
            {
                if (request.Content is not null)
                {
                    LastTokenRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
                }
                return TokenResponseFactory?.Invoke() ?? new HttpResponseMessage(HttpStatusCode.InternalServerError);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }
}
