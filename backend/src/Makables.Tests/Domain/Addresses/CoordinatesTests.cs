using FluentAssertions;
using Makables.Core.Domain.Addresses;

namespace Makables.Tests.Domain.Addresses;

/// <summary>
/// Pins ADR 0010 §"Mapbox autocomplete + geocoding" value-object
/// contract. The factory rejects NaN / ±Infinity (T-0030 sec reviewer
/// M-1: the original (double?, double?) range checks let NaN through
/// because <c>NaN &lt; -90</c> is false AND <c>NaN &gt; 90</c> is false).
/// </summary>
public class CoordinatesTests
{
    [Theory]
    [InlineData(0, 0)]            // null island
    [InlineData(50.0875, 14.4213)] // Prague
    [InlineData(-90, -180)]       // extreme southwest
    [InlineData(90, 180)]         // extreme northeast
    public void Of_accepts_legal_lat_lng_pairs(double lat, double lng)
    {
        var coords = Coordinates.Of(lat, lng);
        coords.Latitude.Should().Be(lat);
        coords.Longitude.Should().Be(lng);
    }

    [Theory]
    [InlineData(-91, 0)]
    [InlineData(91, 0)]
    [InlineData(0, -181)]
    [InlineData(0, 181)]
    public void Of_rejects_out_of_range(double lat, double lng)
    {
        var act = () => Coordinates.Of(lat, lng);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(double.NaN, 0)]
    [InlineData(0, double.NaN)]
    [InlineData(double.PositiveInfinity, 0)]
    [InlineData(0, double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity, 0)]
    [InlineData(0, double.NegativeInfinity)]
    public void Of_rejects_NaN_and_infinity(double lat, double lng)
    {
        // T-0030 sec reviewer M-1: NaN/±Infinity must not slip past the
        // range guards (the original `< -90 || > 90` check accepted them).
        var act = () => Coordinates.Of(lat, lng);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
