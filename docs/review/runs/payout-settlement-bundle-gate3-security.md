# Gate 3 (Security) — payout-settlement-bundle

**Verdict: GATE3_PASS**

Branch: `feat/order-cleanup-bundle` (payout-settlement bundle, 8 commits over T-0103 / T-0112 / T-0112a / T-0116).
Scope: maker-scoped financial reads, fee-invoice file download, a settlement money-terminal admin command.
Reviewer: Security & DevOps. Date: 2026-06-13.

---

## Headline — GetMakerPayoutDetail IDOR: CLEAN

`Makables.Infra.Database/Payouts/PayoutQueries.cs::GetMakerPayoutDetailAsync` (the
class lives under `Infra.Database`, not `Infra.Persistence`).

- **Existence/IDOR gate (L103-107):** `Order.AnyAsync(o => o.PayoutBatchId == batchId && o.MakerId == makerId)`. Unknown id AND cross-maker id both yield `null` → handler returns the same `payoutBatch.notFound` 404. No enumeration oracle.
- **Per-order breakdown (L117-130):** `WHERE o.PayoutBatchId == batchId && o.MakerId == makerId`. This is the dual-filter the ticket demanded — NOT "the batch this maker participated in." A batch holding many makers' orders projects ONLY the requesting maker's lines.
- **Total (L142):** `MakerTotalPaidMinor = lines.Sum(...)` over the already-maker-filtered lines — the per-maker slice, never `PayoutBatch.TotalAmountMinor`.
- **Header (L109-113):** unconditional load but projects only `BatchNumber/State/Currency/CompletedAt` — no money, no cross-maker data — and is gated behind the `anyForMaker` check, so cross-maker probes never reach it.

**Integration test pins it** (`MakerPayoutQueriesIntegrationTests`): one Completed batch claims BOTH makers' orders (makerA 38250 across 2 orders, makerB 17850 across 1; cross-maker total 56100). Asserts: list returns makerA's 38250 (not 56100); detail returns exactly 2 lines and explicitly asserts makerB's `M-CZ-o3` is absent; reconciliation invariant `product − fee + shipping == payout` holds per line and sums to the total; makerB requesting the SAME batch gets 200 with only their 1 line (isolation, not leak); unknown id 404 (same shape).

---

## Checklist results

1. **GetMakerPayoutDetail IDOR — PASS.** See headline.
2. **GetMakerPayouts / GetMakerOutboxEvents IDOR — PASS.** Both maker-scoped twice (handler resolves makerId from session via `IMakerRepository.GetByUserIdAsync`; EF projection re-filters on `o.MakerId == makerId`). List slice is `GROUP BY o.PayoutBatchId WHERE o.MakerId == makerId` (per-maker SUM/COUNT, not batch total). Outbox: `ownsOrder` gate (`o.Id == orderId && o.MakerId == makerId`) → empty page for cross-maker/unknown (no oracle, no 403); `MakerOutboxEventDto` is `EventType + derived Status + OccurredAt` only — NO `PayloadJson`/`LastErrorCode`/`RetryCount`. Test asserts body contains no `payloadJson`, no `lastErrorCode`, no customer email; cross-maker order → totalCount 0.
3. **T-0112a fee-invoice download — PASS.** `IInvoiceRepository.GetForMakerReadOnlyAsync(invoiceId, makerId)` — `i.Id == invoiceId && i.MakerId == makerId` predicate is the shield. Fee-type gate (`invoice.Type != Fee` → 404) fires BEFORE the blob read. Cross-maker, unknown, and Customer-invoice-via-Fee-route all return identical `order.notFound` 404 (test `GET_fee_invoice_404_paths_are_oracle_free` pins all three). `private, no-store` + ETag/304; filename header escaped (`EscapeFilenameForHeader`); range processing off.
4. **CSV non-exposure invariant — PASS.** Bank CSV (carrying every maker's bank account) is served only by `Web.Admin/PayoutBatchesController::DownloadCsv` (`GET {id}/csv`, admin audience). The generated maker client (`maker-api.v1.ts`) exposes only `/payout-batches` (list) and `/payout-batches/{batchId}` (detail) — no `/csv`. The `payouts-client.ts` helper documents "NO CSV affordance anywhere" and a payload carrying "NO bankReference and NO CSV reference." No maker route/UI/helper touches the CSV anywhere in the diff. Maker DTOs (`MakerPayoutListItemDto`, `MakerPayoutDetailDto`) carry no `BankReference` / CSV reference.
5. **MarkPayoutBatchCompleted authz — PASS.** Admin host + admin audience `[Authorize]` (a customer/maker JWT cannot replay). Fail-closed session check FIRST (L121-125, RefundOrder precedent — settlement never "system"). `IAdminAuditableCommand` (`ActionCode payoutBatch.complete`, audit row commits in the same UoW). `BankReference` length-bounded to `PayoutBatch.MaxBankReferenceLength` (clean 400, not a 500). Idempotent silent-success on an already-Completed batch (no re-transition / re-email / second audit row). **Injection:** BankReference is NOT rendered into any email (it is absent from the payout-sent substitution map); it is stored on the batch and surfaced only via the admin audit `Notes` (admin-only consumer). Email free-text injection is already neutralized platform-wide via `NeutralizePlaceholderSyntax` for admin-authored fields; BankReference does not enter that surface.
6. **payout-sent email — PASS.** Recipient `toAddress: payload.MakerEmail` resolved at enqueue from `makerUser.Email` for the order's own maker. `total_paid` = `MakerTotalPaidMinor` (the per-maker accumulator slice, not the batch total). Deep link `/dashboard/maker/vyplaty/{batch.Id}` is the maker's own batch. One email per distinct maker. No cross-maker fields in the payload.
7. **Fee invoice content — PASS.** Platform→maker document. Recipient = maker (`CompanyName` / maker email / `RegistrationNumber` / `VatId`). Line items are `(OrderNumber, PlatformFeeAmountMinor)` only. No customer name/email/address/phone flows into the Fee PDF.
8. **Frontend — PASS.** `(maker)` route group + SSR audience-cookie forwarding enforce the maker session; `Unauthorized` → redirect to `/login`; detail `notFound()` is one shape (no IDOR oracle). No bank data rendered — only the maker's per-order money breakdown via `formatCzk` (no client math). Download goes through `downloadFeeInvoice` → `apiFetch` blob helper (carries the audience cookie), never a raw `<a href>` to blob storage. No CSV affordance.

---

## Findings by severity

- **BLOCKER:** none.
- **HIGH:** none.
- **MEDIUM:** none.
- **LOW / observations:**
  - The detail header (`PayoutQueries` L109-113) loads unconditionally after the `anyForMaker` gate. Safe today (the gate precedes it and the header carries no cross-maker money), but if a future edit reorders or removes the gate the header read becomes a (low-value) existence oracle. Defensive only — no action required this gate.

No new security pattern emerged; `NeutralizePlaceholderSyntax` (email free-text) and the read-only-mirror IDOR predicate are both pre-existing documented patterns reused correctly.
