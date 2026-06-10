---
id: T-0087b
title: Maker order detail page + state-machine actions (/dashboard/maker/objednavky/[orderId])
status: ready
size: M
owner: frontend
created: 2026-06-09
updated: 2026-06-09
depends_on: [T-0071, T-0072, T-0073, T-0075, T-0082, T-0086b, T-0088]
blocks: []
user_stories: [US-maker-0006, US-maker-0007, US-maker-0008, US-maker-0009, US-maker-0010, US-maker-0011]
adrs: [0013, 0022, 0024]
phase: 4
manual_steps: ["QA pass on Vercel preview per docs/test-plans/T-0087b.md (Playwright-style manual plan)"]
security_touching: false
layers: [frontend]
---

# T-0087b — Maker order detail page + state-machine actions (`/dashboard/maker/objednavky/[orderId]`)

## Context

T-0087b is the **sixth and final ticket in Bundle 2 `feat/order-dashboards-bundle`** (T-0088 → T-0089 → T-0086a → T-0086b → T-0087a → **T-0087b**). It replaces the placeholder maker order-detail route with the page where the maker actually runs their workflow: read the order (US-maker-0010), accept it (US-maker-0006), ship it via Zásilkovna (US-maker-0007) or hand it over on personal pickup (US-maker-0008), download the label (US-maker-0009), and message the customer (US-maker-0011). Every backend slice is merged: T-0082 detail query (`makerApi.orders2(orderId)` → `GetMakerOrderDetailsResponse`), T-0071 accept, T-0072 ship, T-0073 handover, T-0075 label download, T-0079 messages trio (`messagesGET` / `messagesPOST` / `markRead`). T-0086b (immediately before this ticket in the bundle) ships the **shared `OrderMessageThread` component** that this page reuses with maker-host wiring; T-0088 supplies the invoice-download endpoint that `InvoicePdfUrl` points at.

The page splits cleanly along the Server/Client boundary: a Server Component fetches the detail DTO on render (SSR cookie forwarding per ADR 0024) and renders the read surfaces — payout breakdown, lifecycle timeline, customer contact card (name + phone, **never email** — the field does not exist on `MakerOrderDetailDto` per T-0082 AC-4 compile-time GDPR lock), pickup-point id, attachments, invoice link. Two Client Component islands carry the interactivity: the **action bar** (state-aware buttons → POST → `router.refresh()`) and the **message thread** (T-0086b component, ~30s polling, mark-read on render — which also clears the unread badge the maker saw on the T-0087a list).

The frontend owns **zero transition rules**. Buttons render from two DTO fields the backend already exposed for exactly this purpose (`State` + `ShippingMethod` — T-0082 §C "Action-buttons in maker detail: NOT in response. Frontend inspects State and conditionally renders"); the backend re-validates every transition (T-0071 `order.invalidTransition`, T-0072/73 `shipping.methodNotEligible`, ownership 404s) and the page simply surfaces those error codes through the i18n catalog. If a button is somehow shown stale (state changed in another tab), the POST fails loudly with the backend's verdict and `router.refresh()` reconciles the view.

One contract wrinkle is absorbed here rather than discovered at implementation time: the NSwag-generated `label(orderId)` method returns `Promise<void>` — NSwag's file-response typing discards the PDF body — so the label button cannot use the generated method. The locked fallback (§C) fetches the PDF as a blob through the runtime fetch layer and triggers a named download. Additionally, grep confirms the cs-CZ catalog is missing `order.notFound` + `order.invalidTransition` keys (error-code parity gap); this ticket adds both.

## Locked design decisions

Captured per `docs/process/deliberation.md`. The user locked 2 dimensions at the 2026-06-09 grooming session (Q-action-buttons + Q6/Q5 thread reuse + polling). The rest is ADR/pattern-locked or PM-absorbed from T-0082/T-0086b/shipping-bundle precedents.

### A. User-locked at grooming (non-negotiable)

