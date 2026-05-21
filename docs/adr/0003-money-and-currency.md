---
id: 0003
title: Money stored as integer minor units, currency-aware
status: accepted
date: 2026-05-21
deciders: [Architect, user]
---

# 0003 — Money stored as integer minor units, currency-aware

## Context
The MVP spec stores prices as whole CZK. We are designing for multi-country expansion, where currencies have different minor-unit conventions (CZK haléře, EUR cents, HUF — no minor unit in practice). Floats are off the table for money. We need a representation that is correct today and forward-compatible.

## Decision
Adopt `patterns.md` §18 with a CZ-specific display rule.

1. **All money is stored as integer minor units** plus a 3-letter ISO currency code. CZK uses haléře (×100); EUR uses cents (×100). HUF stores whole forint with currency=HUF (HUF's minor unit is defunct).
2. **Schema columns** carry the `_minor` suffix: `product_price_minor INTEGER NOT NULL`, plus a sibling `currency CHAR(3) NOT NULL` column on every monetary row (or inherited from the parent — e.g. orders carry `currency`, line items inherit).
3. **`Money` value object** in `src/lib/domain/money/money.ts`: `{ amountMinor: number; currency: 'CZK' | 'EUR' | 'PLN' | 'HUF' }`. Constructor asserts `Number.isInteger(amountMinor)`.
4. **Pure operations** in `src/lib/domain/money/operations.ts`: `addMoney`, `subMoney`, `percentOf(money, basisPoints)`, `compareMoney`, `formatMoney(money, locale)`. Currency-mixing throws (this is a programmer error, not an expected failure).
5. **VAT rates are stored as basis points** (`INTEGER`, e.g. `2100` = 21%). Integer math throughout.
6. **Rounding rule: half-up** (`Math.round`). Documented in `operations.ts`. Banker's rounding rejected because it surprises CZ retail customers.
7. **CZ display rule: round to whole CZK.** The Czech formatter strips the haléře portion. The user expects `579 Kč`, not `579,00 Kč`. Implemented in `formatMoney(money, 'cs-CZ')`. Other locales render minor units per `Intl.NumberFormat` defaults.
8. **CZK haléře are still stored** even though they're not displayed — necessary because the platform fee `Math.round(productMinor × 0.15)` may produce a haléř-precision number that we need to track internally for sums to balance.

## Alternatives considered

- **Whole CZK now, migrate when EUR is added** — rejected. The migration cost grows with every row in the orders table; doing it now while the DB is empty is free. Also leaks "CZ specialness" into the schema, which is exactly the kind of country-coupling we're avoiding.
- **`decimal` / `numeric` Postgres columns + JS BigInt** — rejected. Native Postgres `numeric(12, 2)` is correct but the JS marshaling is awkward (no native decimal type), and we get no advantage over integer minor units for our use case.
- **String-based money (`"500.00"`)** — rejected. Easy to mis-add; pushes the correctness problem to whoever calls `parseFloat`.
- **Float CZK** — rejected (industry-standard wrong answer).

## Consequences

- **Positive:** correct, currency-agnostic, no floats. Forward-compatible.
- **Positive:** schema columns named `_minor` make the unit obvious to anyone reading a migration or query.
- **Positive:** existing TISKNI_MVP_SPEC `INTEGER` columns rename to `_minor` and gain a `currency` column; no data to migrate (empty DB).
- **Negative:** developers must remember "× 100 on the way in, ÷ 100 in display." Mitigation: only `formatMoney` performs the division; all internal math uses minor units.
- **Negative:** display tests must account for "the display drops haléře but storage keeps them." Mitigation: explicit test cases.

## Compliance / verification

- Reviewer checklist: no float arithmetic on money anywhere in `domain/` or `features/`.
- Reviewer checklist: every monetary DB column ends in `_minor` and is `INTEGER NOT NULL`.
- Reviewer checklist: every table with money has a `currency CHAR(3) NOT NULL` column (or inherits from a parent that does).
- Reviewer checklist: VAT rates are `*_rate_bp INTEGER`, never `DECIMAL`.
- Test convention: `formatMoney(money(57900, 'CZK'), 'cs-CZ')` returns `579 Kč` (whole CZK display).
- Test convention: `percentOf(money(50000, 'CZK'), 1500)` returns `money(7500, 'CZK')` (15% of 500 CZK = 75 CZK, rounded half-up).

## Related
- Patterns: §18 Money handling
- Depends on: ADR 0001 (layering — `Money` lives in `domain/`)
- Will be referenced by: ADR for pricing service (Batch 4), invoicing ADR, payout ADR
