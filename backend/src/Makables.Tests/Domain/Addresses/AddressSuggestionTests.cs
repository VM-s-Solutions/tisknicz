using FluentAssertions;
using Makables.Core.Domain.Addresses;

namespace Makables.Tests.Domain.Addresses;

/// <summary>
/// Sanity check on the T-0031 AddressSuggestion record shape — it's the
/// wire contract the frontend autocomplete dropdown binds to. Keep the
/// fields stable so a future contract change is explicit.
/// </summary>
public class AddressSuggestionTests
{
    [Fact]
    public void Record_value_equality_holds_on_identical_fields()
    {
        var a = new AddressSuggestion(
            Label: "Karlovarská 1, 150 00 Praha",
            Street: "Karlovarská",
            HouseNumber: "1",
            City: "Praha",
            Zip: "150 00",
            CountryCodeIso: "CZ",
            Coordinates: Coordinates.Of(50.0875, 14.4213));
        var b = a with { };

        a.Should().Be(b);
    }

    [Fact]
    public void Coordinates_can_be_null_when_Mapbox_returns_no_center()
    {
        var s = new AddressSuggestion(
            Label: "L", Street: "S", HouseNumber: "1",
            City: "C", Zip: "12345", CountryCodeIso: "CZ", Coordinates: null);

        s.Coordinates.Should().BeNull();
    }
}
