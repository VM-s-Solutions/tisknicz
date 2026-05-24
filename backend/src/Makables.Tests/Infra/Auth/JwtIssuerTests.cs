using System.Security.Cryptography;
using FluentAssertions;
using Makables.Core.Domain.Identity;
using Makables.Infra.Common.Auth;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Makables.Tests.Infra.Auth;

public class JwtIssuerTests
{
    private static readonly byte[] TestKeyBytes = RandomNumberGenerator.GetBytes(32);
    private static readonly string TestKeyBase64 = Convert.ToBase64String(TestKeyBytes);

    private static JwtOptions DefaultOptions() => new()
    {
        Issuer = "https://makables.test",
        SigningKeyBase64 = TestKeyBase64,
        AccessTokenLifetime = TimeSpan.FromMinutes(15),
        KeyId = "k1",
    };

    private static IJwtIssuer CreateIssuer(Action<JwtOptions>? configure = null)
    {
        var options = DefaultOptions();
        configure?.Invoke(options);
        return new JwtIssuer(Options.Create(options));
    }

    private static User CreateUser(UserRole role = UserRole.Customer) => User.Create(
        id: "user-01",
        email: "anna@example.cz",
        role: role,
        fullName: "Anna",
        countryCodePrimary: "CZ");

    [Theory]
    [InlineData("customer")]
    [InlineData("maker")]
    [InlineData("admin")]
    public void Issue_produces_a_token_for_every_allowed_audience(string audience)
    {
        var issuer = CreateIssuer();
        var now = new DateTimeOffset(2026, 5, 23, 12, 0, 0, TimeSpan.Zero);

        var token = issuer.Issue(CreateUser(), audience, now);

        token.Token.Should().NotBeNullOrWhiteSpace();
        token.ExpiresAt.Should().Be(now + TimeSpan.FromMinutes(15));
        token.Jti.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("guest")]
    [InlineData("CUSTOMER")]
    [InlineData("")]
    public void Issue_rejects_unknown_audiences(string audience)
    {
        var issuer = CreateIssuer();
        var now = DateTimeOffset.UtcNow;

        Action act = () => issuer.Issue(CreateUser(), audience, now);

        act.Should().Throw<ArgumentException>().WithParameterName("audience");
    }

    [Fact]
    public void Issue_token_carries_all_ADR_claims()
    {
        var issuer = CreateIssuer();
        var now = new DateTimeOffset(2026, 5, 23, 12, 0, 0, TimeSpan.Zero);
        var user = CreateUser(UserRole.Maker);

        var token = issuer.Issue(user, "maker", now);
        var parsed = new JsonWebToken(token.Token);

        parsed.Issuer.Should().Be("https://makables.test");
        parsed.Audiences.Should().ContainSingle().Which.Should().Be("maker");
        parsed.Subject.Should().Be("user-01");
        parsed.GetClaim("role").Value.Should().Be("maker");
        parsed.GetClaim("country_code").Value.Should().Be("CZ");
        parsed.GetClaim("email").Value.Should().Be("anna@example.cz");
        parsed.GetClaim("jti").Value.Should().Be(token.Jti);
        parsed.ValidFrom.Should().Be(now.UtcDateTime);
        parsed.ValidTo.Should().Be((now + TimeSpan.FromMinutes(15)).UtcDateTime);
    }

    [Fact]
    public async Task Token_validates_with_the_same_signing_key()
    {
        var issuer = CreateIssuer();
        var now = DateTimeOffset.UtcNow;
        var token = issuer.Issue(CreateUser(), "customer", now);

        var handler = new JsonWebTokenHandler();
        var result = await handler.ValidateTokenAsync(token.Token, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "https://makables.test",
            ValidateAudience = true,
            ValidAudience = "customer",
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(TestKeyBytes),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(5),
        });

        result.IsValid.Should().BeTrue(result.Exception?.ToString());
    }

    [Fact]
    public async Task Token_fails_validation_when_audience_does_not_match_host_policy()
    {
        // A customer token replayed against the maker host MUST fail.
        var issuer = CreateIssuer();
        var token = issuer.Issue(CreateUser(), "customer", DateTimeOffset.UtcNow);

        var handler = new JsonWebTokenHandler();
        var result = await handler.ValidateTokenAsync(token.Token, new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = true,
            ValidAudience = "maker",
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(TestKeyBytes),
            ValidateLifetime = false,
        });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Token_fails_validation_with_a_different_signing_key()
    {
        var issuer = CreateIssuer();
        var token = issuer.Issue(CreateUser(), "customer", DateTimeOffset.UtcNow);

        var otherKey = RandomNumberGenerator.GetBytes(32);
        var handler = new JsonWebTokenHandler();
        var result = await handler.ValidateTokenAsync(token.Token, new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(otherKey),
            ValidateLifetime = false,
        });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Ctor_rejects_missing_signing_key()
    {
        Action act = () => CreateIssuer(o => o.SigningKeyBase64 = "");
        act.Should().Throw<InvalidOperationException>().WithMessage("*SigningKeyBase64*");
    }

    [Fact]
    public void Ctor_rejects_signing_key_shorter_than_32_bytes()
    {
        var shortKey = Convert.ToBase64String(new byte[16]);
        Action act = () => CreateIssuer(o => o.SigningKeyBase64 = shortKey);
        act.Should().Throw<InvalidOperationException>().WithMessage("*at least 32 bytes*");
    }

    [Fact]
    public void Ctor_rejects_missing_issuer()
    {
        Action act = () => CreateIssuer(o => o.Issuer = "");
        act.Should().Throw<InvalidOperationException>().WithMessage("*Issuer*");
    }

    [Fact]
    public void Ctor_rejects_non_positive_token_lifetime()
    {
        Action act = () => CreateIssuer(o => o.AccessTokenLifetime = TimeSpan.Zero);
        act.Should().Throw<InvalidOperationException>().WithMessage("*lifetime*");
    }

    [Fact]
    public void Each_issued_token_has_a_unique_jti()
    {
        var issuer = CreateIssuer();
        var now = DateTimeOffset.UtcNow;
        var jti1 = issuer.Issue(CreateUser(), "customer", now).Jti;
        var jti2 = issuer.Issue(CreateUser(), "customer", now).Jti;
        jti1.Should().NotBe(jti2);
    }
}
