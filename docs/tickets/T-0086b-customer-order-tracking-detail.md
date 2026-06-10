---
id: T-0086b
title: Customer order tracking detail (post-payment states) + shared OrderMessageThread component
status: ready
size: M
owner: frontend
created: 2026-06-09
updated: 2026-06-09
depends_on: [T-0088, T-0082, T-0079, T-0076, T-0084b]
blocks: [T-0087b]
user_stories: [US-customer-0012, US-customer-0013, US-customer-0014, US-customer-0017]
adrs: [0022, 0024]
phase: 4
manual_steps:
  - "Manual QA pass on the Vercel preview per docs/test-plans/T-0086b.md (375/768/1280, polling visibility-pause check via network tab)"
security_touching: false
layers: [frontend, frontend-i18n]
---

# T-0086b — Customer order tracking detail (post-payment states) + shared OrderMessageThread component

## Context

T-0086b is the **fourth ticket in the order-dashboards bundle** (`feat/order-dashboards-bundle`: T-0088 invoice-download endpoint → T-0089 backend slices + NSwag regen gate → T-0086a customer list → **T-0086b customer tracking detail** → T-0087a maker queue → T-0087b maker detail). It extends the `/objednavka/[id]` route that **T-0084b** (checkout-flow bundle, ships first) creates for the `PendingPayment` payment-retry surface: T-0084b owns the pre-payment view; **T-0086b owns every state from `Paid` onward**. The page branches on `CustomerOrderDetailDto.state` — `PendingPayment` keeps rendering T-0084b's surface untouched; all other states render the tracking detail this ticket ships.

The backend contract is fully merged: `GET /api/v1/customer/orders/{orderId}` (T-0082) returns the audience-scoped `CustomerOrderDetailDto` with all 5 nullable lifecycle timestamps, the full price breakdown, `shippingCarrierTrackingUrl`, inline `attachments[]` (with pre-baked `downloadUrl`), and nullable `invoicePdfUrl`; `POST .../deliver` (T-0076) transitions `Shipped → Delivered` with Silent-Success idempotency; the messages trio (T-0079) provides `GET/POST .../messages` + `POST .../messages/mark-read` with the 2000-char body cap, the `PendingPayment` posting guard, and the denormalized unread counters that T-0086a's badges read. **T-0088 makes `invoicePdfUrl` real** (the customer invoice-download endpoint per US-customer-0017) — this ticket renders the link only when the field is non-null and hard-depends on T-0088.

This ticket satisfies **US-customer-0012 — Track order status** (timeline, tracking link, 404-not-403 on foreign orders, confirm-delivery button), **US-customer-0013 — Confirm delivery** (the customer-source path; auto/carrier paths are backend-only), **US-customer-0014 — Message the maker** (the customer-side thread UI), and the link surface of **US-customer-0017 — Download an invoice**.

The headline deliverable beyond the page itself is the **shared `OrderMessageThread` client component**: T-0086b creates it, T-0087b reuses it verbatim on the maker detail page. It is audience-agnostic by construction — it receives injected `Result`-returning callbacks (fetch page, post, mark-read) so each consumer plugs in its own host's helpers. It is also the platform's **only polling surface** (locked Q5 exception): a ~30-second interval refresh while the tab is visible, paused on `visibilitychange`, cleared on unmount. Everything else on the page stays SSR + `router.refresh()` after actions per the bundle-wide Q5 lock.

**No business logic ships client-side.** State-dependent rendering (which button, which note) is a display lookup over the backend-provided `state` field; the 2000-char post-box limit is a UX mirror; the backend remains authoritative for every transition and validation (T-0076 idempotency, T-0079 guards).

## Locked design decisions

Captured per `docs/process/deliberation.md`. The user locked 2 dimensions at the 2026-06-09 dashboards grooming session (Q5, Q6). 11 PM-absorbed decisions follow from the T-0076/T-0079/T-0082/T-0084b contracts and the §B pattern locks.

### A. User-locked at grooming (non-negotiable)