1. **State-aware action buttons, backend-authoritative.** `State == Paid` → "Přijmout" (POST accept, T-0071). `State == Accepted && ShippingMethod == ZasilkovnaPickupPoint` → "Odeslat" (POST ship, T-0072). `State == Accepted && ShippingMethod == PersonalPickup` → "Předat osobně" (POST handover, T-0073) — **only** for personal pickup; the two Accepted-state buttons are mutually exclusive by shipping method. Any other state → no transition buttons. Each action: POST via the generated client → on success `router.refresh()` (Q5 — the Server Component re-renders with the new state; no client-side state mirroring). **Backend owns ALL transition rules** — the buttons are pure render functions of `State + ShippingMethod`; failures surface as i18n-keyed alerts, never as client-side pre-blocks. **Rejected:** client-side transition guards duplicating the state machine (business logic in the frontend — forbidden; drifts from the backend on the first rule change); optimistic UI flipping the badge before the 200 (lies on failure; `router.refresh()` is cheap and truthful); backend-driven "availableActions[]" field on the DTO (T-0082 §C explicitly rejected putting action hints in the response — re-litigating a closed decision).

2. **Inline message thread = the SHARED `OrderMessageThread` component from T-0086b** (Q6), wired to the maker-host trio (`messagesGET` / `messagesPOST` / `markRead`), polling ~30s (Q5), mark-read fired on render. The component's endpoint-adapter seam (locked in T-0086b) takes the maker helper functions as props — no fork, no copy. Mark-read on render resets `maker_unread_message_count` (T-0079), so the T-0087a list badge clears on back-navigation. **Rejected:** maker-specific thread fork (two divergent thread UIs for one wire shape — T-0086b built the adapter seam precisely to prevent this); link-out to a separate messages page (the thread is order-scoped coordination; US-maker-0010 AC-1 puts it on the detail page); websocket/live updates (no realtime layer at MVP per T-0079 out-of-scope).

### B. ADR + pattern-locked (no relitigation)

