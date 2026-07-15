using FluentAssertions;
using Makables.Core.Domain.Common;

namespace Makables.Tests.Domain.Common;

/// <summary>
/// Pins the <see cref="ProhibitedContent"/> screen used by the category
/// CRUD validators (T-0119): vulgar terms are caught across casing,
/// diacritics, and separator tricks; ordinary Czech taxonomy words —
/// including ones that superficially resemble blocked stems — pass.
/// </summary>
public class ProhibitedContentTests
{
    [Theory]
    [InlineData("kurva")]
    [InlineData("Kurvy tisk")]           // root match inside an inflection
    [InlineData("KURVA")]                // casing
    [InlineData("kůrva")]                // diacritics normalised
    [InlineData("píča")]                 // token match after normalisation
    [InlineData("na-zakázku-píča")]      // separators collapse to word boundaries
    [InlineData("fuck 3D")]
    [InlineData("Zmrdi s.r.o.")]
    [InlineData("hovno")]
    [InlineData("debil")]
    public void Prohibited_terms_are_caught(string text) =>
        ProhibitedContent.ContainsProhibitedTerm(text).Should().BeTrue(text);

    [Theory]
    [InlineData("3D tisk")]
    [InlineData("Potisk textilu")]       // "tisk" stays innocent
    [InlineData("Laser & CNC")]
    [InlineData("Velkoformátový tisk")]
    [InlineData("Šperky a doplňky")]
    [InlineData("Kůže a kožené výrobky")] // diacritic word near "kurva" stem must NOT match
    [InlineData("Keramika")]
    [InlineData("Dárky pro debaty")]      // "debat..." is not "debil"
    [InlineData("")]
    [InlineData(null)]
    public void Ordinary_names_pass(string? text) =>
        ProhibitedContent.ContainsProhibitedTerm(text).Should().BeFalse(text ?? "<null>");
}
