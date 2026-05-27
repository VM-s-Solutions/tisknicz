using FluentAssertions;
using Makables.Core.Domain.Registry.Validators;

namespace Makables.Tests.Domain.Registry;

/// <summary>
/// Pins the Czech IČO mod-11 checksum algorithm per ADR 0018
/// §"Validation before lookup". Sample IČOs are real Czech entities
/// (the values are public registry data) chosen so a future
/// contributor can sanity-check against ARES directly.
/// </summary>
public class CzechIcoValidatorTests
{
    [Theory]
    [InlineData("27074358")]   // Avast Software s.r.o.
    [InlineData("26168685")]   // Seznam.cz, a.s.
    [InlineData("45272956")]   // Komerční banka, a.s.
    [InlineData("25892533")]   // RWE Energie
    [InlineData("48136450")]   // sample with d8=0 branch (mod=1)
    public void Accepts_valid_real_IČOs(string ico) =>
        CzechIcoValidator.IsValid(ico).Should().BeTrue();

    [Theory]
    [InlineData("27074359")]   // off-by-one checksum
    [InlineData("12345678")]   // arbitrary digits, fails mod-11
    [InlineData("00000000")]   // all zeros — mod=0 expects c=1, got 0
    public void Rejects_digits_that_fail_the_mod_11_checksum(string ico) =>
        CzechIcoValidator.IsValid(ico).Should().BeFalse();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1234567")]    // 7 digits
    [InlineData("123456789")]  // 9 digits
    [InlineData("1234567a")]   // non-digit
    [InlineData("12 34 56 7")] // spaces
    public void Rejects_shape_violations(string? ico) =>
        CzechIcoValidator.IsValid(ico).Should().BeFalse();
}
