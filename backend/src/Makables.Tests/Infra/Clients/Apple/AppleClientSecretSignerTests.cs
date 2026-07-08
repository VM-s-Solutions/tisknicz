using System.Security.Cryptography;
using FluentAssertions;
using Makables.Infra.Clients.Apple;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Makables.Tests.Infra.Clients.Apple;

/// <summary>
/// Pure-logic tests for <see cref="AppleClientSecretSigner"/> — no infra
/// dependencies, per <c>docs/process/tdd-policy.md</c>. Asserts the ES256
/// JWT client secret's claims/header per ADR 0026 / T-0139 AC-3.
/// </summary>
public class AppleClientSecretSignerTests
{
    private static string GenerateTestPrivateKeyPem()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return ecdsa.ExportPkcs8PrivateKeyPem();
    }

    private static AppleClientSecretSigner CreateSigner(string privateKeyPem) =>
        new(Options.Create(new AppleOAuthOptions
        {
            ClientId = "cz.makables.web",
            TeamId = "TEAMID1234",
            KeyId = "KEYID5678",
            PrivateKeyPem = privateKeyPem,
        }));

    [Fact]
    public void Mints_a_JWT_with_iss_sub_aud_and_kid_header_per_ADR_0026()
    {
        var pem = GenerateTestPrivateKeyPem();
        var signer = CreateSigner(pem);
        var now = new DateTimeOffset(2026, 7, 7, 12, 0, 0, TimeSpan.Zero);

        var secret = signer.Mint(now);

        var token = new JsonWebToken(secret);

        token.Alg.Should().Be("ES256");
        token.Kid.Should().Be("KEYID5678");
        token.Issuer.Should().Be("TEAMID1234");
        token.Audiences.Should().ContainSingle().Which.Should().Be("https://appleid.apple.com");
        token.Subject.Should().Be("cz.makables.web");
        token.ValidTo.Should().BeCloseTo(now.UtcDateTime + TimeSpan.FromMinutes(15), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Two_mints_produce_different_tokens_no_caching()
    {
        var pem = GenerateTestPrivateKeyPem();
        var signer = CreateSigner(pem);
        var now = DateTimeOffset.UtcNow;

        var first = signer.Mint(now);
        var second = signer.Mint(now.AddSeconds(1));

        first.Should().NotBe(second);
    }

    [Fact]
    public void Throws_AppleOAuthException_when_TeamId_missing()
    {
        var pem = GenerateTestPrivateKeyPem();
        var signer = new AppleClientSecretSigner(Options.Create(new AppleOAuthOptions
        {
            ClientId = "cz.makables.web",
            TeamId = "",
            KeyId = "KEYID5678",
            PrivateKeyPem = pem,
        }));

        var act = () => signer.Mint(DateTimeOffset.UtcNow);

        act.Should().Throw<AppleOAuthException>();
    }

    [Fact]
    public void Throws_AppleOAuthException_when_private_key_is_malformed()
    {
        var signer = CreateSigner("not a valid pem");

        var act = () => signer.Mint(DateTimeOffset.UtcNow);

        act.Should().Throw<AppleOAuthException>();
    }
}
