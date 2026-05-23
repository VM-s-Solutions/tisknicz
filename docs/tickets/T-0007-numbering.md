---
id: T-0007
title: NumberingSequence + IOrderNumberGenerator / IInvoiceNumberGenerator / IPayoutBatchNumberGenerator
status: done
size: M
owner: dotnet-backend
created: 2026-05-22
updated: 2026-05-22
depends_on: [T-0001, T-0002, T-0006]
blocks: [T-0008]
user_stories: []
adrs: [0009]
phase: 1
---

# T-0007 — Numbering

Per ADR 0009 / role files [order-numbering.md](../architecture/roles/order-numbering.md) and [invoice-numbering.md](../architecture/roles/invoice-numbering.md).

## Scope

- `Core.Domain/Numbering/NumberingSequence` — pure entity with PK `(CountryCode, Scope, Year)`, `LastUsedValue`, `UpdatedAt`. Not `Auditable` (this is bookkeeping infrastructure).
- `Core.Domain/Numbering/NumberingScope` — string constants `order` / `invoice` / `payout_batch`.
- `Core.Domain/Numbering/IOrderNumberGenerator` — format `M-CZ-YYYYNNNN`, not legally gap-free.
- `Core.Domain/Numbering/IInvoiceNumberGenerator` — format `FV-CZ-YYYYNNNN`, gap-free by mechanism (allocation inside surrounding tx).
- `Core.Domain/Numbering/IPayoutBatchNumberGenerator` — derives `VYP-CZ-YYYY-Www` from a date; no DB allocation.
- `Infra.Database/Configurations/NumberingSequenceConfiguration` — composite PK mapping, snake_case columns.
- `Infra.Database/Numbering/OrderNumberGenerator` and `InvoiceNumberGenerator` — share `NumberingSequenceAllocator` (internal).
- `Infra.Database/Numbering/NumberingSequenceAllocator` — Postgres `SELECT ... FOR UPDATE` row lock + EF Add-or-Increment.
- `Infra.Database/Numbering/PayoutBatchNumberGenerator` — pure: `ISOWeek.GetWeekOfYear` + format.

## Out of scope

- Concurrent-safety integration test against Testcontainers Postgres (SQLite test harness doesn't support `FOR UPDATE`). Tracked as a follow-up integration-test ticket — won't ship until T-0011 brings in the integration-test harness.
- Migration: `numbering_sequence` table creation lands in T-0010's initial migration alongside `countries` / `country_configuration`.

## Acceptance criteria

- **AC-1** Build clean.
- **AC-2** ≥10 tests pass. (Delivered: 9 NumberingSequence + 5 PayoutBatchNumberGenerator = 14.)
- **AC-3** `NumberingSequence.Increment` produces `M-CZ-20260001` then `M-CZ-20260002` etc.
- **AC-4** `Increment` on Invoice scope formats with `FV-` prefix.
- **AC-5** `PayoutBatchNumberGenerator.For(CZ, 2026-05-22)` returns `VYP-CZ-2026-W21`.
- **AC-6** Same week of year produces the same batch number across different dates within that week.

## Status log

- 2026-05-22 done. 82 tests pass (was 68; +14 numbering).
