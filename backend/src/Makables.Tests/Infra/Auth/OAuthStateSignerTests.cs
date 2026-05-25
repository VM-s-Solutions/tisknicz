using System.Security.Cryptography;
using FluentAssertions;
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

    private const string RedirectUri = "https://makables.cz/auth/google/callback";
    private const string CsrfCookie = "csrf-cookie-value-1";

    [Fact]
    public void Sign_then_TryVerify_round_trips_when_redirect_and_csrf_match()
    {
        var signer = CreateSigner();
        var now = new DateTimeOffset(2026, 5, 25, 10, 0, 0, TimeSpan.Zero);

        var state = signer.Sign("customer", RedirectUri, CsrfCookie, "nonce-1", now);

        var verified = signer.TryVerify(state, RedirectUri, CsrfCookie, now + TimeSpan.FromMinutes(1));
        verified.Should().NotBeNull();
        verified!.Audience.Should().Be("customer");
        verified.Nonce.Should().Be("nonce-1");
        verified.IssuedAt.Should().Be(now);
        verified.RedirectUri.Should().Be(RedirectUri);
    }

    [Fact]
    public void TryVerify_rejects_state_signed_with_a_different_key()
    {
        var signerA = CreateSigner();
        var signerB = CreateSigner(RandomNumberGenerator.GetBytes(32));
        var now = DateTimeOffset.UtcNow;
        var state = signerA.Sign("customer", RedirectUri, CsrfCookie, "n", now);

        signerB.TryVerify(state, RedirectUri, CsrfCookie, now).Should().BeNull();
    }

    [Fact]
    public void HKDF_domain_separation_a_JWT_signed_under_the_raw_key_is_not_a_valid_state()
    {
        // Reviewer T-0026 B-1: even if an attacker controls a string that
        // happens to be a valid JWT signed with the JWT signing key, it
        // must not be accepted as a state. The HKDF sub-key is different,
        // so the HMAC over the JWT body will never match.
        var signer = CreateSigner();
        var hmacOverArbitraryString = HMACSHA256.HashData(TestKey,
            System.Text.Encoding.ASCII.GetBytes("eyJhdWQiOiJjdXN0b21lciJ9"));
        var b64 = Convert.ToBase64String(hmacOverArbitraryString)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var fakeState = "eyJhdWQiOiJjdXN0b21lciJ9." + b64;

        signer.TryVerify(fakeState, RedirectUri, CsrfCookie, DateTimeOffset.UtcNow).Should().BeNull();
    }

    [Fact]
    public void TryVerify_rejects_redirect_uri_mismatch()
    {
        var signer = CreateSigner();
        var now = DateTimeOffset.UtcNow;
        var state = signer.Sign("customer", RedirectUri, CsrfCookie, "n", now);

        signer.TryVerify(state, "https://attacker.test/cb", CsrfCookie, now).Should().BeNull();
    }

    [Fact]
    public void TryVerify_rejects_csrf_cookie_mismatch()
    {
        var signer = CreateSigner();
        var now = DateTimeOffset.UtcNow;
        var state = signer.Sign("customer", RedirectUri, CsrfCookie, "n", now);

        signer.TryVerify(state, RedirectUri, "different-cookie-value", now).Should().BeNull();
    }

    [Fact]
    public void TryVerify_rejects_payload_tampered_after_signing()
    {
        // The user edits the audience between start and callback. The
        // signer's HMAC is over the base64-encoded payload, so any byte
        // change to the payload half invalidates the signature.
        var signer = CreateSigner();
        var now = DateTimeOffset.UtcNow;
        var state = signer.Sign("customer", RedirectUri, CsrfCookie, "n", now);

        var dot = state.IndexOf('.');
        var fakePayload = "{\"audience\":\"admin\",\"redirectUri\":\"" + RedirectUri +
                          "\",\"csrfCookieHash\":\"x\",\"nonce\":\"n\",\"issuedAt\":\"" + now.ToString("o") + "\"}";
        var fakeB64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(fakePayload))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var tampered = fakeB64 + state[dot..];

        signer.TryVerify(tampered, RedirectUri, CsrfCookie, now).Should().BeNull();
    }

    [Fact]
    public void TryVerify_rejects_stale_state_past_the_lifetime_window()
    {
        var signer = CreateSigner();
        var issuedAt = DateTimeOffset.UtcNow;
        var state = signer.Sign("customer", RedirectUri, CsrfCookie, "n", issuedAt);

        var farFuture = issuedAt + OAuthStateSigner.StateLifetime + TimeSpan.FromSeconds(1);
        signer.TryVerify(state, RedirectUri, CsrfCookie, farFuture).Should().BeNull();
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
        signer.TryVerify(input, RedirectUri, CsrfCookie, DateTimeOffset.UtcNow).Should().BeNull();
    }

    [Theory]
    [InlineData("", CsrfCookie)]
    [InlineData(RedirectUri, "")]
    public void TryVerify_rejects_missing_context(string redirect, string cookie)
    {
        var signer = CreateSigner();
        var now = DateTimeOffset.UtcNow;
        var state = signer.Sign("customer", RedirectUri, CsrfCookie, "n", now);
        signer.TryVerify(state, redirect, cookie, now).Should().BeNull();
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
