---
id: T-0086a
title: Customer dashboard order list page (/dashboard/zakaznik/objednavky)
status: ready
size: M
owner: frontend
created: 2026-06-09
updated: 2026-06-09
depends_on: [T-0089, T-0080]
blocks: [T-0086b]
user_stories: [US-customer-0016]
adrs: [0022, 0024]
phase: 4
manual_steps:
  - "Manual QA pass on the Vercel preview per docs/test-plans/T-0086a.md (375/768/1280, JS-disabled SSR check)"
security_touching: false
layers: [frontend, frontend-i18n]
---

# T-0086a — Customer dashboard order list page (`/dashboard/zakaznik/objednavky`)

## Context

T-0086a is the **third ticket in the order-dashboards bundle** (`feat/order-dashboards-bundle`: T-0088 invoice-download endpoint → T-0089 backend slices + NSwag regen gate → **T-0086a customer list** → T-0086b customer tracking detail → T-0087a maker queue → T-0087b maker detail). The bundle ships AFTER `feat/checkout-flow-bundle` (T-0084a → T-0084b → T-0085), which closes the revenue path first. T-0086a is the first frontend ticket in the bundle and the first authenticated customer Server Component list page on the platform.

The backend is fully shipped: `GET /api/v1/customer/orders` (T-0080, merged) returns `GetCustomerOrdersResponse { orders: PagedData<CustomerOrderListItemDto> }` with page-based pagination (default 20, cap 50), `State` + `DateFrom`/`DateTo` filters, and the 5-arm `OrderSort` enum. T-0079 (merged) denormalized `customer_unread_message_count` onto the Order row; **T-0089 finalizes the `UnreadMessageCount` projection on the customer list + the bundle's NSwag regen gate** — this ticket consumes `CustomerOrderListItemDto.unreadMessageCount` from the regenerated client and hard-depends on T-0089.

This ticket directly satisfies **US-customer-0016 — View order list (customer dashboard)**: AC-1 (paginated 20/page, `CreatedAt DESC`), AC-2 (state + date-range filters), and AC-3 (empty state with a "Browse catalog" CTA), which T-0080 explicitly deferred to the frontend. The dashboard route group already exists (`/dashboard/zakaznik/profile` shipped in Phase 3); the orders list lands as a sibling sub-route. The rendering model is the catalog-page precedent end to end: `force-dynamic` Server Component, URL-state filters via `searchParams`, `<Link>`-based pagination (patterns.md §B.8), SSR auth via cookie forwarding (patterns.md §B.14, ADR 0024), and a hand-written `Result<T, ApiError>` helper wrapping the generated client (patterns.md §B.16, ADR 0022).

**No business logic ships client-side.** Filter/sort values are passed through as query params; the backend validates (T-0080 Validator clamps page/pageSize, rejects inverted date ranges) and remains authoritative. The page formats, displays, and links — nothing else.

## Locked design decisions

Captured per `docs/process/deliberation.md`. The user locked 2 dimensions at the 2026-06-09 dashboards grooming session (Q5, Q7). 10 PM-absorbed decisions follow from the T-0046/T-0049 catalog precedents and the T-0080 backend contract.

### A. User-locked at grooming (non-negotiable)

1. **Unread badges on the list (Q7).** Every order row renders an unread-message badge when `unreadMessageCount > 0` (hidden at 0). The count comes from T-0089's customer-list projection of the T-0079 denormalized counter — **never** computed client-side by fetching messages per row. This ticket `depends_on` T-0089 + its NSwag regen; the badge is a pure read of the regenerated `CustomerOrderListItemDto.unreadMessageCount` field. **Rejected:** per-row messages fetch (N+1 against the messages endpoint); shipping the list without badges and retrofitting later (loses the dashboard's "needs my attention" signal at launch).

2. **SSR + URL-state, no client store (Q5).** The page is a `force-dynamic` Server Component. Filters, sort, and page live in URL `searchParams`; changing any of them navigates (small `'use client'` filter bar pushes the new URL) and the server re-renders with fresh data. No Zustand/SWR/React Query, no client-side cache, no `useEffect` data fetching. Post-action refreshes elsewhere in the bundle use `router.refresh()` — the same Q5 lock governs T-0086b/T-0087a/T-0087b. **Rejected:** client-store + background revalidation (violates CLAUDE.md "no Redux/Zustand"; server state lives in the backend); client-rendered page with `useEffect` fetch (pre-ADR-0024 legacy shape; loses SSR TTFB and the B.1 default).

