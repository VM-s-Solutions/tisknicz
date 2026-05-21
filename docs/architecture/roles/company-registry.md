---
role: CompanyRegistry
kind: adapter
status: accepted
---

# CompanyRegistry

## Responsibility

Look up a business by its registration number and return a normalized `CompanyRecord`. Adapter pattern: one implementation per registry; selection per country.

## Collaborators

- **CountryConfiguration** (reads: registry endpoint, credentials if any)
- (Caching layers — `IMemoryCache` and `company_registry_cache` table — wrap calls)

## Knows

- How to call its specific registry (ARES at launch)
- How to map the registry's response to `CompanyRecord`
- The registry's rate limits

## Does NOT know

- Maker entity, maker activation
- Address geocoding (separate role)
- Whether the company is active for our platform's purposes

## Interface

See ADR 0018. Method: `LookupByRegistrationNumberAsync(regNumber)` → `CompanyRecord`.

## Implementations

- **AresCompanyRegistry** (`Infra.Clients/Ares/`)
- Future: FinstatCompanyRegistry (SK), CeidgCompanyRegistry (PL), UnternehmensregisterCompanyRegistry (DE)

## Implementation pointer

Interface: `backend/src/Makables.Core.Domain/Registry/ICompanyRegistry.cs`.

## Related

- ADRs: 0018 (this role's defining ADR)
- Roles: `maker`, `address`, `country-configuration`
