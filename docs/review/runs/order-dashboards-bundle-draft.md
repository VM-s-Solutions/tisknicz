# Order-dashboards bundle — Reviewer preliminary verdict (draft)

> Bundle-scope draft per `docs/process/routing.md` "Bundling related tickets into one PR" §parallel-reviewer. Final verdict happens after the implementers report done; this is the early-warning pass written in parallel with implementation. Bundle: `feat/order-dashboards-bundle` = T-0088 (invoice download endpoints) → T-0089 (unread-count verification gate) → T-0086a (customer list) → T-0086b (customer tracking detail + shared thread) → T-0087a (maker queue) → T-0087b (maker detail + actions). **One PR for all six tickets per the single-PR directive.** Sources verified against the working tree on 2026-06-11.

## Bundle scope (6 tickets: 2 backend + 4 frontend)

The backend pair ships first and gates the NSwag regen the frontend slices consume. T-0088 makes the URL T-0082's projection already emits (`/api/v1/orders/{orderId}/invoice` — verified emitted verbatim at `backend/src/Makables.Infra.Database/Orders/OrderQueries.cs:223` customer + `:287` maker) real on BOTH hosts: controller-direct streaming actions beside the existing T-0064 `DownloadAttachment` siblings (verified at `Web.Customer/Controllers/OrdersController.cs:384` with `EscapeFilenameForHeader:476` + `ETagMatches:484` reusable in-file), plus one mirrored read-only repository method (`GetByIdForMakerReadOnlyAsync` confirmed at `IOrderRepository.cs:106`; the customer mirror is the only domain-surface delta). T-0089 is a **rescoped verification gate** — the DTO field, projection, handler pins, and regen all shipped with the order-cleanup fold (verified: `customer-api.v1.ts:2022` types `unreadMessageCount: number`; `maker-api.v1.ts:2725` types `number | undefined`); the only code deliverable is the missing customer-side integration pin (`GetCustomerOrdersIntegrationTests.cs` — verified: only the maker twin asserts `UnreadMessageCount` today). The four frontend tickets build two dashboards (customer + maker lists), extend T-0084b's `/objednavka/[id]` page with post-payment tracking, create the **shared `OrderMessageThread`** (verified: no such component exists yet — `components/` has no message component), and ship the maker action workflow (accept/ship/handover/label). All consumed backend contracts are merged and typed (`orders2`/`accept`/`ship`/`handover` at `maker-api.v1.ts:48-63`; `messagesGET`/`messagesPOST`/`markRead` at `customer-api.v1.ts:17-27`; `label(orderId): Promise<void>` at `maker-api.v1.ts:15` — see HIGH-5).

61 ACs total (8 + 4 + 12 + 13 + 11 + 13). Backend diff is deliberately thin (no migrations, no outbox, no new error codes, no new i18n keys backend-side); the frontend diff is wide (~10 new route files, 2 helper files, 1 shared component, ~40+ i18n keys, 4 manual QA plans).

## Patterns / locks the diff must honour

### T-0088 (backend, security-touching → Gate 3 SecOps mandatory)
- **§A.1 routes byte-for-byte** = the strings the T-0082 projection emits: `GET /api/v1/orders/{orderId}/invoice` host-relative on each host. Any projection edit or route variant = request changes (regen churn forbidden by the lock).
- **§B controller-direct, NO MediatR feature** (ADR 0014 handler-free read path; T-0075 `FilesController.GetShippingLabel` + T-0064 `DownloadAttachment` precedents — both confirmed controller-direct on the tree). A one-file feature here = request changes for ceremony.
- **§C header policy = T-0064's, NOT T-0075's**: `Cache-Control: private, no-store` + `Content-Disposition: attachment; filename="faktura-{InvoiceNumber}.pdf"` (through `EscapeFilenameForHeader`) + ETag/If-None-Match → 304. Invoices carry PII; `public, immutable` (the label policy) = request changes.
- **ADR 0013 ownership scoping**: lookup chain order is load-bearing — session → ownership-scoped read-only order load → `invoices.GetByOrderIdAsync` (verified Unscoped at `IInvoiceRepository.cs:128`; safe ONLY after the ownership pre-check, and the ticket requires a controller comment saying so) → blob. Cross-tenant and nonexistent must both 404 `order.notFound`, indistinguishable.
- **Error-code reuse only**: `BusinessErrorMessage.OrderNotFound` (:42) + `InvoiceNotYetRendered` (:396) — both verified present; no `InvoiceNotFound` exists and none may be added. Constants, not inline strings (no NEW T5 — `OrdersController.cs:284` already carries a baseline T5; do not add a sibling).
- **No `SaveChangesAsync`, no UoW** anywhere in the slice; `enableRangeProcessing: false`; `Invoice.PdfBlobPath` used verbatim against `BlobContainer.Invoices` (verified `Storage/BlobContainer.cs:24`).

