---
role: OrderNumbering
kind: domain-service
status: accepted
---

# OrderNumbering

## Responsibility

Hand out the next order number for a given country. Sequence is not legally required to be gap-free (orders that fail to pay can leave gaps); contention is handled via row-level lock.

## Collaborators

- **NumberingSequence** (reads + writes: row keyed by `(country_code, "order", year)`)
- **CountryConfiguration** (reads: `TimeZoneId` per country, to compute the country-local year)

## Knows

- The format: `M-{COUNTRY}-{YYYY}{NNNN}` (4-digit zero-padded sequence; annual reset)
- The lock pattern: `SELECT ... FOR UPDATE` inside the surrounding command's transaction
- The year-source contract: the year segment is **country-local**, derived from `CountryConfiguration.TimeZoneId` applied to `IClock.UtcNow`. A 23:30 Prague order on 2026-12-31 buckets into the `2026` sequence; a 00:30 Prague order on 2027-01-01 buckets into the `2027` sequence — matching what the customer sees on the invoice. The caller MUST NOT supply the year; the generator owns the conversion.

## Does NOT know

- Anything about the order itself
- Anything about invoices, payout batches (separate numbering services)

## Interface

```csharp
Task<string> NextAsync(string countryCode, CancellationToken ct)
```

The earlier `(string countryCode, int year, CancellationToken)` signature was removed in T-0062 (see ADR 0009 amendment). A caller passing `clock.UtcNow.Year` to the old signature would compile but ship the wrong year for the 1-hour window between 23:00 UTC Dec 31 and 00:00 UTC Jan 1 (midnight local Prague in winter, since CET = UTC+1). Removing the parameter forces every caller into the TZ-aware contract.

## Invariants

- The increment commits or rolls back atomically with the surrounding command (via `UnitOfWorkPipelineBehavior`).
- Format: matches `^M-[A-Z]{2}-\d{8}$`.
- Year segment is country-local, not UTC.

## Implementation pointer

Interface: `backend/src/Makables.Core.Domain/Numbering/IOrderNumberGenerator.cs`. Impl: `backend/src/Makables.Infra.Database/Numbering/OrderNumberGenerator.cs`. Race + rollback + TZ-aware-year tests: `backend/src/Makables.IntegrationTests/Numbering/`.

## Related

- ADRs: 0009 (this role's defining ADR — see TZ-aware-year amendment), 0004 (CountryConfiguration)
- Roles: `order`
