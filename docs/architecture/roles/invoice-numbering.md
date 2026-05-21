---
role: InvoiceNumbering
kind: domain-service
status: accepted
---

# InvoiceNumbering

## Responsibility

Hand out the next invoice number for a given country and year, **gap-free**, as required by Czech tax law.

## Collaborators

- **NumberingSequence** (reads + writes: row keyed by `(country_code, "invoice", year)`)

## Knows

- The format: `FV-{COUNTRY}-{YYYY}{NNNN}`
- The strict gap-free contract: increment only commits if the surrounding command succeeds
- The lock pattern: `SELECT ... FOR UPDATE`

## Does NOT know

- Anything about the invoice content
- Anything about orders, payouts, fees

## Interface

```csharp
Task<string> NextAsync(string countryCode, int year, CancellationToken ct)
```

## Invariants

- Numbers are consecutive within `(country_code, year)`. No gaps. If `FV-CZ-20260005` exists, then `FV-CZ-20260001` through `FV-CZ-20260004` must also exist.
- Allocation happens inside the `IssueInvoice` command's transaction. If the command fails for any reason, the sequence is not incremented.
- Format: matches `^FV-[A-Z]{2}-\d{8}$`.

## Implementation pointer

Interface: `backend/src/Makables.Core.Domain/Numbering/IInvoiceNumberGenerator.cs`.

## Related

- ADRs: 0009 (this role's defining ADR)
- Roles: `invoice`
