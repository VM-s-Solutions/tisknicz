---
id: T-0030
title: Address entity + IAddressRepository + IAddressFormatValidator (reads CountryConfiguration.ZipFormat) + FluentValidation mixin
status: done
size: S
owner: dotnet-backend
created: 2026-05-25
updated: 2026-05-25
depends_on: [T-0010]
blocks: [T-0031, T-0033]
adrs: [0010]
phase: 2
---

# T-0030 — Address entity + format validator

## Scope

Shared `Address` aggregate (per ADR 0010 §"Domain value object"), minimal repository, per-country ZIP format validator, and a FluentValidation mixin so feature validators (T-0033 RegisterMaker, T-0035 customer-shipping-address-form, later orders / invoices) get a one-liner rule rather than re-implementing `MustAsync` boilerplate.

Unblocks T-0031 (Mapbox geocoder fills the nullable lat/lng), T-0033 (Maker's pickup address FK), and every later flow that ships goods to an address.

### Domain (`Core.Domain/Addresses/`)
- `Address.cs` — `Auditable` aggregate per ADR 0010 §"Domain value object". Fields: `Street`, `HouseNumber`, `City`, `Zip`, `State?`, `CountryCodeIso` (ISO 3166-1 alpha-2), `Latitude?`, `Longitude?`. `Create(...)` trims strings, uppercases the ISO code, mirrors it to `Auditable.CountryCode`, and rejects empty required fields / non-2-char country code. `SetCoordinates(lat?, lng?)` sets both-or-neither and validates lat ∈ [-90, 90], lng ∈ [-180, 180].
- `IAddressRepository.cs` — minimal surface per the size:S scope: `Add(address)` + `GetByIdAsync(id, ct)`. List / replace methods land when a feature actually needs them (T-0033 only needs Add).
- `Validators/IAddressFormatValidator.cs` — `IsValidZipAsync(countryCodeIso, zip, ct)`. Returns true when no country regex is configured (soft-launch posture per ADR 0010).

### Infra.Common (`Addresses/`)
- `ConfigurationDrivenAddressFormatValidator.cs` — reads `CountryConfiguration.ZipFormat` regex and applies it with `RegexOptions.Compiled | CultureInvariant` plus a 200 ms timeout against catastrophic backtracking. Compiled regexes cached in a static `ConcurrentDictionary` keyed on `{country}|{pattern}` so an admin edit just adds a new cache slot rather than needing eviction. Short-circuits on blank inputs without hitting the country lookup.

### Core.AppServices (`Common/`)
- `AddressZipRules.cs` — FluentValidation extension `MustBeValidZipForCountry(IAddressFormatValidator, Func<T, string> countryAccessor)`. Wraps the async lookup; reports the canonical `BusinessErrorMessage.InvalidZipFormat` code. Use:
  ```csharp
  RuleFor(c => c.Zip)
      .MustBeValidZipForCountry(zipValidator, c => c.CountryCode);
  ```

### Infra.Database
- `Addresses/AddressRepository.cs` — tracked reads (caller mutates + UoW SaveChanges).
- `Configurations/AddressConfiguration.cs` — table `addresses`; field lengths sized for Czech address realities (Street/City 200, HouseNumber 20, Zip 16, State 100); double-precision lat/lng nullable. Partial index `ix_addresses_pending_geocode` on `id WHERE latitude IS NULL AND is_active` supports T-0031's retry-sweep without scanning fully-geocoded rows. Auditable columns wired the same way as `EmailTemplate` / `User`.
- `Migrations/20260525065913_Addresses.cs` — creates the `addresses` table + the partial index. `Down()` drops the table.

### DI (`AddMakablesInfrastructure`)
- `IAddressRepository → AddressRepository` (scoped).
- `IAddressFormatValidator → ConfigurationDrivenAddressFormatValidator` (scoped — depends on the scoped `ICountryConfigurationRepository` which holds the per-request DbContext; the regex cache is a static field, so it survives across scopes).

### BusinessErrorMessage
- New: `AddressNotFound = "address.notFound"`. Used by future repo-driven features.
- `InvalidZipFormat = "validation.invalidZip"` already existed; now actually consumed via the mixin.

### Tests (+36 facts; 506 total = 424 unit + 82 integration)
- `Domain/Addresses/AddressTests.cs` — 12 facts (factory trim/uppercase/state-blank-as-null; required-field matrix; coordinates set-both-or-clear-both; lat/lng range guards).
- `Infra/Addresses/ConfigurationDrivenAddressFormatValidatorTests.cs` — 16 facts (valid/invalid CZ ZIPs; trimmed input; null-regex pass-through; missing-row pass-through; blank-input short-circuit without DB hit; country code normalised to uppercase).
- `AppServices/Common/AddressZipRulesTests.cs` — 8 facts (accept; reject with canonical error code; short-circuit on blank zip or country; country accessor is honoured).

## Out of scope
- Mapbox geocoder + autocomplete proxy — T-0031.
- Per-country business rules beyond regex (e.g. CZ-specific street-pattern checks) — ADR 0010 says these live in `Core.Domain/Addresses/Validators/Cz/*` when needed; not needed for CZ MVP.
- Address-CRUD use cases (CreateAddress / UpdateAddress / list-by-owner) — added per consuming feature (T-0033 doesn't need a use case; the maker-registration handler instantiates `Address.Create` directly and calls `Add`).
- US 9-digit ZIP+4, UK alphanumeric postcodes — out of scope per CZ-launch-only directive.

## Acceptance criteria
- **AC-1** Build clean; 506 tests pass (424 unit + 82 integration).
- **AC-2** `Address.Create` rejects every empty required field and non-2-char ISO code; trims strings; uppercases the ISO code; mirrors to `Auditable.CountryCode`.
- **AC-3** `Address.SetCoordinates` enforces both-or-neither + lat/lng range guards.
- **AC-4** Migration `20260525065913_Addresses` creates the `addresses` table with the column shapes from ADR 0010 + the partial index for T-0031's geocode-retry sweep.
- **AC-5** `ConfigurationDrivenAddressFormatValidator` reads `CountryConfiguration.ZipFormat`, applies it with compiled regex + 200 ms timeout + in-process cache, and treats null/empty regex as "accept" per ADR 0010 soft-launch posture.
- **AC-6** `IAddressFormatValidator.IsValidZipAsync` short-circuits on blank inputs without hitting the country lookup.
- **AC-7** `AddressZipRules.MustBeValidZipForCountry` reports `BusinessErrorMessage.InvalidZipFormat` and is callable from any FluentValidation `AbstractValidator<T>` with a country accessor delegate.
- **AC-8** CLAUDE.md hygiene: `Core.Domain` has no third-party packages; the format validator lives in `Infra.Common` (depends on `ICountryConfigurationRepository`); no `SaveChangesAsync` outside the UoW pipeline; all error codes from `BusinessErrorMessage`.

## Status log
- 2026-05-25 done. 506 tests pass. Awaiting dual reviewer (security + code-quality) per workflow.
