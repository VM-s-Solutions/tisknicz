---
id: 0010
title: Address model — structured fields, Mapbox autocomplete + geocoding, per-country format validators
status: accepted
date: 2026-05-21
deciders: [Architect, user]
---

# 0010 — Address model

## Context

Addresses appear in three contexts:
- **Maker legal seat** (from ARES — trusted, structured).
- **Customer shipping address** (typed by the customer — needs validation and ideally autocomplete).
- **Personal pickup address** (typed by the maker — same as customer shipping for trust purposes).

For multi-country readiness, the address shape must accommodate different field orderings, optional fields (state), and country-specific format rules (ZIP regex). We also want coordinates for future "makers near you" features and for distance estimates in shipping.

## Decision

### Domain value object

```csharp
// Core.Domain/Addresses/Address.cs
public class Address : Auditable
{
    public string Street { get; private set; } = default!;
    public string HouseNumber { get; private set; } = default!;
    public string City { get; private set; } = default!;
    public string Zip { get; private set; } = default!;
    public string? State { get; private set; }            // null for CZ; e.g. "Bavaria" for DE
    public string CountryCode { get; private set; } = default!;  // ISO 3166-1 alpha-2
    public double? Latitude { get; private set; }
    public double? Longitude { get; private set; }

    public static Address Create(string street, string houseNumber, string city, string zip,
        string countryCode, string? state = null)
        => new() { Street = street, HouseNumber = houseNumber, City = city, Zip = zip,
                   State = state, CountryCode = countryCode };

    public Address SetCoordinates(double lat, double lng) { Latitude = lat; Longitude = lng; return this; }
}
```

`Address` inherits `Auditable` so the same row can be reused across orders and savedAddresses (when that lands).

> **T-0030 implementation note.** The field shown above as `CountryCode` is
> implemented as `CountryCodeIso` on the concrete `Address` entity to
> disambiguate from the inherited `Auditable.CountryCode` (which represents
> the OWNER's tenant, not the parcel's destination). Both are required at
> `Address.Create` time as separate parameters; they usually match but
> don't have to (a CZ user with a SK shipping address). See the entity
> XML doc for the full rationale.
>
> Coordinates are wrapped in the `Coordinates` value-object (defined in
> the geocoder section below). `Address.SetCoordinates(Coordinates?)`
> accepts the value-object; pass `null` to clear. The value-object
> validates lat/lng finiteness and range at construction so callers can't
> persist NaN.

### Per-country format validation

`CountryConfiguration` stores a ZIP regex per country (already present per ADR 0004):

```csharp
// CZ row:
ZipFormat = @"^\d{3}\s?\d{2}$",   // "12345" or "123 45"
```

Validation in FluentValidation is **dynamic** — looks up the regex at validator construction or via a per-country validator:

```csharp
// Core.Domain/Addresses/Validators/IAddressFormatValidator.cs
public interface IAddressFormatValidator
{
    Task<bool> IsValidZipAsync(string countryCode, string zip, CancellationToken ct);
}

// Infra.Common/Addresses/ConfigurationDrivenAddressFormatValidator.cs
public class ConfigurationDrivenAddressFormatValidator(
    ICountryConfigurationRepository countryConfig
) : IAddressFormatValidator
{
    public async Task<bool> IsValidZipAsync(string countryCode, string zip, CancellationToken ct)
    {
        var config = await countryConfig.GetByCodeAsync(countryCode, ct);
        if (string.IsNullOrEmpty(config.ZipFormat)) return true;   // no rule = accept
        return Regex.IsMatch(zip, config.ZipFormat);
    }
}
```

Per-country business rules that go beyond regex (rare) live in `Core.Domain/Addresses/Validators/Cz/CzAddressValidator.cs` and similar. For CZ MVP, format-only is sufficient.

### Mapbox autocomplete + geocoding

Mapbox Places API (already a Cleansia precedent) is the geocoder adapter.

```csharp
// Core.Domain/Addresses/IAddressGeocoder.cs
public interface IAddressGeocoder
{
    Task<BusinessResult<Coordinates>> GeocodeAsync(Address address, CancellationToken ct);
    Task<BusinessResult<AddressSuggestion[]>> AutocompleteAsync(string query, string countryCode, CancellationToken ct);
}

public record Coordinates(double Latitude, double Longitude);
public record AddressSuggestion(string Street, string HouseNumber, string City, string Zip, string CountryCode, double? Latitude, double? Longitude);

// Infra.Clients/Mapbox/MapboxAddressGeocoder.cs implements both.
```

