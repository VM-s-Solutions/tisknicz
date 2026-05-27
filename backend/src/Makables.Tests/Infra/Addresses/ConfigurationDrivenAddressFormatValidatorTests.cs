using FluentAssertions;
using Makables.Core.Domain.Configuration;
using Makables.Infra.Common.Addresses;
using NSubstitute;

namespace Makables.Tests.Infra.Addresses;

/// <summary>
/// Pins the ADR 0010 §"Per-country format validation" contract.
/// Specifically: null/empty regex on the country row means "accept";
/// configured regex is applied with case-invariant + compiled options.
/// </summary>
public class ConfigurationDrivenAddressFormatValidatorTests
{
    private readonly ICountryConfigurationRepository _countries =
        Substitute.For<ICountryConfigurationRepository>();
    private readonly ConfigurationDrivenAddressFormatValidator _sut;

    public ConfigurationDrivenAddressFormatValidatorTests()
    {
        _sut = new ConfigurationDrivenAddressFormatValidator(_countries);
    }

    private void ArrangeCountry(string code, string? zipRegex)
    {
        var config = CountryConfiguration.Create(
            countryId: code,
            defaultCurrencyCode: "CZK",
            defaultLanguageCode: "cs-CZ",
            timeZoneId: "Europe/Prague",
            phonePrefix: "+420",
            dateFormat: "d. M. yyyy",
            standardVatRateBp: 2100,
            taxIdLabel: "DIČ",
            vatIdLabel: "DIČ",
            registrationNumberLabel: "IČO",
            defaultPaymentProvider: "comgate",
            defaultShippingCarrier: "packeta",
            defaultRegistry: "ares",
            defaultEmailProvider: "sendgrid",
            zipFormat: zipRegex);
        _countries.GetByCodeAsync(code, Arg.Any<CancellationToken>()).Returns(config);
    }

    [Theory]
    [InlineData("12345")]   // CZ ZIPs can be written 5-digit
    [InlineData("123 45")]  // or with the optional space
    public async Task Valid_CZ_ZIPs_pass_the_regex(string zip)
    {
        ArrangeCountry("CZ", @"^\d{3}\s?\d{2}$");

        var result = await _sut.IsValidZipAsync("CZ", zip, CancellationToken.None);

        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("1234")]      // too few digits
    [InlineData("123456")]    // too many digits
    [InlineData("12345 6")]   // extra char
    [InlineData("ABC 12")]    // wrong shape
    public async Task Invalid_CZ_ZIPs_fail_the_regex(string zip)
    {
        ArrangeCountry("CZ", @"^\d{3}\s?\d{2}$");

        var result = await _sut.IsValidZipAsync("CZ", zip, CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ZIP_is_trimmed_before_matching()
    {
        ArrangeCountry("CZ", @"^\d{3}\s?\d{2}$");

        var result = await _sut.IsValidZipAsync("CZ", "  123 45  ", CancellationToken.None);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task Country_with_null_regex_is_accepted_by_default()
    {
        ArrangeCountry("XX", zipRegex: null);

        var result = await _sut.IsValidZipAsync("XX", "anything", CancellationToken.None);

        result.Should().BeTrue("countries without a configured ZIP rule pass through during soft-launch");
    }

    [Fact]
    public async Task Country_row_missing_is_accepted_by_default()
    {
        _countries.GetByCodeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((CountryConfiguration?)null);

        var result = await _sut.IsValidZipAsync("ZZ", "anything", CancellationToken.None);

        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("", "12345")]
    [InlineData("CZ", "")]
    [InlineData("CZ", "   ")]
    [InlineData(null!, "12345")]
    public async Task Blank_inputs_are_rejected_short_circuit(string? country, string zip)
    {
        // Note: doesn't even hit the country lookup.
        var result = await _sut.IsValidZipAsync(country!, zip, CancellationToken.None);

        result.Should().BeFalse();
        await _countries.DidNotReceive().GetByCodeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Country_code_is_normalised_to_uppercase_when_looked_up()
    {
        ArrangeCountry("CZ", @"^\d{3}\s?\d{2}$");

        var result = await _sut.IsValidZipAsync("cz", "12345", CancellationToken.None);

        result.Should().BeTrue();
        await _countries.Received(1).GetByCodeAsync("CZ", Arg.Any<CancellationToken>());
    }
}
