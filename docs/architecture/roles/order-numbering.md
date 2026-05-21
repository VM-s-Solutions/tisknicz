---
role: OrderNumbering
kind: domain-service
status: accepted
---

# OrderNumbering

## Responsibility

Hand out the next order number for a given country and year. Sequence is not legally required to be gap-free (orders that fail to pay can leave gaps); contention is handled via row-level lock.

## Collaborators

- **NumberingSequence** (reads + writes: row keyed by `(country_code, "order", year)`)

## Knows

- The format: `M-{COUNTRY}-{YYYY}{NNNN}` (4-digit zero-padded sequence; annual reset)
- The lock pattern: `SELECT ... FOR UPDATE` inside the surrounding command's transaction

## Does NOT know

- Anything about the order itself
- Anything about invoices, payout batches (separate numbering services)

## Interface

```csharp
Task<string> NextAsync(string countryCode, int year, CancellationToken ct)
```

## Invariants

- The increment commits or rolls back atomically with the surrounding command (via `UnitOfWorkPipelineBehavior`).
- Format: matches `^M-[A-Z]{2}-\d{8}$`.

## Implementation pointer

Interface: `backend/src/Makables.Core.Domain/Numbering/IOrderNumberGenerator.cs`. Impl: `backend/src/Makables.Infra.Database/Numbering/OrderNumberGenerator.cs`.

## Related

- ADRs: 0009 (this role's defining ADR)
- Roles: `order`
