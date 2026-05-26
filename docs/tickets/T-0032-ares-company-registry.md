---
id: T-0032
title: ICompanyRegistry + AresCompanyRegistry + CzechIcoValidator (mod-11) + company_registry_cache (24h TTL + 7-day stale fallback) + in-memory cache
status: done
size: M
owner: dotnet-backend
created: 2026-05-25
updated: 2026-05-25
depends_on: [T-0030]
blocks: [T-0033]
adrs: [0018]
phase: 2
---

# T-0032 — ARES company registry + IČO validator + 2-layer cache

## Scope

Per ADR 0018. Unblocks T-0033 (RegisterMaker: ARES lookup → snapshot → User+Maker atomic).

User-chosen design at planning time:
- Ship the 7-day stale-cache fallback now (the seam is small + T-0033 depends on it).
- Service-only — T-0032 does NOT mount the `/api/public/registry/lookup` HTTP endpoint; T-0033 (or a later UX ticket) adds it.
- Ship the `CzechLegalForms` map for the 12 most-common codes; unknown codes pass through as the trimmed numeric.
- `AresOptions.BaseUrl` with `.ValidateOnStart()` requiring absolute https (same shape as T-0031 sec MN-2).

### Domain (`Core.Domain/Registry/`)
- `ICompanyRegistry.cs` — interface + `CompanyRecord` sealed record (with `IsStale` flag added beyond the ADR sketch — set true when the adapter returns a stale-cache fallback so T-0033 surfaces a user-facing warning).
- `Validators/CzechIcoValidator.cs` — pure static `IsValid(string?)` running the mod-11 weighted-sum checksum (weights 8..2 on digits 1..7; expected check digit is 11 - (sum mod 11) with the standard 0/1/10-edge fallbacks). Pinned against real ARES IČOs (27074358 Avast, 26168685 Seznam, 45272956 KB, etc.) so a future contributor can sanity-check against the live registry.
- `CompanyRegistryCacheEntry.cs` — bookkeeping aggregate (NOT `Auditable`, same posture as `OutboxEvent`). Composite primary key (`RegistryCode`, `RegistrationNumber`). `Refresh(...)` updates payload + expiry in place. Refuses `expiresAt <= fetchedAt`.
- `ICompanyRegistryCacheRepository.cs` — `GetAsync` (regardless of expiry — adapter decides) + `Add`. Caller drives UoW.

### Core.Domain.Common
- `BusinessErrorMessage` adds `CompanyRegistryTransient` + `CompanyRegistryPermanent` under the company-registry block. Adds `IcoFormatInvalid` (alias of the pre-existing `InvalidIcoFormat`) so a future contributor reading the registry adapter sees the code where they expect it.

### Infra.Common (`Czech/`)
- `CzechLegalForms.cs` — frozen-dictionary map of 12 common ARES `pravniForma` codes to Czech-readable labels (`112 → "Společnost s ručením omezeným"`, `121 → "Akciová společnost"`, etc.). Unknown codes pass through as the trimmed input; null/blank returns null.

### Infra.Database
- `Configurations/CompanyRegistryCacheEntryConfiguration.cs` — `company_registry_cache` table. Composite PK; `payload` as `jsonb` (Npgsql picks the per-provider type for SQLite tests); index `ix_company_registry_cache_expires_at` for the future daily `EvictExpiredRegistryCache` sweep + the stale-fallback window query.
- `Registry/CompanyRegistryCacheRepository.cs` — tracked `Get` (adapter may `Refresh` in place); `Add`.
- `Migrations/20260525205044_CompanyRegistryCache.cs` — creates the table.

