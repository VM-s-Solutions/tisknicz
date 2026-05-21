# Multi-country strategy

Ship Czech Republic only. Build so that the second country is a **configuration + adapter** change, not a schema migration or backend rewrite.

## What "multi-country ready" means concretely

| Concern | CZ-only naive | Multi-country ready (our approach) |
|---|---|---|
| Country on entities | Implicit | `CountryCode CHAR(2)` column on `Makers`, `Orders`, `Invoices`, `PayoutBatches`, etc., via `Auditable` base |
| Currency | "always CZK" | `Currency CHAR(3)` column where money is stored; `Money` value object carries currency |
| Tax IDs | "IČO + DIČ" | `RegistrationNumber` + `VatId` columns; format validated per country via `CountryConfiguration.RegistrationNumberFormat` / `VatIdFormat` |
| Address | Czech street/city/ZIP | Structured `Address` value object + per-country validator (`CzAddressValidator`) |
| Phone | Czech format only | E.164 stored; per-country prefix from `CountryConfiguration.PhonePrefix` |
| Payment provider | Comgate hardcoded | `IPaymentProvider` interface, keyed-service registration; selection via `CountryConfiguration.DefaultPaymentProvider` |
| Shipping carrier | Packeta hardcoded | `IShippingCarrier` interface; selection via `CountryConfiguration.DefaultShippingCarrier` |
| Company registry | ARES hardcoded | `ICompanyRegistry` interface; selection via `CountryConfiguration.DefaultRegistry` |
| VAT regime | CZ rules | `ITaxRegime` interface + `InvoicingMode` enum on `CountryConfiguration` |
| Locale | Czech only | i18n catalog `cs-CZ` only at launch; backend stays language-neutral via error codes |
| Numbering | Single sequence | Namespaced per country: `M-CZ-20260001`, `FV-CZ-20260001`, `VYP-CZ-2026-W21` |
| API hosts | Single host | Per-audience hosts already split (Customer / Maker / Admin / Public); country dimension orthogonal |

## What we do NOT do now

- Localize copy beyond Czech.
- Implement other countries' tax regimes (`InvoicingMode = None` or `StandardVat` only for MVP).
- Build a country-selection UI on the frontend.
- Validate addresses for non-CZ countries.
- Add a second payment or shipping adapter.
- Per-country deployment of the application.

## How a future country gets added (illustration: Slovakia)

1. **ADR** "Add Slovakia" — records rationale and scope.
2. **Seed migration** — insert into `Countries` (`SK`, `Slovenská republika`, `IsServiced=false`) and `CountryConfiguration` (default currency `EUR`, language `sk`, VAT 23%, registry `finstat`, payment `comgate` or new, shipping `packeta-sk`).
3. **Registry adapter** — `FinstatCompanyRegistry` implementing `ICompanyRegistry` for IČO lookups in SK.
4. **Tax regime** — `SkTaxRegime` if the rules diverge from `StandardVat`.
5. **(Optional) Payment adapter** — only if Comgate doesn't serve SK or if the user wants a local provider.
6. **(Optional) Shipping adapter** — Packeta covers SK; may not need a new one.
7. **Address validator** — `SkAddressValidator`.
8. **i18n catalog** — `sk-SK` translation file with keys for every `BusinessErrorMessage` code.
9. **Flip `IsServiced=true`** on the SK row.
10. **Frontend** — country selector becomes visible because there's > 1 serviced country (deferred until country #2 is real).

**No schema migration to existing tables is required.** All `_minor`-suffixed money columns, `country_code` columns, and `currency` columns are already in place.

## RLS-equivalent enforcement

Postgres RLS is **not** used (we're not on Supabase anymore). Country and ownership scoping happens in three layers:

1. **JWT audience and role enforcement** at the API host layer (`AddMakablesAuth` validates `aud` and `role` claims).
2. **Repository-level scoping** — repositories that return user/maker/admin data accept the scoping parameters (or read from `IUserSessionProvider`).
3. **EF Core global query filters** — soft delete filter (`IsActive = true`) and an optional country filter on queries that aren't explicitly cross-country.

See [patterns.md §A.19](./patterns.md#a19-ef-core-global-query-filters--multi-country--soft-delete).

## Decisions still open (Batch 3+)

- Is `CountryCode` on `User` (a person) or only on transactional entities? Default proposal: on `User` for the user's primary country (drives default language and JWT claim), but transactional entities also carry their own `CountryCode` because a CZ user can in principle place an order in SK once we operate there.
- "Platform operator entity" per country (JVM YORE s.r.o. for CZ). A future SK operator would be a different legal entity. Schema-level concept: `OperatorEntity` table keyed by country code.
- Single Postgres database or per-country databases? **Default: single database, scoped via application + EF Core filters.**
- Cross-country admin reporting — what aggregations are needed at launch vs deferred?

Open follow-ups land in [`docs/questions/open.md`](../questions/open.md) when an agent hits them during Batch 3+.
