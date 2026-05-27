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

## Reviewer findings and resolutions (commit c06827f)

Two reviewers ran in parallel.

### Security reviewer — 0 BLOCKERs + 2 MAJORs

- **M-1 NaN/Infinity bypassed lat/lng range guards** — `NaN < -90` is false AND `NaN > 90` is false, so the original `(double?, double?)` `SetCoordinates` accepted NaN / ±Infinity and persisted them to Postgres. **Fixed via design refactor (also CQ m-3):** new `Coordinates` value-object in `Core.Domain.Addresses`. `Coordinates.Of(lat, lng)` rejects NaN / ±Infinity / out-of-range at construction. `Address.SetCoordinates(Coordinates?)` simply destructures so the entity can't persist a bad pair. Pinned by 6 `Of_rejects_NaN_and_infinity` theory rows + range-boundary facts.
- **M-2 `IAddressRepository.GetByIdAsync` no owner scoping** — IDOR primitive once T-0035 saved-addresses ships. **Fixed:** xmldoc warning on the method explicitly says "trusted-id only; do not pass through from request without owner/tenant check at the call site"; a `GetByOwnerAsync(addressId, ownerId)` overload will land with the first user-facing address feature.
- **N-3 No length cap inside `Address.Create`** (DB rejected long strings only at SaveChanges time). **Fixed:** length constants on the entity matching the EF column caps; `Address.Create` rejects 100KB+ strings at the boundary. Pinned by 5 `Create_rejects_fields_exceeding_column_caps` theory rows.

Accepted as-is (documented for future tickets):
- **N-1** Compile-time backtracking on admin-supplied regex — admin UI threat model, write-time validation belongs to admin-tooling ADR.
- **N-2** Cache-poisoning surface closed today (admin-only inputs); flag when tenant overrides ever land.
- **N-4** DB lookup before rate-limit — addressed via public-host rate limiter at T-0033/T-0035.
- **NIT-1** Address fields not in masker — process item for ADR 0023.
- **NIT-2** `Auditable.CountryCode` setter is `protected internal`; no drift possible today.

### Code-quality reviewer — 0 BLOCKERs + 1 MAJOR + 3 MINORs

- **M-1 `AddressNotFound` dead code** — **Fixed:** deleted. T-0031 / T-0033 will re-introduce when their handlers throw it.
- **m-1 + m-2 `CountryCodeIso` vs `Auditable.CountryCode` clash + silent mirroring** — **Fixed:** `Address.Create` now takes `countryCodeIso` AND `auditCountryCode` as separate required parameters. The ship-to country and the OWNER's tenant are independent — a CZ-tenanted customer can save a SK shipping address. No more conflation. Class XML doc explains the two-field design in detail; ADR 0010 amended with a "T-0030 implementation note" calling out the rename + the value-object wrap of coordinates.
- **m-3 `Coordinates` value-object** — **Fixed:** added `Makables.Core.Domain.Addresses.Coordinates` per ADR 0010 §"Mapbox autocomplete + geocoding". T-0031 will import the same type; no shape rework needed when the geocoder lands.

Accepted as-is:
- **n-1** `AddressZipRules` location — `Common/Addresses/` subfolder when more mixins arrive.
- **n-2** Partial index column choice — note in T-0031 ticket about possibly switching to `created_at` for FIFO geocoding.
- **n-3** Test boundary on mock-assertion — accepted.

### Test deltas (+18 net facts; 524 total = 442 unit + 82 integration)
- `AddressTests` — rewritten for the new 2-country `Create` signature; added shipto-vs-tenant independence test; added 5 length-cap rejection rows; coordinates tests rewritten against the value-object.
- `CoordinatesTests` (new) — 12 facts (legal extremes, range rejections, NaN / ±Infinity rejections).
- `ConfigurationDrivenAddressFormatValidatorTests` — unchanged.
- `AddressZipRulesTests` — unchanged.

## Acceptance criteria
- **AC-1** Build clean; 524 tests pass (442 unit + 82 integration).
- **AC-2** `Address.Create` rejects every empty required field and non-2-char ISO code; trims strings; uppercases the ISO code; mirrors to `Auditable.CountryCode`.
- **AC-3** `Address.SetCoordinates` enforces both-or-neither + lat/lng range guards.
- **AC-4** Migration `20260525065913_Addresses` creates the `addresses` table with the column shapes from ADR 0010 + the partial index for T-0031's geocode-retry sweep.
- **AC-5** `ConfigurationDrivenAddressFormatValidator` reads `CountryConfiguration.ZipFormat`, applies it with compiled regex + 200 ms timeout + in-process cache, and treats null/empty regex as "accept" per ADR 0010 soft-launch posture.
- **AC-6** `IAddressFormatValidator.IsValidZipAsync` short-circuits on blank inputs without hitting the country lookup.
- **AC-7** `AddressZipRules.MustBeValidZipForCountry` reports `BusinessErrorMessage.InvalidZipFormat` and is callable from any FluentValidation `AbstractValidator<T>` with a country accessor delegate.
- **AC-8** CLAUDE.md hygiene: `Core.Domain` has no third-party packages; the format validator lives in `Infra.Common` (depends on `ICountryConfigurationRepository`); no `SaveChangesAsync` outside the UoW pipeline; all error codes from `BusinessErrorMessage`.

## Status log
- 2026-05-25 initial commit c06827f. 506 tests pass.
- 2026-05-25 reviewer fix folded in. Sec M-1/M-2/N-3 closed; CQ M-1/m-1/m-2/m-3 closed. New `Coordinates` value-object eliminates NaN bypass + matches ADR 0010 shape. `Address.Create` takes ship-to + tenant country codes as separate parameters. Length caps inside the entity. ADR 0010 amended with T-0030 implementation note. `AddressNotFound` deleted. 524 tests pass (442 unit + 82 integration).
