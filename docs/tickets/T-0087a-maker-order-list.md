---
id: T-0087a
title: Maker dashboard order list page (/dashboard/maker/objednavky)
status: ready
size: M
owner: frontend
created: 2026-06-09
updated: 2026-06-09
depends_on: [T-0079, T-0081]
blocks: []
user_stories: [US-maker-0005]
adrs: [0013, 0022, 0024]
phase: 4
manual_steps: ["QA pass on Vercel preview per docs/test-plans/T-0087a.md (Playwright-style manual plan)"]
security_touching: false
layers: [frontend]
---

# T-0087a — Maker dashboard order list page (`/dashboard/maker/objednavky`)

## Context

T-0087a is the **fifth ticket in Bundle 2 `feat/order-dashboards-bundle`** (T-0088 invoice-download endpoint → T-0089 backend slices + NSwag regen gate → T-0086a customer order list → T-0086b customer order detail + shared message thread → **T-0087a maker order list** → T-0087b maker order detail + actions). All six ship sequentially on one branch. Bundle 1 `feat/checkout-flow-bundle` (T-0084a → T-0084b → T-0085) ships FIRST — it closes the revenue path; this bundle closes the post-payment visibility loop.

This ticket replaces the placeholder route at `/dashboard/maker/objednavky` with the real maker order list, satisfying **US-maker-0005 — View incoming orders**: AC-1 (paginated 20/page, `CreatedAt DESC`) and AC-2 (state + date-range filters) are served directly by T-0081's `GET /api/v1/maker/orders` endpoint (merged; generated client method `makerApi.orders(page, pageSize, state, dateFrom, dateTo, sort)`). AC-3's "X nových objednávek čeká" nudge is satisfied structurally rather than as a badge: the **default tab is "Nové"** (orders in `Paid` state awaiting acceptance), so the maker lands on actionable work first — this is the needs-action nudge deferred from T-0071 grooming, delivered without any backend pseudo-state (T-0081 §A.3 lock honored).

Everything the page renders is already on the wire. `MakerOrderListItemDto` carries `OrderId`, `OrderNumber`, `State`, `TotalAmountMinor`, `MakerPayoutAmountMinor`, `Currency`, `CreatedAt`, `CustomerContactName` (never email — T-0081 §A.2 GDPR lock), `ShippingMethod`, `ProductTitle` (nullable), and `UnreadMessageCount` — real values since T-0079 shipped the message thread and flipped the projection from `null` to `maker_unread_message_count`. The page is a pure presentation layer: no client-side filtering, no money math, no state-machine knowledge.

The implementation precedent is the shipped maker products page (`frontend/src/app/(maker)/dashboard/maker/produkty/page.tsx`): Server Component, `force-dynamic`, URL-driven `searchParams` for pagination, a hand-written `Result<T, ApiError>` helper per patterns.md §B.16, empty/error states with i18n keys, and Tailwind-4 dark zinc/brand-400 styling from `components/ui/` primitives. T-0086a (customer order list, earlier in this bundle) establishes the order-list-specific conventions (date-range + sort URL params, state badge mapping, "Vlastní zakázka" null-product label, mobile-cards/desktop-table layout); T-0087a mirrors them on the maker host so both dashboards stay structurally identical for review and maintenance.

## Locked design decisions

Captured per `docs/process/deliberation.md`. The user locked 3 dimensions at the 2026-06-09 grooming session (Q8 state tabs, Q7 unread badges, Q5 SSR freshness model). The rest is ADR/pattern-locked or PM-absorbed from the produkty page + T-0086a precedents.

### A. User-locked at grooming (non-negotiable)

1. **State tabs: "Nové" (Paid) / "Ve výrobě" (Accepted) / "Vše" — one request per tab, default tab = Nové** (Q8). Each tab maps to **at most ONE** `state` value on the single T-0081 list endpoint: Nové → `state=Paid`, Ve výrobě → `state=Accepted`, Vše → no `state` param. Exactly one list request per render — **no client-side merging of multi-state results** (honors T-0081 §A.3 no-pseudo-state lock at the frontend boundary too). Default tab Nové is the needs-action nudge deferred from T-0071. The active tab lives in a URL `searchParam` (`?tab=`), so deep links, back/forward, and refresh all land on the right tab. **Rejected:** a composite "needs action" tab merging Paid+Accepted via parallel requests (re-introduces the pseudo-state client-side; two requests per render; T-0081 explicitly pushed this decision out of MVP); fetching `Vše` once and filtering tabs client-side (business-adjacent filtering in the frontend + unpaginated over-fetch).

