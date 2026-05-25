using FluentAssertions;
using Makables.Core.Domain.Addresses;

namespace Makables.Tests.Domain.Addresses;

public class AddressTests
{
    private static Address ValidDefaults() =>
        Address.Create(
            id: "addr-01",
            street: "Karlovarská třída",
            houseNumber: "123/45a",
            city: "Praha",
            zip: "150 00",
            countryCodeIso: "cz");

    [Fact]
    public void Create_trims_strings_and_uppercases_country_code()
    {
        var a = Address.Create(
            id: "addr-01",
            street: "  Karlovarská třída  ",
            houseNumber: "  123/45a  ",
            city: "  Praha  ",
            zip: "  150 00  ",
            countryCodeIso: "cz",
            state: "  Středočeský  ");

        a.Street.Should().Be("Karlovarská třída");
        a.HouseNumber.Should().Be("123/45a");
        a.City.Should().Be("Praha");
        a.Zip.Should().Be("150 00");
        a.State.Should().Be("Středočeský");
        a.CountryCodeIso.Should().Be("CZ");
        a.CountryCode.Should().Be("CZ", "Auditable.CountryCode mirrors the address country at create time");
    }

    [Fact]
    public void Create_treats_whitespace_state_as_null()
    {
        var a = Address.Create("a", "S", "1", "C", "12345", "CZ", state: "   ");
        a.State.Should().BeNull();
    }

    [Theory]
    [InlineData("", "house", "city", "12345", "CZ")]
    [InlineData("street", "", "city", "12345", "CZ")]
    [InlineData("street", "house", "", "12345", "CZ")]
    [InlineData("street", "house", "city", "", "CZ")]
    [InlineData("street", "house", "city", "12345", "")]
    [InlineData("street", "house", "city", "12345", "CZE")]
    [InlineData("street", "house", "city", "12345", "C")]
    public void Create_rejects_invalid_required_fields(
        string street, string houseNumber, string city, string zip, string countryCodeIso)
    {
        var act = () => Address.Create("a", street, houseNumber, city, zip, countryCodeIso);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_rejects_empty_id()
    {
        var act = () => Address.Create("", "s", "1", "c", "12345", "CZ");
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
    public void SetCoordinates_sets_both_or_clears_both()
    {
        var a = ValidDefaults();

        a.SetCoordinates(50.0875, 14.4213);
        a.Latitude.Should().Be(50.0875);
        a.Longitude.Should().Be(14.4213);

        a.SetCoordinates(null, null);
        a.Latitude.Should().BeNull();
        a.Longitude.Should().BeNull();
    }

    [Fact]
    public void SetCoordinates_refuses_mixed_null()
    {
        var a = ValidDefaults();

        ((Action)(() => a.SetCoordinates(50.0875, null))).Should().Throw<ArgumentException>();
        ((Action)(() => a.SetCoordinates(null, 14.4213))).Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(-91, 0)]
    [InlineData(91, 0)]
    [InlineData(0, -181)]
    [InlineData(0, 181)]
    public void SetCoordinates_rejects_out_of_range(double lat, double lng)
    {
        var a = ValidDefaults();
        ((Action)(() => a.SetCoordinates(lat, lng))).Should().Throw<ArgumentOutOfRangeException>();
    }
}