### T-0089
- **Verification-only**: zero production-code diff. The ONLY allowed code change is one integration test in `GetCustomerOrdersIntegrationTests.cs` mirroring the maker twin. Any DTO/projection/nullability "harmonization" (maker `int?` → `int`) = scope creep, request changes (§C explicit).

### T-0086a / T-0087a (the two lists)
- **Q5 lock: SSR + URL-state, no client store.** `force-dynamic` Server Components; filters/sort/page/tab in `searchParams`; `<Link>` pagination per §B.8 katalog/produkty precedent; zero `useEffect` fetching, zero Zustand/SWR/React-Query.
- **Q7 lock: unread badges from the list payload only** — `unreadMessageCount > 0` renders the count badge; `0`/`null`/`undefined` → nothing. Any per-row messages fetch = request changes (N+1, rejected at grooming).
- **T-0087a Q8 lock: state tabs = at most ONE `state` value per request**, default tab Nové (`state=Paid`); `?tab=vyroba` → `state=Accepted`; `?tab=vse` → no param. Client-side multi-state merging or eager multi-tab prefetch = request changes.
- **T-0087a §C GDPR surfaces**: money column = `MakerPayoutAmountMinor` (never gross-as-primary, never a platform-fee figure — the DTO has none); `CustomerContactName` only — no email-shaped field, no `mailto:` anywhere in the DOM.
- **§B.16 helpers**: new `customer-orders.ts` + `maker-orders.ts` in `lib/api-client-helpers/`; route code never imports the generated client; DTO types re-exported.

### T-0086b (detail + shared thread)
- **Q6 lock: ONE shared `OrderMessageThread`** (`'use client'`), audience-agnostic via **injected `Result`-returning callbacks** (`fetchMessages`/`postMessage`/`markRead` props). The component imports NOTHING from `lib/api-client-helpers/` — consumers inject. Any `host`/`isMaker` prop or internal audience branch = request changes (mirrors the runtime-audience-flag shape T-0082 §A.1 rejected).
- **Q5 exception: the thread is the platform's ONLY polling surface** (joins T-0085's payment poller as the second sanctioned interval). Required shape: `POLL_INTERVAL_MS = 30_000` named constant with the grooming-lock comment; mark-read on mount; `visibilitychange` pause (hidden → stop; visible → immediate refetch + resume); interval + listener cleanup on unmount; in-flight guard against overlapping polls (T-0085 `pollInFlightRef` precedent).
- **Mark-read on render** (+ idempotent re-fires after polls delivering counterparty messages) — this is what zeroes the T-0086a badge. `canPost = state !== PendingPayment` carried in the prop contract.
- **State branch preserves T-0084b**: `PendingPayment` → the existing payment-retry surface untouched (regression gate AC-12); everything else → tracking detail. Note: `(customer)/objednavka/[id]/not-found.tsx` **already exists** (T-0084b shipped it) — do not create a duplicate.
- **§B.9** title branches only on NotFound; `notFound()` for foreign/nonexistent (404-not-403, no oracle).

### T-0087b (maker detail + actions)
- **§A.1: buttons are a pure render function of `State + ShippingMethod`** — Paid → Přijmout; Accepted×Zásilkovna → Odeslat; Accepted×PersonalPickup → Předat osobně; all other states → zero transition buttons. No client transition guards, no optimistic UI, no `availableActions[]`. POST → `router.refresh()`.
- **§C ship-only confirm dialog** (UI primitives, not `window.confirm`); Cancel issues no request; accept/handover stay single-click.
- **§C label download via the blob helper, NOT the generated `label()`** — verified: `maker-api.v1.ts:15,204` types `label(orderId): Promise<void>` (body discarded). Filename `stitek-{orderNumber}.pdf`; visible iff `Shipped && ZasilkovnaPickupPoint && shippingCarrierRef != null`; 503 → `shipping.carrierUnavailable` (key verified present, `cs-CZ.ts:431`).
- **§C i18n parity closure**: `order.notFound` + `order.invalidTransition` are **confirmed missing** from `cs-CZ.ts` (grep 2026-06-11, zero hits) despite being live backend codes (`BusinessErrorMessage.cs:42,44`) — this ticket adds both.

## Pre-flight risks (HIGH first)

### HIGH