2. **Unread message badges per row** (Q7). Each row renders a count badge when `unreadMessageCount > 0`; nothing when `0`/`null`/`undefined`. The value is real since T-0079 (denormalized `maker_unread_message_count` on the Order row — zero extra requests). **Rejected:** fetching unread counts via the messages endpoint per row (N+1 over the wire; the DTO field exists precisely to avoid this); a dot-only badge without the count (the count is free and tells the maker how much is waiting).

3. **SSR + `router.refresh()` freshness model** (Q5). The page is a Server Component fetching on render (`force-dynamic`); tab/filter/page navigation happens via `<Link>` so every navigation re-fetches server-side. No client polling on the list. Mutations happen on the detail page (T-0087b), which calls `router.refresh()` after each action — when the maker navigates back, the list re-renders fresh. **Rejected:** client-side polling of the list (wasted requests for a dashboard the maker actively navigates); SWR/React-Query style client cache (violates the no-client-server-state stance — server state lives in the backend, CLAUDE.md).

### B. ADR + pattern-locked (no relitigation)

- **patterns.md §B.1 — Server Components by default.** `page.tsx` has no `'use client'`. Zero JS shipped for the list itself; interactivity is `<Link>` navigation.
- **patterns.md §B.4 + §B.16 — all data via `apiFetch` + a hand-written helper.** New `lib/api-client-helpers/maker-orders.ts` wraps the generated `makerApi.orders(...)` and returns `Result<MakerOrdersPage, ApiError>`. No raw `fetch`, no `useEffect` data fetching anywhere in the route.
- **patterns.md §B.14 + ADR 0024 — SSR auth cookie forwarding.** The Server Component render forwards the maker-audience cookie to the maker host. A customer JWT replayed against the maker host 401s at the backend (ADR 0013); the frontend adds no parallel auth logic.
- **ADR 0022 — NSwag is the contract.** `frontend/src/lib/api-client/` is consumed, never edited (pre-commit hook). The maker client already types `orders(page, pageSize, state, dateFrom, dateTo, sort)` → `GetMakerOrdersResponse`; **no backend change and no regen** in this ticket.
- **patterns.md §B.8 — URL-state pagination via `searchParams` + `<Link>`.** Page/pageSize/tab/date/sort all live in the URL. Mirrors the produkty page (T-0049 review M2: URL params round-trip cleanly, clamped to backend caps).
- **patterns.md §B.5 + §B.18 — Czech-only UI via i18n keys.** Zero hardcoded Czech outside `lib/i18n/cs-CZ.ts`. Plural-neutral phrasing for counts.
- **patterns.md §B.10 — `formatCzk(amountMinor, currency)`** for every money figure. No arithmetic on the client.
- **No business logic client-side.** Tab→state mapping is presentation routing, not a state machine. Param clamping (page ≥ 1, pageSize ≤ 50) is a UX-only duplicate of the backend Validator — the backend remains authoritative and would 400 anyway.

### C. PM-absorbed (no user input needed)

