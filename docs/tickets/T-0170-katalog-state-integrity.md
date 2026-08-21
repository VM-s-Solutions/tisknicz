---
id: T-0170
title: "Katalog filter/pagination state integrity: URL sync, pending feedback, honest empty states"
status: ready
size: M
owner:
created: 2026-08-21
updated: 2026-08-21
depends_on: []
blocks: []
user_stories: [US-customer-0007, US-customer-0008]
adrs: [0024]
phase: 8
manual_steps: []
security_touching: false
layers: [frontend, l10n, optimizer]
---

# T-0170 — Katalog filter/pagination state integrity

## Context
Audit findings [PUB-H1, PUB-H2, PUB-H3, PUB-M3, PUB-M7](../review/ux-functional-audit-2026-08-21.md).
The filter sidebar is the user's only readout of "what am I filtering by", and it desyncs from the
URL on back/forward and on every reset/retry link; filter and pagination round trips give zero
pending feedback on a 1 s+ SSR (the "insanely slow" complaint compounds this); and three different
situations (empty catalog, filtered-to-zero, out-of-range page) all claim "nothing matches your
filter".

## Scope
- Filter panels (katalog + maker-profile products) derive state from the canonical URL: key the
  client component off the query (or sync-on-props-change) so back/forward/reset/landing links
  never show stale controls.
- `useTransition` around filter/pagination navigation; results grid dims + spinner while pending;
  pagination scroll fires on navigation completion, not click.
- One history policy for filters and pagination (push per meaningful change); fix the JSDoc/tests
  that claim back-restore under `replace`.
- Empty states branch: genuinely empty catalog ("zatím tu nikdo není"), filtered-to-zero (offer
  "Vymazat filtry"), out-of-range page (clamp or "go to page 1" + keep pagination reachable).
- Error-state "Zkusit znovu" retries the **current** URL (`router.refresh()`), preserving filters/page.
- Verified in Chrome and WebKit at 375/768/1280; before/after interaction latency noted (perf gate).

## Alternatives Considered
- **Client-side data fetching for filters to avoid SSR waits** — rejected: violates the
  server-first architecture; pending feedback is the correct fix for a fast-enough SSR.
- **Sessioning filter state outside the URL** — rejected: URL is the state container (B.8).

## Out of scope
- Backend query changes; catalog ranking; visual redesign of the sidebar.

## Acceptance criteria
- **AC-1** Given active filters, when the user navigates back/forward or clicks "Vymazat filtry",
  then every control reflects the URL exactly (vitest on the sync + manual proof).
- **AC-2** Given a filter change, when the SSR round trip is in flight, then the grid shows a
  pending state and the previous scroll position holds until data arrives.
- **AC-3** Given `?page=99` beyond the last page, when the page renders, then the user is clamped
  or offered page 1 — never the "no makers match your filter" copy with no way out.
- **AC-4** Given a transient catalog error on a filtered URL, when "Zkusit znovu" is used, then the
  same filtered request is retried (URL unchanged).
- **AC-5** Back undoes filter changes step-by-step (history policy test).

## Technical notes
`katalog/filters-client.tsx:100-133`, `katalog/page.tsx:168-288`, `pagination.tsx:62-91`,
`[slug]/product-filters-client.tsx:41-56`. Unify the two panels' incidental divergences (debounce
300 vs 400 ms, reset-button visibility) while touching them.

## Files touched (expected)
- `frontend/src/app/(public)/katalog/{filters-client.tsx,page.tsx,pagination.tsx}`
- `frontend/src/app/(public)/katalog/[slug]/{product-filters-client.tsx,page.tsx}`
- `frontend/src/lib/i18n/cs-CZ.ts`
- `frontend/src/app/(public)/katalog/__tests__/*`

## Test plan reference
`docs/test-plans/T-0170.md`

## Status log
- 2026-08-21 `draft → ready` (Phase 8 UX sweep plan; DoR checklist passed)