### B. ADR + pattern-locked (no relitigation)

- **patterns.md §B.1** — Server Components by default; no `useEffect` data fetching; no business logic client-side.
- **patterns.md §B.4 + §B.16 (ADR 0022)** — all calls go through `apiFetch` via a hand-written helper in `lib/api-client-helpers/`; route code never imports the generated client; DTO types re-exported as aliases from the helper. `lib/api-client/` is never edited manually (pre-commit hook).
- **patterns.md §B.14 (ADR 0024)** — SSR auth via audience-cookie forwarding inside `apiFetch`. No per-page session plumbing. Note: 401-refresh is NOT wired yet (§B.3); `Unauthorized` surfaces as a typed `ApiError` and the page redirects to `/auth/login`.
- **patterns.md §B.8** — URL-state pagination via `searchParams` + `<Link>`; `parsePositiveInt` clamping; canonical URLs only emit non-default params.
- **patterns.md §B.5 + §B.18** — all strings from `lib/i18n/cs-CZ.ts`; plural-neutral Czech phrasing for counts.
- **patterns.md §B.7** — route files wrap in `<section>`, never a second `<main>`.
- **patterns.md §B.10** — money via `formatCzk(totalAmountMinor, currency)`; dates via the cs-CZ formatter in `lib/utils/dates.ts` (`9. 5. 2026`).

### C. PM-absorbed (no user input needed)

- **Route:** `frontend/src/app/(customer)/dashboard/zakaznik/objednavky/page.tsx`. US-customer-0016 names `/dashboard/zakaznik`; the locked sub-route mirrors the existing `/dashboard/zakaznik/profile` and maker `/dashboard/maker/produkty` granularity. A dashboard landing/redirect at the group root is out of scope. If the `(customer)` dashboard layout carries a nav, add the "Objednávky" entry (minimal link addition allowed in this ticket).
- **State filter dropdown + date-range inputs** map 1:1 to T-0080's locked GET query params (`state`, `dateFrom`, `dateTo`). A `'use client'` filter bar (mirroring `katalog/filters-client.tsx`) pushes the new URL with `page` reset to 1. Date inputs are native `<input type="date">`; the helper serializes to ISO dates. The state dropdown lists all 9 `OrderState` values labeled via the existing `order.state.*` keys plus an "all states" default.
- **Sort selector** — one `<Select>` with the 5 `OrderSort` arms (`CreatedAtDesc` default, `CreatedAtAsc`, `TotalAmountDesc`, `TotalAmountAsc`, `StateAsc`); only emitted to the URL when non-default.
- **Pagination controls** match the `/katalog` `Pagination` component precedent (`<Link>`-based, preserves all non-page params via `baseParams`). Extract to a shared component only if trivially reusable; copying the small component into the route folder (katalog precedent) is acceptable.
- **State badges** use the existing `Badge` UI primitive with the existing `order.state.*` i18n keys (all 9 already in the catalog). Per-state badge tone mapping (e.g., `Cancelled` → danger, `Delivered`/`Completed` → success) is a display-only lookup table in the route folder.
- **Empty state** renders an icon + copy + "Prohlédnout katalog" CTA linking to `/katalog` (US-customer-0016 AC-3), styled per the `CatalogEmpty` precedent. Shown only when `totalCount === 0` AND no filters are active; a filtered-to-zero result shows a "no match — clear filters" variant linking back to the bare route.
- **Row click → `/objednavka/[id]`** (the T-0084b route, extended by T-0086b). The whole row/card is the link target.
- **Custom orders:** `productTitle == null` renders the "Vlastní zakázka" label (T-0080 context lock) via a new i18n key.
- **Responsive:** stacked cards below `md`, table at `md+` (columns: number, state, maker, product, total, created, unread). Verified at 375/768/1280. No arbitrary Tailwind values; primitives from `components/ui/`.
- **Helper:** NEW `lib/api-client-helpers/customer-orders.ts` exporting `getCustomerOrders(input)` → `apiFetch<CustomerOrdersPage>('customer', '/api/v1/orders?...')` returning `Result<T, ApiError>`, re-exporting `ICustomerOrderListItemDto`/`OrderState`/`OrderSort` types. T-0086b extends this same file with detail/deliver/messages wrappers.
- **Error handling:** `Unauthorized` → `redirect('/auth/login')`; any other `ApiError` → inline error alert with retry link (catalog `CatalogError` precedent). Backend validation errors (inverted date range) surface via the error alert — the filter UI does not pre-block them (UX-only mirrors allowed but optional; backend authoritative).
- **i18n:** new flat-dotted keys under `customer.orders.*` (title, table headers, filter labels, sort labels, empty-state copy, unread badge, custom-order label). Plural-neutral per §B.18. Final key list at implementation time; error-code parity untouched (no new backend codes).

