---
id: T-0175
title: "Admin list infrastructure: one pagination, one filter pattern, resilient routes"
status: in_review
size: M
owner:
created: 2026-08-21
updated: 2026-08-21
depends_on: []
blocks: [T-0176]
user_stories: [US-admin-0002, US-admin-0009, US-admin-0013]
adrs: [0024]
phase: 8
manual_steps: []
security_touching: false
layers: [frontend, l10n]
---

# T-0175 — Admin list infrastructure + route resilience

## Context
Audit findings [ADM-H3, ADM-H4, ADM-M1, ADM-M2, ADM-M3, ADM-L1, ADM-L5, ADM-L7](../review/ux-functional-audit-2026-08-21.md).
The admin surface grew five parallel pagination implementations (drift already visible), one
list search that behaves unlike the other three filter bars, retries that wipe filters, two routes
that strand an expired session instead of redirecting to login, and two routes (kategorie + the
6-probe overview) with no loading/error boundary at all.

## Scope
- **One pagination component** (route path + base params in props) replacing the five copies,
  including the inline order-detail audit pager — page indicator everywhere (ADM-M1).
- **Makers search → native GET form** with history entries + a "Vymazat filtry" reset, matching
  orders/faktury/audit (ADM-M2).
- **Retry preserves params:** inline "Zkusit znovu" hrefs rebuilt from current searchParams on
  orders/faktury/audit (ADM-M3).
- **Session parity:** `Unauthorized → redirect('/admin/login?redirect=…')` on makers + kategorie
  (ADM-H3).
- **Boundaries:** `loading.tsx` + `error.tsx` for kategorie and the overview segment (or one
  shared pair at `dashboard/admin/`); own `error.tsx` for `orders/[orderId]` so copy stops
  describing the list (ADM-H4, L7).
- **Empty states:** filtered-to-zero distinguishes itself from "nothing exists" and offers an
  in-place reset link (ADM-L1).
- **pageSize/page hygiene:** honor `pageSize` uniformly, clamp page to sane bounds (ADM-L5).
- cs-CZ keys for new copy.

## Alternatives Considered
- **Lift the shared pagination into `components/ui/`** — yes if trivially generic; otherwise a
  shared `(admin)` component is acceptable — the public `Pagination` differs deliberately.
- **Middleware-level admin session guard instead of per-page redirects** — rejected: pages must
  keep per-fetch handling anyway (SSR fetch is where expiry surfaces).

## Out of scope
- Action feedback + modals (T-0176). Audit-trail correctness + cross-links (T-0177).

## Acceptance criteria
- **AC-1** One pagination implementation remains under `(admin)` (grep-for-absence of the five
  copies); order-detail audit pager shows "page X of Y".
- **AC-2** Given an expired session on makers or kategorie, when the page loads, then the admin is
  redirected to `/admin/login` with returnUrl (integration-style vitest per page).
- **AC-3** Given a filtered orders list page 3 with a transient failure, when retry is clicked,
  then the same filters + page are requested.
- **AC-4** Given a maker search, when submitted, then Back returns to the pre-search state and a
  reset affordance is visible.
- **AC-5** Given kategorie or overview loading slowly, then a skeleton renders; given an SSR
  throw, then the Czech error boundary with retry renders (forced-throw test).

## Technical notes
Copies inventoried in the audit: `orders/pagination.tsx`, `faktury/pagination.tsx`,
`audit/pagination.tsx`, `ops-pagination.tsx`, inline `AuditPagination`
(`orders/[orderId]/page.tsx:329-390`). Redirect precedent: `orders/page.tsx:83-84`.

## Files touched (expected)
- `frontend/src/app/(admin)/dashboard/admin/**` (pagination consolidation, makers, kategorie,
  overview, orders/faktury/audit retry links, new loading/error files)
- `frontend/src/lib/i18n/cs-CZ.ts`

## Test plan reference
`docs/test-plans/T-0175.md`

## Status log
- 2026-08-21 `draft → ready` (Phase 8 UX sweep plan; DoR checklist passed)
- 2026-08-22 `ready → in_progress`, branch `feat/T-0175-admin-list-infrastructure`
- 2026-08-22 `in_progress → in_review` — tsc clean, vitest 200/200 (+7 new); five pagination copies
  deleted; see [test plan](../test-plans/T-0175.md) + [review run](../review/runs/T-0175.md)
