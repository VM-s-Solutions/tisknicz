---
id: 0004
title: CountryConfiguration as multi-country control plane; seeded via migration with admin UI
status: accepted
date: 2026-05-21
deciders: [Architect, user]
---

# 0004 — CountryConfiguration as multi-country control plane

## Context
We ship CZ only, but the schema and code must be multi-country-ready (see `docs/architecture/multi-country.md`). The cleanest way to absorb per-country variation is a single configuration table that all features consult. The question is **how mutable** that table should be at launch.

## Decision
Adopt `patterns.md` §12 in full. Specifics:

1. **One row per country** in `country_configuration`. CZ row seeded by the initial migration.
2. **Columns** as defined in §12: default currency, default language, timezone, phone prefix, date format, VAT rates (basis points), invoicing mode, tax-ID label/format, VAT-ID label/format, registration-number label/format, default payment provider, default shipping carrier, default registry, `legal_requirements JSONB`, auditable columns.
3. **`Country.IsServiced` vs `IsActive`**: two flags on the related `countries` lookup table. `is_active` = visible in admin pickers; `is_serviced` = open for business (customers can place orders). CZ launches with `is_active=true`, `is_serviced=true`.
4. **Code accesses config via `ICountryConfigRepository.getByCode(countryCode)`.** Never branches on `if (countryCode === 'CZ')`. Cached per request (the same code is read many times during a request).
5. **Admin UI: full edit at launch.** Admin can edit VAT rates, default providers, invoicing mode, and other fields from `/dashboard/admin/countries/[code]`. Every edit writes an audit log entry.
6. **Provider/mode changes are gated by a confirmation modal.** Changing `default_payment_provider` (e.g. CZ Comgate → Stripe-CZ when we add it) is a high-stakes change that affects every new order — the UI requires retyping the provider code to confirm.
7. **Adding a country = inserting a row + flipping `is_serviced=true`.** No migration required for country #2 unless schema changes.

## Alternatives considered

- **Seed via migration, read-only UI for v1** — rejected. The user wants full edit. Acceptable risk because: (a) audit log captures every change, (b) confirmation modal on provider/mode changes, (c) admin team is 2 people both with full context, (d) VAT rates legitimately change.
- **Code-only config (TypeScript constants)** — rejected. Changing a VAT rate would require a deploy. Defeats the "operate without engineering" goal.
- **Per-country deployment of the app** — rejected. Massive operational overhead; needless given Postgres RLS handles tenancy fine.
- **JSON blob without typed columns** — rejected. Type safety on the most-read configuration in the system is worth the schema verbosity. JSON is reserved for the `legal_requirements` field where the shape genuinely varies (e.g. CZ-specific GDPR-controller text).

## Consequences

- **Positive:** zero `if (countryCode === ...)` branches in features. Adding a country is a row insert + adapter registration.
- **Positive:** VAT rate change = admin clicks edit + saves. No deploy.
- **Positive:** admin UI surfaces an authoritative view of how each country is configured (useful when debugging "why did this order use VAT=0?").
- **Negative:** admin can break production by misconfiguring a country (e.g. setting `default_payment_provider='stripe'` before the Stripe adapter is registered). Mitigation: the UI only offers providers that are registered in `paymentProviders` registry at runtime; unknown codes are rejected by a Zod-validated form.
- **Negative:** caching adds complexity. Mitigation: cache per request only (not cross-request) — a config edit takes effect on the very next request.

## Compliance / verification

- Reviewer checklist: any per-country variation in `features/` reads `CountryConfiguration`; no string-literal country branches.
- Reviewer checklist: every admin edit to `country_configuration` writes an audit log entry.
- SecOps checklist: admin UI form validates `default_payment_provider` against the registered providers; same for shipping carrier, registry.
- Test convention: integration test asserts that `getByCode('CZ')` returns the seeded row with the expected provider codes.

## Related
- Patterns: §12 CountryConfiguration, §15 Provider adapter pattern, §13 Enforcement-mode pattern
- Depends on: ADR 0001 (layering)
- Will be referenced by: every integration ADR (Batch 4), RLS ADR (Batch 3)
