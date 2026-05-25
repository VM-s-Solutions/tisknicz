using Makables.Core.Domain.Common;

namespace Makables.Core.Domain.Addresses;

/// <summary>
/// A postal address per ADR 0010. Inherits <see cref="Auditable"/> so
/// the same row can be reused across orders and the future saved-addresses
/// surface — a single <c>addresses</c> table, FK'd from owners (Maker
/// legal seat in T-0033, customer shipping address in later tickets).
///
/// Per-country format validation lives in <see cref="Validators.IAddressFormatValidator"/>;
/// this entity stays a passive value carrier. Latitude / Longitude are
/// nullable from day one — T-0031's Mapbox geocoder fills them
/// asynchronously and the row is valid without them (ADR 0010
/// §"Geocoding policy": non-blocking).
/// </summary>
public sealed class Address : Auditable
{
    public string Street { get; private set; } = default!;
    public string HouseNumber { get; private set; } = default!;
    public string City { get; private set; } = default!;
    public string Zip { get; private set; } = default!;
    public string? State { get; private set; }
    public string CountryCodeIso { get; private set; } = default!;
    public double? Latitude { get; private set; }
    public double? Longitude { get; private set; }

    private Address() { }

    /// <summary>
    /// Factory. Trims each string field; uppercases <paramref name="countryCodeIso"/>;
    /// rejects empty required fields and a country code that isn't the
    /// strict ISO 3166-1 alpha-2 shape. The <paramref name="countryCodeIso"/>
    /// argument is the address's own country (where the parcel goes), NOT
    /// the platform-wide <see cref="Auditable.CountryCode"/> (which scopes
    /// the row for audit / soft-delete purposes). They usually match but
    /// don't have to: a CZ-tenanted customer can save a SK shipping address.
    /// </summary>
    public static Address Create(
        string id,
        string street,
        string houseNumber,
        string city,
        string zip,
        string countryCodeIso,
        string? state = null)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Id is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(street))
            throw new ArgumentException("Street is required.", nameof(street));
        if (string.IsNullOrWhiteSpace(houseNumber))
            throw new ArgumentException("HouseNumber is required.", nameof(houseNumber));
        if (string.IsNullOrWhiteSpace(city))
            throw new ArgumentException("City is required.", nameof(city));
        if (string.IsNullOrWhiteSpace(zip))
            throw new ArgumentException("Zip is required.", nameof(zip));
        if (string.IsNullOrWhiteSpace(countryCodeIso) || countryCodeIso.Length != 2)
            throw new ArgumentException("CountryCodeIso must be 2 chars (ISO 3166-1 alpha-2).", nameof(countryCodeIso));

        var normalisedIso = countryCodeIso.ToUpperInvariant();
        return new Address
        {
            Id = id,
            Street = street.Trim(),
            HouseNumber = houseNumber.Trim(),
            City = city.Trim(),
            Zip = zip.Trim(),
            State = string.IsNullOrWhiteSpace(state) ? null : state.Trim(),
            CountryCodeIso = normalisedIso,
            // Auditable.CountryCode mirrors the address country at create
            // time; an admin or owner-context change can mutate it later
            // via the Auditable surface if the row moves tenancy.
            CountryCode = normalisedIso,
        };
    }

    /// <summary>
    /// Set the latitude/longitude pair (filled by T-0031's geocoder).
    /// Both must be supplied together; passing nulls clears the pair.
    /// </summary>
    public Address SetCoordinates(double? latitude, double? longitude)
    {
        if (latitude is null ^ longitude is null)
            throw new ArgumentException(
                "Latitude and Longitude must be set or cleared together.",
                latitude is null ? nameof(latitude) : nameof(longitude));

        if (latitude is { } lat && (lat < -90 || lat > 90))
            throw new ArgumentOutOfRangeException(nameof(latitude), "Latitude must be -90..90.");
        if (longitude is { } lng && (lng < -180 || lng > 180))
            throw new ArgumentOutOfRangeException(nameof(longitude), "Longitude must be -180..180.");

        Latitude = latitude;
        Longitude = longitude;
        return this;
    }
}
