# T-0127 — Gate 9 (consistency) + QA plan audit note

Branch `feat/admin-read-gaps` (8 commits). Read-only ticket: 4 thin
`Web.Admin` query features + 3 T-0118 FE re-wires. No migration, no command,
no new outbox event.

## Task 1 — Gate 9: `node scripts/check-consistency.mjs`

**Verdict: PASS. Exit 0 at 151 tracked.**

- Baseline (pre-T-0127, commit `ec1c5bc` — the parent of the first T-0127
  commit `61261e4`): **147 tracked**.
- HEAD (`ba593a7`): **151 tracked** → **+4**, all `T1` false-positives.
- Diff `consistency-violations.md` (baseline vs HEAD), `path:line:rule` only —
  the +4 are EXACTLY the 4 new admin query features, nothing else added,
  nothing removed:
  1. `Features/Admin/GetAdminOrderDetail.cs:1:T1`
  2. `Features/Admin/GetPayoutBatches.cs:1:T1`
  3. `Features/Admin/GetStalledOutboxEvents.cs:1:T1`
  4. `Features/CountryConfigurations/GetCountryConfiguration.cs:1:T1`

  These are the known `public static class wrapper` false-positive the checker
  raises on every one-file query feature (the whole `Features/**` tree carries
  it). Benign — the wrapper IS present in each file.

**T8 (error-code ↔ i18n parity): GREEN — zero new codes.**
- `GetCountryConfiguration` 404 reuses `BusinessErrorMessage.CountryConfigurationNotFound`
  (`Error.NotFound("countryCode", ...)`, line 76).
- `GetAdminOrderDetail` 404 reuses `BusinessErrorMessage.OrderNotFound`
  (line 49).
- `git diff ec1c5bc..HEAD -- '**/BusinessErrorMessage*.cs'` → no changes.
- `GetStalledOutboxEvents` + `GetPayoutBatches` use only `MinValue` (validator
  clamps) — no new code.

**T9 (unique-index ↔ translator parity): GREEN — zero new index.**
- `git diff ec1c5bc..HEAD` for `HasIndex` / `CreateIndex` / `IX_` → none.
- No migration touched on the branch.
- T8/T9 are hard findings that bypass the baseline; exit 0 ⇒ zero T8/T9.

## Task 2 — QA plan

`docs/test-plans/T-0127.md` written (NOT committed). **32 manual TCs** + a
10-row must-cover automated matrix, mapped to AC-1…AC-12.

Coverage of the load-bearing items:
- **AC-4/AC-5 fence removal (T-0118c):** TC-3 (VAT-only edit saves WITHOUT the
  modal — the fence removal), TC-4 (provider change → diff-gated modal fires),
  TC-5 (revert-to-loaded → diff clears → no modal — proves the gate diffs vs
  the LOADED config), TC-7/TC-8 (404 → blank form keeps the WARNING fence + the
  friction-preserving "any provider is a change" default). Grounded in
  `country-config-form.tsx` lines 167-175 (`anyProviderChanged` vs
  `loadedProviders`) + 238-248 (`handlePrimaryClick`).
- **Shared-predicate guarantee (AC-5 backend):** TC-19 (LIST returns only the
  stalled set) + TC-20 (LIST `totalCount` == the count tile). Verified in
  `OutboxConsumerRepository.cs`: both `CountStalledAsync` (line 54) and
  `GetStalledPagedAsync` (line 66) call the SAME
  `IOutboxConsumerRepository.StalledPredicate`. Integration test
  `GET_outbox_stalled_returns_only_the_stalled_set` seeds 2 stalled + 1 due +
  1 processed and asserts `TotalCount == 2`.
- **Delete-user pre-disable + backend-authoritative:** TC-15 (pre-disabled
  pre-call), TC-16 (enabled when clear), TC-17 (backend T-0110 gate still
  rejects if the FE is bypassed), TC-18 (`unknown` probe → NOT pre-disabled).
- **Cross-audience 401:** TC-27/TC-28 — all 4 reads + the in-flight filter;
  integration pins 401 on all four.

## Gaps / watch items
- No code defects found. The implementation matches the ticket §A locks:
  field-set parity, reused 404 codes, shared stalled predicate, Unscoped
  cross-maker payout list, no bank field on the payout LIST (CSV-only —
  flagged as TC-24 to verify on the preview), diff-gated provider modal.
- The QA plan's manual `Actual`/`Pass/Fail` columns are unexecuted — they run
  against the admin Vercel preview when the PR opens. The automated layer
  (handler + integration) is the standing proof for the must-cover rows.
- One verification deferred to preview execution: TC-24 (NO bank-account /
  IBAN field on the payout LIST DTO) — asserted by reading the
  `AdminPayoutBatchListItemDto` field list in the ticket; confirm on the wire.
