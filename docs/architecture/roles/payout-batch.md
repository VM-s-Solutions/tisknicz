---
role: PayoutBatch
kind: aggregate
status: accepted
---

# PayoutBatch

## Responsibility

Group all maker payouts for a calendar week, generate the corresponding fee invoices, and produce the CSV export the admin uses to execute the bank transfer.

## Collaborators

- **Order** (reads: delivered, not-yet-paid-out orders)
- **Maker** (reads: bank account, registered company data for fee invoice)
- **Invoice** (creates one fee invoice per maker in the batch)
- **PayoutBatchNumbering** (asks: next batch number `VYP-CZ-2026-W21`)

## Knows

- `BatchNumber` (immutable; encodes year + ISO week)
- `Status` (`Pending | Processing | Completed`)
- `TotalAmountMinor`, `Currency`
- `OrderCount`
- `CsvBlobPath` once generated
- `ProcessedAt`

## Does NOT know

- Whether the bank actually executed the transfer (the admin marks `Completed` after import)
- Customer-facing concerns (customers are unaffected by batching)
- Tax filing (out of scope)

## Lifecycle

- **Created by:** `CreatePayoutBatch.Command` — admin-triggerable AND timer-triggered weekly (Monday 02:00 UTC)
- **Status transitions:**
  - `Pending` (just created) → `Processing` (CSV generated, fee invoices issued, marker on each order set)
  - `Processing` → `Completed` (admin confirms bank executed the transfer; audited)
- **Persisted by:** `IPayoutBatchRepository`
- **Destroyed by:** never

## Invariants

- A batch only includes orders in `Delivered` state with `PayoutBatchId IS NULL`.
- When created, all included orders move to a state that prevents re-inclusion (`PayoutBatchId` set on the order; order transitions `Delivered` → `Completed` once batch is `Completed`).
- Fee invoices for the batch are atomic with the batch creation (`UnitOfWorkPipelineBehavior` commits everything).
- CSV format matches the Czech bank batch transfer convention from `TISKNI_MVP_SPEC.md` §5.6.

## Implementation pointer

`backend/src/Makables.Core.Domain/Payouts/PayoutBatch.cs`. Service: `Core.AppServices/Services/PayoutService.cs`.

## Related

- ADRs: 0003, 0009, 0014 (admin actions audited), 0020 (timer trigger)
- Roles: `order`, `maker`, `invoice`, `payout-batch-numbering`
