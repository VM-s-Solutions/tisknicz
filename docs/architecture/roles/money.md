---
role: Money
kind: value-object
status: accepted
---

# Money

## Responsibility

Represent a monetary amount in a specific currency, with currency-safe arithmetic and consistent rounding.

## Collaborators

- (None — this is a leaf value object used by everything monetary)

## Knows

- `AmountMinor` (`long`) — value in the currency's minor unit (haléře, cents)
- `Currency` (3-char ISO 4217) — `CZK`, `EUR`, `PLN`, `HUF`

## Does NOT know

- Locale or display format (`MoneyFormatter` handles that)
- Exchange rates (no FX in MVP)
- VAT or fees (those live on `CountryConfiguration` and `OrderPricing`)

## Operations

```csharp
Money Add(Money other)                   // currency must match
Money Subtract(Money other)              // currency must match
Money PercentOfBp(int basisPoints)       // half-up rounding to whole minor units
int CompareTo(Money other)               // currency must match
bool Equals(Money other)                 // value + currency
```

Mixed-currency arithmetic throws `InvalidOperationException` — programmer error, not an expected failure.

## Invariants

- `AmountMinor` is integer (`long`); no floats anywhere money state exists.
- Negative amounts are permitted (refunds, adjustments).
- Currency is uppercase 3-letter ISO 4217; constructor normalizes.
- Rounding rule: half-up (`MidpointRounding.AwayFromZero`).

## Implementation pointer

`backend/src/Makables.Core.Domain/Money/Money.cs`. Display: `backend/src/Makables.Core.AppServices/Common/MoneyFormatter.cs` (locale-aware).

## Related

- ADRs: 0003 (this ADR defined the value object)
- Used by: every monetary field on every aggregate (`Order`, `Product`, `Invoice`, `PayoutBatch`)