## Scope

- **`frontend/src/app/(customer)/dashboard/zakaznik/objednavky/page.tsx`** — NEW Server Component. `export const dynamic = 'force-dynamic'`. Reads `searchParams` (page, state, dateFrom, dateTo, sort), clamps/validates display-side (whitelist enum values; invalid → ignored, not errored), calls `getCustomerOrders`, renders header + filter bar + results/empty/error + pagination. `generateMetadata` returns the static dashboard title.
- **`frontend/src/app/(customer)/dashboard/zakaznik/objednavky/filters-client.tsx`** — NEW `'use client'` filter + sort bar; pushes URL on change (`router.push`), resets `page` to 1, preserves other params.
- **`frontend/src/app/(customer)/dashboard/zakaznik/objednavky/order-row.tsx`** — NEW presentational row/card component (server-safe): state badge, unread badge, money + date formatting, link wrapper to `/objednavka/[id]`.
- **`frontend/src/app/(customer)/dashboard/zakaznik/objednavky/pagination.tsx`** — NEW (copied katalog precedent) `<Link>`-based pagination preserving filter params.
- **`frontend/src/app/(customer)/dashboard/zakaznik/objednavky/loading.tsx`** — NEW skeleton (spinner primitive), `<section>` wrapper per §B.7.
- **`frontend/src/lib/api-client-helpers/customer-orders.ts`** — NEW helper per §C; the only new data path. Expected shape (final signatures at impl time; §B.16 conventions):

  ```ts
  import { apiFetch } from '../runtime/api-fetch';
  import type { ApiError, Result } from '../runtime/result';
  // Type-only re-exports so route code never touches the generated client.
  import { OrderSort, OrderState } from '../api-client/customer-api.v1';
  import type { ICustomerOrderListItemDto } from '../api-client/customer-api.v1';

  export { OrderSort, OrderState };
  export type CustomerOrderListItem = Readonly<ICustomerOrderListItemDto>;

  export interface CustomerOrdersInput {
    readonly page?: number;
    readonly state?: OrderState;
    readonly dateFrom?: string; // ISO yyyy-MM-dd from <input type="date">
    readonly dateTo?: string;
    readonly sort?: OrderSort;
  }

  export async function getCustomerOrders(
    input: CustomerOrdersInput,
  ): Promise<Result<CustomerOrdersPage, ApiError>> {
    const params = new URLSearchParams();
    if (input.page !== undefined && input.page > 1) params.set('page', String(input.page));
    if (input.state !== undefined) params.set('state', input.state);
    // ... dateFrom/dateTo/sort emitted only when set / non-default ...
    return apiFetch<CustomerOrdersPage>('customer', `/api/v1/orders?${params.toString()}`, { method: 'GET' });
  }
  ```

  `pageSize` is intentionally NOT a URL/helper input at MVP — the backend default of 20 is the only page size the dashboard uses (the §B.8 `pageSize` URL extension stays available if a future ticket needs it).
- **`frontend/src/lib/i18n/cs-CZ.ts`** — new `customer.orders.*` keys.
- **(Conditional)** `(customer)` dashboard layout nav entry for "Objednávky".

## Alternatives Considered

