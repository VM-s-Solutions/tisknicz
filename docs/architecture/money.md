# Money handling

Authoritative ADR: [0003 — Money as integer minor units, currency-aware](../adr/0003-money-and-currency.md).

## Principles

- **`long` minor units.** Every money value is `long AmountMinor` plus `string Currency` (ISO 4217). CZK uses haléře (×100); EUR uses cents (×100); HUF stores whole forint with currency `HUF` (HUF minor unit defunct).
- **No `decimal` for money state.** `decimal` is reserved for pure intermediate computation only (e.g. VAT percentage math); we round back to `long` before storing.
- **`Money` value object** in `Makables.Core.Domain.Money.Money` carries both fields. Constructor enforces integer storage.
- **Currency-aware operations.** `Add`, `Subtract`, `PercentOfBp` throw if currencies don't match. Currency mixing is a programmer error, not an expected failure.
- **VAT rates stored as basis points** (`int`, e.g. `2100` = 21%). Integer math throughout.
- **Rounding: half-up** (`MidpointRounding.AwayFromZero`). Banker's rounding rejected because it surprises CZ retail customers.

## Storage

| Column convention | Type | Example |
|---|---|---|
| `*_minor` | `BIGINT NOT NULL` | `product_price_minor`, `platform_fee_minor`, `total_price_minor` |
| `currency` | `CHAR(3) NOT NULL` | one column per money-bearing row (or inherited from parent — orders carry `currency`, line items inherit) |
| `*_rate_bp` | `INTEGER NOT NULL` | `standard_vat_rate_bp`, `platform_fee_rate_bp` |
| `*_amount_minor` | `BIGINT NOT NULL` | `vat_amount_minor` |

Every monetary column ends in `_minor` and is a `BIGINT`. Reviewer rejects PRs that violate this naming.

## Display

- CZ (`cs-CZ`) display strips haléře: `579 Kč`. Implemented in `MoneyFormatter.Format(money, locale)`.
- Other locales render minor units per `CultureInfo` defaults.
- Frontend mirrors the formatter in `frontend/src/lib/utils/money.ts` so display matches between server-rendered and client-rendered components.
- Internal storage still holds haléře: `Math.round(50000 * 0.15) = 7500` (15% of 500 CZK = 75 CZK exactly). Sums always balance.

## Computation rules

| Operation | Pattern |
|---|---|
| Add two amounts | `a.Add(b)` — throws on currency mismatch |
| Subtract two amounts | `a.Subtract(b)` — throws on currency mismatch |
| Percentage | `m.PercentOfBp(1500)` — 15% (half-up rounded to whole minor unit) |
| Sum of a list | `Money.Zero("CZK").Add(items.Sum(...))` |
| Convert across currencies | **out of MVP scope** — would require an FX adapter |

## Examples

```csharp
var productPrice = Money.CZK(50000);          // 500 CZK
var platformFee = productPrice.PercentOfBp(1500);  // 75 CZK = 7500 minor
var shippingPrice = Money.CZK(7900);          // 79 CZK
var makerPayout = productPrice.Subtract(platformFee).Add(shippingPrice);  // 504 CZK = 50400 minor
var totalPrice = productPrice.Add(shippingPrice);  // 579 CZK = 57900 minor

// Display:
MoneyFormatter.Format(totalPrice, "cs-CZ");   // "579 Kč"
```

## Forbidden

- Floats on money (`float`, `double` for `Amount`).
- `decimal` columns for stored amounts.
- Mixed-currency arithmetic.
- String-based money (`"500.00"`).
- Manual `× 100` / `÷ 100` outside the `Money` constructor and `MoneyFormatter`.
