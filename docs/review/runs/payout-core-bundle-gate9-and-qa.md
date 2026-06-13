# Payout-core bundle — Gate 9 consistency + QA plan authoring

**Date:** 2026-06-13
**Author:** Tester (QA)
**Branch:** `feat/payout-core-bundle`
**Tickets:** T-0101, T-0102a, T-0102b, T-0104

## Task 1 — Gate 9 consistency

- `node scripts/check-consistency.mjs` → **exit 0**, **129 tracked** (`check-consistency: clean (129 tracked).`).
- Expected 129 (125 prior baseline + 4 claimed T1 false-positives). **Match.**
- `git diff --stat master -- docs/audits/consistency-violations.md` → `1 file changed, 4 insertions(+)`, 0 deletions.
- Baseline diff content — the +4 entries are exactly the new `Features/Payouts` files, all `T1 — feature file must declare a public static class wrapper` (a known false-positive: the linter does not recognise the static-class-wrapper shape these files DO use):
  - `Features/Payouts/CreatePayoutBatch.cs:1:T1`
  - `Features/Payouts/GenericPayoutCsvFormatter.cs:1:T1`
  - `Features/Payouts/IPayoutArtifactService.cs:1:T1`
  - `Features/Payouts/PayoutArtifactService.cs:1:T1`
- Nothing else added or removed. **Verdict: PASS.**

## Task 2 — QA plans authored (committed NOTHING)

| Plan | Manual TCs | Automated must-cover groups | Edge | Regression |
|---|---|---|---|---|
| `docs/test-plans/T-0101.md` | 10 | 4 (PayoutBatch entity, Order set-once claim, number generator, T-0102a-ride integration) | 4 | 3 |
| `docs/test-plans/T-0102a.md` | 14 | 3 (PayoutEligibility red-table, handler, integration) | 5 | 3 |
| `docs/test-plans/T-0102b.md` | 14 | 3 (CSV golden formatter, artifact service, integration) | 6 | 4 |
| `docs/test-plans/T-0104.md` | 10 | 1 (Function response-branch interpretation) | 4 | 2 |

All four follow the T-0105 format (front-matter, scope, preconditions, manual
case table, automated/tdd must-cover, edge cases, regression spot-checks,
defects). AC traceability noted per case. Preconditions state admin account,
seeded Delivered orders with/without bank accounts and with/without partial
refunds, blob + QuestPDF on preview, Comgate N/A.

## Coverage gap found (surfaced for Reviewer — QA does not approve)

**CSV formula injection not neutralized (T-0102b, candidate DEFECT).**
`GenericPayoutCsvFormatter.BuildMessage` writes the maker-controlled company
name into the `message` column verbatim, with no neutralization of cells that
begin with `=`, `+`, `-`, `@`, tab, or CR. The CSV is the bank-upload file an
operator opens in a spreadsheet, so a maker whose company name starts with `=`
yields an executable formula (CSV/formula injection). No automated test covers
the case. Logged as the T-0102b plan's TC-12 + an explicit "GAP" note on the
formatter must-cover row and a Defects-found entry. Recommended fix:
formatter-level neutralization (prefix `'`) + a red-first test.

## Notes

- The 4 Payouts T1 entries are pre-acknowledged false-positives carried into
  the baseline; they are not new violations and do not block.
- Implementation reviewed to anchor AC-to-test mapping: `CreatePayoutBatch.cs`,
  `PayoutArtifactService.cs`, `GenericPayoutCsvFormatter.cs`,
  `PayoutBatch.cs`, `PayoutBatchNumberGenerator.cs`,
  `RunWeeklyPayoutBatchFunction.cs`, `PayoutBatchesController.cs`.
- Batch number format confirmed `VYP-{CC}-{YYYY}-W{ww}` (e.g. `VYP-CZ-2026-W24`),
  VS digit extraction `202624`.
