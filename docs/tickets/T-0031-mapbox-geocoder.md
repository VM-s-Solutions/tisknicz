---
id: T-0031
title: IAddressGeocoder + MapboxAddressGeocoder + autocomplete proxy + per-user rate-limit policy
status: done
size: M
owner: dotnet-backend
created: 2026-05-25
updated: 2026-05-25
depends_on: [T-0030]
blocks: [T-0033]
adrs: [0010]
phase: 2
---

# T-0031 — Mapbox geocoder + autocomplete proxy

## Scope

Per ADR 0010 §"Mapbox autocomplete + geocoding" + §"Frontend integration" + §"Geocoding policy". Two methods on `IAddressGeocoder`:

- **`GeocodeAsync(Address, ct)`** — structured address → `Coordinates`. Called server-side at maker-registration (T-0033) and order placement (later). Per ADR 0010 §"Geocoding policy" a failure is non-blocking — the caller leaves `Address.Latitude` / `.Longitude` null and a future retry-sweep picks the row up via the `ix_addresses_pending_geocode` partial index.
- **`AutocompleteAsync(query, countryCodeIso, ct)`** — partial query → list of `AddressSuggestion`. Driven by the autocomplete proxy endpoint that the frontend hits while the user types.

User-chosen design at planning time:
- Single server-side Mapbox token (frontend never calls Mapbox directly).
- Autocomplete endpoint mounts on Customer + Maker hosts via a shared controller in `Makables.Config/Controllers/Addresses/`.
- New named rate-limit policy `addresses-autocomplete` — 20/min per authenticated user (`sub` claim), 5/min per IP for unauthenticated (kept for future opening).
- Polly retry 2x with short exponential backoff inside Geocode; final failure returns `BusinessResult.Failure(GeocoderTransient)` so the maker-reg / order handler ignores it per ADR 0010 §"Geocoding policy".

### Domain (`Core.Domain/Addresses/`)
- `IAddressGeocoder.cs` — interface + `AddressSuggestion` record. `GeocodeAsync` returns `BusinessResult<Coordinates>`; `AutocompleteAsync` returns `BusinessResult<IReadOnlyList<AddressSuggestion>>`. No exceptions cross the boundary.

