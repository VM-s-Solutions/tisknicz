using FluentAssertions;
using Makables.Core.Domain.Common;

namespace Makables.Tests.Domain.Common;

public class LanguageCodeTests
{
    [Theory]
    [InlineData("cs-CZ")]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    public void IsValid_accepts_well_formed_BCP_47_tags(string tag) =>
        LanguageCode.IsValid(tag).Should().BeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("cs")]                 // bare language
    [InlineData("CS-cz")]               // wrong case on language
    [InlineData("cs-cz")]               // wrong case on region
    [InlineData("cs_CZ")]               // underscore separator
    [InlineData("cs-Latn-CZ")]          // script subtag
    [InlineData("cs-CZ-x-priv")]        // private-use extension
    public void IsValid_rejects_anything_other_than_strict_lang_REGION(string? tag) =>
        LanguageCode.IsValid(tag).Should().BeFalse();

    [Fact]
    public void Supported_contains_both_launch_languages() =>
        LanguageCode.Supported.Should().BeEquivalentTo([LanguageCode.CsCZ, LanguageCode.EnUS]);

    [Fact]
    public void DefaultFallback_is_csCZ_for_the_Czech_launch_market() =>
        LanguageCode.DefaultFallback.Should().Be(LanguageCode.CsCZ);
}
