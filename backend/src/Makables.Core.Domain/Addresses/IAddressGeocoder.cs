using Makables.Core.Domain.Common;

namespace Makables.Core.Domain.Addresses;

/// <summary>
/// Geocoder / autocomplete adapter per ADR 0010 §"Mapbox autocomplete
/// + geocoding". Two methods:
///
/// <list type="bullet">
///   <item><description><see cref="GeocodeAsync"/> — turns a structured
///     <see cref="Address"/> into a <see cref="Coordinates"/> pair.
///     Called server-side at maker-registration time (T-0033) and at
///     order placement (later phase). Per ADR 0010 §"Geocoding policy"
///     a failure is non-blocking — the caller leaves
///     <see cref="Address.Latitude"/> / <see cref="Address.Longitude"/>
///     null and a retry-sweep picks the row up later via the
///     <c>ix_addresses_pending_geocode</c> partial index.</description></item>
///   <item><description><see cref="AutocompleteAsync"/> — turns a partial
///     query string into a small list of <see cref="AddressSuggestion"/>s
///     scoped to the given country. The frontend hits the proxy endpoint
///     (<c>POST /api/v1/{customer|maker}/addresses/autocomplete</c>) which
///     calls this method.</description></item>
/// </list>
///
/// Both methods return <see cref="BusinessResult{T}"/> so callers don't
/// have to catch provider exceptions. Implementation:
/// <c>Makables.Infra.Clients/Mapbox/MapboxAddressGeocoder.cs</c>.
/// </summary>
public interface IAddressGeocoder
{
    Task<BusinessResult<Coordinates>> GeocodeAsync(
        Address address,
        CancellationToken cancellationToken);

    Task<BusinessResult<IReadOnlyList<AddressSuggestion>>> AutocompleteAsync(
        string query,
        string countryCodeIso,
        CancellationToken cancellationToken);
}

/// <summary>
/// One autocomplete result row. Mapbox returns structured fields where
/// it can (street, house number, city, zip); we surface them as a
/// suggestion the frontend can splat into the address form on click.
/// <see cref="Coordinates"/> is present when Mapbox returned a point,
/// null otherwise (rare — usually a full hit comes with a centroid).
/// </summary>
public sealed record AddressSuggestion(
    string Label,
    string Street,
    string HouseNumber,
    string City,
    string Zip,
    string CountryCodeIso,
    Coordinates? Coordinates);