- **patterns.md §B.1 — Server Components by default.** `page.tsx` is a Server Component; only the action bar and the thread island are `'use client'`. Client Components call the API **in event handlers** (and the thread's locked poll interval) — no `useEffect` initial-data fetching.
- **patterns.md §B.4 + §B.16 — all calls via `apiFetch`-wrapped helpers.** `lib/api-client-helpers/maker-orders.ts` (created by T-0087a) gains detail + action + label helpers returning `Result<T, ApiError>`.
- **patterns.md §B.14 + ADR 0024 — SSR cookie forwarding** for the detail fetch. ADR 0013 audience enforcement stays backend-side.
- **ADR 0022 — NSwag is the contract.** No backend change in this ticket → no regen; `lib/api-client/` is never hand-edited. The `label()` `Promise<void>` gap is worked around outside the generated file (§C), not by editing it.
- **patterns.md §B.9 — `generateMetadata` branches title only on NotFound.** 404/not-owned detail → Next.js `notFound()` (the backend's single `order.notFound` shape means the page cannot and must not distinguish "missing" from "not yours").
- **patterns.md §B.5 + §B.17 + §B.18 — i18n + `parseErrorResponse`.** Every surfaced error code has a parallel cs-CZ key; RFC7807 bodies flow through the existing parser.
- **patterns.md §B.10 — `formatCzk`** for payout + breakdown figures. No money math client-side.

### C. PM-absorbed (no user input needed)

- **Ship confirm dialog — ship only.** "Odeslat" opens a confirm dialog (UI primitives, not `window.confirm`) stating that a Zásilkovna shipment will be created and the step is irreversible (T-0072: carrier shipment + label generation are real-world side effects). "Přijmout" and "Předat osobně" stay single-click — no external side effect beyond the state transition + notification; adding confirm friction to the most frequent action (accept) hurts the workflow. (Rebuttal recorded as Option C.)
- **Label download button.** Visible iff `State == Shipped && ShippingMethod == ZasilkovnaPickupPoint && shippingCarrierRef != null` (T-0075 surface; hidden for personal pickup per US-maker-0008 AC-2). The generated `label()` returns `Promise<void>` (NSwag file-response gap — body discarded), so the helper fetches `GET /api/v1/maker/files/orders/{orderId}/label` as a **blob** through the runtime fetch layer (extend `lib/runtime/api-fetch.ts` with a blob-returning variant if T-0086a/b's invoice download hasn't already added one — verify at impl time), then object-URL + programmatic anchor download named `stitek-{orderNumber}.pdf`. 503 + `Retry-After` surfaces as `shipping.carrierUnavailable` with a "try again in a minute" hint.
- **Customer contact card:** `CustomerContactName` + `CustomerContactPhone` (`tel:` link allowed), **never email** — the DTO has no such field (T-0082 AC-4); the page renders no email-shaped UI and no `mailto:`. `ZasilkovnaPickupPointId` displayed in the shipping block when non-null; tracking link rendered from `shippingCarrierTrackingUrl` when non-null.
- **Attachments list:** renders `attachments[]` (filename, human-readable `sizeBytes`, content-type icon) with downloads via each item's backend-built `downloadUrl` (T-0064 maker-scoped endpoint; streams through the backend, no direct blob links).
- **Invoice link:** rendered iff `invoicePdfUrl != null` (T-0088 endpoint target); null → omitted entirely (no disabled placeholder).
- **Payout breakdown:** `MakerPayoutAmountMinor` prominent ("your payout" headline figure) above the supporting breakdown (`TotalAmountMinor`, `ProductPriceMinor`, `ShippingPriceMinor`, `VatAmountMinor` + rate) — all via `formatCzk`. No platform-fee figure exists on the DTO (T-0081/T-0082 lock).
- **Lifecycle timeline (maker view):** ordered steps from the DTO timestamps — Created → Paid → Accepted → Shipped → Delivered, with Cancelled as a terminal branch when `cancelledAt != null`; unreached steps render muted; dates in Czech short format.
- **Error-code i18n keys:** `shipping.methodNotEligible`, `shipping.carrierUnavailable`, `order.message.*` already exist in `cs-CZ.ts` (verified). **`order.notFound` + `order.invalidTransition` are MISSING from the catalog (grep-verified 2026-06-09)** despite being live backend codes — this ticket adds both (error-code parity rule, CLAUDE.md cross-stack).
- **Action error rendering:** failed action → inline `Alert variant="error"` in the action bar with the mapped i18n message; buttons re-enable; pending state shows the `Spinner` primitive and disables the bar (no double-submit).
- **Tykání copy** pending the `docs/questions/open.md` tone question — same handling as T-0087a (keys written tykání, flagged in PR).
- **Route name:** `/dashboard/maker/objednavky/[orderId]` — nests under the T-0087a list route. US-maker-0010's older `/dashboard/maker/objednavka/<id>` wording is superseded by the bundle's locked route map (list/detail nesting consistency).

## Scope

### Route (replaces placeholder)

- **`frontend/src/app/(maker)/dashboard/maker/objednavky/[orderId]/page.tsx`** — Server Component. Fetches via `getMakerOrderDetail(orderId)`; `NotFound` → `notFound()`; renders header (order number + state badge + created date), payout breakdown, timeline, shipping block (method, pickup-point id, tracking link), contact card, attachments, invoice link, then mounts the two client islands. `dynamic = 'force-dynamic'`. `generateMetadata` per §B.9.
- **`.../[orderId]/order-actions.tsx`** — `'use client'` island. Props: `orderId`, `orderNumber`, `state`, `shippingMethod`, `shippingCarrierRef`. Renders the §A.1 button matrix + label button per §C; ship confirm dialog; per-action pending/disabled handling; error alert; `router.refresh()` on success.
- **`.../[orderId]/order-timeline.tsx`** — server-rendered timeline from the lifecycle timestamps.
- **Thread mount:** `OrderMessageThread` (from T-0086b, `components/orders/` per that ticket) rendered inline below the order data, fed the maker adapter (messages helpers below), `pollIntervalMs ≈ 30_000`, mark-read on render.

### API helpers

- **`frontend/src/lib/api-client-helpers/maker-orders.ts`** — EXTEND (file created by T-0087a):
  - `getMakerOrderDetail(orderId)` → wraps `makerApi.orders2`.
  - `acceptOrder(orderId)` / `shipOrder(orderId)` / `handOverOrder(orderId)` → wrap `accept` / `ship` / `handover`.
  - `downloadShippingLabel(orderId)` → blob fetch per §C (NOT the generated `label()` — `Promise<void>` discards the body).
  - `getMakerOrderMessages` / `postMakerOrderMessage` / `markMakerOrderMessagesRead` → wrap the trio; shaped to the T-0086b thread-adapter contract.
- **`frontend/src/lib/runtime/api-fetch.ts`** — extend with a blob-returning variant **only if** T-0086a/b didn't already add one (implementer verifies; do not duplicate).

### i18n

- **`frontend/src/lib/i18n/cs-CZ.ts`** — NEW keys under `dashboard.maker.orderDetail.*`: metadata, section headings (payout, timeline, shipping, contact, attachments, invoice, messages), action labels (`action.accept`, `action.ship`, `action.handover`, `action.downloadLabel`), ship confirm dialog (title/body/confirm/cancel — body states irreversibility + carrier shipment creation), timeline step labels, payout headline + breakdown labels, error/retry strings. PLUS the two missing error-parity keys: `order.notFound`, `order.invalidTransition`.

### No backend change

No endpoint, DTO, or contract change → no NSwag regen, no `api-client` diff (pre-commit hook would block one anyway).

## Alternatives Considered

- **Option A — Client-side transition guards (disable/hide buttons via a frontend state machine).** *Rejected per A.1* — business logic in the frontend; drifts from the backend on the first rule change (e.g., a future "maker can cancel before accept" rule). Render-from-DTO + backend verdict + `router.refresh()` is the whole contract.
- **Option B — Backend-supplied `availableActions[]` on the detail DTO.** *Rejected per A.1 + T-0082 §C* — T-0082 explicitly locked "action-buttons NOT in response; FE inspects State". Re-opening a merged ticket's lock for a convenience field is contract churn with zero behavior gain.
- **Option C — Confirm dialogs on all three actions.** *Rejected per §C* — accept is the highest-frequency action and fully recoverable in workflow terms; handover has no external side effect beyond the timer. Only ship creates an irreversible carrier shipment (T-0072); friction goes where the blast radius is.
- **Option D — Optimistic UI (flip state badge before the POST resolves).** *Rejected per A.1* — lies on failure (`order.invalidTransition` race); the SSR refresh round-trip is fast and always truthful.
- **Option E — Fork a maker-specific message thread component.** *Rejected per A.2* — T-0086b's adapter seam exists precisely so both hosts share one thread UI; a fork doubles every future thread fix.
- **Option F — Use the generated `label()` client method for the label button.** *Rejected per §C* — NSwag typed the file response as `Promise<void>`; the PDF body is discarded. The blob helper is the only working path that still routes through the runtime fetch layer (auth + error parsing preserved).
- **Option G — Direct `<a href>` to the API label/attachment endpoints.** *Rejected* — plain navigation doesn't attach the Authorization header the runtime layer manages; attachment links use the DTO's backend-built `downloadUrl` through the same fetch discipline, and the label uses the blob helper. (If T-0086b resolved cookie-credentialed direct links for attachments, mirror its resolution — do not invent a second mechanism.)
- **Option H — Distinguish "order not found" from "not your order" in the UI.** *Rejected per T-0082 §B* — the backend deliberately returns one shape (no IDOR oracle); the frontend renders one `notFound()` page.
- **Option I — Poll the whole detail page instead of just the thread.** *Rejected per A.2/Q5* — order state changes are maker-initiated on this very page (`router.refresh()` after actions covers them); only the thread has counterparty-driven updates worth polling.

## Out of scope

- **Customer detail page / customer thread wiring** — T-0086b.
- **Shared `OrderMessageThread` component implementation** (polling internals, adapter contract, mark-read mechanics) — T-0086b owns it; this ticket only consumes.
- **Invoice download endpoint** — T-0088 (this page just renders the `invoicePdfUrl` the backend built).
- **Outbox-event audit trail on the detail page** (US-maker-0017 "události" expander) — separate ticket; the DTO doesn't carry events.
- **Maker-side cancel/refuse action** — no backend command exists; not invented here (escalate via PM if product wants it).
- **Auto-accept nudge emails (US-maker-0006 AC-3)** — backend follow-up ticket, post-MVP candidate.
- **Pickup-point name/address resolution** for `ZasilkovnaPickupPointId` (would need a Packeta lookup surface) — id displayed verbatim at MVP; enrichment is a future ticket.
- **401 → refresh → retry in `api-fetch.ts`** — known platform gap; not this ticket.
- **Print-optimized order sheet, status-change history UI** — post-MVP.

## Acceptance criteria

- **AC-1** Given a maker who owns the order, when they visit `/dashboard/maker/objednavky/{orderId}`, then the page renders server-side (`page.tsx` has no `'use client'`; data via `getMakerOrderDetail` + SSR cookie forwarding) showing order number, state badge, payout breakdown, timeline, shipping block, contact card, attachments, and the message thread. No `useEffect` initial-data fetch anywhere in the route.
- **AC-2** Given an order id that doesn't exist OR belongs to another maker, when visited, then the Next.js `notFound()` page renders — one shape for both cases (no IDOR oracle), title branch per §B.9.
- **AC-3** Given `State == Paid`, when rendered, then exactly one transition button shows: "Přijmout". Clicking it POSTs accept; on 200 the page `router.refresh()`es and shows the Accepted badge plus the correct next-action button for the order's shipping method.
- **AC-4** Given `State == Accepted && ShippingMethod == ZasilkovnaPickupPoint`, when rendered, then "Odeslat" shows (and "Předat osobně" does not). Clicking opens the confirm dialog (irreversibility + carrier-shipment copy); **Cancel issues no request** (network proof); Confirm POSTs ship → on 200, refresh → Shipped badge + label button appears.
- **AC-5** Given `State == Accepted && ShippingMethod == PersonalPickup`, when rendered, then "Předat osobně" shows (and "Odeslat" does not). Clicking POSTs handover (single-click, no dialog) → on 200, refresh → Shipped badge; **no label button ever appears** for personal pickup (US-maker-0008 AC-2).
- **AC-6** Given `State` in `{Shipped, Delivered, Completed, Cancelled, PendingPayment}`, when rendered, then **zero** transition buttons show. The render is a pure function of `State + ShippingMethod` — verified by a table-driven pass through all states on the preview.
- **AC-7** Given a stale action (state changed elsewhere; backend returns 409 `order.invalidTransition`, or 400 `shipping.methodNotEligible`, or 503 `shipping.carrierUnavailable`), when the POST fails, then an inline i18n-keyed error alert renders (no raw code, no toast-less silence), buttons re-enable, and a refresh reconciles the view. The two previously-missing keys (`order.notFound`, `order.invalidTransition`) exist in `cs-CZ.ts` after this ticket.
- **AC-8** Given `State == Shipped && ShippingMethod == ZasilkovnaPickupPoint && shippingCarrierRef != null`, when "Stáhnout štítek" is clicked, then the PDF downloads as `stitek-{orderNumber}.pdf` via the blob helper (NOT the generated `label()` method). On 503 the alert shows the `shipping.carrierUnavailable` message with the retry hint. When `shippingCarrierRef` is null, the button does not render.
- **AC-9** Given the contact card, when rendered, then it shows `CustomerContactName` + `CustomerContactPhone` (tappable `tel:`), and **no email and no `mailto:` exists anywhere in the rendered DOM** (the DTO has no email field — T-0082 AC-4; DOM grep proof). `ZasilkovnaPickupPointId` displays in the shipping block when non-null; tracking link renders when `shippingCarrierTrackingUrl` non-null.
- **AC-10** Given attachments + an invoice, when rendered, then every attachment row shows filename + human-readable size and downloads through its backend `downloadUrl`; the invoice link renders iff `invoicePdfUrl != null` (null → nothing, no disabled stub).
- **AC-11** Given the payout + timeline sections, when rendered, then `MakerPayoutAmountMinor` is the prominent headline figure via `formatCzk`, the breakdown lists total/product/shipping/VAT figures, and the timeline shows reached steps with Czech short dates, unreached steps muted, and the Cancelled branch when `cancelledAt != null`.
- **AC-12** Given the message thread, when the page renders, then the shared T-0086b `OrderMessageThread` mounts with the maker trio wired: existing messages listed, posting works (POST → thread updates), mark-read fires on render (back-nav to the T-0087a list shows the unread badge cleared), and the thread polls at ~30s (network proof on preview).
- **AC-13** Hygiene gate: zero `any`, zero `console.*`, zero hardcoded Czech outside `cs-CZ.ts` (tykání pending-note in PR), zero manual edits to `lib/api-client/`, responsive at 375/768/1280 (action bar thumb-reachable on mobile), `npm run lint` + `npm run build` clean, `node scripts/check-consistency.mjs` exit 0.

## Technical notes

### Why the buttons are a pure render function of `State + ShippingMethod`

The order state machine lives in `Order.cs` and nowhere else. The frontend's only job is to offer the action a given snapshot makes plausible and let the backend rule on it. Two DTO fields fully determine the button matrix (Paid → accept; Accepted → ship XOR handover by method; everything else → nothing), which means the matrix is testable as a table, reviewable at a glance, and immune to rule drift — a backend rule change shows up as a new error code or a new state value, both of which fail loudly here instead of silently diverging.

### Why `router.refresh()` instead of optimistic UI

After a successful transition the page needs the NEW DTO anyway (new timestamps for the timeline, possibly a new `shippingCarrierRef` for the label button, a new badge). Optimistically flipping the badge saves ~300ms of perceived latency at the cost of a second source of truth that lies whenever the backend says no. `router.refresh()` re-runs the Server Component against the real row — one mechanism, always truthful, and it doubles as the recovery path for stale-button races.

### Why only ship gets a confirm dialog

Confirm dialogs are friction budget; spend it where the blast radius is. Ship calls Packeta and creates a real shipment (T-0072 — not undoable from the UI; a wrong click means a support escalation and a wasted label). Accept is the most frequent action and merely tells the customer "I'm on it"; handover starts the 7-day auto-deliver timer with no external side effect. Putting dialogs on all three trains the maker to click through them, which defeats the one dialog that matters.

### Why the label button bypasses the generated `label()` method

NSwag types file responses without a schema as `Promise<void>` — the generated method performs the request and discards the PDF bytes. Editing the generated client is forbidden (ADR 0022 + pre-commit hook), so the workaround lives in the helper layer: a blob fetch through the runtime layer keeps auth headers, the 8s timeout, and RFC7807 parsing, then hands the bytes to an object-URL download. If a future NSwag config adds proper file-response typing, the helper body shrinks and its call sites don't move.

### Why mark-read on render is the badge-clearing mechanism

T-0079's denormalized counters make the T-0087a list badge a plain field read; the symmetric write is the thread's mark-read call, which zeroes `maker_unread_message_count` and clears the debounce pointer in one command. Firing it on thread render means "the maker saw the thread" and "the badge clears" are the same event — no separate "mark all read" affordance to build, and the back-navigation experience (badge gone) falls out for free.

### Why the page cannot distinguish "missing" from "not yours"

T-0082 locked one `order.notFound` shape for both cases specifically to deny cross-tenant probes an oracle. Any frontend attempt to render different copy would require information the wire deliberately doesn't carry. One `notFound()` page is therefore not a UX shortcut — it is the contract.

## Risk / mitigation

- **Stale-button race** (order state changes in another tab; maker clicks an outdated action). *Mitigation:* backend re-validates every transition and returns `order.invalidTransition`; the alert + `router.refresh()` reconcile. Pinned by AC-7.
- **Generated `label()` silently "succeeding" while discarding the PDF** if an implementer reaches for it out of habit. *Mitigation:* §C lock + Option F rebuttal + AC-8 names the blob helper explicitly; the helper is the only label entry point exported.
- **Double-submit on slow networks** (maker clicks Odeslat twice → second 409). *Mitigation:* action bar disables + spinner during pending (§C); harmless if it slips through — backend idempotency-by-state-machine rejects the second call.
- **Thread adapter contract drift** (T-0086b changes prop shape late in the bundle). *Mitigation:* both tickets ship on the same branch sequentially; the maker wiring compiles against the final component, and the T-0086b adapter contract is type-checked at build.
- **Missing i18n keys discovered at runtime** (the `order.notFound`/`order.invalidTransition` gap pattern recurring). *Mitigation:* this ticket closes the two known gaps; the parity check + AC-7 verify all surfaced codes resolve to catalog entries.

## Test plan reference

`docs/test-plans/T-0087b.md` (stub created with this ticket) — Playwright-style manual QA plan on Vercel preview: full state-matrix button pass (Paid/Accepted×2 methods/Shipped/terminal states), ship confirm cancel-vs-confirm network assertions, label download incl. 503 path, contact-card DOM grep for email absence, thread post/poll/mark-read round-trip with list-badge clearance, notFound + error-code surfaces, responsive passes at 375/768/1280. No backend tests (no backend change).

## Files touched (expected)

### New
- `frontend/src/app/(maker)/dashboard/maker/objednavky/[orderId]/order-actions.tsx`
- `frontend/src/app/(maker)/dashboard/maker/objednavky/[orderId]/order-timeline.tsx`
- `docs/test-plans/T-0087b.md`

### Modified
- `frontend/src/app/(maker)/dashboard/maker/objednavky/[orderId]/page.tsx` — placeholder replaced with the real Server Component page.
- `frontend/src/lib/api-client-helpers/maker-orders.ts` — detail + action + label + messages helpers added (file created by T-0087a).
- `frontend/src/lib/runtime/api-fetch.ts` — blob-returning variant added **only if** not already added by T-0086a/b (verify first).
- `frontend/src/lib/i18n/cs-CZ.ts` — `dashboard.maker.orderDetail.*` keys + missing parity keys `order.notFound`, `order.invalidTransition`.

## Commits hint

1. **`feat(T-0087b): detail/action/label/messages helpers + i18n keys (incl. order.notFound + order.invalidTransition parity gap)`** — helper extensions + catalog additions.
2. **`feat(T-0087b): maker order detail page — payout, timeline, contact, attachments, invoice`** — Server Component page + timeline; placeholder removed.
3. **`feat(T-0087b): state-aware action bar + ship confirm + label download + thread mount`** — client islands wired; `router.refresh()` flow complete.
4. **`test(T-0087b): manual QA plan + preview fixes`** — `docs/test-plans/T-0087b.md` + QA-pass fixes.

## Status log

- 2026-06-09 `draft` by PM. Created as ticket 6 of 6 in `feat/order-dashboards-bundle`. Backend dependencies all merged: T-0082 maker detail DTO (`MakerPayoutAmountMinor`, contact name/phone NO email, `ShippingCarrierRef`, `ZasilkovnaPickupPointId`, inline attachments + `InvoicePdfUrl`), T-0071/72/73 transitions, T-0075 label endpoint, T-0079 messages trio. In-bundle dependencies: T-0086b shared `OrderMessageThread` + T-0088 invoice endpoint. Contract wrinkle absorbed at grooming: generated `label()` returns `Promise<void>` (body discarded) → blob-helper workaround locked; cs-CZ parity gap (`order.notFound`, `order.invalidTransition` missing) closed by this ticket.
- 2026-06-09 `draft → ready` by PM. User locked 2 dimensions at grooming: **A.1** state-aware action buttons rendered purely from `State + ShippingMethod`, POST + `router.refresh()` per action, backend owns all transition rules (rejected client-side guards + optimistic UI + backend availableActions field); **A.2** (Q6/Q5) reuse of the shared T-0086b thread component with maker-host wiring, ~30s polling, mark-read on render (rejected fork + separate messages page + websockets). PM-absorbed decisions in §C (ship-only confirm dialog, label blob download, contact card GDPR surface, pickup-point display, attachments + invoice rendering, payout-prominent breakdown, timeline shape, error-key parity closure, pending/disabled action handling, tykání-pending copy, route naming superseding US-maker-0010's older slug). No NSwag regen (read-only consumer). **Ready for frontend** — implemented after T-0087a on the same bundle branch.

## Definition of Ready checklist

- [x] Linked user stories present (US-maker-0006/0007/0008/0009/0010/0011).
- [x] Acceptance criteria observable + numbered (AC-1 through AC-13).
- [x] Locked design decisions captured (§A user-locked, §B ADR+pattern-locked, §C PM-absorbed).
- [x] Alternatives Considered with ≥1 rebutted alternative per locked dimension (Options A–I).
- [x] Out of scope explicit (incl. US-maker-0017 events expander + pickup-point enrichment deferred).
- [x] Risk / mitigation called out (stale buttons, label() void gap, double-submit, adapter drift, i18n gaps).
- [x] Test plan reference (docs/test-plans/T-0087b.md stub, Vercel preview QA).
- [x] Files touched listed (new + modified).
- [x] Layers / ADRs / dependencies in the frontmatter; depends on in-bundle T-0086b (shared component) + T-0088 (invoice endpoint).
- [x] Security-touching: NO (no new auth surface; IDOR/GDPR shields are backend compile-time locks this page consumes).
- [x] Size: M.
- [x] No business logic client-side (buttons render from DTO fields; backend re-validates every transition).
