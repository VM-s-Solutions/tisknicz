using FluentAssertions;
using Makables.Core.Domain.Identity;

namespace Makables.Tests.Domain.Identity;

public class OpaqueTokenFactoryTests
{
    [Fact]
    public void GenerateUrlSafe32_produces_url_safe_raw_value()
    {
        var (raw, hash) = OpaqueTokenFactory.GenerateUrlSafe32();

        raw.Should().NotContain("+").And.NotContain("/").And.NotContain("=");
        // 32 bytes -> 43 base64 chars without padding.
        raw.Length.Should().Be(43);
        hash.Length.Should().Be(64);
        hash.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void GenerateUrlSafe32_produces_a_different_raw_each_call()
    {
        var a = OpaqueTokenFactory.GenerateUrlSafe32();
        var b = OpaqueTokenFactory.GenerateUrlSafe32();

        a.Raw.Should().NotBe(b.Raw);
        a.Hash.Should().NotBe(b.Hash);
    }

    [Fact]
    public void Sha256Hex_is_deterministic()
    {
        OpaqueTokenFactory.Sha256Hex("hello")
            .Should().Be("2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Sha256Hex_rejects_blank(string raw)
    {
        Action act = () => OpaqueTokenFactory.Sha256Hex(raw);
        act.Should().Throw<ArgumentException>().WithParameterName("rawToken");
    }
}