- **Payout column, not platform fee.** Rows show `MakerPayoutAmountMinor` via `formatCzk` — the maker sees THEIR payout (T-0081 §C: the DTO deliberately has no `PlatformFeeAmountMinor`). Column header keyed `dashboard.maker.orders.column.payout`.
- **`CustomerContactName` column — never email.** The DTO cannot carry email (T-0081 §A.2 compile-time GDPR lock); the frontend renders no email-shaped field and no `mailto:` anywhere on the page.
- **Date-range + sort + pagination mirror T-0086a.** URL params `dateFrom`/`dateTo` (ISO dates, parsed to `Date`, invalid values dropped), `sort` (mapped to the generated `OrderSort` enum, invalid → `CreatedAtDesc` default), `page`/`pageSize` (clamped 1–50, default 20, produkty `parsePositiveInt` precedent). The generated client signature is the source of truth for param names.
- **Tab URL values:** `?tab=nove|vyroba|vse`; missing/unknown → `nove`. Czech slugs match the route language (`objednavky`, `produkty`).
- **Tykání copy.** Maker-facing strings are written in tykání (T form) per CLAUDE.md — **pending the open question in `docs/questions/open.md`**; if it resolves to vykání, the i18n catalog flips without code changes. Noted in the PR description.
- **Empty states per tab, distinct copy.** Nové empty = informational/positive ("no new orders waiting" — not an error, not a CTA); Ve výrobě empty = neutral; Vše empty = onboarding-flavored ("orders will appear here once customers buy"). Keys `dashboard.maker.orders.empty.{nove,vyroba,vse}.{title,description}`.
- **Cards on mobile, table on desktop.** Single server-rendered markup with responsive Tailwind classes (cards `< md`, table `≥ md`); verified at 375/768/1280. No table library.
- **State badge mapping** reuses the existing `order.state.*` cs-CZ keys (already in the catalog, lines `order.state.paid` etc.) with the `Badge` primitive from `components/ui/badge`.
- **`ProductTitle == null` renders "Vlastní zakázka"** (custom order, no product link) — T-0080/T-0086a convention, key shared with the customer list if T-0086a already added it.
- **Row click navigates to `/dashboard/maker/objednavky/{orderId}`** (T-0087b detail). Date display in Czech short format (`9. 5. 2026`) via the shared date formatter.
- **Pagination component:** reuse whatever T-0086a promoted (shared `components/` or local-copy per produkty precedent) — implementer matches T-0086a's resolution; do not invent a third variant.

## Scope

### Route (replaces placeholder)

- **`frontend/src/app/(maker)/dashboard/maker/objednavky/page.tsx`** — Server Component. `export const dynamic = 'force-dynamic'` (dashboard reflects fresh mutations). `generateMetadata` from i18n keys. Reads `searchParams` (`tab`, `page`, `pageSize`, `dateFrom`, `dateTo`, `sort`), maps tab → single `OrderState | undefined`, calls the helper once, renders tabs header + results/empty/error states.
- **`frontend/src/app/(maker)/dashboard/maker/objednavky/order-tabs.tsx`** — server-rendered tab strip of three `<Link>`s preserving the other searchParams; active tab styled, with per-tab `aria-current`.
- **`frontend/src/app/(maker)/dashboard/maker/objednavky/order-row.tsx`** — server-rendered row/card: order number, created date, state badge, `CustomerContactName`, `ProductTitle` (or "Vlastní zakázka"), payout via `formatCzk`, unread badge when `unreadMessageCount > 0`, link wrapper to detail.
- **Pagination** — per §C, reuse T-0086a's component or the produkty-local pattern.

### API helper

- **`frontend/src/lib/api-client-helpers/maker-orders.ts`** — NEW (T-0087b extends it with detail + action helpers):
  - `MAKER_ORDERS_DEFAULT_PAGE_SIZE = 20`, `MAKER_ORDERS_MAX_PAGE_SIZE = 50` (mirror backend Validator clamps — UX-only duplicates; backend authoritative).
  - `getMakerOrders({ page, pageSize, state?, dateFrom?, dateTo?, sort? }): Promise<Result<MakerOrdersPage, ApiError>>` wrapping `makerApi.orders(...)` per §B.16; runs server-side with cookie forwarding per §B.14.

### i18n

- **`frontend/src/lib/i18n/cs-CZ.ts`** — NEW keys under `dashboard.maker.orders.*`: metadata title/description, page title/subtitle, tab labels (`tab.nove`, `tab.vyroba`, `tab.vse`), column headers (order, date, customer, product, payout, state), unread badge aria-label, per-tab empty states, error title/body/retry, count line (plural-neutral per §B.18). No error-code keys needed — the list surface has no mutations.

### Out of placeholder

- Delete the placeholder content of the route; no other routes touched. No backend change, no NSwag regen, no `api-client` diff.

## Alternatives Considered

