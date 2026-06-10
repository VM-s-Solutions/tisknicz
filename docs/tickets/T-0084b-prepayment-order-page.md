---
id: T-0084b
title: Pre-payment order page /objednavka/[id] — explicit pay CTA + attachment retry surface
status: ready
size: M
owner: frontend
created: 2026-06-09
updated: 2026-06-09
depends_on: [T-0084a, T-0065, T-0082, T-0083]
blocks: [T-0085]
user_stories: [US-customer-0010]
adrs: [0005, 0016, 0022, 0024]
phase: 4
manual_steps: [vercel-preview-manual-qa]
security_touching: false
layers: [frontend]
---

# T-0084b — Pre-payment order page at /objednavka/[id]

## Context

T-0084b is the **second ticket in the checkout-flow bundle** (`feat/checkout-flow-bundle`: T-0084a order form → **T-0084b pre-payment order page** → T-0085 payment confirmation; one PR, sequential implementation). T-0084a navigates here immediately after `CreateOrder` succeeds; the order sits in `PendingPayment` and this page is where the customer actually pays — closing US-customer-0010 AC-2 (redirect to Comgate) and AC-3 (retry from `/objednavka/<id>` within the 24h window before T-0083 auto-cancel).

The page renders server-side from `ordersGET2(orderId)` → `GetCustomerOrderDetailsResponse { detail: CustomerOrderDetailDto }` (T-0082): state + timestamps, full money breakdown (`productPriceMinor`, `shippingPriceMinor`, `vatAmountMinor`, `vatRateBp`, `totalAmountMinor`, `currency`), contact snapshot (`contactName`, `contactPhone`), `shippingMethod`, `attachments[]` (`OrderAttachmentSummaryDto { id, filename, contentType, sizeBytes, downloadUrl }`), `createdAt`. Payment goes through `paymentSession(orderId)` → `CreatePaymentSessionResponse { paymentProviderRef, redirectUrl }` (T-0065) — whose backend handler already implements verify-then-recreate + the cached `PaymentRedirectUrl`, so the frontend's only job is: **call on click, redirect to the URL**. The page is also the locked retry surface for attachment uploads that failed during T-0084a's post-create phase (`attachmentsPOST`, T-0064 limits).

This ticket is intentionally **PendingPayment-shaped**. Orders in any other state get a minimal state banner; the full tracking view (timeline, tracking URL, deliver CTA, messages) is T-0086b in the order-dashboards bundle. Until T-0086b ships, the banner is loudly incomplete — per the CLAUDE.md no-mocks rule that is correct, not a bug. Frontend-only ticket: zero backend changes, no NSwag regen.

## Locked design decisions

### A. User-locked (2026-06-09 grooming, non-negotiable)

1. **Explicit "Zaplatit" button — payment session created on click, never on page load (Q3).** The click POSTs `payment-session`; on success the browser navigates to the returned Comgate `redirectUrl`. No session is created as a side effect of rendering. The T-0065 verify-then-recreate logic + 24h-cached redirect URL are honored server-side; the frontend does not duplicate them. **Rejected:** auto-create on load (Option A), auto-redirect without a click (Option B).
2. **Attachment retry surface (Q2 of T-0084a).** An upload manager shows the already-uploaded files (from `detail.attachments`), allows adding more up to the 10-file cap, and offers per-file retry on failure (the failed `File` stays in client memory until the page unloads). **Rejected:** retry-only-in-form (T-0084a Option C).

### B. ADR + pattern-locked (no relitigation)

- **patterns.md B.1** — Server Component page; `'use client'` only on the pay button and the attachment manager; no `useEffect` data fetching (initial data is the SSR pass; uploads/pay fire in event handlers).
- **patterns.md B.4 + B.16** — `paymentSession` + `ordersGET2` + `attachmentsPOST` wrapped in `lib/api-client-helpers/` (`payments-client.ts`, `orders-client.ts`); `apiFetch` returns `Result<T, ApiError>`.
- **patterns.md B.14 / ADR 0024** — the SSR detail fetch forwards the customer audience cookie; `Unauthorized` → login redirect, `NotFound` → `notFound()` (backend already returns 404 for foreign orders — IDOR-resistant, US-customer-0012 AC-3).
- **patterns.md B.15** — attachment uploads pass `FormData` raw.
- **patterns.md B.5 + B.7 + B.10** — i18n keys only (vykání); `<section>` wrapper; money via `formatCzk`; dates via `lib/utils/dates.ts` cs-CZ format.
- **ADR 0022** — generated client untouched.
- **CLAUDE.md "all payments verified server-side"** — this page never marks anything paid; it only hands the customer to Comgate. State truth arrives via the T-0066/67 webhook and is read back on T-0085.

