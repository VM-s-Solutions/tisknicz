# order-dashboards-bundle — Gate 9 consistency + QA-plan audit

- **Date:** 2026-06-12
- **Author:** Tester (QA)
- **Scope:** (1) Gate 9 consistency run + baseline re-key audit; (2) audit of
  existing test plans T-0086a/T-0086b; (3) authoring of the two missing maker
  plans T-0087a/T-0087b (maker implementer skipped them — deviation 6).

## 1. Gate 9 — consistency check

**Verdict: PASS.**

- `node scripts/check-consistency.mjs` → exit **0**, `clean (118 tracked)` —
  matches the expected 118.
- Baseline re-key audit (`git diff master...HEAD -- docs/audits/consistency-violations.md`):
  the diff is exactly **one line pair** —

  ```
  - backend/src/Makables.Web.Customer/Controllers/OrdersController.cs:284:T5
  + backend/src/Makables.Web.Customer/Controllers/OrdersController.cs:286:T5
  ```

  Same rule (T5), same file, same message, +2-line shift, count unchanged
  (118 → 118), **no new entries, no removed entries**. Source verified: line
  286 of `OrdersController.cs` is the same grandfathered
  `Error.Validation("file", code)` call (the inline `"file"` field name trips
  the T5 heuristic; the codes themselves are `BusinessErrorMessage` constants).
  The `--update-baseline` use was re-key-only. **Audit result: legitimate.**

## 2. QA-plan audit — existing plans

### T-0086a (`docs/test-plans/T-0086a.md`) — coverage 11/12 ACs ≈ 92 %

| AC | Case | Status |
|---|---|---|
| AC-1…AC-11 | TC-1…TC-11 (1:1) | Covered |
| (canonicalization note) | TC-12 junk `?state=Banana&sort=Nope` | Covered (extra) |
| AC-12 hygiene gate | — | **GAP: no row** (lint/build/check-consistency/no api-client diff) |

Findings:
- **Gap A1:** AC-12 (hygiene gate) has no test case. Low risk — CI/Gate 9
  cover most of it — but the plan should record it for traceability.
- **Discrepancy A2:** TC-11 expects redirect to `/login?redirect=…`; ticket
  AC-11 says `/auth/login`. Resolve at execution time against the actual auth
  route; whichever is real, plan and ticket should agree.
- Edge cases present (page > totalPages, single-day range, badge ≥ 10) — plan
  is not suspiciously all-happy-path.

### T-0086b (`docs/test-plans/T-0086b.md`) — coverage 13/13 ACs = 100 % (2 weaknesses)

| Asked verification | Case | Status |
|---|---|---|
| Confirm-delivery Shipped-only | TC-5 ("Button absent in all other states") | Covered |
| Invoice link iff non-null | TC-8 | Covered |
| Thread polling pause/resume | TC-11 (visible ~95 s / hidden / return / navigate away) | Covered |
| Mark-read badge clearing end-to-end | TC-9 | **PARTIAL** |

Findings:
- **Gap B1:** TC-9 starts from pre-seeded unread messages. The badge-APPEARS
  leg (post as maker → customer list badge increments → open detail → badge
  clears) is not exercised; TC-9 only proves the clearing half. The
  end-to-end round-trip is now pinned on the maker side instead
  (T-0087b TC-15, symmetric flow) — executing both gives full coverage of the
  counter symmetry, but T-0086b TC-9 should add the customer-side
  badge-appears step when executed.
- **Gap B2:** AC-13's hygiene sub-clause (lint/build/parity keys) has no row;
  TC-13 covers only the responsive part.
- Edge cases (SSR prefetch failure, two-tab polling, 120 s blob budget) and
  regression spot-checks (T-0084b surface, potvrzeni poller, badge zeroing)
  are solid.

## 3. Plans written by QA (deviation-6 remediation)

- **`docs/test-plans/T-0087a.md`** — NEW, 15 manual TCs + 5 edge cases +
  3 regression spot-checks. Covers all AC-1…AC-11: default-Nové tab +
  single-request-per-tab network proof, deep-link round-trip + back/forward,
  page reset on tab switch (stale-`page` trap), unread badges (3 vs 0/null),
  payout-not-gross column cross-check, DOM no-email/no-mailto grep,
  date/sort/junk-param clamps, three distinct per-tab empty states,
  375/768/1280, error state, hygiene gate. Cross-audience 401 in edge cases.
- **`docs/test-plans/T-0087b.md`** — NEW, 18 manual TCs + 5 edge cases +
  3 regression spot-checks. Covers all AC-1…AC-13: full state-action matrix
  (Paid→accept; Accepted×Zasilkovna→ship with confirm-dialog
  **cancel-issues-no-request** network proof; Accepted×PersonalPickup→
  handover, no-dialog, label-never; terminal-states zero buttons; two-tab 409
  conflict→refresh reconcile), label download (Shipped+carrier-ref gate,
  named PDF via blob helper, 503 `shipping.carrierUnavailable`, throttled
  big-PDF against the 120 s blob budget in edge cases), invoice iff non-null,
  attachments, payout/timeline, thread reuse (post/poll/visibility matrix),
  **mark-read end-to-end badge round-trip (TC-15)**, PendingPayment-guard
  verification framed as verify-or-record-N/A (TC-16), cross-audience 404/401
  (TC-2/TC-3), responsive, hygiene. Preconditions note **no new manual
  third-party steps** (Packeta/Comgate already listed on prior bundle plans)
  and the staging seed requirement: **Shipped orders with non-null
  `shippingCarrierRef`** for the label path.

## 4. Open items for the fold

1. T-0086a Gap A1 + discrepancy A2 (hygiene row; `/login` vs `/auth/login`
   redirect target) — fix at plan execution.
2. T-0086b Gap B1 (badge-appears leg) — add the maker-post step to TC-9 at
   execution; T-0087b TC-15 covers the symmetric maker-side flow.
3. T-0086b Gap B2 (AC-13 hygiene sub-clause row).
4. Staging seed dependency: label-download cases (T-0087b TC-9/TC-10) are
   blocked until Shipped orders with carrier refs exist on staging.
5. T-0087b TC-16 outcome (PendingPayment reachable on maker surface or not)
   should be recorded with proof either way — it pins the `canPost` guard
   contract for future thread consumers.

Nothing committed — fold agent owns the commit.