- **Option A — Composite "needs action" tab merging Paid + Accepted client-side.** *Rejected per A.1* — re-introduces the pseudo-state T-0081 §A.3 explicitly pushed out of the contract, doubles requests per render, and pre-empts the product question ("does Shipped count as needs-action?") that the backend refused to bake in. The default-Nové tab delivers the same nudge with one state.
- **Option B — Fetch all orders once, filter tabs client-side.** *Rejected per A.1* — unpaginated over-fetch; filtering is business-adjacent logic the frontend must not own; breaks at the first maker with 200 orders.
- **Option C — Client-side tab state (`useState`) instead of URL searchParam.** *Rejected per A.1 + §B.8* — loses deep-linking, back/forward, and refresh-survival; turns the page into a Client Component for zero gain.
- **Option D — Poll the list every N seconds for new orders.** *Rejected per A.3* — the SSR + navigation-refresh model covers the dashboard workflow; polling belongs to the message thread (T-0086b component, used on the detail page), not the list.
- **Option E — Dot-only unread indicator without count.** *Rejected per A.2* — the count is already on the row DTO; hiding it discards free information the maker uses to prioritize.
- **Option F — Show platform fee or gross total instead of payout in the money column.** *Rejected per §C + T-0081 §C* — the DTO intentionally carries the maker's net; the gross total is customer-facing noise here. (Gross remains visible on the detail page breakdown, T-0087b.)
- **Option G — Generic shared OrderList component parameterized for customer + maker.** *Rejected* — the two lists have different columns (payout vs total, contact name vs maker name), different empty states, and different hosts; a parameterized mega-component couples the audiences the bundle's DTO split deliberately separated. Mirroring conventions ≠ sharing code.
- **Option H — Table library (TanStack Table) for sorting/filtering.** *Rejected* — sorting and filtering are backend concerns surfaced via URL params; a client table library re-implements them client-side and adds a dependency for a 7-column read-only table.

## Out of scope