- **Option A — Client store (Zustand / React Query) with background revalidation.** *Rejected per A.2* — CLAUDE.md forbids client state libraries; server state lives in the backend. URL-state + SSR re-render gives shareable, back-button-correct views for free, matching every existing list page.
- **Option B — `'use client'` page fetching in `useEffect`.** *Rejected per A.2 + §B.1* — the pre-ADR-0024 legacy shape. ADR 0024 exists precisely so authenticated SSR pages work; regressing to client fetch loses TTFB and re-opens the pattern split T-0049 closed.
- **Option C — Per-row unread count via messages endpoint fetch.** *Rejected per A.1* — N+1 request fan-out (20 rows = 20 extra calls per render). T-0089's projection delivers the count in the same list payload at O(1) per row.
- **Option D — Ship without unread badges; retrofit post-bundle.** *Rejected per A.1 (user lock Q7)* — the badge is the dashboard's "needs my attention" signal; T-0079/T-0089 already paid the backend cost.
- **Option E — Infinite scroll / "Načíst další" append.** *Rejected* — §B.8 locked `<Link>` pagination across all list surfaces (katalog, maker produkty); infinite scroll breaks back-button restore and URL shareability for filtered views.
- **Option F — Fetch a large page once and filter/sort client-side.** *Rejected* — duplicates T-0080's backend filtering as client business logic (forbidden), breaks at >50 rows (backend cap), and lies about `totalCount`.
- **Option G — Adopt a table library (TanStack Table).** *Rejected* — a static read-only table with server-side everything needs zero client table logic; new dependency for negligible gain.
- **Option H — Mount the list at `/dashboard/zakaznik` root.** *Rejected per §C.1* — sub-route keeps parity with `/dashboard/zakaznik/profile` + maker dashboard granularity and leaves the root free for a future dashboard home.

## Out of scope

- **Order tracking detail page** — T-0086b owns `/objednavka/[id]` post-payment states + the message thread.
- **Maker-side order queue + detail** — T-0087a/T-0087b.
- **Invoice download surface** — T-0086b renders the link; T-0088 owns the endpoint.
- **Backend changes of any kind** — T-0080/T-0089 shipped the contract; this ticket is consume-only. No NSwag regen here (T-0089 is the regen gate).
- **OrderNumber text search / extra filters / extra sorts** — explicitly rejected at T-0080 §A.2/§C; the frontend exposes exactly the locked param set.
- **"Needs action" pseudo-state filter** — not at MVP (T-0080 out-of-scope carries over).
- **401 → refresh → retry wiring in `apiFetch`** — post-launch roadmap per §B.3; this page redirects to login on `Unauthorized`.
- **Dashboard landing page / customer dashboard home redirect.**
- **CSV export, bulk actions, realtime updates.**

## Acceptance criteria

- **AC-1** Given a logged-in customer with 3 orders, when `/dashboard/zakaznik/objednavky` is server-rendered (verify with JS disabled), then the list shows 3 rows sorted newest-first (CreatedAt DESC), rendered server-side via the forwarded audience cookie (ADR 0024) — no client fetch waterfall, no loading flash for the initial data.
- **AC-2** Given any order row, when rendered, then it shows: order number, state badge labeled via the existing `order.state.*` key, maker name, product title (or "Vlastní zakázka" when `productTitle` is null), total via `formatCzk` (`1 234 Kč`, haléře stripped), and the created date in Czech short format (`9. 5. 2026`).
- **AC-3** Given an order with `unreadMessageCount = 2`, when the row renders, then an unread badge displaying the count is visible; given `unreadMessageCount = 0`, no badge renders. The value comes from the T-0089-regenerated `CustomerOrderListItemDto` — no per-row message fetch appears in the network log.
- **AC-4** Given the customer selects state "Zaplaceno" in the filter dropdown, when the filter applies, then the URL becomes `?state=Paid`, the page is reset to 1, and the server-rendered list contains only Paid orders (backend-filtered; `totalCount` reflects the filtered count).
- **AC-5** Given the customer sets a date range, when applied, then `?dateFrom=...&dateTo=...` drive the SSR query; given the backend rejects an inverted range (400 validation), then the page renders the error alert with Czech copy mapped from the error code — no crash, no raw English message.
- **AC-6** Given the customer picks "Nejdražší" (TotalAmountDesc) in the sort selector, when applied, then `?sort=TotalAmountDesc` is in the URL and rows order by total descending; the default sort emits NO `sort` param (canonical URL stays clean per §B.8).
- **AC-7** Given 25 orders, when the customer clicks page 2, then the URL is `?page=2` with all active filter/sort params preserved, rows 21–25 render, and the browser back button returns to page 1 with identical filter state.
- **AC-8** Given a customer with zero orders and no active filters, when the page loads, then the empty state renders with a "Prohlédnout katalog" CTA linking to `/katalog` (US-customer-0016 AC-3). Given filters reduce results to zero, then the "no match" variant with a clear-filters link renders instead.
- **AC-9** Given any row is clicked, when navigation completes, then the browser is at `/objednavka/{orderId}` for that order.
- **AC-10** Given viewports 375 / 768 / 1280, when the page renders, then: stacked cards at 375 (no horizontal scroll), table layout at 768+, all controls reachable and tappable. Verified in the manual QA pass.
- **AC-11** Given no valid customer session, when the page is requested, then the user is redirected to `/auth/login` (typed `Unauthorized` ApiError → `redirect()`); no partial data renders.
- **AC-12** Build, lint, and typecheck clean. Zero `any`, zero `console.*`, zero `useEffect` data fetching, zero client-store imports. All new strings via `customer.orders.*` i18n keys (plural-neutral per §B.18). `lib/api-client/` untouched (pre-commit hook passes). Route code imports only from `lib/api-client-helpers/customer-orders.ts`, never the generated client.

