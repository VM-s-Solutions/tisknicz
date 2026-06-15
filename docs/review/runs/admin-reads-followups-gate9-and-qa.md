# Gate 9 + QA — T-0126 (admin-reads-followups)

**Branch:** `feat/admin-reads-followups` · **Ticket:** T-0126 · **Date:** 2026-06-15 · **Role:** Tester (QA)

## Gate 9 — consistency gate

**`node scripts/check-consistency.mjs` → exit 0, "clean (147 tracked)".** VERDICT: **PASS.**

### The +2 baseline audit (the load-bearing verification)

The raw `git diff master -- docs/audits/consistency-violations.md` shows **+22 / -0**
entries, NOT +2 — because the branch stacks the entire un-merged bundle chain
(payout-core → payout-settlement → reviews-loop → admin-ops → debt-codification →
admin-dashboard) on top of `master`. `master` sits at **125**; the branch tip at **147**.
The merge-base of the branch and `master` IS `master`'s tip (`f0a07e2`), so the diff
necessarily carries all of those prior bundles' baseline rows.

Isolating **T-0126's own delta** resolves the discrepancy with the expected framing:

- The only T-0126 commit that touches the consistency file is `01c0020`
  (`feat(T-0126): overview count reads`). Its diff to `consistency-violations.md` is
  exactly **+2 / -0**:
  - `…/Features/Admin/GetProcessingPayoutsCount.cs:1:T1  feature file must declare a public static class wrapper`
  - `…/Features/Admin/GetStalledOutboxCount.cs:1:T1  feature file must declare a public static class wrapper`
- Baseline **immediately before** `01c0020` (its parent) = **145**; **at** `01c0020` =
  **147**. So **147 = 145 + 2**, and the +2 are **exactly** the two count features —
  nothing else.
- The invoice-download commit `2ece9a9` touches the consistency file **0 times**
  (controller-direct stream, no new MediatR feature file → no T1 row).

Both +2 entries are **expected T1 false-positives**: the static-class-wrapper rule
fires on count features whose only nested type is a single `record …Response(int Count)`
— the rule can't see that shape as a valid one-file feature. They are correctly tracked,
not new genuine violations. **Audit: PASS — the +2 are precisely the two count features.**

### T8 (i18n parity) / T9 (unique-index translator)

- **T8 GREEN — zero new codes.** No `BusinessErrorMessage.cs` change across the three
  T-0126 code commits. The invoice 404 **reuses `InvoiceNotYetRendered`** (existing
  `invoice.notYetRendered` cs-CZ key), exactly per the ticket lock. The count endpoints
  have no failure mode (empty → `{ count: 0 }`, never 404).
- **T9 GREEN — zero new unique index.** No migration in the T-0126 commit range → no new
  `UniqueConstraintTranslator` entry required.
- **NSwag:** one regen commit (`3b87c2e`), **admin host only** (`admin-api.v1.ts` +
  `.spec-hashes.json`); customer/maker clients untouched by T-0126. (The working-tree
  `M` on `customer-api.v1.ts` / `maker-api.v1.ts` belongs to the prior un-merged bundle
  diff vs master, not to a T-0126 hand-edit.)

**Gate 9 verdict: PASS.** Exit 0 @ 147; the +2 vs the branch's pre-T-0126 baseline are
exactly the two count features; T8/T9 clean.

## QA

- Test plan written: `docs/test-plans/T-0126.md` — **15 manual TCs + 6 hygiene rows**,
  automated-case inventory, 7 edge cases, 5 regression spot-checks.
- AC traceability: AC-1…AC-4 (invoice: stream/byte-equal/headers, 404 no-row, 404
  null-path, 404 purged-race, ETag/304, customer+maker+unauth → 401) → TC-1…TC-8;
  AC-5 (Processing count, Completed excluded, empty→0) → TC-9…TC-11; AC-6 (stalled
  predicate) → **TC-12 (the load-bearing predicate-correctness case)** + TC-13; AC-7
  (count audience) → TC-14; AC-8 (hygiene/contract) → H-1…H-6.
- **TC-12** pins the exact predicate `ProcessedAt == null AND NextRetryAt == null AND
  LastErrorKind != None`, seeding one of each non-stalled shape (parked/due, acknowledged,
  processed, fresh) to prove only the genuinely-stalled row counts — acknowledged excluded
  by `ProcessedAt`, NOT a separate `AcknowledgedAt` clause.
- The branch's `AdminOverviewCountsIntegrationTests` already proves this predicate against
  real Postgres (2 stalled counted; due/fresh/processed/acknowledged excluded; +401s);
  `AdminInvoiceDownloadIntegrationTests` proves byte-equal stream + disposition +
  `private, no-store` + cross-host 401.

## Note — T-0118a unblock
T-0118a's overview tiles (Processing-payouts + stalled-outbox) and the faktury
"Stáhnout fakturu" button are now **UNBLOCKED** by these endpoints. The frontend
re-enable (wiring tiles + un-disabling the button) is a **separate small T-0118a FE
follow-up, NOT in scope** for T-0126 (backend-only bundle).

## Gaps / findings
- **No code defect found.** Endpoints match the ticket locks (controller-direct
  Unscoped invoice stream reusing `InvoiceNotYetRendered`; T-0064/T-0088 PII headers;
  exact stalled predicate; admin-audience 401s).
- **Process note (not a defect):** the task brief's "exit 0 at 147 = 145 + 2" is correct
  against the **branch's own pre-T-0126 baseline (145)**, but a reviewer running a raw
  `git diff master` will see **+22** because the branch carries the whole un-merged bundle
  stack atop `master`(125). The true T-0126 delta is the +2 count features (commit
  `01c0020`); documented above so the +20 prior-bundle rows aren't mistaken for T-0126
  scope creep at PR review.