- **HIGH-1: invoice endpoint IDOR — file streaming on PII documents.** The ownership predicate must run BEFORE any invoice/blob access on BOTH hosts, and the unscoped `GetByOrderIdAsync` must never be reachable without it. I will trace the lookup chain in both controller actions line by line and verify: (a) cross-tenant probe → `404 order.notFound` byte-identical to nonexistent-id (no existence oracle, AC-3); (b) unit tests pin `Received(0)` on `IInvoiceRepository.GetByOrderIdAsync` AND `IBlobStorageClient.DownloadAsync` for the not-owned path (wiring proof, not just status-code proof); (c) maker host resolves maker BEFORE the order load (user-without-maker-row → 404 with order repo never called, AC-6); (d) `Cache-Control: private, no-store` on every 200 and NO cache header on 404s (a cached PII PDF outliving logout is the T-0064 threat model); (e) the controller comment "safe ONLY after the ownership-scoped order load above" is present on the invoice-repo call. Gate 3 SecOps is mandatory (`security_touching: true`).

- **HIGH-2: `OrderMessageThread` reuse trap — one component, two audiences.** Watch for: (a) audience branching inside the component (`if (isMaker)`, a `host` prop, or importing both helpers) — must be injected callbacks only; (b) **mark-read wired to the wrong audience's endpoint** — the customer page must inject the customer `markRead` (zeroes `customer_unread_message_count`), the maker page the maker one; a cross-wire 401s at the backend but silently leaves the badge stuck, which manual QA must catch (T-0086b AC-9 + T-0087b AC-12 both pin list-badge clearance via back-navigation); (c) **T-0087b must pass `canPost={state !== PendingPayment}`** — the maker detail page CAN render `PendingPayment` orders (T-0087b AC-6 lists it in the zero-buttons matrix) and the ticket text does not spell the prop value out for the maker consumer; a hardcoded `canPost={true}` gives the maker a post box that 409s `order.message.notAllowedInState`; (d) `isMine` orientation — derived from the audience's own message-DTO field per host, not inferred client-side from author names.

- **HIGH-3: poller discipline regression — second polling surface joins T-0085's.** The thread poll must match the sanctioned shape exactly: named `POLL_INTERVAL_MS = 30_000` export with the Q5/Q6 lock comment; visibility-pause that genuinely stops the timer (not just skips the fetch); immediate refetch + resume on visible; cleanup of BOTH interval and `visibilitychange` listener on unmount; in-flight guard so a slow fetch and the next tick never overlap (T-0085 `payment-poll-client.tsx` precedent — `pollInFlightRef`). Also: **no double-interval when `router.refresh()` re-renders the server tree around the client island** — React preserves the client component instance, but a careless `useEffect` dependency array re-arms the timer; AC-11 "exactly one poll request per interval" is the network-tab proof. And per the Gate 9 precedent (checkout gate9 artifact): the T7 heuristic does not trace helper/callback-based fetching, so the sanction must live in a JSDoc on the effect referencing T-0086b §A.1 — not silence, not a suppress marker. **No other new component may adopt an interval** (T-0086b out-of-scope is explicit).

- **HIGH-4: large-payload transfers vs the 8 s `apiFetch` default — the checkout B-1 lesson applies to DOWNLOADS here.** The B-1 fix is on the tree (`api-fetch.ts:49` `timeoutMs?: number`, `:132-133` composes it; `orders-client.ts:156` uses `UPLOAD_TIMEOUT_MS`). This bundle adds the **first binary download paths through `apiFetch`**: attachments (customer-uploaded, up to 10 MiB per T-0064 — a slow downlink blows an 8 s ceiling exactly like the upload case), the label PDF, and the invoice PDF (~100 KB, less exposed). **No blob-returning variant exists yet** (grep `api-fetch.ts`: zero hits) — T-0086b creates it, T-0087b reuses it (its §C says "verify, do not duplicate"). I will verify: one blob variant in `lib/runtime/api-fetch.ts` sharing the auth/timeout/RFC7807 machinery (no forked second fetch path), a generous named `timeoutMs` (DOWNLOAD_TIMEOUT_MS-style constant with a source comment) on the attachment/label/invoice helpers, and default 8 s behaviour unchanged for every existing call site.

