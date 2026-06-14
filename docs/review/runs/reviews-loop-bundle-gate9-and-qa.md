# reviews-loop-bundle — Gate 9 + QA authoring audit

Date: 2026-06-14 · Branch: `feat/order-cleanup-bundle` · Role: Tester (QA)
Tickets: T-0100 (backend), T-0115 (customer UI), T-0117 (maker UI)

## Task 1 — Gate 9 consistency

- `node scripts/check-consistency.mjs` → **exit 0**, `check-consistency:
  clean (138 tracked)`. Matches the expected 138 (133 working baseline +
  5 claimed Reviews T1 false-positives).
- **Baseline audit (vs master).** `git diff --no-index` of
  `docs/audits/consistency-violations.md` (current) vs `master` shows
  **+13 lines, −0**: the +5 Reviews T1 entries AND +8 Payouts T1 entries.
  Reconciled:
  - master baseline file = **125**; merge-base
    (`f0a07e2`) = **125**, with **no** Reviews and **no** Payouts feature
    files present.
  - The **8 Payouts** T1 entries entered earlier on this branch line via
    T-0102a (`feat(T-0102a): CreatePayoutBatch ...`, commit `0ac6c2b`) —
    a separate bundle absorbed into the **133** working baseline
    (125 + 8). They are NOT part of the reviews loop.
  - The **5 Reviews** T1 entries entered via the reviews loop
    (`feat(T-0100): Review entity ...`, commit `8bd496c`):
    `GetCustomerReviewableOrders`, `GetCustomerSubmittedReviews`,
    `GetMakerReceivedReviews`, `RespondToReview`, `SubmitReview` — all the
    same T1 "feature file must declare a public static class wrapper"
    false-positive that every existing `public static class X` feature
    file already trips (the checker does not recognize the static-class
    wrapper idiom). 133 + 5 = **138**.
- **Verdict: CLEAN.** The +5 Reviews entries are exactly the five new
  `Features/Reviews/*.cs` files and nothing else; no new T3/T4/T5
  violations (no inline error strings, no `SaveChangesAsync` in handlers,
  no `dynamic`) were introduced by the reviews loop. The 8 Payouts entries
  are pre-existing-on-branch, not a reviews-loop regression.

## Task 2 — QA plans authored (committed: NOTHING)

- `docs/test-plans/T-0100.md` — backend. **14 manual TC** + 10 automated
  must-cover rows + 5 edge + 3 regression. Covers: submit happy
  (persist + atomic stat + row-count==rating_count reconcile),
  per-order uniqueness, non-delivered gate, rating out-of-range (0/6),
  body >1000, cross-tenant submit → 404, recompute self-healing after
  soft-delete (admin-N/A — DB-level deactivation), reply happy +
  overwrite, cross-tenant reply → 404, reply >500, host/audience
  enforcement, bp-rounding reconcile.
- `docs/test-plans/T-0115.md` — customer UI manual checklist (T-0105/T-0116
  format). CTA-eligibility three states, star-required gating, body
  counter@1000, submit→read-only re-sync, existing-review read-only,
  maker-reply render, responsive 375/768/1280, i18n (vykání) + 5 error
  parity keys, hygiene gate.
- `docs/test-plans/T-0117.md` — maker UI manual checklist. List +
  pagination, reply form per review + overwrite, aggregate header (avg +
  count from maker field, page-2 invariant + grep-no-`items`-math),
  empty state, **customer-identity-absent (GDPR) assertion** (no
  email/full name in DOM/payload), responsive, i18n (tykání), hygiene
  gate.
- Preconditions in all three: customer + maker accounts, seeded
  `Delivered` order between them, reduced-parallelism note (Testcontainers
  concurrent double-submit + recompute serialization).

## Gaps / flags for Reviewer
- **Checker T1 blind spot (cosmetic, not a defect):** the 5 Reviews (and
  8 Payouts) T1 entries are the known false-positive on the
  `public static class` wrapper idiom — the canonical pattern. No code
  change warranted; baseline grew as expected. Worth a follow-up to teach
  the checker the static-wrapper shape so the baseline stops accreting
  noise.
- **Comgate/Resend N/A** — no external provider touched by the reviews
  loop (synchronous, no outbox/email at MVP).
- **No admin moderation endpoint** — T-0100 ships only the `Auditable`
  soft-delete hook; the recompute self-healing case (TC-9) is exercised
  via DB-level deactivation, not an admin command. Flagged as a precondition.