### C. PM-absorbed (no user input needed)

- **PendingPayment-only actions.** Pay CTA + attachment manager render exclusively when `detail.state === OrderState.PendingPayment`. Any other state renders the minimal banner (below) — no pay button, no upload UI (even though T-0064 allows uploads in `Paid`/`Accepted`, that surface belongs to T-0086b's tracking view).
- **Auto-cancel notice (T-0083):** a visible notice that unpaid orders are cancelled 24 hours after creation, with the concrete deadline rendered as `createdAt + 24h` in Czech date-time format. Display-only mirror of the documented T-0083 rule; the backend job remains authoritative.
- **Payment error mapping:** `payment.providerUnavailable`, `payment.providerRejected`, `payment.providerMisconfigured`, `payment.providerNotRegistered`, `payment.unknownError` render as alerts via `t(code)` with the pay button re-enabled for retry. `order.invalidStateForPayment` / `order.paymentAlreadyCaptured` additionally trigger `router.refresh()` so the server re-render swaps to the correct state banner (the webhook beat the customer to it).
- **Order summary** rendered purely from the detail DTO: order number heading, state badge, breakdown lines (product, shipping with method label, VAT with `vatRateBp / 100` % display, total), contact snapshot. `productTitle` nullable → "Vlastní zakázka" fallback label (T-0080 convention).
- **Non-PendingPayment banner:** Czech state label via a new `OrderState → 'order.state.*'` mapping util (the i18n keys already exist) + a note that the full order detail view is coming + a link back to the catalog. Replaced by a redirect/render of T-0086b's tracking view once it ships.
- **`?attachmentsFailed=<n>` param** (set by T-0084a): renders a one-time alert prompting the customer to re-add the files that did not upload; the param is presentational only.
- **Redirect mechanics:** on pay success, `window.location.assign(redirectUrl)` (full document navigation to the external gateway — `router.push` is for in-app routes). In-flight guard disables the button until navigation or error.
- **Attachment manager state:** initial list from SSR props; successful uploads append optimistically to local list (and the count gate uses existing + pending). Upload errors map `order.attachmentLimitReached`, `order.stateForbidsAttachment`, `file.invalid`, `file.tooLarge`, `file.unsupportedType` via `t(code)`; per-file retry re-POSTs the in-memory `File`.

## Scope

### Helpers

- **`frontend/src/lib/api-client-helpers/payments-client.ts`** — NEW per B.16:

  ```ts
  import { apiFetch } from '../runtime/api-fetch';
  import type { ApiError, Result } from '../runtime/result';
  import type { ICreatePaymentSessionResponse } from '../api-client/customer-api.v1';

  export type PaymentSession = Readonly<ICreatePaymentSessionResponse>; // { paymentProviderRef, redirectUrl }

  export async function createPaymentSession(orderId: string): Promise<Result<PaymentSession, ApiError>> {
    return apiFetch<PaymentSession>('customer', `/api/v1/orders/${encodeURIComponent(orderId)}/payment-session`, { method: 'POST' });
  }
  ```

- **`frontend/src/lib/api-client-helpers/orders-client.ts`** — EXTEND (created in T-0084a):

  ```ts
  export type CustomerOrderDetail = Readonly<ICustomerOrderDetailDto>;
  export type OrderAttachmentSummary = Readonly<IOrderAttachmentSummaryDto>;
  export { OrderState };

  export async function getCustomerOrderDetail(orderId: string): Promise<Result<CustomerOrderDetail, ApiError>> {
    return apiFetch<{ detail: CustomerOrderDetail }>('customer', `/api/v1/customer/orders/${encodeURIComponent(orderId)}`, { method: 'GET' })
      // unwrap the GetCustomerOrderDetailsResponse envelope at the helper boundary
  }
  ```

  (Exact unwrap shape per the generated `GetCustomerOrderDetailsResponse { detail }` envelope — the helper returns the inner DTO so page code never touches the envelope.)
- **`frontend/src/lib/orders/state-labels.ts`** — NEW presentational util: `orderStateLabelKey(state: OrderState): string` — exhaustive `switch` (compile-time `never` check) mapping the enum to the existing `order.state.*` i18n keys (`PendingPayment` → `'order.state.pending_payment'`, `Paid` → `'order.state.paid'`, ... all 9 states). Shared with T-0085 and later T-0086/87. Display mapping only — no transition logic lives here or anywhere on the frontend.

### Page + components

- **`frontend/src/app/(customer)/objednavka/[id]/page.tsx`** — Server Component. Flow:

  ```
  Step 1 — SSR getCustomerOrderDetail(params.id)   // audience cookie forwarded per B.14
  Step 2 — result.error.type === 'NotFound'     → notFound()        // foreign order = 404 (IDOR)
           result.error.type === 'Unauthorized' → redirect('/auth/login?next=…')
           any other error                       → error state + retry link (loudly broken)
  Step 3 — detail.state === OrderState.PendingPayment
             → render: <order-breakdown> (server)
                       <pay-button-client orderId>
                       <attachment-manager-client orderId initialAttachments={detail.attachments}>
                       auto-cancel notice (createdAt + 24h, Czech date-time)
                       attachmentsFailed alert when searchParams carries it
           else
             → minimal state banner: t(orderStateLabelKey(state)) + "detail připravujeme" note
               + catalog link. No pay CTA, no upload UI.
  ```

- **`frontend/src/app/(customer)/objednavka/[id]/order-breakdown.tsx`** — Server Component. Renders from the DTO only:
  - heading `Objednávka {orderNumber}` + state badge (`components/ui/badge`);
  - breakdown rows: product (`productTitle` ?? custom-order fallback) → `formatCzk(productPriceMinor)`; shipping (method label: Zásilkovna point / osobní odběr) → `formatCzk(shippingPriceMinor)`; VAT row `DPH {vatRateBp / 100} %` → `formatCzk(vatAmountMinor)`; total emphasised → `formatCzk(totalAmountMinor)`;
  - contact snapshot (`contactName`, `contactPhone`);
  - auto-cancel notice: deadline computed as `createdAt + 24h`, formatted via `lib/utils/dates.ts` (display-only mirror of T-0083).
- **`frontend/src/app/(customer)/objednavka/[id]/pay-button-client.tsx`** — `'use client'`. "Zaplatit" CTA; on click (in-flight guard): `createPaymentSession(orderId)`; success → `window.location.assign(redirectUrl)` (full document navigation to the gateway); failure → alert via `t(error.code)`; on `order.invalidStateForPayment` / `order.paymentAlreadyCaptured` additionally `router.refresh()`. Button disabled from click until navigation or error.
- **`frontend/src/app/(customer)/objednavka/[id]/attachment-manager-client.tsx`** — `'use client'`. Local state seeded from `initialAttachments` SSR prop. Lists filename + human-readable size; add-more picker reuses the T-0084a `validation.ts` mirrors; count gate = existing + queued ≤ `ORDER_ATTACHMENT_MAX_FILES`; per-file lifecycle `queued → uploading → done | failed(retry)`; failed rows keep the in-memory `File` and re-POST on "Zkusit znovu"; successes append optimistically (no `router.refresh()` — see Alternatives Option E). Error codes map via `t(code)`.
- **`frontend/src/app/(customer)/objednavka/[id]/loading.tsx`** — skeleton.
- **`frontend/src/app/(customer)/objednavka/[id]/not-found.tsx`** — Czech 404 state with catalog CTA.

### i18n

Add `order.page.*` UI keys to `cs-CZ.ts` (vykání throughout). Expected set (~20 keys; exact wording drafted by implementer, PM/UX reviews on PR):

| Group | Keys |
|---|---|
| Page | `order.page.title` (with order-number placeholder), `.loadError`, `.loadErrorRetry` |
| Payment | `order.page.payCta`, `.paying`, `.expiresNotice` (with deadline placeholder) |
| Breakdown | `order.page.breakdown.product`, `.shipping`, `.vat`, `.total`, `.contact`, `.customOrderFallback` |
| Attachments | `order.page.attachments.heading`, `.addMore`, `.retry`, `.uploading`, `.done`, `.failed`, `.failedHandoffAlert` (the `attachmentsFailed` banner) |
| Banner | `order.page.banner.detailComing`, `.backToCatalog` |

No new error-code keys — `payment.*`, `order.invalidStateForPayment`, `order.paymentAlreadyCaptured`, `order.attachmentLimitReached`, `order.stateForbidsAttachment`, `file.*`, and the nine `order.state.*` labels all exist with parity enforced.

## Alternatives Considered

- **Option A — Create the payment session during SSR and render the Comgate link directly.** *Rejected per A.1* — a page view is not payment intent; every refresh/back-navigation would hit Comgate (and the verify round-trip), and crawlers/prefetchers could mint sessions. Click = intent.
- **Option B — Auto-redirect to Comgate on arrival from T-0084a (skip this page).** *Rejected per A.1* — kills the retry surface (US-customer-0010 AC-3 names this page as the retry entry), hides the attachment-failure recovery, and gives the customer no chance to review the authoritative total before paying.
- **Option C — Frontend caches/inspects `redirectUrl` freshness itself (e.g. localStorage TTL).** *Rejected* — T-0065 already owns verify-then-recreate + the cached `PaymentRedirectUrl` server-side; a client copy is duplicated business logic (B.1 violation) that would drift.
- **Option D — Render the full tracking timeline for all states now.** *Rejected* — that is T-0086b's scope in bundle 2 (locked bundle composition); duplicating it here would create two divergent order-detail surfaces. The minimal banner is loudly incomplete by design (no-mocks rule).
- **Option E — Re-fetch the detail after every attachment upload (`router.refresh()`).** *Rejected per §C* — full server re-render per file is heavy and drops in-memory failed `File` objects needed for per-file retry; optimistic local append over SSR-seeded props matches the T-0049 image-manager precedent.
- **Option F — Persist failed files (IndexedDB) so retry survives navigation.** *Rejected* — storage quota + lifecycle complexity for a marginal case; the locked recovery is "re-pick the file on the order page", which the manager supports.

## Out of scope

- **Payment confirmation / polling** — T-0085 (`/objednavka/[id]/potvrzeni`).
- **Full tracking view** (timeline, tracking URL, deliver CTA, messages thread, invoice download) — T-0086b / order-dashboards bundle (US-customer-0012/0013/0014/0017).
- **Attachment uploads in `Paid`/`Accepted` states** — allowed by T-0064 but surfaced via T-0086b, not here.
- **Attachment download links UI polish** — `downloadUrl` is rendered as a plain link; viewer/preview is post-MVP.
- **Countdown timer UX for the 24h deadline** — static deadline text suffices at MVP.
- **Cancel-order button** — no customer-cancel command exists at MVP (T-0083 auto-cancel only).

## Acceptance criteria

- **AC-1** Given a customer owns an order in `PendingPayment`, when they visit `/objednavka/<id>`, then the SSR pass renders order number, state badge, full breakdown (product, shipping, VAT %, total via `formatCzk`), contact snapshot, the "Zaplatit" CTA, the attachment manager, and the auto-cancel notice showing the `createdAt + 24h` deadline in Czech format. No client-side data fetch fires on initial render.
- **AC-2** Given the page loads, when network traffic is inspected, then **no** `payment-session` request was made — the session is created only on CTA click (Q3 lock; network panel proof).
- **AC-3** Given the customer clicks "Zaplatit", when `paymentSession` succeeds, then the browser performs a full navigation to `redirectUrl` (Comgate). The button is disabled from click until navigation/error (rapid double-click fires one POST).
- **AC-4** Given the customer returns (browser back) and clicks "Zaplatit" again within 24h, when the backend serves the cached/verified session, then the redirect works again with no frontend-special-casing — the frontend made the identical call (T-0065 owns the retry logic).
- **AC-5** Given `paymentSession` fails with `payment.providerUnavailable` (or any `payment.*` code), when the response settles, then a Czech alert renders via `t(code)` and the CTA re-enables. Given `order.invalidStateForPayment` or `order.paymentAlreadyCaptured`, then the alert renders AND `router.refresh()` re-renders the page into the state banner.
- **AC-6** Given the order has 2 uploaded attachments, when the page renders, then both show filename + size; the add-more picker allows at most 8 further files (10-cap counts existing), and client pre-checks (type/size) reject invalid files before any network call.
- **AC-7** Given an upload fails (network or `file.*`/`order.*` error), when it settles, then the file row shows a Czech error + "Zkusit znovu" which re-uploads the same in-memory file; a success appends the file to the list without a full page reload.
- **AC-8** Given arrival at `/objednavka/<id>?attachmentsFailed=2`, when the page renders, then a one-time alert prompts re-adding the files that did not upload.
- **AC-9** Given the order is in any state other than `PendingPayment`, when the page renders, then a minimal banner shows the Czech state label (`order.state.*` via the mapping util) + "detail view coming" note + catalog link; no pay CTA, no attachment manager. Given a foreign or unknown order id → `notFound()` (404 page); given no session → redirect to login with `next`.
- **AC-10** Hygiene + responsive: zero `any`/`console.*`/`useEffect`-fetching; `'use client'` only on `pay-button-client.tsx` + `attachment-manager-client.tsx`; `<section>` wrapper; all copy from `cs-CZ.ts`; `lib/api-client/` untouched; lint + `tsc --noEmit` + `next build` clean; layout verified at 375/768/1280 on the Vercel preview.

## Risk / mitigation

- **Webhook races the customer (pays in another tab, returns here)** → stale `PendingPayment` view may show the pay CTA; clicking it returns `order.invalidStateForPayment`/`order.paymentAlreadyCaptured` which triggers refresh into the correct banner (AC-5). Server-side verification means no double charge is possible.
- **bfcache shows a stale page after returning from Comgate** → same mitigation; T-0085 is the canonical post-payment landing anyway.
- **Auto-cancel fires while the customer dawdles past 24h** → pay click returns the state-conflict error → refresh → `Cancelled` banner. The deadline notice (AC-1) reduces surprise.
- **Attachment cap confusion (form uploads + manager uploads)** → count gate uses existing + pending (AC-6); backend 409 remains the authority and maps to existing i18n.
- **Customers landing here in post-payment states before T-0086b ships** → minimal banner is intentionally loud about being incomplete; bundle 2 closes it. Flagged in the PR description.

## Test plan reference

Manual Playwright-style plan at **`docs/test-plans/T-0084b.md`** (stub — filled before bundle PR review). QA surface: Vercel preview against staging backend + Comgate sandbox (`Comgate:TestMode = true`). Pre-conditions: order created via T-0084a flow; one order force-advanced to `Paid` on staging for the banner case. The stub must cover at minimum: fresh-order render (breakdown + deadline correctness), pay-click → Comgate redirect, back-navigation + second pay-click (cached session), every `payment.*` error alert (fake provider toggle on staging), attachment add/retry/cap, `attachmentsFailed` alert, non-PendingPayment banner, 404/login redirects, and the three breakpoints.

## Files touched (expected)

### New
- `frontend/src/app/(customer)/objednavka/[id]/page.tsx`
- `frontend/src/app/(customer)/objednavka/[id]/order-breakdown.tsx`
- `frontend/src/app/(customer)/objednavka/[id]/pay-button-client.tsx`
- `frontend/src/app/(customer)/objednavka/[id]/attachment-manager-client.tsx`
- `frontend/src/app/(customer)/objednavka/[id]/loading.tsx`
- `frontend/src/app/(customer)/objednavka/[id]/not-found.tsx`
- `frontend/src/lib/api-client-helpers/payments-client.ts`
- `frontend/src/lib/orders/state-labels.ts`
- `docs/test-plans/T-0084b.md` (stub)

### Modified
- `frontend/src/lib/api-client-helpers/orders-client.ts` — add `getCustomerOrderDetail` + DTO re-exports
- `frontend/src/lib/i18n/cs-CZ.ts` — `order.page.*` UI keys

## Commits hint

1. `feat(T-0084b): payments-client + order detail helper + state-label util + i18n keys`
2. `feat(T-0084b): /objednavka/[id] page, breakdown, pay CTA, attachment manager`

## Status log

- 2026-06-09 `draft` by PM. Created as the second ticket in the checkout-flow bundle (after T-0084a, before T-0085; one PR, sequential). Consumes merged T-0065 (`paymentSession`), T-0082 (`ordersGET2` detail), T-0064 (`attachmentsPOST`), T-0083 (24h auto-cancel rule for the notice). Frontend-only — no contract change, no regen.
- 2026-06-09 `draft → ready` by PM. User locked 2 dimensions at grooming: **A.1** explicit "Zaplatit" click creates the session — never on page load (rejected SSR-create + auto-redirect); **A.2** this page is the attachment retry surface (upload manager: existing files + add-more to 10 + per-file retry). 8 PM-absorbed decisions captured in §C (PendingPayment-only actions, auto-cancel notice, payment error → i18n mapping + refresh-on-conflict, DTO-only summary, minimal state banner until T-0086b, `attachmentsFailed` alert, `window.location.assign` redirect, optimistic attachment list). **Ready for frontend** after T-0084a lands in the bundle branch.

## Definition of Ready

- [x] User story linked (US-customer-0010 AC-2/AC-3) and AC traceable
- [x] All backend dependencies merged (T-0064, T-0065, T-0082, T-0083); DTO shapes verified in `customer-api.v1.ts`
- [x] User-locked decisions captured with rebutted alternatives (deliberation policy)
- [x] No blocking open questions; absorbed defaults flagged in §C for PR review
- [x] i18n keys enumerated; error-code parity confirmed pre-existing (`payment.*`, `order.*`, `file.*`, `order.state.*`)
- [x] Test plan stub path agreed (`docs/test-plans/T-0084b.md`); QA surface = Vercel preview + Comgate sandbox
- [x] Owner assigned (`frontend`); size M; bundle position fixed (2 of 3)