- **HIGH-5: NSwag file-response gap also swallows the NEW invoice endpoints.** T-0088's prose claims "NSwag generates FileResponse-returning client methods; T-0086b/T-0087b consume them" — **contradicted by the tree**: the same generator already produced `label(orderId): Promise<void>` (`maker-api.v1.ts:15`), discarding the body. The regenerated invoice methods will almost certainly type `Promise<void>` too. The consuming tickets already absorbed this correctly (T-0087b §C/Option F for the label; T-0086b §C blob-fetches `invoicePdfUrl`/`downloadUrl` verbatim) — but watch for an implementer trusting T-0088's prose and calling the generated invoice method expecting bytes: it "succeeds" and downloads nothing. Rule: **every binary download goes through the blob helper against the backend-provided URL string; the generated file-response methods are never called for bytes.** DTO/tree-over-prose, same as checkout MEDIUM-5.

### MEDIUM

- **MEDIUM-1: state-tab request discipline (T-0087a).** One list request per render, period. Watch for eager prefetching of all three tabs (3× backend load per paint), or `<Link prefetch>` on the tab strip triggering speculative SSR renders of the other tabs on hover/viewport — Next.js defaults prefetch for static routes only, but verify `force-dynamic` + default `prefetch` behaviour on the preview network tab (AC-2 pins it).
- **MEDIUM-2: invoice/attachment link auth — SameSite=Strict cookies.** The audience cookies are `HttpOnly + SameSite=Strict`; a plain `<a href>` to the API host drops them → 401 for a logged-in user. Both tickets lock the authenticated `apiFetch` blob + programmatic-anchor path and T-0086b's grooming flagged the SameSite concern explicitly. Verify no plain anchors to API-host URLs anywhere (invoice link, attachment rows, label button); if the implementer claims same-site topology makes anchors work, the proof must be on the preview against the live T-0064/T-0088 endpoints, mirrored identically across T-0086b and T-0087b (no two mechanisms).
- **MEDIUM-3: i18n parity + key inventory (~40+ new keys, 4 namespaces).** Verified missing and must be added in this PR: `order.notFound`, `order.invalidTransition` (T-0087b owns; note T-0086b's deliver-409 alert ALSO renders `order.invalidTransition` — fine in a single PR, but the key must be present in the final diff or AC-6 renders a fallback). Verified present: `invoice.notYetRendered` (:422), `order.message.bodyEmpty/.bodyTooLong/.notAllowedInState` (:446-448), `shipping.carrierUnavailable` (:431), `shipping.methodNotEligible` (:441), all `order.state.*`. **Ticket prose correction:** T-0086b §B names `order.notPayableYet` as the post-box guard code — the actual guard is `OrderMessageNotAllowedInState` → `order.message.notAllowedInState` (verified `PostCustomerOrderMessage.cs:110`, key present); `order.notPayableYet` is the payment-session family and is NOT surfaced by this bundle — do not add it here on the ticket's say-so. Also: `customer.orders.markDeliveredButton` ("reserved" by T-0076 prose) is **not in the catalog** — T-0086b must add it. All error rendering through the `resolveErrorMessage`/`isMessageKey` machinery from checkout HIGH-5 — `t(code as MessageKey)` casts = request changes.
- **MEDIUM-4: helper duplication — `getCustomerOrderDetail` already exists.** Checkout shipped it at `orders-client.ts:168`. T-0086b §C (written pre-checkout-merge) plans a new `getCustomerOrderDetail` in `customer-orders.ts`. Reuse or re-export the existing one; a second wrapper for the same endpoint = request changes (two sources of truth for one unwrap).
- **MEDIUM-5: T-0084b surface regression (shared `page.tsx` surgery).** T-0086b edits the checkout bundle's `/objednavka/[id]/page.tsx`. The `PendingPayment` branch must render the existing pay-CTA + attachment-manager surface byte-for-byte (AC-12 is the regression gate); watch for accidental removal of the `?attachmentsFailed` alert, the non-PendingPayment banner replacement (now superseded by the real tracking detail — verify the banner path is cleanly replaced, not left as dead code), and `not-found.tsx` duplication (exists already).
- **MEDIUM-6: branch hygiene / regen leak (checkout MEDIUM-9 redux).** The working tree sits on `feat/order-cleanup-bundle` with UNCOMMITTED `lib/api-client/*` + `.spec-hashes.json` modifications. `feat/order-dashboards-bundle` must be cut from master AFTER both prior bundles merge. The only sanctioned `lib/api-client/` hunks in this PR are T-0088's regen (both hosts + `.spec-hashes.json`); any unrelated client drift = flag PM.
- **MEDIUM-7: T-0089 scope guard.** The ticket's entire production diff is zero; one integration test only. Verify the test mirrors the maker twin's seeding mechanism (don't invent a third way to bump the counter) and asserts `0`-not-null on the untouched order. Any "while I'm here" production edits = request changes.
- **MEDIUM-8: route prose vs tree (T-0087a/b "replaces placeholder").** No placeholder routes exist under `(maker)/dashboard/maker/objednavky` (glob verified: only `profil` + `produkty`). The routes are net-new — fine, but the implementer should not hunt for a placeholder to delete; and the customer dashboard sibling is `dashboard/zakaznik/profile` (English slug — T-0086a's new `objednavky` sibling is Czech; the mixed slugs are the existing convention, not a finding).
- **MEDIUM-9: maker list `unreadMessageCount` is `number | undefined`** on the wire (verified :2725/:2797) vs customer's `number`. T-0087a AC-5 already pins the collapse (`undefined`/`null`/`0` → no badge); verify no `!` or non-null assertion sneaks in to "fix" the type.

### LOW / INFO

- **LOW-1:** T-0088 integration test 2 must assert the 404 **bodies** match shape across (a)/(c) (same code, same envelope), not just the status — the oracle-freedom claim lives in the body.
- **LOW-2:** Timeline components are being built twice (T-0086b `timeline.tsx` customer, T-0087b `order-timeline.tsx` maker) with near-identical logic (timestamps → filled/muted/cancelled-branch). Acceptable per the Option-G "mirroring ≠ sharing" stance for lists, but this is a harvest candidate if a third timeline appears.
- **INFO-1:** Checkout N-5 flagged bespoke radio-row markup as a harvest candidate "when T-0086/87 needs radio rows again" — the dashboards ship tabs and selects, not radios; likely no trigger. Re-check at final review.
- **INFO-2:** Recurring-finding watch: "ticket claims i18n parity exists / key reserved, catalog disagrees" is now on its **second** bundle (checkout HIGH-5: `auth.emailNotConfirmed` + `file.*`; this bundle: `order.notFound` + `order.invalidTransition` + `markDeliveredButton` + the `notPayableYet` misattribution). Per harvest workflow this earns a `recurring-findings.md` row (count = 2) at final review. A mechanical i18n-parity check (BusinessErrorMessage constants ↔ cs-CZ keys) is the obvious codification.

## AC traceability matrix (61 ACs: 8 + 4 + 12 + 13 + 11 + 13)

### T-0088 — invoice download endpoints (8)

| AC | How I verify in the diff |
|---|---|
| AC-1 | Customer action: byte-equal stream, `application/pdf`, `faktura-{InvoiceNumber}.pdf` disposition, `private, no-store` — unit test 4 + integration test 1 assert all four headers + body. |
| AC-2 | Maker action mirrors (integration test 1 covers both hosts on the same seeded order). |
| AC-3 | `404 order.notFound` for not-owned AND nonexistent, identical shape; `Received(0)` pins on invoice repo + blob client (unit tests 2/customer, 2/maker). |
| AC-4 | Three `invoice.notYetRendered` arms (no row / null `PdfBlobPath` / blob-miss race) — unit tests 3 + maker test 3; no Cache-Control on any 404. |
| AC-5 | 401 on anonymous/wrong-audience — unit test 1 + ADR 0013 host audience (existing middleware; verify `[Authorize]` present on the actions or controller). |
| AC-6 | Maker-row-missing → 404 with order lookup never performed (maker unit test 1, `Received(0)`). |
| AC-7 | `If-None-Match` → 304 no body — mirror of T-0064 `ETagMatches` path; covered in unit test 4 ("ETag echoed when present") + I will check the 304 arm explicitly. |
| AC-8 | Build + ~8 unit + 2 integration green; `check-consistency` exit 0 (baseline 118, no new T1/T5); NSwag regen BOTH hosts committed; zero manual client edits; no new codes/migrations/keys. |

### T-0089 — verification gate (4)

| AC | How I verify in the diff |
|---|---|
| AC-1 | PR description cites DTO field / `o.CustomerUnreadMessageCount` projection (`OrderQueries.cs:96`) / handler pins — proofs, not code. |
| AC-2 | New `GET_orders_UnreadMessageCount_returns_denormalized_value` in `GetCustomerOrdersIntegrationTests.cs`: bumped order → 2, untouched → 0 (never null), mirrors the maker twin. |
| AC-3 | PR description cites PR #44 / commit `ea3271f`; CI contract parity green. |
| AC-4 | `git diff --stat` shows test-only change for this ticket; consistency exit 0. |

### T-0086a — customer order list (12)

| AC | How I verify in the diff |
|---|---|
| AC-1 | `page.tsx` Server Component, `force-dynamic`, `getCustomerOrders` SSR call; JS-disabled render in manual plan; no client fetch for initial data. |
| AC-2 | `order-row.tsx`: number, state badge (`order.state.*`), maker, product / "Vlastní zakázka" on null, `formatCzk`, Czech short date. |
| AC-3 | Badge renders iff `unreadMessageCount > 0`; value from the regenerated DTO; no messages call in the route folder (grep) + network-log proof in plan. |
| AC-4 | Filter bar pushes `?state=Paid` + resets page; list backend-filtered (totalCount from `PagedData`). |
| AC-5 | `?dateFrom/dateTo` drive SSR; backend 400 (inverted range) → error alert with mapped Czech copy — manual plan forces it. |
| AC-6 | `?sort=TotalAmountDesc` emitted only when non-default; default emits no param (§B.8 canonical URLs). |
| AC-7 | `<Link>` pagination preserves filter params; back-button restores page 1 + filters (manual plan). |
| AC-8 | Two empty-state variants: zero-orders + no-filters → katalog CTA; filtered-to-zero → clear-filters link. Both keyed. |
| AC-9 | Row link target `/objednavka/{orderId}`. |
| AC-10 | 375 cards / 768+ table; manual QA sweep. |
| AC-11 | `Unauthorized` → `redirect('/auth/login')` server-side (note: checkout review established the real route is `/login` — implementer follows the tree, ticket prose says `/auth/login`; same deviation-judgement as checkout #1). |
| AC-12 | Hygiene grep (zero `any`/`console.*`/effect-fetch/store imports); `customer.orders.*` keys; zero generated-client edits beyond T-0088 regen; route imports only from the helper. |

### T-0086b — customer tracking detail + shared thread (13)

| AC | How I verify in the diff |
|---|---|
| AC-1 | SSR header render (number, badge, maker, product) via forwarded cookie; JS-disabled check in plan. |
| AC-2 | `timeline.tsx`: filled vs muted from nullable timestamps; cancelled terminal branch replaces future steps. |
| AC-3 | Breakdown rows all `formatCzk` from DTO minors; VAT % from `vatRateBp` via named constant (checkout N-1: `BASIS_POINTS_PER_PERCENT` should now exist — reuse it, no magic `/ 100`). |
| AC-4 | Tracking link iff `shippingCarrierTrackingUrl` non-null; `target="_blank"` + `rel="noopener noreferrer"`. |
| AC-5 | Deliver button iff `state === Shipped`; success → `router.refresh()`; absent in all other states (display map, no state machine). |
| AC-6 | 409 `order.invalidTransition` → inline Czech alert (key added in this PR — MEDIUM-3); page stays usable. |
| AC-7 | Attachments rows download via blob helper (HIGH-4 timeout + MEDIUM-2 auth path); card hidden when empty. |
| AC-8 | Invoice link iff `invoicePdfUrl != null`; downloads via blob helper against the verbatim URL (HIGH-5); null → no element at all. |
| AC-9 | Mark-read on render (badge-zero proof via back-nav in plan); newest-first; load-older appends page 2. |
| AC-10 | Post success → clear + page-1 refetch; 2001-char UX mirror + forced backend 400 renders `order.message.bodyTooLong` (key verified :447). |
| AC-11 | Polling matrix: one request per ~30 s visible, zero hidden, resume on return, zero after unmount (network-tab plan rows; HIGH-3 shape). |
| AC-12 | Foreign/nonexistent → `notFound()` (one shape); `PendingPayment` → T-0084b surface unchanged (regression gate, MEDIUM-5). |
| AC-13 | Hygiene + parity verification (corrected per MEDIUM-3: `order.message.notAllowedInState`, not `order.notPayableYet`); `'use client'` only thread + actions island; responsive sweep. |

### T-0087a — maker order list (11)

| AC | How I verify in the diff |
|---|---|
| AC-1 | Server Component, `force-dynamic`, `getMakerOrders` via `apiFetch`; no `'use client'` in `page.tsx`; zero `useEffect` in the route folder (grep). |
| AC-2 | Tab→state map: no param→`Paid` default, `vyroba`→`Accepted`, `vse`→none; exactly ONE request per render (network proof; MEDIUM-1). |
| AC-3 | Tabs are `<Link>`s preserving sibling params; deep-link + back/forward land correctly. |
| AC-4 | Row fields incl. payout via `formatCzk(makerPayoutAmountMinor)`; **no email / `mailto:` in DOM** (grep diff + DOM inspection in plan). |
| AC-5 | Badge iff `> 0`; `0`/`null`/`undefined` collapse (MEDIUM-9 — no `!`). |
| AC-6 | Junk-param clamping (1 / 20 / 50) without error page; backend remains authority (forced 400 in plan). |
| AC-7 | Date + sort passthrough; invalid values dropped to defaults. |
| AC-8 | Three distinct per-tab empty states, keyed `dashboard.maker.orders.empty.*`. |
| AC-9 | Cards `< md`, table `≥ md`; 375/768/1280 sweep. |
| AC-10 | API failure → `Alert variant="error"` + retry (produkty precedent). |
| AC-11 | Hygiene + tykání-pending note in PR + zero `lib/api-client/` edits + lint/build/consistency green. |

### T-0087b — maker detail + actions (13)

| AC | How I verify in the diff |
|---|---|
| AC-1 | SSR page with all read surfaces; no `useEffect` initial fetch in the route. |
| AC-2 | Foreign/nonexistent → `notFound()`, one shape (no oracle), §B.9 title branch. |
| AC-3 | `Paid` → only "Přijmout"; success → refresh → Accepted + correct next button per method. |
| AC-4 | Accepted×Zásilkovna → "Odeslat" only; confirm dialog; **Cancel issues no request** (network proof); confirm → Shipped + label button. |
| AC-5 | Accepted×PersonalPickup → "Předat osobně" only, single-click; label button NEVER for personal pickup. |
| AC-6 | Terminal/other states (incl. PendingPayment) → zero transition buttons; table-driven state pass in plan (pure function of State+ShippingMethod). |
| AC-7 | 409/400/503 → inline i18n alert, buttons re-enable; `order.notFound` + `order.invalidTransition` keys exist post-PR (verified missing today — MEDIUM-3). |
| AC-8 | Label via blob helper (NOT generated `label()` — verified `Promise<void>`); `stitek-{orderNumber}.pdf`; 503 → `shipping.carrierUnavailable`; hidden when `shippingCarrierRef` null. |
| AC-9 | Contact card name + `tel:` phone; no email/`mailto:` in DOM (grep proof); pickup-point id + tracking link when non-null. |
| AC-10 | Attachments via backend `downloadUrl` through the shared download mechanism (MEDIUM-2 — one mechanism, mirror T-0086b's resolution); invoice link iff non-null. |
| AC-11 | Payout headline + breakdown via `formatCzk`; timeline with cancelled branch. |
| AC-12 | Shared thread mounted with maker trio + `canPost` wired (HIGH-2c); mark-read clears T-0087a badge (back-nav proof); ~30 s poll (network proof). |
| AC-13 | Hygiene + responsive + consistency + zero manual client edits. |

## Gate 5 — tests

**Backend:** T-0088 prescribes ~8 controller unit tests + 2 integration; T-0089 one integration test. Controller streaming actions are not "pure logic" per `must-cover-tests.md` categories, so the TDD commit-order HARD-FAIL does not mechanically trigger — but the bundle precedent (order-cleanup led with `test(...): pin pure-logic predicates (red)`) and Gate 5's backend clause ("integration test for any new endpoint") both apply: the two integration tests are non-negotiable, and test-first ordering is expected where any pure predicate appears. If the diff contains after-the-fact tests for any NEW pure logic (e.g., a filename-escaping or ETag predicate extracted as a pure function), that is a HARD FAIL per Gate 5.

**Frontend:** no harness exists (unchanged). New pure-logic candidates: tab→state mapping (T-0087a), the action-button matrix (T-0087b — explicitly "testable as a table"), timeline derivation (both detail pages). If automated tests appear, commit order must show test-before-implementation; if not, the **four manual plans must all exist in the diff** (`docs/test-plans/T-0086a.md`, `T-0086b.md`, `T-0087a.md`, `T-0087b.md`) and pin those behaviours (T-0087b's table-driven state pass covers the matrix), and the PR description must state the no-harness decision explicitly — same Gate-5 condition as checkout.

## Mechanical-check expectations (Gate 9)

- **Baseline:** `check-consistency: clean (118 tracked)` — verified on the working tree 2026-06-11. T-0088 is controller-direct → no new `Features/` file → no T1; the new actions must reference `BusinessErrorMessage` constants (no NEW T5 — :284 in the same file is a baseline warning, not a license). HARD FAIL on any NEW violation.
- **T4 `any`:** 0 new (frontend + backend).
- **T7 `useEffect` fetch:** the thread poll is the single new sanctioned surface; per the checkout Gate-9 precedent the heuristic won't see helper/callback-based polling — the JSDoc justification referencing T-0086b §A.1/Q5 must be present, no silent suppression. Zero other hits.
- **`console.*` / `Console.WriteLine`:** 0.
- **NSwag regen:** T-0088 is the bundle's only contract change → regenerated `customer-api.v1.ts` + `maker-api.v1.ts` + `.spec-hashes.json` committed in this PR; pre-commit `check-api-client-manual-edits.mjs` passes; CI parity green; PR description flags the contract change (Gate 6). NO other `lib/api-client/` drift (MEDIUM-6).
- **i18n:** ~40+ keys across `customer.orders.*`, `customer.orderDetail.*`, `dashboard.maker.orders.*`, `dashboard.maker.orderDetail.*`, plus parity keys `order.notFound` + `order.invalidTransition` + `customer.orders.markDeliveredButton`. Customer copy vykání, maker copy tykání-pending (PR note). Plural-neutral counts per §B.18.
- **CI:** backend build + test suite green; `tsc --noEmit` + lint + `next build` green (new route table entries: `/dashboard/zakaznik/objednavky`, `/dashboard/maker/objednavky`, `/dashboard/maker/objednavky/[orderId]`).
- **Docs:** `docs/architecture/roles/invoice.md` gains the T-0088 read-surface note (Gate 7); INDEX flips are PM's post-merge.
- **Gate 3:** SecOps pass mandatory (T-0088 `security_touching: true` — file streaming + PII). **Gate 8:** Optimizer applies to T-0088 (blocking external blob round-trip on a request path) — expect sign-off or an explicit N/A rationale.

## Open items the implementers should confirm before/while coding

1. **Blob download helper** — ONE variant in `lib/runtime/api-fetch.ts` sharing timeout/auth/error machinery, with a named generous `timeoutMs` for attachment/label/invoice downloads (HIGH-4). T-0086b creates; T-0087b verifies-then-reuses.
2. **Never call generated file-response methods for bytes** — `label()` and the regenerated invoice methods type `Promise<void>` (HIGH-5). Blob helper + backend-provided URL string, always.
3. **`OrderMessageThread`**: injected callbacks only, no audience knowledge; maker page passes `canPost={state !== PendingPayment}` explicitly (HIGH-2c); poll shape per HIGH-3.
4. **Reuse `getCustomerOrderDetail` from `orders-client.ts:168`** — do not duplicate into `customer-orders.ts` (MEDIUM-4).
5. **i18n corrections over ticket prose**: post-box guard key is `order.message.notAllowedInState` (present), NOT `order.notPayableYet` (absent — don't add); `order.notFound` + `order.invalidTransition` + `customer.orders.markDeliveredButton` must be added (MEDIUM-3).
6. **Login redirect target is `/login`** per the checkout-review route-table finding, not the tickets' `/auth/login` prose.
7. **Branch cut from master after checkout-flow merges**; zero unsanctioned `lib/api-client/` hunks (MEDIUM-6).
8. **T-0088 controller comment** on the unscoped `GetByOrderIdAsync` call ("safe ONLY after the ownership-scoped order load above") + `Received(0)` pins in unit tests (HIGH-1).
9. **`not-found.tsx` under `(customer)/objednavka/[id]/` already exists** — extend/verify, don't duplicate (MEDIUM-5).
10. **T-0089 is test-only** — any production hunk attributed to it gets bounced (MEDIUM-7).

## Preliminary verdict

**STRUCTURALLY_SOUND_PENDING_DIFF** — with **HIGH-4 (blob-download timeout + missing blob variant)** and **HIGH-5 (NSwag `Promise<void>` file-response gap reaching the new invoice endpoints, contradicting T-0088's consumption prose)** as the two named pre-flight concerns that must be resolved inside this PR, and **HIGH-1 (invoice IDOR chain)** as the row the final review will trace line-by-line with SecOps.

Rationale: all six tickets satisfy DoR; every consumed contract is verified present on the working tree (projection URLs, repository surface, generated-client methods, error codes, i18n keys — including the two confirmed-missing parity keys the bundle itself closes); the user-locked dimensions (T-0088 routes/scoping, Q5/Q6/Q7/Q8, the action-button lock) are internally consistent and match the T-0064/T-0075/T-0085 precedents the tree actually contains. The ticket-prose defects found (T-0088's "consume the generated file methods", T-0086b's `order.notPayableYet` misattribution, T-0087a/b's phantom placeholders, `/auth/login` vs `/login`) are all tree-over-prose corrections, not ticket revisions. Hold the line on: ownership-before-blob on both hosts, one audience-blind thread component, exactly two sanctioned polling surfaces platform-wide, every binary through the timeout-aware blob helper, and zero generated-client hunks beyond the T-0088 regen.