1. **Inline message thread at the bottom of the detail page (Q6), built as the shared `OrderMessageThread` component.** THIS ticket creates `components/dashboard/order-message-thread.tsx`; T-0087b reuses it. Behavior: paged messages **newest first** (T-0079 sort, 50/page) with a "load older" affordance; a post box with the ≤2000-char UX mirror; **mark-read fired on thread render** (zeroes the T-0086a list badge); **polls ~30 s while visible** — the Q5 exception and the ONLY polling surface on the platform, with visibility-pause (no polling in hidden tabs) and cleanup on unmount. **Rejected:** separate `/objednavka/[id]/zpravy` route (splits the single coordination surface; the thread is why the customer returns to the page); WebSocket/SSE realtime (out of MVP; T-0079 explicitly shipped async messaging with no realtime layer).

2. **SSR + `router.refresh()` after actions (Q5).** The page is a Server Component fetching the detail via the forwarded audience cookie. Mutations (confirm delivery, post message) run in client event handlers via `apiFetch` helpers; on success the surface re-syncs — `router.refresh()` for the order detail (state badge, timeline, buttons), a thread refetch for messages. No client store, no `useEffect` data fetching outside the locked polling exception. **Rejected:** client-rendered page (pre-ADR-0024 shape); polling the whole detail via interval `router.refresh()` (full SSR re-render every 30 s is heavyweight; only the thread polls — order state changes pull on user action).

### B. ADR + pattern-locked (no relitigation)