### Infra.Clients (`Mapbox/`)
- `MapboxOptions.cs` — `Mapbox:AccessToken` (Key Vault ref in prod) + `BaseUrl` (overridable for tests; **required https**) + `AutocompleteLimit` (1..10, default 5) + `RetryCount` (0..5, default 2) + `RetryBaseDelayMs` (0..5000, default 200 — capped per Copilot review so an accidental large value can't stretch the retry chain past `OverallTimeoutSeconds`) + `OverallTimeoutSeconds` (1..30, default 5). Every value validated with `.ValidateOnStart()`.
- `MapboxAddressGeocoder.cs` — calls Mapbox Geocoding v5 (`/geocoding/v5/mapbox.places/{q}.json`). Named HttpClient via `IHttpClientFactory`. Polly v8 `ResiliencePipeline<HttpResponseMessage>` retries 408/429/5xx. Per-call timeout via `CancellationTokenSource.CancelAfter` so a stuck connection can't pin a worker. Mapbox returns `[lng, lat]` in `feature.center` — adapter swaps to `(lat, lng)` when constructing `Coordinates.Of(...)`, which also catches out-of-range / NaN responses (sec hardening from T-0030).

### Core.AppServices
- No use cases added — `AddressAutocompleteController` is a 5-line passthrough to `IAddressGeocoder.AutocompleteAsync`; no MediatR command needed today. Adding one is a future cleanup if/when the autocomplete path grows business logic.

### Config (`Controllers/Addresses/`)
- `AddressAutocompleteController.cs` — `GET /api/v1/addresses/autocomplete?q={...}&country={CC}`. `[ApiController][ApiVersion("1.0")][Authorize][EnableRateLimiting("addresses-autocomplete")]`. Lives in `Makables.Config` so both Customer + Maker hosts pick it up via the shared MVC application part (verified via `MvcApplicationPartsAssemblyInfo`).

### Config (`Extensions/AddMakablesRateLimiting.cs`)
- New named policy `addresses-autocomplete`. `PartitionedRateLimiter<HttpContext>`:
  - Authenticated (sub claim present): 20/min per user.
  - Unauthenticated: 5/min per remote IP (fall-through bucket `ip:unknown` if header missing — prevents header-drop bypass).
  - No queue (`QueueLimit = 0`) — autocomplete is latency-sensitive; reject fast instead of queueing.

### Config (`Extensions/AddMakablesClients.cs`)
- `MapboxOptions` registered with `.ValidateOnStart()` (5 validators including absolute-URI base, retry-count range, timeout range, autocomplete-limit range, non-empty token).
- Named HttpClient `Makables.Infra.Clients.Mapbox`.
- `ResiliencePipeline<HttpResponseMessage>` (Polly v8) shared by every Mapbox call. Same shape as the SendGrid retry pipeline from T-0028.
- `IAddressGeocoder → MapboxAddressGeocoder` (scoped).

### `BusinessErrorMessage`
- New codes: `GeocoderInvalidInput`, `GeocoderNoMatch`, `GeocoderTransientFailure`, `GeocoderPermanentFailure`.

### CI + tests config
- `.github/workflows/ci.yml` seeds `Mapbox__AccessToken` so the NSwag spec-parity job can boot the Customer + Maker hosts.
- `Makables.IntegrationTests` (both `JwtAuthMiddlewareTests` and `WebHostStartupTests`) seed `Mapbox:AccessToken` for the same reason.

### Tests (+23 facts; 547 total = 465 unit + 82 integration)
- `Infra/Clients/Mapbox/MapboxAddressGeocoderTests.cs` — 21 facts:
  - Geocode: happy-path coord swap; no-match → Permanent; transient status matrix (5 rows); permanent 4xx (3 rows); malformed JSON → Permanent; out-of-range coords → Permanent; URL carries country + token + limit.
  - Autocomplete: feature → suggestion mapping (label, components, coordinates); blank/malformed-input matrix (5 rows) short-circuits without calling Mapbox; transient bubble-up; URL carries country + autocomplete=true + limit; cancellation propagates.
- `Domain/Addresses/AddressSuggestionTests.cs` — 2 facts pinning the wire-shape record.

## Out of scope
- A retry-sweep background job that re-geocodes addresses with `latitude IS NULL` via `ix_addresses_pending_geocode` — T-0029-style follow-up ticket. The partial index landed in T-0030; the sweep doesn't exist yet.
- Mapbox event-webhook / usage telemetry. ADR 0010 mentions "monitor" cost — Application Insights covers the HTTP call shape today.
- Frontend autocomplete component — T-0035 / order-form ticket.
- Allowing anonymous autocomplete on the registration flow — out-of-scope by design (the registration flow can defer autocomplete to first-authenticated step, or open up later with the 5/min/IP partition).

## Reviewer findings and resolutions (commit 292be2a)

### Security reviewer — 1 BLOCKER + 2 MAJORs + 3 MINORs

- **B-1 Mapbox access token leaks to App Insights via OTel HttpClient instrumentation** — the adapter embedded `access_token=...` in the URL query string; OTel captures `url.full` into App Insights span attributes; the SensitivePropertyMasker is Serilog-only and doesn't redact OTel attribute keys. **Fixed:** token now sent as `Authorization: Bearer {token}` header per request. `Authorization` is stripped from OTel HTTP spans by default AND is on the masker's Serilog redaction list. Pinned by `Geocode_request_url_includes_country_filter_and_NEVER_carries_the_access_token` and `Geocode_request_carries_Bearer_authorization_header` (+ matching autocomplete pair).
- **M-1 / M-2 ForwardedHeaders + IP-bucket bypass** — not reachable today because `[Authorize]` makes the IP path dead code. **Documented** in `docs/security/function-key-rotation.md` as a hard prerequisite for the "anonymous opening" path (any future ticket that drops `[Authorize]` MUST first wire `UseForwardedHeaders` + a regression test pinning it).
- **MN-2 `Mapbox:BaseUrl` unrestricted (PII / token leak via attacker-controlled host)** — **Fixed:** validator now requires `Uri.Scheme == "https"`. Hostname allow-list (restrict to `api.mapbox.com`) is a future hardening ticket; today the prod config is pinned in deploy templates.
- **MN-3 `PerCallTimeoutSeconds` misleading name** — **Fixed:** renamed to `OverallTimeoutSeconds`. The linked CTS bounds the entire retry chain, not a single HTTP attempt.
- **MN-1, N-1..N-5** — accepted as-is or already correct.

### Code-quality reviewer — 0 BLOCKERs + 4 MINORs

- **M-1 ADR drift (`IReadOnlyList` vs `[]` + `AddressSuggestion` shape)** — **Fixed via ADR addendum.** ADR 0010 §"Mapbox autocomplete + geocoding" gets a T-0031 implementation note documenting the two deviations: collection-return is `IReadOnlyList<T>` (project precedent — compare `PagedData<T>`), suggestion record adds `Label` + wraps coords in the T-0030 `Coordinates` value-object. Missing fields surface as empty strings rather than null so the frontend form binding doesn't need null-forgiveness ceremony.
- **M-2 `(HttpResponseMessage?, Error?)` tuple → use `BusinessResult<HttpResponseMessage>`** — **Fixed:** `CallMapboxAsync` now returns `BusinessResult<HttpResponseMessage>`. Same discriminator the rest of the codebase uses; no tuple-of-nullables pattern at call sites.
- **M-3 double validation of `AutocompleteLimit` (ValidateOnStart + `Math.Clamp`)** — **Fixed:** dropped the `Math.Clamp` in `BuildUrl`. Validator owns the invariant; no shadow check.
- **M-4 input-validation in the adapter (boundary blur)** — **Fixed (partial):** extracted the blank/length guard into a `AutocompleteInputGuard` internal helper next to the adapter. The XML doc says a future MediatR-command-style controller can call the same guard from a FluentValidator. Promoting it to a shared `Core.AppServices` mixin lands with the first feature that needs both an adapter call AND an HTTP-layer validation pass (e.g. when T-0035 adds anonymous autocomplete on registration).
- **N-3 empty-string-vs-null** — **Documented inline** on `ToSuggestion`.
- **N-1/N-2/N-4..N-7** — accepted as-is.

### Test deltas (+2 facts; 549 total = 467 unit + 82 integration)
- `MapboxAddressGeocoderTests` — replaced "URL carries token" assertions with their inverse (URL must NOT carry token) + added two `Authorization: Bearer` header presence facts (one per endpoint).

## Acceptance criteria
- **AC-1** Build clean; 549 tests pass (467 unit + 82 integration).
- **AC-2** `IAddressGeocoder.GeocodeAsync` returns `BusinessResult<Coordinates>` with [lat, lng] (correct order, swapped from Mapbox's [lng, lat]) on success; `Permanent`/`GeocoderNoMatch` on empty Mapbox response; `Transient`/`GeocoderTransientFailure` on 408/429/5xx; `Permanent`/`GeocoderPermanentFailure` on 4xx, malformed JSON, or out-of-range coords.
- **AC-3** `IAddressGeocoder.AutocompleteAsync` rejects blank/malformed inputs with `Validation`/`GeocoderInvalidInput` WITHOUT calling Mapbox; maps Mapbox `features[]` into `AddressSuggestion` records with structured components extracted from `context[]`.
- **AC-4** `MapboxAddressGeocoder` calls Mapbox via named `IHttpClientFactory` HttpClient + shared `ResiliencePipeline<HttpResponseMessage>`. Per-call timeout via linked `CancellationTokenSource.CancelAfter`. Caller cancellation propagates.
- **AC-5** New rate-limit policy `addresses-autocomplete` partitions by `sub` claim (20/min) when authenticated, by remote IP (5/min) otherwise; `QueueLimit = 0` so rejected calls fail fast.
- **AC-6** `AddressAutocompleteController` mounts on Customer + Maker hosts via shared `Makables.Config` MVC application part; `[Authorize][EnableRateLimiting("addresses-autocomplete")]` applied.
- **AC-7** `MapboxOptions.ValidateOnStart()` enforces non-empty token, absolute URI base, retry/timeout ranges.
- **AC-8** CLAUDE.md hygiene: `Core.Domain` unchanged regarding third-party packages; HTTP calls only inside `Infra.Clients/Mapbox/`; all error codes from `BusinessErrorMessage`; no `SaveChangesAsync` (no entity mutations in T-0031); CI seeds the new `Mapbox__AccessToken`.

## Status log
- 2026-05-25 initial commit 292be2a. 547 tests pass.
- 2026-05-25 reviewer fix folded in. Sec B-1 closed (token → Authorization header); sec MN-2/MN-3 closed (https-only BaseUrl, OverallTimeoutSeconds rename); sec M-1/M-2 documented (ForwardedHeaders prereq for anonymous opening); CQ M-1 closed via ADR 0010 addendum; CQ M-2/M-3/M-4/N-3 closed. 549 tests pass (467 unit + 82 integration; +2 token-leak-regression facts).
