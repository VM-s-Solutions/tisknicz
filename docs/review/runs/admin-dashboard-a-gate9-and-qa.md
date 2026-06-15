# Audit note — T-0118a (admin dashboard slice a): Gate 9 consistency + QA plan

**Date:** 2026-06-15
**Author:** QA (Tester)
**Branch:** `feat/order-cleanup-bundle` (HEAD 85725f5)
**Scope:** Gate 9 consistency verdict + authored QA plan for T-0118a.

---

## Task 1 — Gate 9 consistency

### Command + result
`node scripts/check-consistency.mjs` →

```
check-consistency: clean (145 tracked).
EXIT:0
```

**Verdict: PASS.** Exit 0 at **145 tracked** baseline rows — matches the
task's expected count.

### T8 / T9 (hard-fail, never baselined) — GREEN on the new admin code
- T8 (BusinessErrorMessage ↔ cs-CZ i18n-key parity) and T9 (named unique index
  ↔ UniqueConstraintTranslator parity) bypass the baseline and hard-fail in
  place. Exit 0 ⇒ **both GREEN**.
- This is the **first bundle shipping behind T8/T9** (the gates landed in the
  prior debt-codification bundle, T-0125). The slice introduces a full admin
  i18n key set under `dashboard.admin.*` (vykání) in
  `frontend/src/lib/i18n/cs-CZ.ts` — login, shell/nav, overview tiles, all three
  list headers/empties/errors, the invoice-download tooltip. T8 passing confirms
  no admin `BusinessErrorMessage` code is missing a cs-CZ key. The slice adds no
  EF unique index, so T9 has nothing new to map — trivially GREEN.
- **T8 is documented in the QA plan as the i18n-coverage mechanism**: a missing
  admin key is a CI build break, not a runtime `[missing key]`.

### Baseline diff vs master — reconciled (NOT a regression)
`git diff master -- docs/audits/consistency-violations.md` shows **+20 rows**
(Admin GetAllOrders/GetAllInvoices/GetAdminAuditLog, plus Payouts, Reviews,
Users, CountryConfigurations, Outbox Retry/Acknowledge). At face value this
contradicts the "UNCHANGED" expectation. Root cause: **the local `master` ref
is stale** — it points at `f0a07e2` (PR #48, refund-dispute-bundle), the
merge-base of this branch. Five backend bundles merged onto the integration
line AFTER that ref was last fetched (payout-core #49, payout-settlement #50,
reviews-loop #51, admin-ops #52, debt-codification #53) and carry those baseline
rows. The Payouts/Reviews/Users/CountryConfig feature files do not exist on the
stale local master at all (2419 insertions in `git diff master`), confirming the
delta is cumulative prior-bundle work, not this slice.

**Correct comparison** — the T-0118a slice against its true parent
(`21c65e0`, the debt-codification merge):

```
git diff 21c65e0 HEAD -- docs/audits/consistency-violations.md   → EMPTY
```

**The T-0118a slice adds ZERO new baseline T-rows** — exactly as expected for a
read-only frontend slice (it adds no `.cs` feature file; the 3 Admin feature
rows in the `vs master` diff came from T-0111 in admin-ops bundle #52). The
slice's 34-file delta is frontend pages/islands/i18n + docs only.

**Action item for the operator:** `git fetch` / refresh the local `master` ref
before the next `vs master` baseline diff so the stale-ref artifact does not
recur in review.

---

## Task 2 — QA plan

Authored `docs/test-plans/T-0118a.md` in the T-0105 format (ID | Steps |
Expected | Actual | Pass/Fail). Replaced the implementer-authored checklist
with a structured plan; preserved the implementer's flagged backend follow-ups.

- **35 manual TCs** (TC-1..TC-35) + **4 hygiene rows** (TC-H1..TC-H4) = **39
  cases**, grouped: admin auth (9), overview (6), all-orders (9), all-invoices
  (5), audit-log (4), responsive/tone (2), hygiene (4).
- Plus 9 edge cases and 4 regression spot-checks.

### Findings surfaced in the plan (for Reviewer)
1. **Watch-1 — Phase-1 gate is cookie-presence only.** Both the edge
   `middleware.ts` admin branch AND the `(admin)/dashboard/admin/layout.tsx`
   server gate check only cookie PRESENCE, not JWT signature/audience (real
   validation is T-0027). The genuine cross-audience rejection ("a customer/maker
   JWT can't reach admin pages") is the **backend 401** on the SSR fetch, mapped
   by each page to a Server-Component `redirect('/admin/login')` — no flash,
   because the redirect fires before any admin HTML streams (TC-6). A
   forged/copied admin-named cookie value would pass the frontend gate; T-0027 is
   the closing dependency. Not a slice defect (Phase-1 contract is explicit in
   the layout doc-comment), but logged so it is not mistaken for full enforcement.
2. **customerEmail PII** is intentionally on the admin operator surface only —
   regression check added that the maker list still exposes none.
3. **Backend follow-ups** (from the slice, retained): admin invoice-PDF endpoint
   absent (download button disabled-with-tooltip — assert disabled, not broken);
   overview payout/outbox count endpoints absent (tiles render "—", not 0);
   orders-list date-range filter absent from the contract.

### Not executed here
Manual TCs run against the Vercel preview when the PR opens (this pass authored
the plan + ran the static gate). No product code written (tests/plan only). No
commit made.
