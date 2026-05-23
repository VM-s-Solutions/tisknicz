using FluentAssertions;
using Makables.Core.Domain.Configuration;

namespace Makables.Tests.Domain.ConfigurationTests;

public class CountryConfigurationTests
{
    [Fact]
    public void Create_Normalizes_Country_Code()
    {
        var cfg = BuildCz(countryId: "cz");

        cfg.CountryId.Should().Be("CZ");
        cfg.CountryCode.Should().Be("CZ");
        cfg.Id.Should().Be("CZ");
    }

    [Fact]
    public void Create_Rejects_Bad_Country_Code()
    {
        var act = () => BuildCz(countryId: "CZE");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_Rejects_Currency_Of_Wrong_Length()
    {
        var act = () => BuildCz(currency: "CZ");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_Rejects_OutOfRange_Standard_Vat()
    {
        var act = () => BuildCz(standardVatBp: 99_999);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Create_Rejects_Reduced_Vat_Greater_Than_Standard()
    {
        var act = () => BuildCz(standardVatBp: 1500, reducedVatBp: 2000);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void UpdateVatRates_Validates_Bounds()
    {
        var cfg = BuildCz();

        var act = () => cfg.UpdateVatRates(99_999, null);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void UpdateInvoicingMode_Round_Trips()
    {
        var cfg = BuildCz();

        cfg.UpdateInvoicingMode(InvoicingMode.StandardVat);

        cfg.InvoicingMode.Should().Be(InvoicingMode.StandardVat);
    }

    [Fact]
    public void UpdatePlatformFeeRate_Rejects_OutOfRange()
    {
        var cfg = BuildCz();

        var act = () => cfg.UpdatePlatformFeeRate(15_000);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Defaults_Are_Reasonable_For_Czechia()
    {
        var cfg = BuildCz();

        cfg.PlatformFeeRateBp.Should().Be(1500);  // 15%
        cfg.InvoicingMode.Should().Be(InvoicingMode.None);
        cfg.DefaultCurrencyCode.Should().Be("CZK");
        cfg.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Country_Create_Normalizes_IsoCode()
    {
        var country = Country.Create("cz", "Česká republika", isServiced: true);

        country.Id.Should().Be("CZ");
        country.IsoCode.Should().Be("CZ");
        country.CountryCode.Should().Be("CZ");
        country.IsServiced.Should().BeTrue();
    }

    [Fact]
    public void Country_Create_Rejects_Empty_Name()
    {
        var act = () => Country.Create("CZ", "  ", isServiced: true);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Country_SetServiced_Flips_The_Flag()
    {
        var country = Country.Create("CZ", "Česká republika", isServiced: false);
        country.SetServiced(true);
        country.IsServiced.Should().BeTrue();
    }

    // === Helper ===

    private static CountryConfiguration BuildCz(
        string countryId = "CZ",
        string currency = "CZK",
        int standardVatBp = 2100,
        int? reducedVatBp = 1200) =>
        CountryConfiguration.Create(
            countryId: countryId,
            defaultCurrencyCode: currency,
            defaultLanguageCode: "cs-CZ",
            timeZoneId: "Europe/Prague",
            phonePrefix: "+420",
            dateFormat: "d. M. yyyy",
            standardVatRateBp: standardVatBp,
            reducedVatRateBp: reducedVatBp,
            taxIdLabel: "DIČ",
            vatIdLabel: "DIČ",
            registrationNumberLabel: "IČO",
            defaultPaymentProvider: "comgate",
            defaultShippingCarrier: "packeta",
            defaultRegistry: "ares",
            defaultEmailProvider: "resend");
}
