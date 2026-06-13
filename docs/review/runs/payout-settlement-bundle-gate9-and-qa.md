# Payout-settlement bundle — Gate 9 consistency + QA plan authoring

**Date:** 2026-06-13
**Author:** Tester (QA)
**Branch:** `feat/order-cleanup-bundle` (payout-settlement work)
**Tickets:** T-0103, T-0112, T-0112a (+ existing T-0116 plan audited)

## Task 1 — Gate 9 consistency

- `node scripts/check-consistency.mjs` → **exit 0**, **133 tracked**
  (`check-consistency: clean (133 tracked).`).
- `git diff master -- docs/audits/consistency-violations.md` →
  **+8 insertions, 0 deletions**, all under `Features/Payouts`, all
  `T1 — feature file must declare a public static class wrapper`:
  - `Features/Payouts/CreatePayoutBatch.cs:1:T1`
  - `Features/Payouts/GenericPayoutCsvFormatter.cs:1:T1`
  - `Features/Payouts/GetMakerOutboxEventsForOrder.cs:1:T1`
  - `Features/Payouts/GetMakerPayoutDetail.cs:1:T1`
  - `Features/Payouts/GetMakerPayouts.cs:1:T1`
  - `Features/Payouts/IPayoutArtifactService.cs:1:T1`
  - `Features/Payouts/MarkPayoutBatchCompleted.cs:1:T1`
  - `Features/Payouts/PayoutArtifactService.cs:1:T1`
- Nothing outside `Features/Payouts` added or removed.

### Discrepancy with the briefed expectation (surfaced, not blocking)

The task briefed **exit 0 at 133 (129 + 4)**. The arithmetic does not
hold against **master**, and the 133 figure is correct for a different
reason:

- **master baseline = 125 tracked**, with **zero** `Features/Payouts`
  entries (the payout-core bundle that introduced the first 4 Payouts
  files is **not yet on master** — verified `git ls-tree -r master` lists
  no `Features/Payouts` files).
- This branch carries **all 8** Payouts feature files (4 from payout-core
  — `CreatePayoutBatch`, `GenericPayoutCsvFormatter`,
  `IPayoutArtifactService`, `PayoutArtifactService` — plus 4 new from
  settlement/queries — `MarkPayoutBatchCompleted`,
  `GetMakerOutboxEventsForOrder`, `GetMakerPayoutDetail`,
  `GetMakerPayouts`).
- So **125 + 8 = 133**, delta **+8 vs master**, not +4 vs 129. The "129 +
  4" framing assumed payout-core (129) was the baseline; master is still
  at 125. The end count (133) and exit code (0) are correct; only the
  delta framing in the brief was off.

All 8 are confirmed **legitimate false-positives**. Each file DOES declare
the required `public static class` wrapper (or is a pure interface/service
the T1 heuristic does not model): `MarkPayoutBatchCompleted`,
`CreatePayoutBatch`, `GetMakerPayouts`, `GetMakerPayoutDetail`,
`GetMakerOutboxEventsForOrder` are well-formed one-file features with the
post-PR-#38 globally-unique `…Response` record (named, not nested, so the
T1 nested-`Response` probe misfires); `IPayoutArtifactService` is a domain
interface + result record; `PayoutArtifactService` is a service class;
`GenericPayoutCsvFormatter` is a pure formatter. Source read to confirm.

**Verdict: PASS.** Exit 0, 133 tracked, +8 vs master are exactly the 8
Payouts T1 false-positives, nothing else. (Note the brief's "129 + 4" is a
baseline-framing slip; the true math is 125 + 8.)

## Task 2 — QA plans authored (committed NOTHING)

| Plan | Manual TCs | Automated must-cover groups | Edge | Regression |
|---|---|---|---|---|
| `docs/test-plans/T-0103.md` | 14 (TC-1..13 incl. TC-7b) | 3 (PayoutBatchComplete red-first domain, handler, integration) | 5 | 3 |
| `docs/test-plans/T-0112.md` | 14 | 4 (3 handler files + cross-maker integration) | 5 | 3 |
| `docs/test-plans/T-0112a.md` | 10 (TC-1..9 incl. TC-4b) | 2 (controller unit, integration) | 5 | 3 |

All three follow the T-0105 format (front-matter, scope, preconditions,
manual case table, automated/tdd must-cover with per-AC verification, edge
cases, regression spot-checks, defects). Every AC maps to ≥1 case; the
IDOR/cross-tenant cases are called out explicitly:

- **T-0103** centres money correctness: mark-paid happy (batch + all
  orders Completed, bank ref + date stored, N one-per-maker emails, audit
  row), idempotent re-call (Silent-Success, first facts retained, no
  second email/audit), multi-maker grouping (3 makers → 3 emails, correct
  per-maker totals), single-maker dedup, NotProcessing 409, blank/oversize
  bank ref 400, not-found 404, **reconciliation (Σ per-maker totals ==
  batch total)**, atomicity rollback, admin-only 401/403.
- **T-0112** centres IDOR-twice: list paged + sort + empty; detail per-
  order breakdown reconciles; **cross-maker IDOR on detail (maker B on
  maker A's batch → only B's lines, unknown == cross-maker == same 404)**;
  outbox events maker-scoped + payload-free; **cross-tenant outbox →
  empty page (not 403)**.
- **T-0112a** centres the type gate + ownership: maker-owned Fee happy,
  **cross-maker 404**, **Customer-invoice-via-route 404 (Type==Fee gate
  before blob read)**, null-PDF/blob-miss `notYetRendered`, ETag/304.

### Reduced-parallelism note (known-env, documented per Preconditions)

All three plans' Preconditions document the Testcontainers Postgres
connection-ceiling workaround on busy boxes:
`dotnet test --filter "<suite>" -- xUnit.MaxParallelThreads=2`. This is
recorded as an **environment workaround, NOT a bundle defect** — the
integration suites are correct; the limit is the host's connection pool
under full parallelism.

## Existing T-0116 plan audit (AC coverage)

`docs/test-plans/T-0116.md` (maker payout dashboard frontend, Playwright-
style against the Vercel preview, no backend change) was audited against
its surface:

- **Covered well:** list rows + DESC-by-date server sort, the two-value
  state badges (`Připravujeme`/`Vyplaceno`, no `Pending` — matches the
  T-0103/T-0112 two-state enum lock), pagination + junk-param clamping,
  empty + error states, detail per-order breakdown via `formatCzk` with a
  reconciliation eyeball (product − fee + shipping == net; Σ == total),
  `notFound()` for foreign batch ids, Fee-invoice download via the blob
  helper + failure alert + absent-when-null CTA, the **CSV-absence grep
  gate (AC-10)**, responsive 375/768/1280, and the hygiene gate.
- **Gaps surfaced (for the T-0116 owner, not blocking this backend
  bundle):**
  1. **No per-order events drawer / outbox-events UI coverage.** T-0112
     ships `GET /orders/{orderId}/events` (US-maker-0017); if T-0116
     renders an events drawer, the plan should add a case asserting the
     derived status badges + the payload-free contract (no
     customer-PII/payloadJson on the wire). If the drawer is a later
     ticket, note it explicitly as out-of-scope.
  2. **Per-maker-slice trust not asserted at the UI.** The plan reconciles
     figures internally but does not assert the displayed total is the
     **per-maker slice**, never the cross-maker `TotalAmountMinor` — worth
     one case cross-checking the rendered total against the API
     `MakerTotalPaidMinor` so a future API/UI drift is caught.
  3. **No tykání-vs-vykání spot-check beyond the header note.** The plan
     says "tykání throughout" but has no explicit case verifying a sampled
     string (e.g. the empty state `Zatím nemáš žádné výplaty`) is T-form.
  These are enhancements; the plan is otherwise AC-complete for the
  list/detail/download surface it owns.

## Notes
- The 8 Payouts T1 entries are pre-acknowledged false-positives; not new
  violations, not blocking.
- Implementation read to anchor AC-to-test mapping: `MarkPayoutBatchCompleted.cs`,
  `GetMakerPayouts.cs`, `GetMakerPayoutDetail.cs`,
  `GetMakerOutboxEventsForOrder.cs`, `PayoutArtifactService.cs`,
  `CreatePayoutBatch.cs`, `GenericPayoutCsvFormatter.cs`,
  admin `PayoutBatchesController.cs` (the `{id}/complete` action), maker
  `PayoutsController.cs`, maker `FilesController.cs` (the
  `invoices/{invoiceId}` Fee-download action), maker `OrdersController.cs`
  (the `/events` action).
- Endpoints confirmed: settle = `POST /api/v1/payout-batches/{id}/complete`
  (admin); maker list/detail = `GET /api/v1/payout-batches` +
  `/{batchId}`; events = `GET /api/v1/orders/{orderId}/events`; Fee
  download = `GET /api/v1/maker/files/invoices/{invoiceId}`.
- No separate `docs/test-plans/T-0103|T-0112|T-0112a.md` previously
  existed (tickets carry inline test plans); these three are net-new QA
  plans, authored to the T-0105 format. Committed nothing.
