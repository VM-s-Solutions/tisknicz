---
id: T-0173
title: "Customer + maker dashboard polish: failure states, filter-preserving retries, entity links, list ergonomics"
status: done
size: M
owner:
created: 2026-08-21
updated: 2026-08-21
depends_on: []
blocks: []
user_stories: [US-customer-0012, US-customer-0016, US-customer-0018, US-maker-0005, US-maker-0012, US-maker-0015]
adrs: [0024]
phase: 8
manual_steps: []
security_touching: false
layers: [frontend, l10n]
---

# T-0173 — Customer + maker dashboard list/detail polish

## Context
Audit findings [CUST-M1, CUST-L2, CUST-L4, CUST-L5, MAKER-M4, MAKER-L1–L5](../review/ux-functional-audit-2026-08-21.md).
Both dashboards share a family of small punishments: profile pages render raw backend
`error.message` with no retry and no login redirect (violating the project's own rule), error
retries wipe the user's filters, order surfaces never link to the product/maker they reference,
the dispute-escalation form sits permanently expanded contradicting its own intro, makers can't
filter terminal states or find `/dashboard/maker` at all, and a missing bank account is never
surfaced where payouts fail without it.

## Scope
- **Profile pages (both dashboards):** Unauthorized → login redirect with returnUrl;
  other failures → `resolveErrorMessage` + retry link (CUST-M1 + maker twin, one sweep).
- **Retry links preserve state:** customer + maker order lists rebuild the retry href from current
  searchParams; a backend Validation failure (inverted dates) labels the action "Vymazat filtry"
  instead (CUST-L2, MAKER-L2).
- **Entity links:** customer order list/detail link product → `/produkt/[id]` and maker → catalog
  profile (CUST-L4).
- **Dispute escalation:** collapse behind a "Reklamovat" disclosure (CUST-L5).
- **Maker orders list:** state dropdown on the "Vše" tab (backend already accepts `state`);
  client-side date-range swap/validate before pushing; out-of-range page clamps to the last page;
  `/dashboard/maker` gains a `page.tsx` redirect to `objednavky` (MAKER-L3–L5, L1).
- **Bank-account prompt:** `/vyplaty` (and payouts empty state) shows a warning banner when the
  maker profile has no bank account, linking the profile section; static copy explains the weekly
  payout cadence (MAKER-M4 + the copy half of MAKER-M3).
- cs-CZ keys; Chrome + WebKit at 375/768/1280.

## Alternatives Considered
- **Backend pseudo-state for "needs action" filtering** — rejected long ago (T-0081 lock);
  frontend state dropdown reuses the existing single-`state` param.
- **Auto-redirect on any profile fetch error** — rejected: only Unauthorized redirects; transient
  errors must keep the user in place with retry.

## Out of scope
- Accrued-payout balance + attention badges (need new reads — draft T-0179).
- Product-form/review-form feedback (T-0174).

## Acceptance criteria
- **AC-1** Given an expired session on either profile page, when it renders server-side, then the
  user is redirected to login with returnUrl; given a transient failure, then translated copy +
  retry render (never raw `error.message` — grep-for-absence + vitest).
- **AC-2** Given a filtered maker order list page 2 and a transient error, when "Zkusit znovu" is
  used, then tab/filters/page are preserved.
- **AC-3** Given a maker on the "Vše" tab, when they pick "Odesláno" in the state filter, then only
  Shipped orders render and the URL carries the state.
- **AC-4** Given `?page=99` on the maker list, when it loads, then the user lands on the last real
  page (clamp), not an empty box under a nonzero count.
- **AC-5** Given a maker with no bank account, when `/vyplaty` renders, then the warning banner
  links to the profile bank-account section.
- **AC-6** Given a Paid order thread, when the detail renders, then the escalation form is collapsed
  until "Reklamovat" is pressed (jest-axe clean on the disclosure).

## Technical notes
`profile/page.tsx:28-32`, `profil/page.tsx:33-36`, `objednavky/page.tsx` (both trees),
`order-tabs.tsx`, `dispute-escalation-client.tsx:123-169`, `vyplaty/page.tsx:84-114`,
`route-audience.ts:60`. Bank-account read uses the existing `GetMyMakerProfile` — no new contract.

## Files touched (expected)
- `frontend/src/app/(customer)/dashboard/zakaznik/**`, `frontend/src/app/(customer)/objednavka/[id]/*`
- `frontend/src/app/(maker)/dashboard/maker/**`
- `frontend/src/lib/i18n/cs-CZ.ts`

## Test plan reference
`docs/test-plans/T-0173.md`

## Status log
- 2026-08-21 `draft → ready` (Phase 8 UX sweep plan; DoR checklist passed)
- 2026-08-22 `ready → in_progress`, branch `feat/T-0173-dashboard-polish`
- 2026-08-22 `in_progress → in_review` — tsc clean, vitest 226/226 (+1 new); `/dashboard/maker`
  307-verified on the running app; CUST-L4 blocked on a contract change (neither order DTO carries
  productId/makerSlug — verified against the generated client) and moved to T-0179; MAKER-L3/L4
  deferred as LOW. See [test plan](../test-plans/T-0173.md) + [review run](../review/runs/T-0173.md)
- 2026-08-22 `in_review → done` — merged via PR #148 (merge e62284d; CI green)
