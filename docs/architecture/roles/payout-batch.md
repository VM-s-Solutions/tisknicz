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
- `State` (`Processing | Completed` — no `Pending` per T-0101 lock A.4: creation is atomic in one UoW, so `Pending` would be an unobservable instant)
- `TotalAmountMinor`, `Currency`
- `OrderCount`, `MakerCount`
- Exclusion counts (T-0102a Q3/Q5): `ExcludedPartiallyRefundedOrderCount`, `ExcludedNoBankAccountOrderCount`, `ExcludedNoBankAccountMakerCount` — creation-time snapshots
- `CsvBlobPath` once generated (set-once)
- `CompletedAt`, `CompletedBy`

## Does NOT know

- Whether the bank actually executed the transfer (the admin marks `Completed` after import)
- Customer-facing concerns (customers are unaffected by batching)
- Tax filing (out of scope)

## Lifecycle

- **Created by:** `CreatePayoutBatch.Command` — admin-triggerable AND timer-triggered weekly (Monday 02:00 UTC)
- **State transitions:**
  - Born directly in `Processing` (claim + batch insert + fee invoices commit atomically in one UoW; no observable `Pending` per T-0101 lock A.4)
  - `Processing` → `Completed` (T-0103: admin confirms bank executed the transfer; audited)
- **Persisted by:** `IPayoutBatchRepository`
- **Destroyed by:** never

## Invariants

- A batch only claims orders in `Delivered` state with `PayoutBatchId IS NULL`, `RefundedAmountMinor == 0` (Q3 — partially-refunded orders are EXCLUDED and ride the next batch), and whose maker has a non-null `BankAccount` (Q5 — NULL-bank-account makers' orders are EXCLUDED). Exclusion counts are surfaced in the response + audit and persisted as immutable columns on the batch.
- Immutable once created (lock A.4): no order removal, no repository update/delete. `PayoutBatchId` is set once on each claimed order; the order transitions `Delivered` → `Completed` only when the batch is marked `Completed` (T-0103).
- Fee invoices for the batch are atomic with the batch creation (`UnitOfWorkPipelineBehavior` commits the claim + batch insert + audit in one UoW).
- CSV format matches the generic documented format behind `IPayoutCsvFormatter` (Q1 — bank-native exporters are follow-ups once the operator names the bank).

## Implementation pointer

- Entity: `backend/src/Makables.Core.Domain/Payouts/PayoutBatch.cs` (+ `PayoutBatchState`, `IPayoutBatchRepository`). Repository: `Makables.Infra.Database/Payouts/PayoutBatchRepository.cs`.
- Claim command: `Core.AppServices/Features/Payouts/CreatePayoutBatch.cs`. Artifacts: `Core.AppServices/Features/Payouts/PayoutArtifactService.cs` + `GenericPayoutCsvFormatter.cs`.

## Related

- ADRs: 0003, 0009, 0014 (admin actions audited), 0020 (timer trigger)
- Roles: `order`, `maker`, `invoice`, `payout-batch-numbering`
