using FluentAssertions;
using Makables.Core.Domain.Addresses;

namespace Makables.Tests.Domain.Addresses;

public class AddressTests
{
    private static Address ValidDefaults(string? countryCodeIso = "cz", string? auditCountryCode = "cz") =>
        Address.Create(
            id: "addr-01",
            street: "Karlovarská třída",
            houseNumber: "123/45a",
            city: "Praha",
            zip: "150 00",
            countryCodeIso: countryCodeIso!,
            auditCountryCode: auditCountryCode!);

    [Fact]
    public void Create_trims_strings_and_uppercases_both_country_codes()
    {
        var a = Address.Create(
            id: "addr-01",
            street: "  Karlovarská třída  ",
            houseNumber: "  123/45a  ",
            city: "  Praha  ",
            zip: "  150 00  ",
            countryCodeIso: "cz",
            auditCountryCode: "cz",
            state: "  Středočeský  ");

        a.Street.Should().Be("Karlovarská třída");
        a.HouseNumber.Should().Be("123/45a");
        a.City.Should().Be("Praha");
        a.Zip.Should().Be("150 00");
        a.State.Should().Be("Středočeský");
        a.CountryCodeIso.Should().Be("CZ");
        a.CountryCode.Should().Be("CZ", "Auditable.CountryCode is supplied independently as the OWNER's tenant");
    }

    [Fact]
    public void Create_keeps_shipto_country_and_tenant_country_independent()
    {
        // A CZ-tenanted customer with an SK shipping address.
        var a = Address.Create(
            id: "addr-01",
            street: "S", houseNumber: "1", city: "C", zip: "83100",
            countryCodeIso: "SK", auditCountryCode: "CZ");

        a.CountryCodeIso.Should().Be("SK");
        a.CountryCode.Should().Be("CZ");
    }

    [Fact]
    public void Create_treats_whitespace_state_as_null()
    {
        var a = Address.Create("a", "S", "1", "C", "12345", "CZ", "CZ", state: "   ");
        a.State.Should().BeNull();
    }

    [Theory]
    [InlineData("", "house", "city", "12345", "CZ", "CZ")]
    [InlineData("street", "", "city", "12345", "CZ", "CZ")]
    [InlineData("street", "house", "", "12345", "CZ", "CZ")]
    [InlineData("street", "house", "city", "", "CZ", "CZ")]
    [InlineData("street", "house", "city", "12345", "", "CZ")]
    [InlineData("street", "house", "city", "12345", "CZE", "CZ")]
    [InlineData("street", "house", "city", "12345", "C", "CZ")]
    [InlineData("street", "house", "city", "12345", "CZ", "")]
    [InlineData("street", "house", "city", "12345", "CZ", "CZE")]
    public void Create_rejects_invalid_required_fields(
        string street, string houseNumber, string city, string zip,
        string countryCodeIso, string auditCountryCode)
    {
        var act = () => Address.Create("a", street, houseNumber, city, zip, countryCodeIso, auditCountryCode);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_rejects_empty_id()
    {
        var act = () => Address.Create("", "s", "1", "c", "12345", "CZ", "CZ");
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(201, 20, 200, 16, 100)]   // street too long
    [InlineData(200, 21, 200, 16, 100)]   // house number too long
    [InlineData(200, 20, 201, 16, 100)]   // city too long
    [InlineData(200, 20, 200, 17, 100)]   // zip too long
    [InlineData(200, 20, 200, 16, 101)]   // state too long
    public void Create_rejects_fields_exceeding_column_caps(
        int street, int house, int city, int zip, int state)
    {
        var act = () => Address.Create(
            id: "a",
            street: new string('a', street),
            houseNumber: new string('a', house),
            city: new string('a', city),
            zip: new string('a', zip),
            countryCodeIso: "CZ",
            auditCountryCode: "CZ",
            state: new string('a', state));
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void New_address_has_no_coordinates_by_default()
    {
        var a = ValidDefaults();
        a.Latitude.Should().BeNull();
        a.Longitude.Should().BeNull();
    }

    [Fact]
    public void SetCoordinates_with_value_sets_both_fields()
    {
        var a = ValidDefaults();

        a.SetCoordinates(Coordinates.Of(50.0875, 14.4213));

        a.Latitude.Should().Be(50.0875);
        a.Longitude.Should().Be(14.4213);
    }

    [Fact]
    public void SetCoordinates_with_null_clears_both_fields()
    {
        var a = ValidDefaults();
        a.SetCoordinates(Coordinates.Of(50.0875, 14.4213));

        a.SetCoordinates(null);

        a.Latitude.Should().BeNull();
        a.Longitude.Should().BeNull();
    }
}
