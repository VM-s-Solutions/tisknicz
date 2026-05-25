using Makables.Core.Domain.Common;

namespace Makables.Core.Domain.Addresses;

/// <summary>
/// A postal address per ADR 0010. Inherits <see cref="Auditable"/> so
/// the same row can be reused across orders and the future saved-addresses
/// surface — a single <c>addresses</c> table, FK'd from owners (Maker
/// legal seat in T-0033, customer shipping address in later tickets).
///
/// <b>Two country fields, two responsibilities</b> (T-0030 CQ reviewer m-1/m-2):
/// <list type="bullet">
///   <item><description><see cref="CountryCodeIso"/> — the address's OWN country
///     (where the parcel goes). Drives shipping-carrier selection, VAT zone,
///     postal-format validation.</description></item>
///   <item><description><see cref="Auditable.CountryCode"/> — the OWNER's tenant
///     (which platform region the row is audited under). Drives row scoping,
///     soft-delete filtering, admin visibility.</description></item>
/// </list>
/// They usually match (a CZ user with a CZ shipping address) but don't
/// have to (a CZ user with a SK shipping address). The
/// <see cref="Create"/> factory takes both as explicit parameters so a
/// caller can't accidentally conflate them. ADR 0010's sketch shows
/// only <c>CountryCode</c>; the rename to <c>CountryCodeIso</c> is
/// noted at the bottom of that ADR — keep the two purposes separate in
/// every later address-touching ticket.
///
/// Per-country format validation lives in <see cref="Validators.IAddressFormatValidator"/>;
/// this entity stays a passive value carrier. <see cref="Latitude"/> /
/// <see cref="Longitude"/> are nullable from day one — T-0031's Mapbox
/// geocoder fills them asynchronously and the row is valid without them
/// (ADR 0010 §"Geocoding policy": non-blocking).
/// </summary>
public sealed class Address : Auditable
{
    // Length caps mirror the EF configuration (AddressConfiguration.cs)
    // so a 100 KB Street is rejected at the boundary, not at SaveChanges
    // time (T-0030 sec reviewer N-3).
    private const int MaxStreetLength = 200;
    private const int MaxHouseNumberLength = 20;
    private const int MaxCityLength = 200;
    private const int MaxZipLength = 16;
    private const int MaxStateLength = 100;

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
    /// Factory. Trims each string field; uppercases both country codes;
    /// rejects empty required fields, non-ISO country codes, and string
    /// values longer than their column caps. Length caps live in the
    /// entity so callers can't sneak a 100 KB Street past validation
    /// only to die at SaveChanges (T-0030 sec reviewer N-3).
    /// </summary>
    /// <param name="countryCodeIso">The address's own country (where the parcel goes).</param>
    /// <param name="auditCountryCode">The OWNER's tenant country (see class XML doc).</param>
    public static Address Create(
        string id,
        string street,
        string houseNumber,
        string city,
        string zip,
        string countryCodeIso,
        string auditCountryCode,
        string? state = null)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Id is required.", nameof(id));

        ValidateRequiredField(street, nameof(street), MaxStreetLength);
        ValidateRequiredField(houseNumber, nameof(houseNumber), MaxHouseNumberLength);
        ValidateRequiredField(city, nameof(city), MaxCityLength);
        ValidateRequiredField(zip, nameof(zip), MaxZipLength);
        ValidateOptionalField(state, nameof(state), MaxStateLength);

        if (string.IsNullOrWhiteSpace(countryCodeIso) || countryCodeIso.Length != 2)
            throw new ArgumentException("CountryCodeIso must be 2 chars (ISO 3166-1 alpha-2).", nameof(countryCodeIso));
        if (string.IsNullOrWhiteSpace(auditCountryCode) || auditCountryCode.Length != 2)
            throw new ArgumentException("AuditCountryCode must be 2 chars (ISO 3166-1 alpha-2).", nameof(auditCountryCode));

        return new Address
        {
            Id = id,
            Street = street.Trim(),
            HouseNumber = houseNumber.Trim(),
            City = city.Trim(),
            Zip = zip.Trim(),
            State = string.IsNullOrWhiteSpace(state) ? null : state.Trim(),
            CountryCodeIso = countryCodeIso.ToUpperInvariant(),
            CountryCode = auditCountryCode.ToUpperInvariant(),
        };
    }

    /// <summary>
    /// Set the geocoded coordinates (filled by T-0031's Mapbox adapter).
    /// Pass <c>null</c> to clear. The <see cref="Coordinates.Of"/> factory
    /// already guarantees finiteness + range, so this method only
    /// destructures.
    /// </summary>
    public Address SetCoordinates(Coordinates? coordinates)
    {
        if (coordinates is null)
        {
            Latitude = null;
            Longitude = null;
        }
        else
        {
            Latitude = coordinates.Latitude;
            Longitude = coordinates.Longitude;
        }
        return this;
    }

    private static void ValidateRequiredField(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{name} is required.", name);
        if (value.Length > maxLength)
            throw new ArgumentException($"{name} must be at most {maxLength} chars.", name);
    }

    private static void ValidateOptionalField(string? value, string name, int maxLength)
    {
        if (value is not null && value.Length > maxLength)
            throw new ArgumentException($"{name} must be at most {maxLength} chars.", name);
    }
}
