using FluentAssertions;
using Makables.Core.Domain.Makers.Validators;

namespace Makables.Tests.Domain.Makers;

/// <summary>
/// Pins the ČNB mod-11 checksum and structural rules for Czech bank
/// accounts. Canonical examples taken from real Czech bank account
/// numbers that satisfy the rule (and tweaked siblings that don't).
/// </summary>
public class CzechBankAccountValidatorTests
{
    [Theory]
    [InlineData("2000145399/0100")]         // 10-digit number, mod-11 sum=121
    [InlineData("19-2000145399/0800")]      // prefix + number (the canonical ČNB example)
    [InlineData("670100-2000145399/0100")]  // 6-digit prefix (sum=99)
    public void Valid_canonical_accounts_pass(string value)
    {
        CzechBankAccountValidator.IsValid(value).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("2000145399")]              // missing bank code
    [InlineData("2000145399/")]             // empty bank code
    [InlineData("/0100")]                   // empty number
    [InlineData("2000145399/01000")]        // bank code too long
    [InlineData("2000145399/abcd")]         // non-digit bank code
    [InlineData("abc/0100")]                // non-digit number
    [InlineData("1/0100")]                  // number too short (< 2 digits)
    [InlineData("20001453990/0100")]        // number too long (> 10 digits)
    [InlineData("19-1/0800")]               // number too short with prefix
    [InlineData("1234567-2000145399/0800")] // prefix too long (> 6)
    [InlineData("19/2000145399/0800")]      // multiple slashes
    [InlineData("19-20-001/0800")]          // multiple dashes
    public void Invalid_inputs_are_rejected(string? value)
    {
        CzechBankAccountValidator.IsValid(value).Should().BeFalse();
    }

    [Fact]
    public void Rejects_number_with_bad_checksum()
    {
        // 123456789 has weighted sum 210 — 210 mod 11 = 1, not 0.
        CzechBankAccountValidator.IsValid("123456789/0100").Should().BeFalse();
    }

    [Fact]
    public void Rejects_prefix_with_bad_checksum()
    {
        // 18: weighted sum = 8+2 = 10, not divisible by 11.
        // 19: weighted sum = 9+2 = 11, OK. Pair with a valid number so
        // only the prefix is at fault.
        CzechBankAccountValidator.IsValid("18-2000145399/0800").Should().BeFalse();
    }
}
