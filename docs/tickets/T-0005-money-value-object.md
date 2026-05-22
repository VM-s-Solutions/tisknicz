---
id: T-0005
title: Money value object + MoneyFormatter + tests
status: done
size: S
owner: dotnet-backend
created: 2026-05-22
updated: 2026-05-22
depends_on: [T-0001, T-0004]
blocks: [T-0006, T-0010]
user_stories: []
adrs: [0003]
phase: 1
---

# T-0005 — Money value object

Per ADR 0003 / patterns §A.18: `Money(long AmountMinor, string Currency)` `readonly record struct` with `Add`/`Subtract`/`PercentOfBp` (basis points + half-up rounding), `CompareTo`, factory `Money.Of(amount, "CZK")`. `MoneyFormatter.Format(money, locale)` strips haléře for CZK ("579 Kč"), uses `Intl`-like culture-aware format for other currencies.

## Acceptance criteria

- **AC-1** Build clean.
- **AC-2** ≥10 tests covering construction, currency normalization, add/subtract/percent, currency mismatch throws, half-up rounding, overflow protection, value equality, formatter cs-CZ + EUR.
- **AC-3** `Money.CZK(50000).PercentOfBp(1500) == Money.CZK(7500)` (15% platform fee on 500 CZK = 75 CZK).
- **AC-4** Mixed-currency `Add`/`Subtract`/`Compare` throws `InvalidOperationException`.
- **AC-5** Overflow throws (checked arithmetic).
- **AC-6** Formatter cs-CZ: `Format(CZK(57900)) == "579 Kč"`. Half-up: `Format(CZK(57950)) == "580 Kč"`.

## Status log

- 2026-05-22 done. 20 Money tests pass; build clean; namespace `Makables.Tests.Money` clashed with `Makables.Core.Domain.Money` so tests live under `Makables.Tests.Domain.MoneyTests`.
