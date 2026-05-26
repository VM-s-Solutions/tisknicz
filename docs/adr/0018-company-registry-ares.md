---
id: 0018
title: Company registry — ARES (CZ) as the launch registry; CompanyRegistry role; aggressive caching
status: accepted
date: 2026-05-21
deciders: [Architect]
---

# 0018 — Company registry (ARES)

## Context

Czech maker registration starts with an IČO lookup against ARES (the state company registry). The lookup is public, unauthenticated, and rate-limited (10 req/min/IP per ARES docs). Company data rarely changes; caching pays off.

We need a `CompanyRegistry` role with ARES as the first implementation, designed so SK (FinStat), PL (CEIDG), DE (Unternehmensregister) drop in with new adapters.

## Decision

### Role: CompanyRegistry

`docs/architecture/roles/company-registry.md` (adapter role):

**Responsibility:** Look up a business by its registration number and return a normalized company record.

**Collaborators:**
- `CountryConfiguration` (read: registry endpoint, credentials if any)
- `BlobStorage` — none; this role is read-only HTTP

**Does NOT know:**
- Maker entity, maker validation, maker activation
- Address geocoding (the geocoder is a separate role)
- Whether the company is active for our platform's purposes

### Interface

```csharp
// Core.Domain/Registry/ICompanyRegistry.cs
public interface ICompanyRegistry
{
    string Code { get; }   // "ares", "finstat", ...

    Task<BusinessResult<CompanyRecord>> LookupByRegistrationNumberAsync(
        string registrationNumber,
        CancellationToken ct);
}

public record CompanyRecord(
    string RegistrationNumber,                  // IČO
    string? VatId,                              // DIČ
    string CompanyName,
    string? LegalForm,                          // "s.r.o.", "OSVČ"
    Address RegisteredAddress,                  // structured (ADR 0010)
    DateOnly? IncorporatedOn,
    bool IsActive,                              // is the business still operating per the registry
    string SourceRegistry,                      // "ARES"
    DateTimeOffset FetchedAt
);
```

> **T-0032 implementation note.** Two deviations from the sketch above:
>
> 1. `bool IsActive` shipped as `bool IsActiveInRegistry` — the name leaves
>    room for a separate platform-level "is active for our purposes" flag
>    on the future `Maker` aggregate without collision.
> 2. `CompanyRecord` carries an extra `bool IsStale = false` property — set
>    `true` when the adapter served the 7-day stale-cache fallback (see
>    §"Caching policy"). T-0033 `RegisterMaker` reads it to surface a
>    "registry data may be outdated" warning while still allowing the user
>    to complete registration.
>
> **Cache persistence isolation.** The cache store is built on
> `IDbContextFactory<MakablesDbContext>`, NOT the request-scoped
> `IUnitOfWork`. T-0032 sec reviewer M-1: a mid-command `ICompanyRegistry`
> call (T-0033 RegisterMaker is the trigger) must NOT flush the caller's
> tracked-but-uncommitted aggregates via the adapter's cache write. The
> dedicated DbContext scope decouples the two commits.
>
> **Incomplete `sidlo` is Permanent.** The adapter rejects ARES responses
> missing required address fields (`nazevObce`, `psc`, AND either
> `nazevUlice` or `cisloDomovni`) as `Error.Permanent` per §"Error
> classification". The earlier behaviour of substituting literal
> "unknown" / "0" / "00000" was caught in code review and corrected.

### ARES adapter

Lives in `Makables.Infra.Clients/Ares/AresCompanyRegistry.cs`.

**Endpoint:** `GET https://ares.gov.cz/ekonomicke-subjekty-v-be/rest/ekonomicke-subjekty/{ICO}` — no authentication.

**Mapping** from ARES response to `CompanyRecord`:

| ARES field | CompanyRecord property |
|---|---|
| `ico` | `RegistrationNumber` |
| `obchodniJmeno` | `CompanyName` |
| `pravniForma` (code) | `LegalForm` (mapped via static dictionary in `Infra.Common/Czech/CzechLegalForms.cs`) |
| `dic` | `VatId` |
| `datumVzniku` | `IncorporatedOn` |
| `sidlo.{nazevUlice, cisloDomovni, nazevObce, psc}` | `RegisteredAddress` |
| `datumZaniku` is null AND no `ZaniklySubjekt` flag | `IsActive` |

### Caching policy

Two-layer cache:

