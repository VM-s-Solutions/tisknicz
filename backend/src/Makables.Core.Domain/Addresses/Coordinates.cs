namespace Makables.Core.Domain.Addresses;

/// <summary>
/// A geocoded point per ADR 0010 §"Mapbox autocomplete + geocoding".
/// Used as the result type of <c>IAddressGeocoder.GeocodeAsync</c>
/// (T-0031) and as the input to <see cref="Address.SetCoordinates"/>.
///
/// <see cref="Of"/> is the only construction path: it enforces that
/// both values are finite (rejects NaN / ±Infinity, which would slip
/// through naive <c>&lt;</c>/<c>&gt;</c> range checks) and within the
/// legal lat/lng ranges. Constructing via <c>new Coordinates(...)</c>
/// is intentionally not callable from outside this file (record's
/// primary constructor stays private to the assembly via the factory
/// pattern below — same shape we use on <c>Address</c>).
/// </summary>
public sealed record Coordinates
{
    public double Latitude { get; }
    public double Longitude { get; }

    private Coordinates(double latitude, double longitude)
    {
        Latitude = latitude;
        Longitude = longitude;
    }

    /// <summary>
    /// Validate and construct. Rejects NaN / ±Infinity AND values outside
    /// the legal WGS-84 ranges (T-0030 sec reviewer M-1).
    /// </summary>
    public static Coordinates Of(double latitude, double longitude)
    {
        if (!double.IsFinite(latitude))
            throw new ArgumentOutOfRangeException(nameof(latitude), "Latitude must be a finite number.");
        if (!double.IsFinite(longitude))
            throw new ArgumentOutOfRangeException(nameof(longitude), "Longitude must be a finite number.");
        if (latitude is < -90 or > 90)
            throw new ArgumentOutOfRangeException(nameof(latitude), "Latitude must be -90..90.");
        if (longitude is < -180 or > 180)
            throw new ArgumentOutOfRangeException(nameof(longitude), "Longitude must be -180..180.");

        return new Coordinates(latitude, longitude);
    }
}