- **Order detail page + actions** — T-0087b (next ticket in the bundle; consumes this page's row links).
- **Customer order list / detail** — T-0086a / T-0086b.
- **Message thread UI** — T-0086b owns the shared component; the list only shows the unread count badge.
- **Multi-state filter or backend pseudo-state** — rejected at T-0081 §A.3; not re-opened here.
- **"X nových objednávek čeká" summary badge on the dashboard landing page** — US-maker-0005 AC-3's literal badge on a dashboard overview is a separate surface (no dashboard-overview route exists yet); the default-Nové tab covers the nudge at MVP. Logged for the dashboard-overview ticket.
- **Customer-name text search** — rejected at T-0081 (Option E); no search box.
- **401 → refresh → retry in `api-fetch.ts`** — known platform gap (not yet built); unauthenticated SSR renders fall back to the (maker) route group's existing auth guard behavior. Not this ticket.
- **CSV export, bulk actions, column preferences** — post-MVP.

## Acceptance criteria

- **AC-1** Given a logged-in maker visiting `/dashboard/maker/objednavky`, when the page renders, then it is a Server Component (`page.tsx` contains no `'use client'`), `dynamic = 'force-dynamic'` is set, the data comes from `getMakerOrders` via `apiFetch` with SSR cookie forwarding, and no `useEffect` data fetching exists anywhere in the route folder.
- **AC-2** Given no `tab` searchParam, when the page renders, then the Nové tab is active and exactly **one** list request is issued with `state=Paid`. Given `?tab=vyroba`, exactly one request with `state=Accepted`. Given `?tab=vse`, exactly one request with no `state` param. At no point are multiple states merged client-side (network tab proof on Vercel preview).
- **AC-3** Given any active tab, when the maker uses browser back/forward or pastes a deep link (`?tab=vyroba&page=2`), then the page lands on that exact tab + page (tabs are `<Link>`s preserving sibling params).
- **AC-4** Given a row for an order, when rendered, then it shows: order number, `CreatedAt` in Czech short format, state badge using existing `order.state.*` keys, `CustomerContactName`, `ProductTitle` or "Vlastní zakázka" when null, and the payout formatted via `formatCzk(makerPayoutAmountMinor, currency)`. No email address and no `mailto:` appears anywhere in the rendered DOM (grep + DOM inspection proof).
- **AC-5** Given a row with `unreadMessageCount = 3`, when rendered, then a badge with "3" (and an i18n aria-label) appears on the row. Given `unreadMessageCount` of `0`/`null`/`undefined`, no badge renders.
- **AC-6** Given `?page=2&pageSize=10`, when rendered, then the helper is called with page 2 / pageSize 10 and pagination controls reflect `PagedData` totals. Given junk params (`page=0`, `pageSize=999`, `page=abc`), the values clamp to defaults/caps (1 / 20 / 50) without an error page — and the backend Validator remains the authority (a forced out-of-range request 400s).
- **AC-7** Given `?dateFrom=2026-01-01&dateTo=2026-06-01&sort=TotalAmountDesc`, when rendered, then the helper passes the parsed dates + `OrderSort.TotalAmountDesc` through to the generated client; invalid date strings and unknown sort values are dropped to defaults.
- **AC-8** Given a tab with zero results, when rendered, then the tab-specific empty state shows: Nové uses informational/positive copy (no new orders waiting — not an error), Ve výrobě and Vše use their own keys. All three are distinct strings in `cs-CZ.ts`.
- **AC-9** Given viewports 375 / 768 / 1280, when rendered, then rows display as cards below `md` and as a table at `md` and above; no horizontal scroll at 375; interactive targets remain tappable.
- **AC-10** Given the API call fails (network error / 5xx), when rendered, then an `Alert variant="error"` with i18n title/body and a retry link renders (produkty error-state precedent) — no blank page, no thrown render error.
- **AC-11** Hygiene gate: zero `any`, zero `console.*`, zero hardcoded Czech outside `cs-CZ.ts` (new keys appended; tykání tone noted as pending in the PR), zero edits to `lib/api-client/` (pre-commit hook), `npm run lint` + `npm run build` clean, `node scripts/check-consistency.mjs` exit 0.

## Technical notes

### Why the default tab IS the needs-action nudge (not a badge)

US-maker-0005 AC-3 asks that attention-required orders surface immediately. A count badge needs a summary surface to live on (a dashboard overview route that doesn't exist yet) and a count source (either a dedicated endpoint or a second list request). Defaulting the list to the Nové tab achieves the same outcome with zero new contract surface: the maker's first paint after login IS the queue of orders waiting for acceptance. When the dashboard-overview ticket lands, its badge can deep-link to `?tab=nove` and the URL-state design pays off again.

### Why exactly one request per tab (no client merging)

T-0081 §A.3 deliberately kept the backend single-state-filter-only because "needs action" semantics will evolve. If the frontend quietly merged `state=Paid` + `state=Accepted` into a composite tab, it would own the very product question the backend refused to encode — just one layer up and harder to find. One tab = one state = one request keeps the product decision in one greppable mapping function and keeps render cost flat (one `PagedData` page per paint).

### Why URL searchParams own all view state

Tab, page, pageSize, date range, and sort in the URL means: deep links work (a maker can bookmark "Ve výrobě, page 2"), back/forward works, refresh survives, and the Server Component re-fetches on every navigation without any client cache to invalidate. This is the §B.8 produkty precedent verbatim — the T-0049 review specifically called out that URL params must round-trip cleanly, and this page inherits that fix.

### Why the unread badge costs zero extra requests

T-0079 denormalized `maker_unread_message_count` onto the Order row precisely so list views read it as a plain field. The badge is a conditional render of `unreadMessageCount > 0` — no messages endpoint call, no N+1, no client polling. Mark-as-read on the detail page (T-0087b) resets the counter server-side, so the badge self-clears on the next list render.

### Why the money column is the maker's payout (not the gross total)

The maker's operative question at list-scan time is "what does this order earn me", and `MakerPayoutAmountMinor` is the only figure the DTO was designed to answer it with — T-0081 explicitly rejected `PlatformFeeAmountMinor` as maker-irrelevant noise. The gross total stays available on the detail breakdown (T-0087b) where it has context.

## Risk / mitigation

- **Tab semantics drift** (product later wants "needs action" to include Shipped). *Mitigation:* tab→state mapping is one tiny function in `page.tsx`; changing it is a frontend-only edit because the backend stayed single-state (T-0081 §A.3 paying off).
- **`UnreadMessageCount` undefined on older serialized rows** (NSwag types it `number | undefined`). *Mitigation:* badge renders only on `> 0`; `undefined`/`null`/`0` all collapse to "no badge" — pinned by AC-5.
- **Param clamp divergence from backend Validator** (frontend clamps at 50, backend changes cap). *Mitigation:* clamps are UX-only; the helper passes through and the backend 400s as authority. Constants named after the backend rule with a comment pointing at T-0081's Validator.
- **Tykání/vykání open question resolves late.** *Mitigation:* all copy behind i18n keys; a tone flip is a catalog-only change.

## Test plan reference

`docs/test-plans/T-0087a.md` (stub created with this ticket) — Playwright-style manual QA plan executed against the Vercel preview: tab switching + deep links, single-request-per-tab network assertion, badge rendering, empty/error states, responsive passes at 375/768/1280, junk-param handling. No backend tests (no backend change).

## Files touched (expected)

### New
- `frontend/src/app/(maker)/dashboard/maker/objednavky/order-tabs.tsx`
- `frontend/src/app/(maker)/dashboard/maker/objednavky/order-row.tsx`
- `frontend/src/lib/api-client-helpers/maker-orders.ts`
- `docs/test-plans/T-0087a.md`

### Modified
- `frontend/src/app/(maker)/dashboard/maker/objednavky/page.tsx` — placeholder replaced with the real Server Component page.
- `frontend/src/lib/i18n/cs-CZ.ts` — `dashboard.maker.orders.*` keys appended.
- Pagination: reuse T-0086a's resolution (shared component or local copy) — file location follows that ticket.

## Commits hint

1. **`feat(T-0087a): maker-orders api helper + i18n keys`** — `maker-orders.ts` helper + cs-CZ catalog additions.
2. **`feat(T-0087a): maker order list page with state tabs + unread badges`** — page + tabs + row + pagination wiring; placeholder removed.
3. **`test(T-0087a): manual QA plan + preview fixes`** — `docs/test-plans/T-0087a.md` + any QA-pass fixes from the Vercel preview run.

## Status log

- 2026-06-09 `draft` by PM. Created as ticket 5 of 6 in `feat/order-dashboards-bundle` (T-0088 → T-0089 → T-0086a → T-0086b → T-0087a → T-0087b). Backend dependencies all merged: T-0081 maker list endpoint (+ generated client `orders(...)`), T-0079 messages (real `UnreadMessageCount`). Precedents: produkty page (Server Component + URL pagination + §B.16 helper), T-0086a (order-list conventions on the customer side, earlier in this bundle).
- 2026-06-09 `draft → ready` by PM. User locked 3 dimensions at grooming: **A.1** (Q8) state tabs Nové/Ve výrobě/Vše with one single-state request per tab, default Nové as the T-0071-deferred needs-action nudge, tab in URL (rejected composite needs-action tab + client-side filtering + useState tabs); **A.2** (Q7) per-row unread count badges from the real DTO field (rejected per-row fetch + dot-only); **A.3** (Q5) SSR + `router.refresh()` freshness, no list polling (rejected polling + client cache). PM-absorbed decisions in §C (payout column, contact-name GDPR surface, T-0086a param mirroring, tab slugs, tykání-pending copy, per-tab empty states, responsive layout, badge/state-key reuse, Vlastní zakázka label, pagination reuse). No manual_steps beyond preview QA. **Ready for frontend.**

## Definition of Ready checklist

- [x] Linked user story present (US-maker-0005).
- [x] Acceptance criteria observable + numbered (AC-1 through AC-11).
- [x] Locked design decisions captured (§A user-locked, §B ADR+pattern-locked, §C PM-absorbed).
- [x] Alternatives Considered with ≥1 rebutted alternative per locked dimension (Options A–H).
- [x] Out of scope explicit (incl. US-maker-0005 AC-3 literal badge deferred to dashboard-overview ticket).
- [x] Risk / mitigation called out.
- [x] Test plan reference (docs/test-plans/T-0087a.md stub, Vercel preview QA).
- [x] Files touched listed (new + modified).
- [x] Layers / ADRs / dependencies in the frontmatter; no NSwag regen needed (read-only consumer of the existing contract).
- [x] Security-touching: NO (auth enforced by backend audience + existing route-group guard; no new surface).
- [x] Size: M.
- [x] No business logic client-side (tab mapping + clamps are presentation/UX-only; backend authoritative).