1. **In-memory** (`IMemoryCache`, registered as singleton): TTL 1 hour. Hot lookups (a maker mid-registration retrying) bypass HTTP entirely.
2. **Database table** `company_registry_cache`: TTL 24 hours.

```sql
CREATE TABLE company_registry_cache (
  registry_code TEXT NOT NULL,
  registration_number TEXT NOT NULL,
  payload JSONB NOT NULL,                      -- serialized CompanyRecord
  fetched_at TIMESTAMPTZ NOT NULL,
  expires_at TIMESTAMPTZ NOT NULL,
  PRIMARY KEY (registry_code, registration_number)
);
CREATE INDEX idx_company_registry_cache_expires_at ON company_registry_cache(expires_at);
```

Lookup order: memory → DB → HTTP. Background Function `EvictExpiredRegistryCache` runs daily.

The DB cache also protects against ARES downtime: if ARES is unreachable but we have a DB entry up to 7 days old, we return it with a stale-warning flag. The maker can still complete registration; admin can re-verify later.

### Rate limiting

The endpoint `/api/public/registry/lookup?ico=...` is rate-limited at **5 req/min per source IP** (well below ARES's 10/min limit to leave headroom for batch operations elsewhere). Frontend debounces input by 500ms.

### Validation before lookup

The handler validates IČO format (8 digits with checksum) **before** hitting ARES. Invalid format returns `Error.Validation` immediately without consuming the rate-limit budget. Validation logic lives in `Core.Domain/Registry/Validators/CzechIcoValidator.cs` — the checksum is the standard mod-11 algorithm.

### Error classification

| ARES response | Maps to |
|---|---|
| HTTP 404 (ICO not in registry) | `Error.NotFound("company.notFound")` — not a system failure, returned to the user |
| HTTP 5xx / timeout | `Transient` — retry with backoff; fall back to stale DB cache if available |
| Malformed JSON / unexpected shape | `Permanent` — alert admin; the parser needs an update |
| 429 (rate limit hit) | `Transient` — schedule retry; cache is the primary defense |

### Multi-country

When SK launches, `FinstatCompanyRegistry` joins as a keyed `ICompanyRegistry` implementation. `CountryConfiguration.DefaultRegistry` selects per country. `CompanyRecord` is the shared shape; FinStat-specific fields (if any beyond the common set) go into a `JsonElement? Raw` property reserved for future extension.

## Alternatives considered

- **No caching; hit ARES on every lookup** — rejected. ARES is rate-limited and occasionally slow (multi-second responses). Cache shifts cost off ARES.
- **Cache forever (until manual eviction)** — rejected. Companies change name, address, VAT status; 24h DB cache is a reasonable balance.
- **Use ARES's bulk download** — rejected. The bulk file is large and rarely needed for our flow. Per-request lookup with caching is cheaper.
- **Persist `CompanyRecord` directly onto Maker at registration and never refresh** — partially adopted. The maker's `RegisteredAddress` and `CompanyName` snapshot at registration is what we use for invoicing — re-fetching ARES on every invoice would be wrong (legal: invoice carries the data as it was when the transaction happened). But the lookup endpoint itself caches as described; the Maker entity holds the snapshot.

## Consequences

### Positive
- ARES outages don't immediately break maker registration; stale-cache fallback keeps onboarding running.
- Rate limit headroom protects against accidental DDoS-self.
- Multi-country: new registry = new adapter; same shape.

### Negative
- Two-layer cache adds complexity. Mitigation: small in-memory layer + simple DB table; both straightforward.
- Stale cache could mislead a maker registering with outdated data. Mitigated by the 24h TTL — most relevant changes (address, VAT status) move slower than that.

## Compliance / verification

- Reviewer: no direct HTTP to ARES outside `Infra.Clients/Ares/`.
- Reviewer: IČO format validated before HTTP.
- Reviewer: cache TTLs match this ADR; bypass options documented if added.
- Integration test: rate limit returns 429 after 5 req/min from same IP.
- Integration test: stale cache returned with warning flag when ARES returns 503.

## Related

- Patterns: §A.14 error classification, §A.15 provider adapter
- Roles: `docs/architecture/roles/company-registry.md` (to be authored), `docs/architecture/roles/maker.md`, `docs/architecture/roles/address.md`
- ADR 0010 (Address is the value object the registered address maps to)
- ADR 0004 (`CountryConfiguration.DefaultRegistry` selects per country)