> **T-0031 implementation note.** Two deviations from the sketch above:
>
> 1. `AutocompleteAsync` returns `BusinessResult<IReadOnlyList<AddressSuggestion>>`
>    rather than `BusinessResult<AddressSuggestion[]>` — `IReadOnlyList<T>` is
>    the project's idiomatic collection-return shape (compare `PagedData<T>`)
>    and keeps the contract a read surface.
> 2. `AddressSuggestion` is `(string Label, string Street, string HouseNumber,
>    string City, string Zip, string CountryCodeIso, Coordinates? Coordinates)`
>    — wraps the lat/lng pair in the T-0030 `Coordinates` value-object (which
>    enforces finite/range at construction) and adds a `Label` for the
>    dropdown UI line ("Karlovarská 1, 150 00 Praha, Česko" — Mapbox's
>    `place_name`). Missing fields surface as empty strings rather than null
>    so the frontend form binding doesn't need null-forgiveness ceremony.
>
> **Token transport.** The Mapbox access token is sent as an
> `Authorization: Bearer` header, not as a `?access_token=` query
> parameter. The OTel HttpClient instrumentation captures `url.full`
> into App Insights span attributes; sending the token in the URL would
> leak it to anyone with App Insights read access. The `Authorization`
> header is stripped from OTel HTTP spans by default and is on the
> `SensitivePropertyMasker` Serilog redaction list.

### Frontend integration

The order form and maker registration form call `GET /api/v1/addresses/autocomplete?q=...&country=CZ` (against the Customer or Maker host — the shared controller in `Makables.Config` is mounted on both via the MVC application part; audience is enforced by JWT validation, not by route prefix). The backend proxies to Mapbox and rate-limits per authenticated user.

The form binds to the structured fields. When the user picks a suggestion, all five fields populate at once. Manual edits remain possible.

### Geocoding policy

- **Order time:** if the customer-typed shipping address has no coordinates, the order handler calls `IAddressGeocoder.GeocodeAsync` and populates them. Failure is non-blocking — the order still proceeds; coordinates stay null. We re-attempt at fulfillment time if needed.
- **Maker registration:** ARES address is geocoded once at registration. Failure non-blocking; admin can trigger re-geocoding from the admin UI.

### Validation policy

- **Customer shipping address:** format-validated only at MVP. Existence is not verified — Zásilkovna validates the pickup point at ship time, which is the authoritative check for delivery feasibility.
- **Maker legal seat from ARES:** trusted as-is; format also format-validated but ARES rarely returns malformed data.

## Alternatives considered

- **Free-text address only** — rejected. Czech invoices require structured fields by law.
- **No Mapbox** — rejected by user. Autocomplete UX is a strong Cleansia precedent and reduces input errors meaningfully.
- **Validate against `post.cz` registry** — rejected for MVP. Adds an external dependency for marginal benefit on top of Mapbox + format check. Post-MVP candidate.
- **Geocode synchronously on every form keystroke** — rejected. Causes Mapbox rate-limit risk and unnecessary cost. Autocomplete debounced; final geocode at submit time only if needed.

## Consequences

### Positive
- Structured fields satisfy CZ invoicing law and shipping label requirements.
- Mapbox autocomplete improves order form UX significantly.
- Coordinates unlock "makers near you" (post-MVP) without a schema migration.
- Per-country format rules live in `CountryConfiguration` — adding SK/PL is a row update, no code change.

### Negative
- Mapbox is a runtime dependency. If it's down, autocomplete fails (graceful degradation: form still works, no autocomplete).
- Mapbox costs scale with usage. At MVP scale (<10k autocomplete calls/month) it's well within their free tier; monitor.
- Backend rate-limits the autocomplete proxy per user to prevent abuse.

## Compliance / verification

- Reviewer checklist: addresses use the `Address` value object, not free-text fields.
- Reviewer checklist: ZIP validation uses `IAddressFormatValidator`, not inline regex.
- Reviewer checklist: Mapbox calls only inside `Infra.Clients/Mapbox/`.
- Integration test: invalid CZ ZIP (`12345 6`) fails validation; valid CZ ZIP (`123 45`) passes.
- Integration test: geocoding failure does not block order placement.
- SecOps: Mapbox API key is server-side only (Mapbox tokens carry usage scoping — use a public token for the autocomplete proxy with domain restriction, a secret token for server-side geocoding if needed).

## Related
- Patterns: §A.15 provider adapter pattern, §A.12 CountryConfiguration
- ADR 0004 — CountryConfiguration carries `ZipFormat`
- Will be referenced by: Batch 4 ADR for Mapbox integration specifics
