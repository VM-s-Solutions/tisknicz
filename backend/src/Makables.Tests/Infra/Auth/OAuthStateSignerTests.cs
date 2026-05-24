using System.Security.Cryptography;
using FluentAssertions;
using Makables.Core.Domain.Identity;
using Makables.Infra.Common.Auth;
using Microsoft.Extensions.Options;

namespace Makables.Tests.Infra.Auth;

public class OAuthStateSignerTests
{
    private static readonly byte[] TestKey = RandomNumberGenerator.GetBytes(32);

    private static OAuthStateSigner CreateSigner(byte[]? key = null) =>
        new(Options.Create(new JwtOptions
        {
            Issuer = "https://makables.test",
            SigningKeyBase64 = Convert.ToBase64String(key ?? TestKey),
            AccessTokenLifetime = TimeSpan.FromMinutes(15),
        }));

    [Fact]
    public void Sign_then_TryVerify_round_trips_the_payload()
    {
        var signer = CreateSigner();
        var now = new DateTimeOffset(2026, 5, 25, 10, 0, 0, TimeSpan.Zero);
        var payload = new OAuthStatePayload("customer", "nonce-1", now);

        var state = signer.Sign(payload);

        var verified = signer.TryVerify(state, now + TimeSpan.FromMinutes(1));
        verified.Should().NotBeNull();
        verified!.Audience.Should().Be("customer");
        verified.Nonce.Should().Be("nonce-1");
        verified.IssuedAt.Should().Be(now);
    }

    [Fact]
    public void TryVerify_rejects_state_signed_with_a_different_key()
    {
        var signerA = CreateSigner();
        var signerB = CreateSigner(RandomNumberGenerator.GetBytes(32));
        var now = DateTimeOffset.UtcNow;
        var state = signerA.Sign(new OAuthStatePayload("customer", "nonce", now));

        var verified = signerB.TryVerify(state, now);

        verified.Should().BeNull();
    }

    [Fact]
    public void TryVerify_rejects_payload_tampered_after_signing()
    {
        // The user edits the audience between start and callback.
        var signer = CreateSigner();
        var now = DateTimeOffset.UtcNow;
        var state = signer.Sign(new OAuthStatePayload("customer", "nonce", now));

        // Replace the payload half with a different one (constructed the
        // way an attacker would: base64-encoded JSON for the swap).
        var dot = state.IndexOf('.');
        var attackerPayload = "{\"audience\":\"admin\",\"nonce\":\"nonce\",\"issuedAt\":\"" + now.ToString("o") + "\"}";
        var fakeB64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(attackerPayload))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var tampered = fakeB64 + state[dot..];

        signer.TryVerify(tampered, now).Should().BeNull();
    }

    [Fact]
    public void TryVerify_rejects_stale_state_past_the_lifetime_window()
    {
        var signer = CreateSigner();
        var issuedAt = DateTimeOffset.UtcNow;
        var state = signer.Sign(new OAuthStatePayload("customer", "nonce", issuedAt));

        var farFuture = issuedAt + OAuthStateSigner.StateLifetime + TimeSpan.FromSeconds(1);
        signer.TryVerify(state, farFuture).Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-state")]
    [InlineData("only-one-part")]
    [InlineData(".empty-payload")]
    [InlineData("empty-signature.")]
    public void TryVerify_rejects_malformed_inputs(string input)
    {
        var signer = CreateSigner();
        signer.TryVerify(input, DateTimeOffset.UtcNow).Should().BeNull();
    }

    [Fact]
    public void Ctor_rejects_missing_signing_key()
    {
        var act = () => new OAuthStateSigner(Options.Create(new JwtOptions
        {
            SigningKeyBase64 = "",
            Issuer = "x",
            AccessTokenLifetime = TimeSpan.FromMinutes(1),
        }));
        act.Should().Throw<InvalidOperationException>();
    }
}
