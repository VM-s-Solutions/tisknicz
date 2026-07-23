---
id: T-0158
title: Orders filters on the katalog Dropdown + first perf pass (SSR profile, parallel katalog fetches)
status: done
size: S
owner: frontend
created: 2026-07-23
updated: 2026-07-23
depends_on: [T-0086a, T-0087a, T-0036, T-0046, T-0119]
blocks: []
user_stories: [US-customer-0016, US-customer-0018, US-maker-0005]
adrs: [0024, 0022]
phase: 7
manual_steps: []
security_touching: false
layers: [frontend, optimizer]
---

# T-0158 — Orders dropdowns + perf pass

## Context

Operator requests: (a) "Moje objednávky — use the dropdowns we created for
katalog"; (b) "loading of katalog or profile is insanely slow."

Measured before changing anything (warm dev, curl): catalog API direct
~0.24 s, via proxy ~0.29 s, SSR /katalog ~0.4–0.6 s — the backend is fine
warm. The *felt* slowness came from the frontend patterns and environment:

1. **Profile page fetched client-side in a `useEffect`** — against the
   project's own no-effect-fetch rule (T-0036 debt): render shell → load
   JS → hydrate → fetch → re-render, with a spinner the whole time. And
   until the T-0156 cookie bridge deployed, that fetch 401'd into the
   T-0154 refresh-retry dance, making the page feel broken-slow.
2. **/katalog ran its two backend reads serially** (categories, then
   makers) — two full round trips before first byte.
3. Environment tax (not code-fixable here): dev runs 6 apps on one shared
   B2 plan and **Postgres sits cross-region** (northeurope vs westeurope,
   subscription offer-restriction) — every query pays the inter-region
   RTT; cold starts add seconds after idle. Documented for the prod
   sizing decision, where none of these constraints need apply.

## Scope

- **Dropdowns**: `customer/objednavky` filter bar swaps both native
  `Select`s (state, sort) for the katalog `Dropdown` (WAI-ARIA listbox,
  hairline-styled); `maker/objednavky` sort select swapped too for
  consistency. Behavior identical (URL-state push per Q5 lock).
- **Profile → Server Component fetch**: `page.tsx` fetches
  `getMyProfile` via SSR cookie forwarding (rides the T-0154
  middleware-refreshed cookie) and hands `initialProfile` to the client
  boundary, which now exists only for form interactivity — no spinner,
  no waterfall, one round trip.
- **/katalog parallel reads**: without a `category` param (hot path) the
  categories + makers fetches run in `Promise.all`; category-filtered
  URLs keep the original order because the slug canonicalises against
  the fetched list (invalid slug → unfiltered, not empty).

## Out of scope

- Backend query changes — measured warm latency did not justify any
  (list endpoints are paginated, indexed, `.AsNoTracking()` per project
  rules; the catalog projection is a single query).
- Postgres region move / plan upsizing (infra decision, operator).
- Response caching for the categories list (candidate follow-up if the
  catalog gets hot; today it costs one parallelized read).

## Acceptance criteria

- **AC-1** Given the customer orders page, when filtering by state or
  sorting, then the katalog-style Dropdown drives the same URL-state
  behavior (history entries, page reset) as before.
- **AC-2** Given `/dashboard/zakaznik/profile`, when the page loads, then
  the profile arrives server-rendered — no spinner, no client fetch on
  mount (network panel shows zero `/api/v1/me` XHR on load).
- **AC-3** Given `/katalog` without a category param, when the page
  renders, then the categories and makers requests overlap (server log
  timing), cutting one full backend round trip.

## Test plan reference

`tsc`, eslint, vitest 76/76, `next build` — all clean. Manual after
deploy: orders filter dropdowns keyboard-navigable; profile paints
populated; /katalog TTFB drops by roughly one backend round trip.

## Status log

- 2026-07-23 `draft → in_progress → in_review` — operator-requested UI
  polish + perf pass, built on the day's auth-fix stack.
- 2026-07-23 `in_review → done` — PR #107 merged; live on dev with the day's stack.
