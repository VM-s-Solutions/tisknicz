---
role: AddressGeocoder
kind: adapter
status: accepted
---

# AddressGeocoder

## Responsibility

Autocomplete partial address queries for the order/registration forms, and geocode (lat/long) finalized addresses. Adapter pattern.

## Collaborators

- (Caller passes structured input — this role does not load entities)

## Knows

- The geocoding service (Mapbox at launch)
- Its rate limits and pricing model

## Does NOT know

- Whether an address "exists" in the postal sense (geocoder is approximate; postal validity is a separate concern)
- The entity using the result

## Interface

```csharp
Task<BusinessResult<Coordinates>> GeocodeAsync(Address)
Task<BusinessResult<AddressSuggestion[]>> AutocompleteAsync(string query, string countryCode)
```

## Implementations

- **MapboxAddressGeocoder** (`Infra.Clients/Mapbox/`)
- Future: GoogleAddressGeocoder, OpenCageAddressGeocoder

## Behaviour

- Geocoding failure is **non-blocking**: callers (Order create, Maker registration) continue without coordinates and can retry later.
- Autocomplete is proxied through the backend to keep the API key server-side and rate-limit per user.

## Implementation pointer

Interface: `backend/src/Makables.Core.Domain/Addresses/IAddressGeocoder.cs`.

## Related

- ADRs: 0010 (this role's defining ADR)
- Roles: `address`