- **patterns.md §B.1** — Server Components by default; `'use client'` only for the interactive islands (thread, action buttons); no business logic client-side. Validation mirrors (char cap) are UX-only duplicates; backend authoritative.
- **patterns.md §B.4 + §B.16 (ADR 0022)** — every endpoint gets a hand-written `Result<T, ApiError>` wrapper in `lib/api-client-helpers/customer-orders.ts` (extends T-0086a's file); route + component code never imports the generated client; `lib/api-client/` never edited manually.
- **patterns.md §B.14 (ADR 0024)** — SSR detail fetch authenticates via cookie forwarding. 401-refresh not wired (§B.3); `Unauthorized` → `redirect('/auth/login')`.
- **patterns.md §B.9** — `generateMetadata` branches the title ONLY on `NotFound`; transient errors fall back to the brand title. `NotFound` in the page → `notFound()` + sibling `not-found.tsx` (404 not 403 per US-customer-0012 AC-3 — the backend already collapses "not yours" into `order.notFound`).
- **patterns.md §B.5 + §B.18** — all strings from `cs-CZ.ts`; every backend error code renders via its parity i18n key (`order.notFound`, `order.invalidTransition`, `order.notPayableYet`, `OrderMessageBodyEmpty`/`TooLong` parity keys — verify present from the T-0079 parity gate, add if missing).
- **patterns.md §B.7 + §B.10** — `<section>` route wrapper; `formatCzk` for all money; cs-CZ date-time formatting.
- **T-0082 §C action-button stance** — the response is pure data; the FE conditionally renders actions by inspecting `state`. No state-machine re-implementation: one `state → visible actions` display map.

### C. PM-absorbed (no user input needed)

- **State-dependent layout:** `state === PendingPayment` → render T-0084b's existing payment-retry surface unchanged (this ticket must not regress it); every other state → tracking detail (header, timeline, breakdown, shipping, attachments, invoice, thread). One branch at the top of the page component.
- **Vertical lifecycle timeline** built from the nullable timestamps: Vytvořeno (`createdAt`) → Zaplaceno (`paidAt`) → Přijato (`acceptedAt`) → Odesláno (`shippedAt`) → Doručeno (`deliveredAt`). Steps with a timestamp render filled + Czech date-time; future steps render muted. **Cancelled branch:** when `cancelledAt` is non-null, remaining steps are replaced by a terminal "Zrušeno" node with the timestamp. `Completed`/`Refunded`/`Disputed` have no dedicated timestamps — the state badge in the header carries them; the timeline ends at its last filled node.
- **Price breakdown card:** rows for `productPriceMinor`, `shippingPriceMinor`, `vatAmountMinor` (+ rate from `vatRateBp` rendered as `%`), total `totalAmountMinor` — all via `formatCzk`.
- **Tracking link:** rendered iff `shippingCarrierTrackingUrl` is non-null (T-0082: populated only for Zásilkovna with a created packet); external link, `target="_blank"` + `rel="noopener noreferrer"` (US-customer-0012 AC-2).
- **Confirm-delivery button:** visible iff `state === Shipped` (US-customer-0012 AC-4). Caption uses the key T-0076 reserved: `customer.orders.markDeliveredButton` = "Označit jako doručeno" (US-customer-0012/0013 draft wording "Potvrdit doručení" is superseded by the T-0076 i18n reservation; final copy belongs to l10n). Click → `deliver(orderId)` helper → on success `router.refresh()` (badge flips to Doručeno, button disappears); on failure render the mapped Czech error (`order.invalidTransition` covers the race where auto/carrier delivered first — though T-0076's Silent Success means the common race returns 200). Button disabled + spinner while in flight.
- **Attachments card:** lists `attachments[]` (filename, human-readable `sizeBytes`); each row downloads via an authenticated `apiFetch` blob fetch against `downloadUrl` + programmatic anchor (audience cookies are `SameSite=Strict`, so a plain cross-origin `<a href>` would drop them — implementer verifies against the T-0064 endpoint and keeps the blob path if confirmed). Empty list → card hidden.
- **Invoice download link:** rendered **iff `invoicePdfUrl` is non-null** (T-0088 makes the URL real; pre-invoice orders show nothing — no dead link, satisfying US-customer-0017 AC-3 since the backend nulls the field until the invoice exists). Same authenticated download mechanics as attachments.
- **`OrderMessageThread` component contract** (`frontend/src/components/dashboard/order-message-thread.tsx`, `'use client'`): props = `orderId`, `initialPage` (SSR-fetched page 1 passed from the server page — keeps first paint data-complete per §B.1), `canPost: boolean`, and injected callbacks `{ fetchMessages(page), postMessage(body), markRead() }` each returning `Result<…, ApiError>`. Renders newest-first; "Načíst starší zprávy" appends older pages (local component state — allowed UI state); own messages right-aligned via `isMine`; author name + Czech timestamp per message. `POLL_INTERVAL_MS = 30_000` constant. Mark-read fires on mount and re-fires after any poll/post-refetch that delivers new counterparty messages (keeps the dashboard badge at 0 while the thread is open; backend mark-read is idempotent per T-0079).
- **Post box:** `Textarea` primitive + char counter, `maxLength={2000}` UX mirror, submit disabled while empty/in-flight. On success: clear box + refetch thread page 1. Backend 400s (`OrderMessageBodyEmpty`/`TooLong`) and the `order.notPayableYet` guard render via parity i18n keys — the mirror never replaces the backend rule.
- **Thread state-guard awareness:** `canPost = state !== PendingPayment` per the T-0079/US-customer-0014 AC-2 ruling — on `PendingPayment` the post box is hidden and an info note ("messaging opens after payment") renders. Moot in practice on this page (PendingPayment renders T-0084b's surface, thread omitted entirely), but the prop contract carries it so T-0087b and future consumers inherit the guard.
- **Helper extensions** in `lib/api-client-helpers/customer-orders.ts`: `getCustomerOrderDetail(orderId)`, `markOrderDelivered(orderId)`, `getOrderMessages(orderId, page)`, `postOrderMessage(orderId, body)`, `markOrderMessagesRead(orderId)`, `downloadOrderFile(url)` — all `apiFetch`-based, types re-exported from the generated customer client.
- **i18n:** new `customer.orderDetail.*` keys (timeline labels, breakdown labels, tracking/attachments/invoice headings + actions, thread headings, post-box placeholder/send/counter, load-older, pending-payment note). Plural-neutral per §B.18.

## Scope

- **`frontend/src/app/(customer)/objednavka/[id]/page.tsx`** — MODIFY (T-0084b's file). Add the state branch; render the tracking detail for `Paid`+: header (order number, state badge, maker, product/"Vlastní zakázka"), timeline, price breakdown, shipping block (method + tracking link), attachments card, invoice link, `OrderMessageThread` (server-fetched `initialPage` via `getOrderMessages(orderId, 1)`). `generateMetadata` per §B.9; `notFound()` on `NotFound`; `redirect('/auth/login')` on `Unauthorized`.
- **`frontend/src/app/(customer)/objednavka/[id]/not-found.tsx`** — NEW (if T-0084b didn't ship it) — friendly Czech 404.
- **`frontend/src/components/dashboard/order-message-thread.tsx`** — NEW shared `'use client'` component per §C (the T-0087b reuse target). Expected contract (final shape at impl time):

  ```ts
  export interface OrderMessage {
    readonly id: string;
    readonly authorName: string;
    readonly body: string;
    readonly createdAt: string; // ISO; formatted via lib/utils/dates
    readonly isMine: boolean;
  }

  export interface OrderMessagesPage {
    readonly items: readonly OrderMessage[];
    readonly page: number;
    readonly totalCount: number;
    readonly hasNextPage: boolean;
  }

  export interface OrderMessageThreadProps {
    readonly orderId: string;
    readonly initialPage: OrderMessagesPage; // SSR-fetched page 1
    readonly canPost: boolean;               // false on PendingPayment (T-0079 guard)
    readonly fetchMessages: (page: number) => Promise<Result<OrderMessagesPage, ApiError>>;
    readonly postMessage: (body: string) => Promise<Result<unknown, ApiError>>;
    readonly markRead: () => Promise<Result<unknown, ApiError>>;
  }

  export const POLL_INTERVAL_MS = 30_000; // Q5 exception — the ONLY polling surface
  ```

  No `lib/api-client-helpers` imports inside the component — consumers inject the callbacks (T-0087b passes maker-host equivalents). Internal state: loaded pages (append-on-load-older), draft text, in-flight flags. Lifecycle: mount → `markRead()` → start interval; `visibilitychange` hidden → pause; visible → immediate refetch + resume; unmount → clear interval.
- **`frontend/src/app/(customer)/objednavka/[id]/order-actions-client.tsx`** — NEW `'use client'` island: confirm-delivery button + download buttons (blob mechanics), `router.refresh()` on success.
- **`frontend/src/app/(customer)/objednavka/[id]/timeline.tsx`** — NEW presentational (server-safe) timeline component.
- **`frontend/src/lib/api-client-helpers/customer-orders.ts`** — MODIFY: add the 6 wrappers per §C.
- **`frontend/src/lib/i18n/cs-CZ.ts`** — `customer.orderDetail.*` keys; verify `order.notPayableYet` + message-body error parity keys exist (T-0079 gate), add if missing.

## Alternatives Considered

- **Option A — Separate `/objednavka/[id]/zpravy` route for the thread.** *Rejected per A.1 (Q6)* — the thread is the order's coordination surface (the GDPR lock makes it the ONLY maker channel); splitting it behind a second click buries it and doubles the page surface for zero gain.
- **Option B — WebSocket / SSE realtime thread.** *Rejected per A.1* — T-0079 shipped async messaging with an explicit no-realtime stance; a socket layer (infra, reconnect logic, per-host auth) is wildly over-scoped for MVP threads measured in messages per day. 30 s visible-tab polling approximates liveness at near-zero cost.
- **Option C — Two thread components (customer copy + maker copy).** *Rejected per A.1* — T-0087b reuse is locked; injected `Result`-returning callbacks keep the component audience-blind, mirroring the backend's compile-time audience split without duplicating ~200 lines of thread UI.
- **Option D — Poll the entire order detail (interval `router.refresh()`).** *Rejected per A.2* — full SSR re-render every 30 s multiplies backend load and discards thread-local state (draft message text). Only the thread polls; the detail re-syncs on user action.
- **Option E — Optimistic message append (no refetch after post).** *Rejected* — refetching page 1 after post keeps the server authoritative (IDs, timestamps, ordering) and costs one small request; optimistic state invites drift with the concurrent-poll path.
- **Option F — Render the confirm-delivery button in all states, disabled outside `Shipped`.** *Rejected per T-0082 §C* — visible-but-dead actions confuse; the locked stance is conditional rendering from the `state` field (display map, not a client state machine).
- **Option G — Client constructs the invoice URL from the invoice number.** *Rejected per T-0082 §A.3* — backend owns URL construction; the FE renders `invoicePdfUrl` verbatim and only when non-null. T-0088's endpoint shape stays a backend-private concern.
- **Option H — Mark-read on post-box focus instead of thread render.** *Rejected per A.1 (Q6)* — read-only visits (the common case: "did the maker reply?") would never clear the T-0086a badge. Mark-read-on-render matches what the user actually did: they read the thread.
- **Option I — New standalone route `/objednavka/[id]/sledovani` instead of extending T-0084b's page.** *Rejected* — two routes for one resource forks the canonical order URL used in emails (T-0067/T-0076 ActionUrl pre-bakes `/objednavka/{id}`); the state branch inside one route keeps every emailed link correct for the order's whole life.

## Out of scope

- **PendingPayment surface** (payment retry, Comgate redirect) — T-0084b owns it; this ticket only preserves it behind the state branch.
- **Maker detail page + maker thread consumer** — T-0087b (reuses `OrderMessageThread`).
- **Invoice-download backend endpoint** — T-0088 (this ticket renders the link only).
- **Review submission UI** (US-customer-0015) — separate ticket; the Delivered state renders no review CTA yet.
- **Read receipts, typing indicators, message attachments, edit/delete** — T-0079 out-of-scope carries over.
- **Push/SMS/browser notifications for new messages** — email digest only (T-0079).
- **Polling anywhere else** — the thread is the single locked polling surface; no other component may adopt an interval.
- **401 → refresh → retry wiring in `apiFetch`** — post-launch roadmap per §B.3.
- **Backend changes / NSwag regen** — contract fully shipped by T-0076/T-0079/T-0082/T-0088/T-0089.

## Acceptance criteria

- **AC-1** Given a customer's own order in `Paid` or later, when `/objednavka/{id}` is server-rendered (JS disabled check), then the page shows order number, state badge (`order.state.*` key), maker name, product title (or "Vlastní zakázka"), fetched SSR via the forwarded audience cookie — no client fetch for the initial paint.
- **AC-2** Given an order with `paidAt` + `acceptedAt` set and `shippedAt` null, when the timeline renders, then Vytvořeno/Zaplaceno/Přijato show filled with Czech timestamps and Odesláno/Doručeno render muted. Given `cancelledAt` non-null, then the timeline terminates in a "Zrušeno" node with its timestamp and no muted future steps.
- **AC-3** Given the price breakdown card, when rendered, then product, shipping, VAT (amount + `%` from `vatRateBp`), and total rows all display via `formatCzk` and sum consistently with the backend-provided fields (no client arithmetic beyond display).
- **AC-4** Given `shippingCarrierTrackingUrl` is non-null, when the shipping block renders, then the tracking link opens the carrier page in a new tab; given null (personal pickup / pre-shipment), no tracking link renders.
- **AC-5** Given `state === Shipped`, when the page renders, then the "Označit jako doručeno" button is visible; clicking it calls the deliver endpoint and on success `router.refresh()` re-renders the page with state Doručeno and the button gone. The button does NOT render in any other state.
- **AC-6** Given the deliver call fails with `order.invalidTransition` (409), when the error returns, then the mapped Czech copy renders inline and the page stays usable; no raw error text, no crash.
- **AC-7** Given an order with 2 attachments, when the attachments card renders, then both rows show filename + human-readable size and download via the authenticated fetch path (file arrives with correct content; verified on preview). Given zero attachments, the card is absent.
- **AC-8** Given `invoicePdfUrl` is non-null, when the page renders, then the "Stáhnout fakturu" link is visible and downloads the PDF; given null, NO invoice element renders (no disabled/dead link).
- **AC-9** Given the thread renders with unread maker messages, when the page loads, then mark-read fires (verified: returning to `/dashboard/zakaznik/objednavky` shows the row badge at 0), messages render newest-first with author name, Czech timestamp, and `isMine` alignment, and "Načíst starší zprávy" appends page 2 when >50 messages exist.
- **AC-10** Given the customer types a valid message and submits, when the post succeeds, then the box clears and the new message appears via the page-1 refetch. Given 2001 chars, the UX mirror blocks input client-side AND a forced backend 400 (`OrderMessageBodyTooLong`) renders its parity Czech copy.
- **AC-11** Given the thread is mounted in a visible tab, when ~30 s elapses, then exactly one poll request fires per interval (network tab); when the tab is hidden, polling stops; on return it resumes; after navigation away no further requests fire. No other component on the page polls.
- **AC-12** Given a foreign or nonexistent order id, when the page is requested, then `notFound()` renders the 404 page (never 403, never an existence oracle) and the document title branches per §B.9 only on `NotFound`. Given `state === PendingPayment`, then T-0084b's payment surface renders unchanged — no tracking detail, no thread.
- **AC-13** Build, lint, typecheck clean. Zero `any`, `console.*`, client-store imports, or `useEffect` data fetching outside the locked thread polling. All strings via i18n keys; error-code parity verified for `order.notFound`, `order.invalidTransition`, `order.notPayableYet`, message-body codes. `lib/api-client/` untouched; responsive at 375/768/1280 per the manual QA plan.

## Technical notes

### Why the thread is the only polling surface (and why ~30 s)

The thread is the one part of the page whose data changes without the customer doing anything — the maker can reply at any moment, and a 5-minute email digest (T-0079 §A.2) is the wrong latency for a customer who is actively sitting in the conversation. Everything else on the page (state, timestamps, breakdown) only changes through backend transitions the customer either triggers themselves (deliver → `router.refresh()`) or learns about via email; polling those would be pure waste. 30 s splits the difference between conversational liveness and load: at MVP traffic a visible thread costs two list reads per minute, and the visibility-pause means a wall of background tabs costs nothing. The interval is a named constant so a future tune is a one-line change.

### Why `initialPage` is SSR-fetched and passed as a prop

§B.1 forbids `useEffect` data fetching for first paint, and the locked polling exception covers refreshes only. The server page fetches messages page 1 alongside the order detail (both under the forwarded cookie, ADR 0024) and hands the result to the client component — the thread is data-complete on first render with zero client waterfall, and the poll loop only takes over for subsequent deltas. This also keeps the component honest as a shared artifact: T-0087b's maker page does the same SSR prefetch with its own helper.

### Why injected callbacks instead of a `host` prop

A `host: 'customer' | 'maker'` prop would force the shared component to import both audiences' helpers and branch internally — re-creating, in miniature, the runtime-audience-flag shape the backend rejected at T-0082 §A.1. Injected `Result`-returning callbacks invert the dependency: the component knows how to render a thread; each consumer knows how to talk to its host. The customer page passes wrappers over `messagesGET`/`messagesPOST`/`markRead` from `customer-orders.ts`; T-0087b passes its maker equivalents. The component stays free of any `lib/api-client-helpers` import entirely.

### Why mark-read fires on render (and why re-fires are safe)

The dashboard badge (T-0086a) is fed by the denormalized counter that ONLY mark-read resets — if reading the thread didn't clear it, the badge would lie until the customer happened to post. Firing on render matches the user's actual behavior (they read the messages), and re-firing after polls that deliver new counterparty messages keeps the counter at zero for the whole visit. T-0079 made mark-read idempotent and side-effect-free beyond the reset (no outbox emission), so repeated calls are cheap and race-proof by design — it also clears the debounce pointer, so the maker's next message after the visit emails immediately.

### Why a state branch in one route, not a second route

T-0067/T-0076 email payloads pre-bake `ActionUrl = {WebBaseUrl}/objednavka/{id}` — the same URL must be correct whether the order is awaiting payment, in transit, or delivered. Forking post-payment tracking to a sibling route would either break emailed links for half the lifecycle or require a redirect hop on every visit. One route, one `state` branch: T-0084b's surface for `PendingPayment`, this ticket's surface for everything after.

### Why downloads go through `apiFetch` instead of plain anchors

The audience cookies are `HttpOnly + SameSite=Strict` (§B.3). A plain `<a href>` to the API host is a cross-site top-level navigation, and Strict cookies are not sent on it — the download endpoint would 401 even for a logged-in customer. `apiFetch` with `credentials: 'include'` is a same-context fetch where the cookie rides along; the blob + programmatic-anchor pattern turns the authenticated response into a user-visible download. If implementation-time verification shows the deployment topology makes anchors work (same-site domains), the simpler shape may be used — but the spec defaults to the path that is correct under the documented cookie policy.

## Risk / mitigation

- **Route collision with T-0084b** (both tickets edit `/objednavka/[id]/page.tsx`). *Mitigation:* hard `depends_on` + bundle ordering (checkout bundle merges first); T-0086b adds a state branch around T-0084b's surface rather than rewriting it; AC-12 pins the PendingPayment view as a regression gate.
- **Poll + mark-read race** (poll refetch and mark-read overlap; double mark-read). *Mitigation:* backend mark-read is idempotent (T-0079 `MarkAsReadIsIdempotentTests`; second call returns `markedCount: 0`); the component serializes refetch-then-mark-read so ordering is deterministic.
- **Cookie `SameSite=Strict` breaks naive `<a href>` downloads** (attachments/invoice on the cross-origin API host). *Mitigation:* §C locks the authenticated `apiFetch` blob + programmatic-anchor path; implementer verifies against the live T-0064/T-0088 endpoints on preview before closing AC-7/AC-8.
- **Battery/server cost of polling.** *Mitigation:* single locked surface, 30 s interval, visibility-pause, unmount cleanup — AC-11 verifies all four behaviors via the network tab.
- **Draft message lost on `router.refresh()`** (confirm-delivery refresh re-renders the server tree). *Mitigation:* the thread is a client component whose local state survives `router.refresh()` (React reconciliation preserves client islands); manual QA includes "type draft → confirm delivery → draft intact".
- **Missing parity i18n keys for message-body codes.** *Mitigation:* §C verification step; the T-0079 error-code parity gate should have added them — if absent, this ticket adds them (keys, not codes; backend untouched).

## Test plan reference

Manual QA plan at **`docs/test-plans/T-0086b.md`** — Playwright-style step list against the Vercel preview (stub authored alongside implementation): state-branch walk (PendingPayment regression / Paid / Shipped / Delivered / Cancelled), timeline + breakdown checks, deliver happy + 409 path, downloads, thread post/mark-read/load-older, polling visibility matrix, 404 oracle check, 375/768/1280 sweep. No automated frontend harness at MVP; the plan is the verification artifact.

## Files touched (expected)

### New
- `frontend/src/components/dashboard/order-message-thread.tsx` (shared; T-0087b reuse target)
- `frontend/src/app/(customer)/objednavka/[id]/order-actions-client.tsx`
- `frontend/src/app/(customer)/objednavka/[id]/timeline.tsx`
- `frontend/src/app/(customer)/objednavka/[id]/not-found.tsx` (if not shipped by T-0084b)
- `docs/test-plans/T-0086b.md`

### Modified
- `frontend/src/app/(customer)/objednavka/[id]/page.tsx` — state branch + tracking detail composition (T-0084b surface preserved).
- `frontend/src/lib/api-client-helpers/customer-orders.ts` — detail/deliver/messages/download wrappers.
- `frontend/src/lib/i18n/cs-CZ.ts` — `customer.orderDetail.*` keys + parity-key verification.
- `docs/tickets/INDEX.md` — PM flips T-0086b to `**done**` post-merge.

## Commits hint

1. **`feat(T-0086b): customer-orders helper extensions + i18n keys`** — detail/deliver/messages/download wrappers + `customer.orderDetail.*` catalog + parity-key check.
2. **`feat(T-0086b): shared OrderMessageThread component`** — the audience-agnostic thread (paging, post box, mark-read, 30 s visibility-aware polling) with injected callbacks.
3. **`feat(T-0086b): tracking detail page (state branch, timeline, breakdown, actions, downloads)`** — page extension + timeline + actions island + not-found.
4. **`chore(T-0086b): responsive polish + manual QA plan`** — 375/768/1280 sweep + `docs/test-plans/T-0086b.md`.

## Status log

- 2026-06-09 `draft` by PM. Created as the customer-detail ticket in the order-dashboards bundle (T-0088 → T-0089 → T-0086a → **T-0086b** → T-0087a → T-0087b). Reference contracts on master: T-0082 `CustomerOrderDetailDto` (timestamps, breakdown, attachments[], nullable `invoicePdfUrl`/`shippingCarrierTrackingUrl`), T-0076 deliver endpoint (Silent-Success idempotency), T-0079 messages trio (2000-char cap, PendingPayment guard, mark-read idempotency, denormalized unread counters), T-0084b route ownership of the PendingPayment surface, T-0088 invoice-download endpoint. Slice scope: page extension + shared `OrderMessageThread` + actions island + timeline + 6 helper wrappers + i18n keys + manual QA plan. No backend changes, no NSwag regen.
- 2026-06-09 `draft → ready` by PM. User locked 2 blocking dimensions at the dashboards grooming session: **A.1 (Q6)** inline thread at the page bottom as the shared `OrderMessageThread` component with mark-read-on-render and ~30 s visible-tab polling as the platform's only polling surface (rejected separate thread route + realtime layer + per-audience component copies); **A.2 (Q5)** SSR + `router.refresh()` after actions, no client store (rejected client-rendered page + whole-page interval refresh). 11 PM-absorbed decisions captured in §C (state-dependent layout, timeline shape incl. cancelled branch, breakdown card, tracking link, confirm-delivery button wiring + T-0076 caption reservation, authenticated download mechanics, thread component contract + props, post-box mirror, state-guard prop, helper extensions, i18n keys). 7 pattern/ADR locks extracted in §B. **Ready for frontend.** Implement after T-0086a (shares the helper file) and after Bundle 1's T-0084b merges; T-0087b consumes the thread component downstream.

## Definition of Ready checklist

- [x] Linked user stories present (US-customer-0012, -0013, -0014, -0017 link surface).
- [x] Acceptance criteria observable + numbered (AC-1 through AC-13).
- [x] Locked design decisions captured (§A user-locked Q5/Q6, §B pattern/ADR-locked, §C PM-absorbed).
- [x] Alternatives Considered with ≥1 rebutted alternative per locked dimension (Options A–I).
- [x] Out of scope explicit (T-0084b surface, maker side, T-0088 endpoint, reviews, realtime).
- [x] Risk / mitigation called out for the 6 leading risks (route collision, races, SameSite downloads, polling cost, draft loss, parity keys).
- [x] Test plan referenced (`docs/test-plans/T-0086b.md` stub; manual Vercel-preview QA incl. polling matrix).
- [x] Files touched listed (new + modified).
- [x] Layers / ADRs / dependencies in the frontmatter (depends_on T-0088, T-0082, T-0079, T-0076, T-0084b; blocks T-0087b via the shared component).
- [x] Security-touching: NO (consumes existing IDOR-shielded endpoints; 404-not-403 oracle stance preserved client-side).
- [x] Size: M.
- [x] No NSwag regen in this ticket (contract shipped upstream; T-0089 is the bundle's regen gate).
- [x] No business logic client-side; all mirrors UX-only; backend authoritative for transitions, guards, and validation.
