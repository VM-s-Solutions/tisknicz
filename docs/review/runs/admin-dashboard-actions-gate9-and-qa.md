---
run: admin-dashboard-actions-gate9-and-qa
branch: feat/admin-dashboard-actions
tickets: [T-0118b, T-0118c]
role: Tester (QA)
date: 2026-06-15
---

# Gate 9 + QA — T-0118b + T-0118c (admin-dashboard-actions)

PR 2 of the admin-frontend split (b order-actions → c ops/control-plane).
Frontend-only, read-only NSwag consumer (no `lib/api-client/` edit, no regen).
5 commits: `28ecc59` (b route+link) · `374af2f` (b modals/dispute) · `a565d7e`
(c outbox+country) · `446c11e` (c payout+invoice+tiles) · `9f0d022` (c delete-user).

## Task 1 — Gate 9 (T8/T9 consistency)

- `node scripts/check-consistency.mjs` → **exit 0**, **147 tracked** (baseline).
  GREEN.
- **T8 (i18n-parity): zero violations on the new admin string surface** — the
  biggest admin-UI string surface to date. Verified key blocks present:
  - `dashboard.admin.orderActions.*` (T-0118b — refund/state/dispute modals +
    audit panel + degraded/notFound headers).
  - `dashboard.admin.ops.*` (T-0118c — `ops.outbox.* / ops.country.* /
    ops.payout.* (vyplaty) / ops.users.* / ops.error.*`) +
    `dashboard.admin.overview.ops.*` (the re-wired count tiles).
  Exit 0 ⇒ T8 found no missing cs-CZ key across all surfaced backend codes
  (`payment.refund.*`, `order.manualTransition.*`, `order.dispute.*`,
  `outbox.*`, `country.*`, `payoutBatch.*`, `user.*`). **Confirmed.**
- **Baseline diff:** the 5 T-0118 commits (`28ecc59^..HEAD`) touch
  **zero lines** of `docs/audits/consistency-violations.md`
  (`git log 28ecc59^..HEAD -- docs/audits/consistency-violations.md` → empty;
  `git diff` → no change). **UNCHANGED vs the pre-T-0118b baseline, as expected.**
  (The 22-line diff vs `master` is from earlier-merged tickets T-0100/0102b/0103/
  0126 — not this slice; pre-T-0118b baseline commit = `ca2be12`.)

**Gate 9 verdict: PASS.**

## Task 2 — QA plans

- `docs/test-plans/T-0118c.md` — **exists** (implementer-authored), thorough:
  25 TCs across outbox / country-config / payout / delete-user + cross-cutting,
  with the 4 contract-gap callouts (no outbox list, no payout list, no country
  GET, no user-lookup) flagged as backend follow-ups, not bugs. **Accepted.**
- `docs/test-plans/T-0118b.md` — **was absent → written.** 22 TCs +
  4 risk-targeted edge cases, AC-1..AC-12 traceability, money-math priority on
  the refund path, state-machine coverage, the audience-replay security negative,
  responsive 375/768/1280.

### Code-level verification done while writing the plans
- **Type-to-confirm EMAIL exclusivity holds** — `confirmEmail === userEmail`
  appears ONLY in `users/delete-user-panel.tsx`; refund/state modals use the
  `submitting`/`busy` disabled-button lock, no email retype. Confirmed by grep.
- **Refund money-grade lock present** — `order-actions.tsx:172` `submitting`
  state, `busy = submitting || isRefreshing` gates the submit (double-click
  blocked).
- **Post-payout-ack gate** — `requiresAck = state === OrderState.Completed`
  (`order-actions.tsx:174`); checkbox renders + `ackValid` blocks submit only
  when Completed. Backend re-validates (plan exercises the forced-call path).
- **Order-row is a real Link** — `orders/order-row.tsx:50`
  `/dashboard/admin/orders/${encodeURIComponent(item.orderId)}` (slice-a deferral
  closed; detail route resolves, no 404).
- **Delete-user backend authority** — verdicts surfaced at
  `delete-user-panel.tsx:188-191` (`user.cannotDeleteWithInFlightOrders`,
  `user.notFound`). FE email match is case-sensitive exact (`===`, no
  normalization) — backend is authoritative; plan covers both.
- **Country-config no-GET** — `country-config-form.tsx` has no GET/prefill/
  `useEffect` config load (the operator re-enters the full editable set). The
  no-pre-fill hazard is documented in the T-0118c plan.

## Gaps / findings (no blockers)

1. **Delete-user (T-0118c) coverage — adequate, with one residual risk to watch.**
   The FE type-to-confirm is **case-sensitive exact** (`confirmEmail === userEmail`,
   no `toLowerCase`). If the displayed email and the operator's known email differ
   only in case, the button stays disabled — a usability snag, NOT a safety hole
   (it fails closed). The backend mismatch check is authoritative and the plan
   pins both the UX block and the backend verdict. **No fix required; flagged for
   the Reviewer's awareness.**
2. **Country-config no-GET (T-0118c) — covered as a documented contract gap, not a
   bug.** With no server pre-fill the operator enters the full editable set every
   save; a partial edit silently re-writes unedited fields to whatever is typed.
   The architect-flagged re-enter-all-fields hazard is documented in the T-0118c
   plan (§Contract-gap + §2). The clean fix is a `GetCountryConfiguration` read —
   a backend follow-up, correctly out of scope for this frontend bundle.
3. **Manual-transition allow-list is intentionally backend-only** — the dropdown
   offers all non-current states; the plan drives a policy-rejected pair to assert
   the named-command Czech string surfaces. No client-side state machine (correct).

## Verdict
Gate 9 GREEN (exit 0 / 147, T8 zero-violation, baseline UNCHANGED). Both QA
plans in place (T-0118b authored, T-0118c accepted) covering 47 manual TCs +
risk edges. No blocking findings. Manual execution pending the Vercel preview;
Reviewer approves.
