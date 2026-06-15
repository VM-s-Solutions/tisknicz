# Gate 9 + QA audit — debt-codification bundle (T-0125)

- **Branch:** `chore/debt-codification-bundle` (6 T-0125 commits over the
  admin-ops merge `8788e41`)
- **Auditor:** QA
- **Date:** 2026-06-15
- **Verdict:** **PASS** — Gate 9 clean (exit 0, 145 tracked); baseline
  unperturbed by T-0125; both hard gates proven to bite and refuse
  grandfathering; all 11 AC traced. One working-tree hygiene fix applied (a
  stale gate-regression probe), one non-blocking nuance reconciled.

## Task 1 — Gate 9 self-verification (script-modifying bundle)

### Run
`node scripts/check-consistency.mjs` → **exit 0**, `clean (145 tracked)`,
**0 NEW** findings (incl. T8/T9). JSON: `NEW 0 TRACKED 145`. Matches the
expected 145 (the partial index is non-unique → no tracked T-row; the script
changes add no tracked row; T8/T9 are hard-fail, green today).

### Baseline-unperturbed confirmation
The task premise was "baseline UNCHANGED vs master." `git diff master..branch`
shows **+20 rows**, which I reconciled:

- The +20 rows are **all T1**, and all originate from **un-merged prior
  bundles** (Admin/, Payouts/, CountryConfigurations/, Outbox/ feature files)
  carried in this branch's history — added by commits `2bc3274`, `8bd496c`,
  `d0dc0b7`, `23d405e`, NOT by any T-0125 commit.
- Diffing the baseline across the **T-0125 commit range only**
  (`2c26e61^..branch`) → **no changes**. The script-modifying commit `281604f`
  does not touch the baseline file.
- Running the check at the **pre-T-0125** commit `2bc3274` already reports
  `145 tracked, clean`. So the T1–T7 baseline is **identical before and after**
  the T-0125 work — genuinely unperturbed by this bundle.
- **0 T8/T9 rows** appear in the baseline (hard rules are never baselined),
  and **no T1–T7 rows were removed or edited**.

### --update-baseline still works for T1–T7 grandfathering
Negative-case proof (with a temp unkeyed BEM code present): `--update-baseline`
wrote **145** findings — NOT 146 — i.e. it grandfathered the soft T1–T7 rows
and **excluded the hard T8 finding**. A subsequent plain run **still exited 1**
on the T8 finding, and the baseline file gained **0** T8 rows. The legitimate
soft-rule grandfather mechanism is intact; the hard rules are immune to it.

### Hard-gate bite (T8 + T9)
- **T8:** unkeyed + unallowlisted `BusinessErrorMessage` const → T8 finding,
  exit 1. `--update-baseline` cannot suppress it (above). Keyed OR allowlisted
  → satisfied.
- **T9:** named `.IsUnique().HasDatabaseName("x")` with no translator key + no
  marker → T9 finding, exit 1. With a `// no-translator:` marker → clean. With
  a translator entry (the real `ux_disputes_order_open`) → clean. EF-auto-named
  (no `HasDatabaseName`) → out of scope, clean.

## Task 2 — QA findings per AC

- **AC-1/2 (T8):** verified — fires + exit 1; hard, not baselineable; 70-seed
  allowlist yields 0 on master.
- **AC-3/4 (T9):** verified — fires for named-unmapped-unmarked; auto-named out
  of scope; 5 markers present (OrderConfiguration ×2, InvoiceConfiguration ×2,
  MakerConfiguration ×1).
- **AC-5 (dispute):** `OpenDisputeConcurrencyTests` asserts loser →
  `OrderInvalidTransition` / `ErrorType.Conflict` (409, not 500), exactly one
  OPEN dispute survives, order state rolls back. `ux_disputes_order_open`
  mapped in `UniqueConstraintTranslator` (line 97).
- **AC-6 (Q-0013):** 0 `/auth/login` refs in frontend source; 4 api-client
  `/api/v1/auth/login` refs preserved; targets resolve to `/login`.
- **AC-7 (Q-0019):** `ix_orders_payout_unclaimed` non-unique partial on
  `(state, payout_batch_id)` filter `state='Delivered' AND payout_batch_id IS
  NULL AND is_active`; no translator entry (T9 N/A); `ix_orders_state`
  preserved. Scan predicate (`OrderRepository` ~251–256) matches the index.
- **AC-10 (docs):** recurring-findings #2/#3 → `codified-in-script` (ruleT8 /
  ruleT9); checklist §J lists T8 + T9 (both hard, never baselined).

## Findings

- **F1 (fixed, hygiene):** the working tree carried an **uncommitted** stale
  probe `builder.HasIndex(m => m.Slug).IsUnique().HasDatabaseName("ix_temp_t9")`
  in `MakerConfiguration.cs` — a leftover from a prior gate-regression test that
  was never reverted. Not committed (won't ship) but it makes a live
  `check-consistency` run report a phantom T9. **Reverted** via
  `git checkout --`. The committed bundle is clean. Recommend the implementer
  confirm `git status --short` is clean before opening the PR.
- **F2 (non-blocking, documented):** T9's `// no-translator:` marker binds to
  the **next** `HasIndex` only; injecting a line between a marker and its index
  severs the pairing (surfaced as a test-harness artifact when probing
  mid-chain). This is correct script behavior, but I documented it in the test
  plan (gate-regression §H + §C guidance: inject at end of `Configure`) so a
  future re-verifier does not mistake the artifact for a regression.
- **F3 (informational):** `git diff master..branch` baseline +20 rows are prior
  un-merged bundles, not T-0125 (reconciled above). Will land on master
  legitimately when this branch merges.

## Deliverables
- Test plan: `docs/test-plans/T-0125.md` (16 TCs + gate-regression §H).
- This audit note.
- No product code written; one working-tree hygiene revert (F1).