## Technical notes

### Why URL-state instead of a client store

Every input the list needs (page, state, dateFrom, dateTo, sort) is already a query parameter on T-0080's locked GET contract — encoding them in the page URL means the frontend holds zero duplicated state. The URL is shareable ("here's my filtered view" pasted into support), the back-button restores the exact previous view including filters, and the Server Component re-render guarantees the data always matches the URL with no cache-invalidation logic. A client store would re-implement all three behaviors by hand and still need the URL for deep links. This is the same reasoning T-0080 §A.3 used for GET-with-query-params on the backend; the frontend simply completes the round trip.

### Why the unread count must come from the list payload

T-0079 denormalized `customer_unread_message_count` onto the Order row precisely so list consumers read it as a flat field (T-0079 §A.3 rejected per-row subqueries as N+1). Re-introducing the N+1 on the frontend — 20 `messagesGET` calls per list render to count unread — would undo that decision at a worse layer (HTTP round trips instead of SQL). The badge is a one-field read; T-0089's projection + regen is the only correct source.

### Why invalid searchParams are ignored, not errored

The page whitelists `state` against the `OrderState` enum and `sort` against `OrderSort` before forwarding; an unrecognized value (typo'd URL, stale bookmark after an enum rename) silently falls back to the default instead of rendering an error page. This is display-side input canonicalization, not validation — the backend Validator remains the authority for anything that reaches it (page clamps, date-range inversion). A hand-edited `?state=Banana` URL degrading to "all orders" is strictly friendlier than a 400 page, and the filter bar re-canonicalizes the URL on next interaction.

### Why default params are omitted from generated URLs

Per §B.8 (T-0049 precedent), the pagination and filter link builders only emit a param when it diverges from the default: `?page=2`, never `?page=2&sort=CreatedAtDesc&pageSize=20`. Canonical URLs deduplicate browser history entries, keep shared links readable, and avoid cache-key explosion if response caching is ever added in front of the SSR layer.

### Why Unauthorized redirects instead of rendering an error alert

A dashboard page with no session has exactly one useful next step: log in. Rendering an inline "you are not authorized" alert on an empty dashboard shell is a dead end; `redirect('/auth/login')` matches the existing auth-page bounce convention (§B.3) and the eventual 401-refresh wrapper will make the redirect rare (expired-access-token cases will silently recover). The redirect happens server-side during SSR, so the user never sees a flash of empty UI.

## Risk / mitigation

- **T-0089 regen gate slips and `unreadMessageCount` is missing from the client.** *Mitigation:* hard `depends_on`; the helper's re-exported type makes the missing field a compile-time error, not a silent `undefined` render. Per the no-mocks rule, the badge stays loudly broken until T-0089 merges.
- **Date param serialization drift** (helper sends a format the backend binder rejects). *Mitigation:* helper serializes `<input type="date">` values as ISO `yyyy-MM-dd`; AC-5 manually verifies both the happy path and the backend 400 path against the preview.
- **Filter UI state desync from URL** (back-button shows stale dropdown values). *Mitigation:* the filter bar derives its initial values from the canonicalized server-read params (katalog `filters-client` precedent) — URL is the single source of truth.
- **Czech plural trap on the unread badge** ("2 zprávy" vs "5 zpráv"). *Mitigation:* plural-neutral phrasing per §B.18 (count + noun-free badge, e.g., numeric badge with accessible label "Nepřečtené zprávy: N").
- **Expired access token with valid refresh on SSR** renders the login redirect for a "logged-in" user (known ADR 0024 out-of-scope). *Mitigation:* accepted MVP behavior, same as every existing dashboard page; tracked on the post-launch 401-refresh roadmap item.

## Test plan reference

Manual QA plan at **`docs/test-plans/T-0086a.md`** — Playwright-style step list executed against the Vercel preview (stub authored alongside implementation): SSR render with JS disabled, filter/sort/page URL-state walk, empty-state both variants, unread badge presence/absence, 375/768/1280 sweep, unauthenticated redirect. No automated frontend test harness exists at MVP; the plan is the verification artifact.

## Files touched (expected)

### New
- `frontend/src/app/(customer)/dashboard/zakaznik/objednavky/page.tsx`
- `frontend/src/app/(customer)/dashboard/zakaznik/objednavky/filters-client.tsx`
- `frontend/src/app/(customer)/dashboard/zakaznik/objednavky/order-row.tsx`
- `frontend/src/app/(customer)/dashboard/zakaznik/objednavky/pagination.tsx`
- `frontend/src/app/(customer)/dashboard/zakaznik/objednavky/loading.tsx`
- `frontend/src/lib/api-client-helpers/customer-orders.ts`
- `docs/test-plans/T-0086a.md`

### Modified
- `frontend/src/lib/i18n/cs-CZ.ts` — `customer.orders.*` keys.
- `(customer)` dashboard layout — nav entry (only if a nav exists; verify at impl time).
- `docs/tickets/INDEX.md` — PM flips T-0086a to `**done**` post-merge.

## Commits hint

1. **`feat(T-0086a): customer-orders api helper + i18n keys`** — `customer-orders.ts` wrapper (`getCustomerOrders`) + type re-exports + `customer.orders.*` catalog entries.
2. **`feat(T-0086a): orders list page + filters + pagination`** — page.tsx + filters-client + order-row + pagination + loading skeleton.
3. **`chore(T-0086a): responsive polish + manual QA plan`** — 375/768/1280 sweep fixes + `docs/test-plans/T-0086a.md`.

## Status log

- 2026-06-09 `draft` by PM. Created as the first frontend ticket in the order-dashboards bundle (T-0088 → T-0089 → **T-0086a** → T-0086b → T-0087a → T-0087b), downstream of the checkout-flow bundle. Reference precedents on master: T-0046 catalog page (`force-dynamic` SSR + URL-state filters + `<Link>` pagination), T-0049 maker produkty (first authenticated SSR list, ADR 0024), T-0080 backend list contract (params + PagedData envelope), T-0079/T-0089 unread-count projection. Slice scope: 1 route (5 files) + 1 api-client helper + i18n keys + manual QA plan. No backend changes, no NSwag regen, no new error codes.
- 2026-06-09 `draft → ready` by PM. User locked 2 blocking dimensions at the dashboards grooming session: **A.1 (Q7)** unread badges on the list fed by T-0089's projection (rejected per-row fetch + badge-less launch); **A.2 (Q5)** SSR + URL-state with no client store (rejected client store + client-rendered page). 10 PM-absorbed decisions captured in §C (route placement, filter/sort/pagination shapes, state-badge mapping, empty-state variants, row link target, custom-order label, responsive layout, helper shape, error handling, i18n keys). 7 pattern/ADR locks extracted in §B. **Ready for frontend.** Implement after T-0089's NSwag regen merges; T-0086b stacks on the same helper file in the same bundle.

## Definition of Ready checklist

- [x] Linked user story present (US-customer-0016; AC-1/2/3 all covered).
- [x] Acceptance criteria observable + numbered (AC-1 through AC-12).
- [x] Locked design decisions captured (§A user-locked Q5/Q7, §B pattern/ADR-locked, §C PM-absorbed).
- [x] Alternatives Considered with ≥1 rebutted alternative per locked dimension (Options A–H).
- [x] Out of scope explicit (detail page, maker side, invoice endpoint, search, 401-refresh).
- [x] Risk / mitigation called out for the 5 leading risks.
- [x] Test plan referenced (`docs/test-plans/T-0086a.md` stub; manual Vercel-preview QA).
- [x] Files touched listed (new + modified).
- [x] Layers / ADRs / dependencies in the frontmatter (depends_on T-0089 + T-0080).
- [x] Security-touching: NO (read-only authenticated page; auth via existing cookie pipeline).
- [x] Size: M.
- [x] No NSwag regen in this ticket (T-0089 is the bundle's regen gate).
- [x] No business logic client-side; backend authoritative for filtering/validation.
