using FluentAssertions;
using Makables.Infra.Common.Czech;

namespace Makables.Tests.Infra.Common.Czech;

public class CzechLegalFormsTests
{
    [Theory]
    [InlineData("112", "Společnost s ručením omezeným")]
    [InlineData("121", "Akciová společnost")]
    [InlineData("101", "Fyzická osoba podnikající dle živnostenského zákona")]
    public void Resolve_known_codes_to_human_readable_Czech_labels(string code, string expected) =>
        CzechLegalForms.Resolve(code).Should().Be(expected);

    [Fact]
    public void Resolve_unknown_code_returns_the_trimmed_code_itself()
    {
        // Better than null — a Maker profile screen can still render
        // something while the catalogue gap is filled.
        CzechLegalForms.Resolve(" 999 ").Should().Be("999");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_null_or_blank_returns_null(string? code) =>
        CzechLegalForms.Resolve(code).Should().BeNull();
}