### Infra.Clients (`Ares/`)
- `AresOptions.cs` — `Ares:BaseUrl` (https-validated, default `https://ares.gov.cz`) + `RetryCount` + `RetryBaseDelayMs` + `OverallTimeoutSeconds` + `InMemoryCacheTtlMinutes` + `DbCacheTtlHours` + `StaleFallbackDays`. Every value validated at `.ValidateOnStart()`.
- `AresCompanyRegistry.cs` — implements `ICompanyRegistry`. Flow:
  1. Format gate via `CzechIcoValidator` — invalid input returns `Error.Validation` without any I/O.
  2. **In-memory** cache (the hot path) — `IMemoryCache` keyed `ares:{ico}` with TTL `InMemoryCacheTtlMinutes`.
  3. **DB** cache — fresh row (`expires_at > now`) served and promoted to in-memory.
  4. **HTTP** fetch from ARES — `Polly` retry on 408/429/5xx within `OverallTimeoutSeconds`.
  5. **404 →** `Error.NotFound("company")`.
  6. **5xx / 429 / timeout →** stale-fallback: a DB row up to `StaleFallbackDays` days past `expires_at` is returned with `IsStale = true`. Not cached in-memory (so the next call retries ARES).
  7. **Permanent failures (4xx-not-404 / malformed JSON / structural map error) →** `Error.Permanent`. Stale-fallback does NOT trigger on permanent errors — that would paper over a deterministic parse bug with stale data.
  - Cache payload is a flat private `CachedRecord` DTO, NOT the live `CompanyRecord` (which embeds an `Address` aggregate whose `Address.Create` factory `System.Text.Json` can't round-trip).
  - HTTP retry pipeline is shared via Polly's `ResiliencePipelineRegistry<string>` keyed by HttpClient name (replaces an earlier latent T-0031 collision where two adapters both registered `ResiliencePipeline<HttpResponseMessage>` as singleton, overwriting each other).

### DI
- `AddMakablesClients` registers `AresOptions` (ValidateOnStart) + named HttpClient + a single `ResiliencePipelineRegistry<string>` carrying both the Mapbox and ARES pipelines + `ICompanyRegistry → AresCompanyRegistry`. The shared `HttpRetryStrategy(...)` helper factory keeps the retry-policy shape in one place.
- `AddMakablesInfrastructure` registers `ICompanyRegistryCacheRepository → CompanyRegistryCacheRepository` and adds `services.AddMemoryCache()`.

### Adapter refactor (T-0031 follow-up)
- `MapboxAddressGeocoder` constructor now takes `ResiliencePipelineRegistry<string>` and resolves its named pipeline via `GetPipeline<HttpResponseMessage>(HttpClientName)`. Test fixture updated to match.

### Packages
- `Microsoft.Extensions.Caching.Memory 10.0.0` — added to central `Directory.Packages.props` + Infra.Clients csproj.

### CI + integration tests
- `Ares:BaseUrl` seeded in `JwtAuthMiddlewareTests` (two sites) + `WebHostStartupTests` + `.github/workflows/ci.yml` so the new `ValidateOnStart` predicate is satisfied at host build.

### Tests (+42 facts; 591 total = 509 unit + 82 integration)
- `Domain/Registry/CzechIcoValidatorTests.cs` — 14 facts (5 real-IČO accepts + 3 mod-11 rejects + 6 shape rejects).
- `Domain/Registry/CompanyRegistryCacheEntryTests.cs` — 5 facts (factory init, required-field matrix, expires-after-fetched, Refresh, Refresh expires-after-fetched).
- `Infra/Common/Czech/CzechLegalFormsTests.cs` — 4 facts (known codes resolve, unknown code passes through trimmed, blank → null × 3).
- `Infra/Clients/Ares/AresCompanyRegistryTests.cs` — 12 facts pinning the 7-step flow: format gate (3 rows), happy path with cache promotion, fresh DB cache short-circuits HTTP, 404 → NotFound, 5xx/429 → Transient (3 rows), malformed JSON → Permanent, stale fallback with `IsStale=true`, too-old DB entry doesn't serve stale, Permanent failure doesn't serve stale, cancellation propagates.

## Out of scope
- `/api/public/registry/lookup?ico=...` HTTP endpoint — T-0033 (or a future UX ticket).
- `EvictExpiredRegistryCache` background Function — ADR 0020-style follow-up.
- SK / PL / DE registries — new adapters per ADR 0018 §"Multi-country". `CountryConfiguration.DefaultRegistry` already exists.
- ARES `IsActiveInRegistry` consumer logic — T-0033 RegisterMaker handler will decide what to do with `false` (probably block registration; not in T-0032 scope).

## Reviewer findings and resolutions (commit c1d06fa)

Two reviewers ran in parallel.

### Security reviewer — 0 BLOCKERs + 1 MAJOR + 3 MINORs

- **M-1 `unitOfWork.SaveChangesAsync` mid-handler** would commit the calling command's tracked-but-uncommitted aggregates when T-0033 RegisterMaker invokes the registry mid-handler. **Fixed:** introduced `ICompanyRegistryCacheStore` (replaces `ICompanyRegistryCacheRepository`) backed by `IDbContextFactory<MakablesDbContext>`. Each call constructs a fresh DbContext + commits in isolation. The adapter no longer touches `IUnitOfWork`. `AddDbContextFactory<MakablesDbContext>` registered next to `AddDbContext`, sharing the configurator delegate so connection string + interceptors stay identical.
- **Mn-1 LogWarning conflated JSON failure + post-deserialise validation failure** — **Fixed:** the cache-row "could not be reconstructed" log message now spells out "corrupt payload or post-deserialise validation failure" so DB tampering vs forward-compat field rename can be triaged.
- **Mn-2 IČO is PII for OSVČ** (sole proprietor — natural person under GDPR Art 4(1)) — **Documented** in the adapter's class-level XML doc as a comment to the future OTel PII-redaction policy.
- **Mn-3 stale-fallback under 429 induction** — accepted; rate-limit + stale-cache UX is bounded by 5/min IP on the public lookup endpoint (T-0033 will mount it).

### Code-quality reviewer — 0 BLOCKERs + 1 MAJOR + 4 MINORs + nits

- **M-1 `MapToRecord` silent "unknown" / "0" / "00000" fallback for incomplete ARES `sidlo`** — **Fixed:** the extracted `AresResponseMapper.TryMap` rejects responses missing required `sidlo.nazevObce` / `psc` / (`nazevUlice` OR `cisloDomovni`) as `MapFailure.IncompleteSidlo`, surfaced as `Error.Permanent(CompanyRegistryPermanent)`. Mapper-allowed special case: an entity with only city + ZIP + house number still constructs via the city-name-as-street fallback (covers small-village / OSVČ-at-home). Pinned by 4 reject-row `[Theory]` cases + 1 city-fallback success case.
- **m-1 Adapter doing too many things** — **Fixed:** extracted `Makables.Infra.Clients.Ares.Mapping.AresResponseMapper` (static, ~120 lines) holding `TryMap` + the `AresEkonomickySubjekt` / `AresSidlo` JSON shapes. Adapter shrinks to ~250 lines focused on flow.
- **m-2 `CachedRecord` should be `internal`** — **Fixed:** extracted to `Makables.Infra.Clients.Ares.Caching.CachedCompanyRecord` (file scope), `internal sealed record`. The future `EvictExpiredRegistryCache` Function + admin inspector now have a public-enough shape to deserialise.
- **m-3 ADR 0018 lacked the `IsStale` / `IsActiveInRegistry` addendum** — **Fixed:** ADR 0018 §"Interface" now carries a "T-0032 implementation note" sub-section documenting the `IsActive → IsActiveInRegistry` rename, the new `IsStale` property, the `IDbContextFactory` isolation rationale (sec M-1), and the "Incomplete sidlo is Permanent" rule (CQ M-1).
- **m-4 Same as sec M-1** — **Fixed** with sec M-1.
- **n-1 `IcoFormatInvalid` alias** — **Fixed:** renamed canonical `InvalidIcoFormat` → `IcoFormatInvalid` codebase-wide; alias deleted. Naming now consistent with `CompanyRegistryTransient` / `GeocoderTransientFailure`.
- **n-2 `CzechIcoValidator` doc-comment off-by-one (`(9 - i)` vs code's `(8 - i)`)** — **Fixed.**
- **n-4 Full ČSÚ právní-forma číselník source-of-truth question** — **Documented** as Q-0006 in `docs/questions/open.md`.

### Side-improvements during the fix
- `CompanyRegistryCacheStore` reads via `AsNoTracking()` then re-loads for update — keeps each store call atomic without splitting the API surface.
- Old `ICompanyRegistryCacheRepository` + `CompanyRegistryCacheRepository` files deleted.

### Test deltas (+5 facts; 596 total = 514 unit + 82 integration)
- `AresCompanyRegistryTests` — 4 new reject-incomplete-sidlo theory rows + 1 city-as-street-fallback success row; `_uow` assertion dropped; `.Add(...)` assertions migrated to `.UpsertAsync(...)`.

## Acceptance criteria
- **AC-1** Build clean; 596 tests pass (514 unit + 82 integration).
- **AC-2** `CzechIcoValidator.IsValid` returns true for real ARES sample IČOs (27074358 Avast, 26168685 Seznam, 45272956 KB) and false for off-by-one checksum / wrong length / non-digit / blank inputs.
- **AC-3** `AresCompanyRegistry` enforces the 7-step lookup flow (format → memory cache → DB cache → HTTP → 404 / Transient / Permanent / stale fallback) pinned per ADR 0018 §"Caching policy" + §"Error classification".
- **AC-4** Stale-fallback returns `IsStale = true` only when the failure is `Transient` AND the DB row's `FetchedAt > now - StaleFallbackDays`. Stale entries are NOT promoted to the in-memory cache.
- **AC-5** Cache payload uses a flat `CachedRecord` DTO that round-trips through `System.Text.Json`; the live `CompanyRecord` (with its embedded `Address` aggregate) is reconstructed on deserialize via `Address.Create`.
- **AC-6** `AresOptions.ValidateOnStart()` enforces absolute https `BaseUrl` + range guards on every numeric option (retry count, timeout seconds, TTLs).
- **AC-7** Shared `ResiliencePipelineRegistry<string>` carries both Mapbox + ARES pipelines under their HttpClient-name keys; closes the latent T-0031 singleton-collision.
- **AC-8** CI + integration tests seed `Ares:BaseUrl` so hosts boot under the new `ValidateOnStart`.
- **AC-9** CLAUDE.md hygiene: all HTTP confined to `Infra.Clients/Ares/`; `Core.Domain` no third-party packages; all error codes from `BusinessErrorMessage`; no SaveChangesAsync outside the adapter's explicit UoW commit (orchestrating its own UoW outside the MediatR pipeline, same posture as T-0029 SendEmailHandler).

## Status log
- 2026-05-25 initial commit c1d06fa. 591 tests pass.
- 2026-05-25 reviewer fix folded in. Sec M-1 + CQ m-4 closed via `IDbContextFactory`-backed `ICompanyRegistryCacheStore`; CQ M-1 closed by rejecting incomplete `sidlo` as Permanent; CQ m-1 + m-2 closed via extraction of `AresResponseMapper` + `CachedCompanyRecord`; CQ m-3 ADR 0018 amended; CQ n-1 / n-2 nits closed; CQ n-4 filed as Q-0006. 596 tests pass (514 unit + 82 integration).
