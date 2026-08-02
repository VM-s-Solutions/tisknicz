using FluentAssertions;
using Makables.Core.Domain.Makers;
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

    // === Classify (the "Firma / Živnostník" catalog filter) ===

    [Theory]
    [InlineData("101")]  // OSVČ — the overwhelmingly common maker shape
    [InlineData(" 421 ")]  // foreign natural person, and trimmed on the way in
    public void Classify_natural_person_codes(string code) =>
        CzechLegalForms.Classify(code).Should().Be(MakerLegalType.NaturalPerson);

    [Theory]
    [InlineData("112")]  // s.r.o.
    [InlineData("121")]  // a.s.
    [InlineData("111")]
    [InlineData("205")]
    [InlineData("422")]  // sits next to 421 but is a legal entity
    [InlineData("706")]
    public void Classify_legal_entity_codes(string code) =>
        CzechLegalForms.Classify(code).Should().Be(MakerLegalType.LegalEntity);

    [Fact]
    public void Classify_does_not_guess_from_the_numeric_range()
    {
        // 421 and 422 are adjacent but land in opposite buckets, which is
        // precisely why an uncatalogued code must stay unclassified rather
        // than be inferred from the block it sits in.
        CzechLegalForms.Classify("421").Should().Be(MakerLegalType.NaturalPerson);
        CzechLegalForms.Classify("422").Should().Be(MakerLegalType.LegalEntity);
        CzechLegalForms.Classify("423").Should().BeNull();
    }

    [Theory]
    [InlineData("999")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Classify_unknown_or_blank_returns_null(string? code)
    {
        // Null means "matches neither filter bucket" — an unclassified
        // maker is only ever visible in the unfiltered catalog.
        CzechLegalForms.Classify(code).Should().BeNull();
    }

    [Fact]
    public void Every_catalogued_code_has_both_a_label_and_a_classification()
    {
        // Label and type share one table entry so they cannot drift; this
        // pins that invariant for the codes the mapper actually resolves.
        foreach (var code in new[]
                 { "101", "111", "112", "117", "121", "205", "301", "421", "422", "601", "611", "706" })
        {
            CzechLegalForms.Resolve(code).Should().NotBeNullOrWhiteSpace(because: $"code {code} is catalogued");
            CzechLegalForms.Resolve(code).Should().NotBe(code, because: $"code {code} should resolve to a label");
            CzechLegalForms.Classify(code).Should().NotBeNull(because: $"code {code} is catalogued");
        }
    }
}
