---
id: 0009
title: Numbering — orders, invoices, payout batches; country-namespaced and gap-free where law requires
status: accepted
date: 2026-05-21
deciders: [Architect, user]
---

# 0009 — Numbering

## Context

Three things need human-readable, sortable, multi-country-safe identifiers: **orders**, **invoices**, and **payout batches**. Each has different legal and operational constraints:

- Orders: customer and maker reference these constantly; need to be short and readable.
- Invoices: Czech tax law requires **gap-free** sequential numbering per issuer per year. Gaps trigger audit risk.
- Payout batches: weekly admin-facing; need to encode the week for bookkeeping.

In addition: the multi-country plan (ADR 0004) demands that numbering scoped to CZ today doesn't collide with SK/PL/HU when added.

## Decision

### Formats

| Concern | Format | Example | Notes |
|---|---|---|---|
| Order | `M-{COUNTRY}-{YYYY}{NNNN}` | `M-CZ-20260001` | `M` = Makables. Per-country, per-year, 4-digit zero-padded sequence. Resets on January 1. |
| Invoice | `FV-{COUNTRY}-{YYYY}{NNNN}` | `FV-CZ-20260001` | `FV` = faktura. Per-country, per-year, **gap-free** (legal requirement in CZ). Resets January 1. |
| Payout batch | `VYP-{COUNTRY}-{YYYY}-W{ww}` | `VYP-CZ-2026-W21` | `VYP` = výplata. Per-country, per-year, per-ISO-week. No NNNN suffix — one batch per week max. |

### Storage

A single table backs all sequences:

```sql
CREATE TABLE numbering_sequence (
  country_code CHAR(2) NOT NULL,
  scope TEXT NOT NULL,          -- 'order' | 'invoice' | 'payout_batch'
  year INT NOT NULL,
  last_used_value INT NOT NULL DEFAULT 0,
  updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  PRIMARY KEY (country_code, scope, year)
);
```

Rows are created on first use of a `(country_code, scope, year)` triple. They live forever (no cleanup).

### Allocation: gap-free for invoices, sequential for others

The allocation runs inside the surrounding command's transaction (via `UnitOfWorkPipelineBehavior`). Pattern:

```csharp
public async Task<string> NextAsync(string countryCode, string scope, int year, CancellationToken ct)
{
    // SELECT ... FOR UPDATE locks the row for the duration of the transaction.
    // If the surrounding command fails, EF Core rolls back; the increment never commits.
    var row = await _db.NumberingSequences
        .FromSqlInterpolated($@"
            SELECT * FROM numbering_sequence
            WHERE country_code = {countryCode}
              AND scope = {scope}
              AND year = {year}
            FOR UPDATE")
        .FirstOrDefaultAsync(ct);

    if (row is null)
    {
        row = new NumberingSequence(countryCode, scope, year);
        _db.NumberingSequences.Add(row);
    }

    row.Increment();
    return row.Format();   // e.g. "FV-CZ-20260001"
}
```

**Why `FOR UPDATE` instead of `SERIAL` or a separate sequence object:**
- `SERIAL` increments on `INSERT` and a `ROLLBACK` consumes the number. That would create gaps in invoice numbers — illegal in CZ.
- `FOR UPDATE` serializes concurrent allocators within the same transaction. The cost is contention on high-volume scopes, which we don't expect at MVP scale (< 1k orders/day).
- If we later need higher throughput, we can shard the lock per `(country_code, scope, year)` — the schema already supports it.

### Order numbering is not gap-free by policy

Orders can be cancelled before payment, leaving "gaps" in the visible numbering. This is acceptable because:
- Order numbers are not legally regulated.
- A gap there is a feature: customers know their order didn't complete (their number was issued but isn't followed by an invoice).

To minimize gaps, the allocator runs **after** input validation and right before persistence — not at the start of the handler.

### Invoice numbering is gap-free by mechanism

Invoice numbers are allocated **only when the invoice row is being persisted**. The allocator runs inside the `IssueInvoice.Handler` transaction. If anything fails, the row is rolled back; the number isn't consumed. We will never have `FV-CZ-20260001` exist without all of `FV-CZ-20260002` through `FV-CZ-20260010` existing.

### Payout batches don't use the sequence table

`VYP-CZ-2026-W21` is derived from the ISO week number of the batch run date — no sequence increment needed. Uniqueness is enforced by the table's unique index.

## Domain types

```csharp
// Core.Domain/Numbering/INumberingGenerator.cs
public interface IOrderNumberGenerator
{
    Task<string> NextAsync(string countryCode, int year, CancellationToken ct);
}

public interface IInvoiceNumberGenerator
{
    Task<string> NextAsync(string countryCode, int year, CancellationToken ct);
}

public interface IPayoutBatchNumberGenerator
{
    string For(string countryCode, DateOnly batchDate);
}
```

## Alternatives considered

- **`SERIAL`/`IDENTITY` for orders, separate gap-free mechanism for invoices** — rejected. Two patterns for the same problem; the unified `numbering_sequence` table is simpler.
- **UUID + display-only label** — rejected. Czech accountants want the invoice number to be the actual key, not a label.
- **Allocate invoice number at order paid, before invoice persisted** — rejected. If the invoice generation fails (e.g. PDF generator down), we'd have an allocated-but-unused number. Allocating inside the persistence transaction is the only fully gap-free design.
- **Monthly reset (`M-CZ-202605-NNNN`)** — rejected. Annual reset matches Czech accounting conventions and produces shorter numbers (4 digits sufficient for the first decade at MVP scale).
- **6-digit sequence (`NNNNNN`)** — rejected for now. 4 digits = 9999 orders/year. We start with 4; expand to 6 in a superseding ADR when one of our country sequences crosses 8000.

## Consequences

### Positive
- One pattern for three concerns.
- Gap-free invoices satisfy CZ tax law.
- Country namespace means SK/PL/HU additions can't collide.
- Annual reset keeps numbers short and readable.
- `FOR UPDATE` lock is a Postgres-native primitive; no application-level distributed lock needed.

### Negative
- Contention on the `(CZ, order, 2026)` row under high concurrency. Acceptable at MVP scale; mitigation path exists (shard by sub-key).
- 4-digit sequences will exhaust if a country sees > 9999 orders or invoices in a single year. Migration path: extend `last_used_value` width and the formatter; existing numbers stay valid (sortable as text).

## Compliance / verification

- Reviewer checklist: any new number-generating use case calls `IOrderNumberGenerator` / `IInvoiceNumberGenerator` / `IPayoutBatchNumberGenerator`, not inline string composition.
- Reviewer checklist: number allocation happens inside the surrounding command's transaction (i.e. between handler start and `UnitOfWorkPipelineBehavior.SaveChangesAsync`).
- Integration test: a failing invoice command leaves `last_used_value` unchanged.
- Integration test: two concurrent invoice commands serialize and produce consecutive numbers.

## Related
- Patterns: §A.5 pipeline behaviors, §A.20 idempotent webhooks (where webhook handlers issue invoices)
- ADR 0004 — CountryConfiguration (sequences are scoped per country code)
- Depends on: ADR 0001 (layering), ADR 0007 (pivot)
