---
role: Address
kind: value-object
status: accepted
---

# Address

## Responsibility

Represent a structured postal location, with optional geocoded coordinates, suitable for invoicing, shipping labels, and map display.

## Collaborators

- **AddressGeocoder** (asks: geocode + autocomplete)
- **CountryConfiguration** (asks: ZIP format for validation)

## Knows

- `Street`, `HouseNumber`, `City`, `Zip`
- `State` (nullable — null for CZ; populated for countries with provinces/states)
- `CountryCode` (ISO 3166-1 alpha-2)
- `Latitude`, `Longitude` (nullable — populated when geocoding succeeds)

## Does NOT know

- The entity it belongs to (an address is reusable: a maker's seat, a customer's shipping target, a saved address)
- Whether it is "valid" beyond format checks (existence is verified at shipping time by the carrier)
- Distance to other addresses (a future role, if added)

## Validation

- Format-only at the value-object boundary (constructor enforces non-empty fields).
- ZIP regex validation deferred to `IAddressFormatValidator` which reads `CountryConfiguration.ZipFormat`.
- Per-country business rules (rare) live in `Core.Domain/Addresses/Validators/<CountryCode>AddressValidator.cs`.

## Lifecycle

- Addresses are persisted as their own table (rows reused via foreign keys to avoid duplication) per ADR 0010.
- `AddressRepository.GetOrCreate(street, houseNumber, city, zip, countryCode, state)` returns an existing row if exact match, else creates one.

## Implementation pointer

`backend/src/Makables.Core.Domain/Addresses/Address.cs`.

## Related

- ADRs: 0010 (this ADR defined the value object)
- Roles: `address-geocoder`, `country-configuration`
