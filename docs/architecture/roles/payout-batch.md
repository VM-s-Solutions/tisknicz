---
role: PayoutBatch
kind: aggregate
status: accepted
---

# PayoutBatch

## Responsibility

Group all maker payouts for a calendar week, generate the corresponding fee invoices, and — depending on the country's active payment provider (ADR 0027) — either produce the CSV export the admin uses to execute the bank transfer (Comgate-active countries), or issue the gateway release/transfer instructions directly (Stripe-active countries, "B-tok 3").

## Collaborators

- **Order** (reads: delivered, not-yet-paid-out orders; writes: `PayoutTransferProviderRef` when the active provider is Stripe)
- **Maker** (reads: bank account and/or `PayoutAccountRef`/`PayoutAccountStatus`, registered company data for fee invoice)
- **Invoice** (creates one fee invoice per maker in the batch)
- **PayoutBatchNumbering** (asks: next batch number `VYP-CZ-2026-W21`)
- **PaymentProvider** (Stripe-active countries only, per ADR 0027: calls `ReleaseFundsAsync` per claimed order at claim time — this is new; Comgate-active countries never call it)

## Knows

- `BatchNumber` (immutable; encodes year + ISO week)
- `State` (`Processing | Completed` — no `Pending` per T-0101 lock A.4: creation is atomic in one UoW, so `Pending` would be an unobservable instant)
- `TotalAmountMinor`, `Currency`
- `OrderCount`, `MakerCount`
- Exclusion counts (T-0102a Q3/Q5): `ExcludedPartiallyRefundedOrderCount`, `ExcludedNoBankAccountOrderCount`, `ExcludedNoBankAccountMakerCount` — creation-time snapshots
- `ExcludedPayoutAccountNotReadyOrderCount` (ADR 0027, T-0142) — Stripe-active countries only: orders excluded because the maker's `PayoutAccountStatus != Enabled` at claim time (KYC never completed, or revoked mid-flow). Same creation-time-snapshot shape as the bank-account exclusion it mirrors.
- `CsvBlobPath` once generated (set-once) — Comgate-active countries only; stays null for Stripe-active batches (nothing to attach; the release instructions ARE the artifact)
- `CompletedAt`, `CompletedBy`

## Does NOT know

- Whether the bank actually executed the transfer (the admin marks `Completed` after import)
- Customer-facing concerns (customers are unaffected by batching)
- Tax filing (out of scope)

## Lifecycle

- **Created by:** `CreatePayoutBatch.Command` — admin-triggerable AND timer-triggered weekly (Monday 02:00 UTC)
- **State transitions:**
  - Born directly in `Processing` (claim + batch insert + fee invoices commit atomically in one UoW; no observable `Pending` per T-0101 lock A.4). **ADR 0027 (Stripe-active countries):** the per-order `ReleaseFundsAsync` calls happen inside this same creation step — transfers are issued at claim time, not deferred to `Complete`.
  - `Processing` → `Completed` — Comgate-active countries: T-0103, admin confirms bank executed the transfer (audited). **ADR 0027 (Stripe-active countries):** `Complete` becomes a reconciliation confirmation (ops confirms the Stripe transfers/payout match expectations), not the release trigger — the transfers already happened when the batch was created.
- **Persisted by:** `IPayoutBatchRepository`
- **Destroyed by:** never

## Invariants

- A batch only claims orders in `Delivered` state with `PayoutBatchId IS NULL`, `RefundedAmountMinor == 0` (Q3 — partially-refunded orders are EXCLUDED and ride the next batch), and whose maker has a non-null `BankAccount` (Q5 — NULL-bank-account makers' orders are EXCLUDED, Comgate-active countries) or an `Enabled` `PayoutAccountStatus` (ADR 0027, Stripe-active countries — same exclusion shape, different gate). Exclusion counts are surfaced in the response + audit and persisted as immutable columns on the batch.
- An order already in `Disputed` state is never `Delivered`, so it is excluded from the claim set by the existing state predicate with no code change (patterns §A.22 "sweep exclusion by definition," same property this batch's claim already relies on). This is how ADR 0027's dispute-hold-extension requirement is satisfied for the internal `Dispute` entity — opening a dispute on a delivered-but-not-yet-batched order silently defers its payout to whenever it resumes.
- Immutable once created (lock A.4): no order removal, no repository update/delete. `PayoutBatchId` is set once on each claimed order; the order transitions `Delivered` → `Completed` only when the batch is marked `Completed` (T-0103).
- Fee invoices for the batch are atomic with the batch creation (`UnitOfWorkPipelineBehavior` commits the claim + batch insert + audit in one UoW). **ADR 0027:** the per-order Stripe `ReleaseFundsAsync` calls join this same atomic step for Stripe-active countries.
- CSV format matches the generic documented format behind `IPayoutCsvFormatter` (Q1 — bank-native exporters are follow-ups once the operator names the bank). Comgate-active countries only; Stripe-active countries produce transfer receipts instead (ADR 0027).

## Implementation pointer

- Entity: `backend/src/Makables.Core.Domain/Payouts/PayoutBatch.cs` (+ `PayoutBatchState`, `IPayoutBatchRepository`). Repository: `Makables.Infra.Database/Payouts/PayoutBatchRepository.cs`.
- Claim command: `Core.AppServices/Features/Payouts/CreatePayoutBatch.cs`. Artifacts: `Core.AppServices/Features/Payouts/PayoutArtifactService.cs` + `GenericPayoutCsvFormatter.cs`.
- ADR 0027 (T-0142, not yet built): claim-path branch calling `IPaymentProvider.ReleaseFundsAsync` per order when the active provider supports it; new `Order.PayoutTransferProviderRef` column.

## Related

- ADRs: 0003, 0009, 0014 (admin actions audited), 0020 (timer trigger), 0027 (amends — release-instruction mode for Stripe-active countries)
- Roles: `order`, `maker`, `invoice`, `payout-batch-numbering`, `payment-provider` (new collaborator per ADR 0027), `dispute` (the hold-extension mechanism)
