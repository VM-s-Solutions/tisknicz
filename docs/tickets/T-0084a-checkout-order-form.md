---
id: T-0084a
title: Checkout order form at /objednavka — contact + shipping + attachments
status: ready
size: M
owner: frontend
created: 2026-06-09
updated: 2026-06-09
depends_on: [T-0063, T-0064, T-0070]
blocks: [T-0084b]
user_stories: [US-customer-0010, US-customer-0011]
adrs: [0005, 0016, 0017, 0022, 0024]
phase: 4
manual_steps: [vercel-preview-manual-qa]
security_touching: false
layers: [frontend]
---

# T-0084a — Checkout order form at /objednavka

## Context

T-0084a is the **first ticket in the checkout-flow bundle** (`feat/checkout-flow-bundle`: T-0084a order form → T-0084b pre-payment order page → T-0085 payment confirmation). The bundle ships FIRST among the Phase-4 frontend bundles because it closes the revenue path: every backend piece (T-0063 CreateOrder, T-0064 attachments, T-0065 payment session, T-0066/67 webhook + emails, T-0070 widget-config) is merged and user-callable, but the customer has no UI to call any of it. The order routes are placeholders today.

This ticket ships the order placement form at `/objednavka?productId=...` (route group `(customer)` per patterns.md B.2), satisfying **US-customer-0010** AC-1 + AC-4 (form, validation in Czech) and **US-customer-0011** AC-1 + AC-3 (personal-pickup option shown/gated). The redirect-to-Comgate half of US-customer-0010 (AC-2/AC-3) is T-0084b's job — per the T-0063 Q1 lock, CreateOrder returns `{orderId, orderNumber, totalPriceMinor, currency}` and the frontend navigates to `/objednavka/<orderId>` where payment is a separate explicit action.

Consumed contracts (all on master, NSwag-typed — **zero backend changes in this ticket**):
- `ordersPOST(CreateOrderRequest)` → `CreateOrderResponse` — customer host, T-0063. Validator: name 2–100, Czech phone `^(\+420\s?)?[6-9]\d{2}\s?\d{3}\s?\d{3}$`, email ≤254, notes ≤2000, quantity == 1, `zasilkovnaPickupPointId` required iff `ShippingMethod.ZasilkovnaPickupPoint`.
- `attachmentsPOST(orderId, FileParameter)` → `UploadOrderAttachmentResponse` — T-0064. PDF/JPEG/PNG/WebP, ≤10 MiB each, ≤10 per order; attachments are uploaded AFTER order creation by API design (T-0063 Q3 lock).
- `widgetConfig(country, locale)` → `PickupPointWidgetConfig { scriptUrl, publicKey, options }` — public host, T-0070, anonymous, `Cache-Control: public, max-age=3600`.
- `ProductDetail` (public host, T-0045/T-0048) — order-summary data + `makerSlug`; `MakerProfile` (public host, T-0044/T-0047) — `personalPickupEnabled` + `pickupNote` + `city` for the personal-pickup gate.

Error-code surface already has full i18n parity (`order.invalidQuantity`, `product.notActive`, `maker.deactivated`, `maker.notVerified`, `maker.personalPickupDisabled`, `auth.emailNotConfirmed`, `file.*`, `order.attachmentLimitReached`, `order.stateForbidsAttachment`). This ticket adds **UI copy keys only** — no error-code keys, no contract change, no NSwag regen.

## Locked design decisions

### A. User-locked (2026-06-09 grooming, non-negotiable)

1. **Single page + sticky order summary (Q1).** One scroll, mobile-first single column. Summary card (product image, title, maker, price breakdown from product data) becomes sticky on desktop ≥1280 (`lg:sticky`). **Rejected:** multi-step wizard (see Alternatives, Option A).
2. **Attachments selected IN the form, uploaded AFTER CreateOrder succeeds (Q2).** File picker lives in the form (PDF/JPEG/PNG/WebP, ≤10 files, ≤10 MiB each — T-0064 limits mirrored client-side as UX pre-checks only). Files are held client-side; on submit the flow is create → upload each file → navigate to `/objednavka/<orderId>`. Partial upload failures still navigate — the order page (T-0084b attachment manager) is the retry surface. **Rejected:** block-on-failure (Option C), pre-create temp upload (Option B).

### B. ADR + pattern-locked (no relitigation)

- **patterns.md B.1** — Server Components by default; `'use client'` only at the form boundary; no `useEffect` data fetching; **no business logic client-side** — every validation mirror in this ticket is a UX-only duplicate of a T-0063/T-0064 backend rule, and the backend stays authoritative.
- **patterns.md B.4 + B.16** — every call goes through `apiFetch` via a hand-written helper in `lib/api-client-helpers/`; route code never imports `lib/api-client/` directly; helpers re-export DTO types.
- **patterns.md B.14 / ADR 0024** — SSR fetches forward the audience cookie automatically; the customer-host profile fetch on the server render is what detects the unauthenticated case.
- **patterns.md B.15** — attachment upload passes `FormData` as raw `body`; no manual `Content-Type`.
- **patterns.md B.17** — `ApiError.fields` drives inline field errors; FluentValidation PascalCase property names normalised to camelCase at the form layer.
- **patterns.md B.5 + B.7 + B.10** — all copy via `cs-CZ.ts` i18n keys (vykání for customers); route wrapper is `<section>`; money via `formatCzk`.
- **ADR 0022** — generated client untouched; pre-commit hook enforces.
- **CLAUDE.md no-mocks rule** — nothing is stubbed; if a fetch fails the page shows the error state loudly.

### C. PM-absorbed (no user input needed)

- **Contact-field UX mirrors** duplicate the T-0063 validator exactly: name 2–100 chars, Czech phone regex `^(\+420\s?)?[6-9]\d{2}\s?\d{3}\s?\d{3}$`, email format (prefilled from the customer profile, editable), notes ≤2000 with a live character counter. Mirrors live in `lib/utils/validation.ts` per patterns.md B.2; backend response remains the authority (server-side rejections render via `ApiError.fields`).
- **Shipping method selection:** radio between `ZasilkovnaPickupPoint` and `PersonalPickup`. Zásilkovna opens the official Packeta widget v6 in a modal (script URL + public key + options from the public widget-config endpoint, fetched SSR and passed as props). Personal pickup is gated on `MakerProfile.personalPickupEnabled` — when false the radio is disabled with a tooltip (US-customer-0011 AC-3); when chosen, the maker's `pickupNote` + `city` render (the only pickup fields the public DTO exposes; precise address coordination is post-order via the message thread, consistent with the escrow model).
- **Widget failure handling:** script-load failure or widget runtime error disables the Zásilkovna option, shows an error notice + a retry affordance that re-attempts the script load. If the maker also lacks personal pickup, the submit stays disabled with an explanatory alert (loudly broken beats silently wrong).
- **Submit idempotency** via disabled button + in-flight guard (T-0063 Q2 lock — no backend Idempotency-Key).
- **Pricing display:** the summary shows the product price from `ProductDetail` via `formatCzk`. The shipping price is NOT computed client-side (no business logic); the shipping line reads an i18n note that shipping is itemised in the order summary after submission. The authoritative total comes back in `CreateOrderResponse.totalPriceMinor` and is displayed on T-0084b. No checkout-preview endpoint exists at MVP.
- **Entry guards:** missing/blank `productId` → invalid-link state with catalog CTA. Product fetch `NotFound` → `notFound()`. `priceType == 'on_request'` → redirect to the product page (no order CTA per US-customer-0009 AC-4). Unauthenticated (profile SSR fetch returns `Unauthorized`) → `redirect('/auth/login?next=...')`. Email-unconfirmed is NOT pre-checked client-side — the T-0063 middleware 403 `auth.emailNotConfirmed` maps to a form-level alert with a resend hint.
- **Upload-failure handoff:** when ≥1 attachment upload fails after a successful create, navigation appends `?attachmentsFailed=<n>`; T-0084b renders a one-time alert from it.
- **Quantity** is fixed at 1 (T-0061/T-0063 invariant): hidden constant in the request, no quantity UI.

## Scope

### Helpers

- **`frontend/src/lib/api-client-helpers/orders-client.ts`** — NEW per B.16, sibling of `catalog.ts` / `profile.ts`:

  ```ts
  import { apiFetch } from '../runtime/api-fetch';
  import type { ApiError, Result } from '../runtime/result';
  import type { ICreateOrderRequest, ICreateOrderResponse, IUploadOrderAttachmentResponse } from '../api-client/customer-api.v1';
  import { ShippingMethod } from '../api-client/customer-api.v1';

  const Base = '/api/v1/orders';

  export type CreateOrderInput = Readonly<ICreateOrderRequest>;
  export type CreateOrderResult = Readonly<ICreateOrderResponse>;
  export { ShippingMethod };

  export async function createOrder(input: CreateOrderInput): Promise<Result<CreateOrderResult, ApiError>> {
    return apiFetch<CreateOrderResult>('customer', Base, { method: 'POST', json: input });
  }

  export async function uploadOrderAttachment(orderId: string, file: File): Promise<Result<Readonly<IUploadOrderAttachmentResponse>, ApiError>> {
    const formData = new FormData();
    formData.append('file', file);
    return apiFetch('customer', `${Base}/${encodeURIComponent(orderId)}/attachments`, { method: 'POST', body: formData }); // B.15 — no manual Content-Type
  }
  ```

  T-0084b extends this file with `getCustomerOrderDetail`; T-0085 reuses it.
- **`frontend/src/lib/api-client-helpers/shipping.ts`** — NEW: `getWidgetConfig(country = 'CZ', locale = 'cs-CZ')` → `Result<Readonly<IPickupPointWidgetConfig>, ApiError>` against the public host (anonymous; the endpoint serves `Cache-Control: public, max-age=3600`, so SSR fetches are CDN/browser-cacheable for free).
- **`frontend/src/lib/utils/validation.ts`** — add UX mirrors, each constant commented with its backend source of truth:

  ```ts
  // Mirror of T-0063 CreateOrder.Validator (backend authoritative — UX pre-check only)
  export const CZECH_PHONE_PATTERN = /^(\+420\s?)?[6-9]\d{2}\s?\d{3}\s?\d{3}$/;
  export const ORDER_CONTACT_NAME_MIN = 2;
  export const ORDER_CONTACT_NAME_MAX = 100;
  export const ORDER_NOTES_MAX = 2000;
  // Mirror of T-0064 OrderAttachmentValidator + Order.MaxAttachmentCount
  export const ORDER_ATTACHMENT_MAX_FILES = 10;
  export const ORDER_ATTACHMENT_MAX_BYTES = 10 * 1024 * 1024;
  export const ORDER_ATTACHMENT_ALLOWED_TYPES = new Set(['application/pdf', 'image/jpeg', 'image/png', 'image/webp']);
  ```

### Page + components

- **`frontend/src/app/(customer)/objednavka/page.tsx`** — Server Component. Reads `searchParams.productId`; SSR-fetches product detail, maker profile (via `product.makerSlug`), widget config, and customer profile (cookie-forwarded per B.14); applies the §C entry guards; renders `<section>` with the summary + form.
- **`frontend/src/app/(customer)/objednavka/order-summary.tsx`** — Server Component. Product image (`buildProductImageUrl`), title, maker name + verified badge, price via `formatCzk`, shipping i18n note. Sticky ≥1280; stacks above the form on mobile.
- **`frontend/src/app/(customer)/objednavka/order-form-client.tsx`** — `'use client'`. Owns form state, mirror validation, shipping-method radio + pickup-point state, `ApiError.fields` → inline errors (camelCase-normalised per B.17), error-code → `t(code)` alerts, disabled-button in-flight guard. Submit orchestration:

  ```
  Step 1 — Mirror validation. Any failure → field errors, no network. Stop.
  Step 2 — Guard: if submitting, return (in-flight guard). Set submitting = true; disable button.
  Step 3 — createOrder({ productId, quantity: 1, shippingMethod, zasilkovnaPickupPointId,
            customerName, customerEmail, customerPhone, customerNotes })
            failure → map: fields → inline; known codes (product.notActive, maker.*,
            auth.emailNotConfirmed, order.invalidQuantity) → form-level alert via t(code);
            Unauthorized → redirect /auth/login?next=…  Re-enable button. Stop.
  Step 4 — For each picked file, sequentially: uploadOrderAttachment(orderId, file);
            update per-file status (nahrává se → hotovo | chyba). Never abort the loop on failure.
  Step 5 — Navigate: router.push(`/objednavka/${orderId}` + (failedCount > 0
            ? `?attachmentsFailed=${failedCount}` : '')). The button stays disabled
            through navigation — the create already succeeded; re-enabling invites a duplicate.
  ```
- **`frontend/src/app/(customer)/objednavka/attachment-picker.tsx`** — `'use client'`. Multi-file input; pre-checks per file (type/size) + count; list with name + size + remove; during submit shows per-file status (čeká → nahrává se → hotovo/chyba).
- **`frontend/src/components/shared/zasilkovna-widget.tsx`** — `'use client'` wrapper per patterns.md B.2 layout (`components/shared/ZasilkovnaWidget` slot). Contract:

  ```ts
  export interface ZasilkovnaWidgetProps {
    readonly scriptUrl: string;            // from PickupPointWidgetConfig (SSR prop)
    readonly publicKey: string;
    readonly options: Readonly<Record<string, string>>; // { country, language } from widget-config
    readonly onPick: (point: { readonly id: string; readonly name: string }) => void;
    readonly onError: () => void;          // script-load OR runtime failure
  }
  ```

  Lazily injects the Packeta v6 script on first open (one `<script>` tag, load/error handlers); calls `Packeta.Widget.pick(publicKey, callback, options)`; the callback's null/point result maps to close/`onPick`. The Packeta global is typed via a minimal local `declare global` interface (no `any`); the script tag is the one sanctioned third-party exception — it is the official widget, not an API call (Comgate/Packeta REST APIs remain backend-only per CLAUDE.md).
- **`frontend/src/app/(customer)/objednavka/loading.tsx`** — skeleton (summary card + form blocks).

### i18n

Add `checkout.*` UI keys to `frontend/src/lib/i18n/cs-CZ.ts` (vykání throughout — customer audience). Expected set (~30 keys; exact wording drafted by implementer, PM/UX reviews on PR):

| Group | Keys |
|---|---|
| Page | `checkout.title`, `checkout.subtitle`, `checkout.invalidLink.title`, `checkout.invalidLink.cta` |
| Contact | `checkout.contact.legend`, `.name`, `.namePlaceholder`, `.email`, `.phone`, `.phonePlaceholder`, `.notes`, `.notesCounter` |
| Shipping | `checkout.shipping.legend`, `.zasilkovna`, `.personalPickup`, `.personalPickupDisabled` (tooltip), `.pickupInfo` (note + city frame) |
| Widget | `checkout.pickupPoint.choose`, `.change`, `.chosen`, `checkout.widget.error`, `.retry` |
| Attachments | `checkout.attachments.label`, `.hint` (limits copy), `.remove`, `.statePending`, `.stateUploading`, `.stateDone`, `.stateFailed`, `.rejectedType`, `.rejectedSize`, `.rejectedCount` |
| Summary | `checkout.summary.product`, `.shippingNote`, `.totalNote` |
| Submit | `checkout.submit`, `.submitting`, `.uploadProgress`, `.emailNotConfirmedHint` |

No new error-code keys — `BusinessErrorMessage` parity is already complete for every code this form can receive.

## Alternatives Considered

- **Option A — Multi-step wizard (contact → shipping → attachments → review).** *Rejected per A.1* — a single product, one quantity, and ≤8 fields don't justify step navigation; wizards multiply abandonment points on mobile, and the sticky summary keeps orientation without steps.
- **Option B — Upload attachments BEFORE order creation to temp storage.** *Rejected per A.2 + T-0063 Q3* — the backend API is deliberately post-create (`POST /orders/{id}/attachments`); a temp-blob area would need backend work, orphan GC, and a claim step. Out of charter for a frontend ticket.
- **Option C — Block navigation until every failed upload is retried in-form.** *Rejected per A.2* — holds the customer hostage on a flaky connection between them and their money. The order exists; the retry surface is the order page (T-0084b), which also covers the "closed the tab mid-upload" case for free.
- **Option D — Next.js Server Action for submission.** *Rejected per B.4/B.16* — `apiFetch` is the single HTTP chokepoint (auth cookies, RFC7807 parsing, `Result<T, ApiError>`); a Server Action would bypass the established error-mapping path and split the call-site convention.
- **Option E — Inline pickup-point `<select>` from a Packeta REST list instead of the widget.** *Rejected per T-0070 design* — thousands of pickup points need the official widget's map/search UX; the widget-config endpoint exists precisely to feed it.
- **Option F — Compute the shipping price + total client-side for the summary.** *Rejected per CLAUDE.md / B.1* — pricing is backend business logic (CountryConfiguration-driven); a drifting client copy is a guaranteed parity bug. The note-then-authoritative-total flow costs one line of copy.
- **Option G — Pre-emptive email-confirmed gate via a session-state check in the form.** *Rejected* — no endpoint exposes confirmation state today; the backend 403 is authoritative and already has an i18n key. Building UI on an unbuilt contract violates the no-mocks rule.

## Out of scope

- **Pay CTA / payment session** — T-0084b (`/objednavka/[id]`, explicit "Zaplatit" click).
- **Attachment retry manager** — T-0084b owns the post-create upload surface.
- **Payment confirmation page** — T-0085.
- **Order tracking view for non-PendingPayment states** — T-0086b (order-dashboards bundle).
- **Quantity > 1, promo codes, B2B invoice fields** — per US-customer-0010 out-of-scope + T-0061 invariant.
- **Checkout pricing-preview endpoint** — future T-0099; until then the total is confirmed post-create.
- **401 → refresh → retry in `apiFetch`** — post-launch roadmap per patterns.md B.3; 401 redirects to login.
- **Custom orders (no productId)** — messages-thread flow, not this form.

## Acceptance criteria

- **AC-1** Given an authenticated customer visits `/objednavka?productId=<active fixed-price product>`, when the server render completes, then the page shows the order form + summary card (product image, title, maker name, `formatCzk` price) with the email field prefilled from the customer profile. Initial render is a Server Component pass — no client-side data fetch fires on load (network panel proof).
- **AC-2** Given the viewport is ≥1280, when the form scrolls, then the summary card stays sticky; at 375 and 768 the layout is a single column with the summary above the form.
- **AC-3** Given an unauthenticated visitor, when they request the page, then the server redirects to `/auth/login?next=<encoded original URL>`. Given a missing/blank `productId`, then the invalid-link state with a catalog CTA renders. Given an inactive/unknown product, then `notFound()` renders.
- **AC-4** Given the customer enters a non-Czech phone, a 1-char name, or 2001-char notes, when they attempt submit, then mirror validation blocks the POST and renders Czech field-level errors; given the backend rejects a field anyway, then `ApiError.fields` entries render under the matching inputs (camelCase-normalised).
- **AC-5** Given Zásilkovna is selected, when the customer clicks the pickup-point button, then the Packeta widget opens (script lazily loaded from the SSR-fetched `scriptUrl` + `publicKey`); choosing a point closes the modal and shows the point's name with a "Změnit" affordance; submit without a chosen point is blocked with a Czech error.
- **AC-6** Given the widget script fails to load or errors, when the failure surfaces, then the Zásilkovna option is disabled with an error notice + retry affordance; retry re-attempts the script load. Submit stays blocked while no valid shipping method is selectable.
- **AC-7** Given the maker has `personalPickupEnabled == false`, when the form renders, then the personal-pickup radio is disabled with a tooltip (US-customer-0011 AC-3). Given it is enabled and selected, then the widget UI hides and the maker's pickup note + city render, and the request carries `shippingMethod: PersonalPickup` with `zasilkovnaPickupPointId` undefined.
- **AC-8** Given valid inputs and 2 attachments, when the customer submits, then exactly one `ordersPOST` fires (button disabled + in-flight guard; rapid double-click proof), followed by sequential `attachmentsPOST` calls with per-file status, then navigation to `/objednavka/<orderId>`.
- **AC-9** Given 1 of 3 attachment uploads fails, when uploads settle, then navigation still proceeds to `/objednavka/<orderId>?attachmentsFailed=1` (retry surface = T-0084b). Given an 11th file or an 11-MiB file or a `.zip` is picked, then the pre-check rejects it client-side with Czech copy before any network call.
- **AC-10** Given the backend returns `product.notActive`, `maker.deactivated`, `maker.notVerified`, `maker.personalPickupDisabled`, or `auth.emailNotConfirmed`, when the submit settles, then a form-level alert renders the existing i18n copy via `t(code)`; no raw `error.message` leaks.
- **AC-11** Hygiene: zero `any`, zero `console.*`, zero `useEffect` data fetching; `'use client'` only on `order-form-client.tsx`, `attachment-picker.tsx`, `zasilkovna-widget.tsx`; route wrapper is `<section>`; all copy via `cs-CZ.ts`; `lib/api-client/` untouched (pre-commit hook passes); lint + `tsc --noEmit` + `next build` clean.
- **AC-12** Responsive at 375/768/1280 verified on the Vercel preview per the manual test plan; UI primitives come from `components/ui/` (no arbitrary Tailwind values).

## Risk / mitigation

- **Third-party Packeta script outage or CSP friction** → lazy load only on demand; failure degrades to disabled option + retry (AC-6); personal pickup remains a path when the maker offers it.
- **Partial attachment upload after successful create** → by design lands on the T-0084b retry surface (AC-9); order is never lost.
- **Double submission creating two orders** → disabled-button + in-flight guard is the locked mitigation (T-0063 Q2); AC-8 pins it.
- **Preview QA blocked by missing Packeta public widget key** → T-0070 manual step (`packeta-public-widget-key-secret`) must be set in the preview environment; test plan flags it as a pre-condition.
- **Validator drift between mirror and backend** → mirrors carry source-of-truth comments; backend rejection path (AC-4 second half) keeps UX correct even when drifted.

## Test plan reference

Manual Playwright-style plan at **`docs/test-plans/T-0084a.md`** (stub — filled before bundle PR review). QA surface: Vercel preview against the staging backend. Pre-conditions: confirmed customer account, active fixed-price product, maker with and without personal pickup, Packeta public widget key configured.

## Files touched (expected)

### New
- `frontend/src/app/(customer)/objednavka/page.tsx`
- `frontend/src/app/(customer)/objednavka/order-summary.tsx`
- `frontend/src/app/(customer)/objednavka/order-form-client.tsx`
- `frontend/src/app/(customer)/objednavka/attachment-picker.tsx`
- `frontend/src/app/(customer)/objednavka/loading.tsx`
- `frontend/src/components/shared/zasilkovna-widget.tsx`
- `frontend/src/lib/api-client-helpers/orders-client.ts`
- `frontend/src/lib/api-client-helpers/shipping.ts`
- `docs/test-plans/T-0084a.md` (stub)

### Modified
- `frontend/src/lib/utils/validation.ts` — contact + attachment UX mirrors
- `frontend/src/lib/i18n/cs-CZ.ts` — `checkout.*` UI keys

## Commits hint

1. `feat(T-0084a): orders-client + shipping helpers, validation mirrors, checkout i18n keys`
2. `feat(T-0084a): zasilkovna widget wrapper + attachment picker`
3. `feat(T-0084a): /objednavka page, sticky summary, order form + submit flow`

## Status log

- 2026-06-09 `draft` by PM. Created as the first ticket in the checkout-flow bundle (T-0084a form → T-0084b pre-payment page → T-0085 confirmation; one PR, sequential implementation). All consumed backend contracts (T-0063/64/65/70) merged; NSwag client carries `ordersPOST`, `attachmentsPOST`, `widgetConfig`. Frontend-only — no contract change, no regen.
- 2026-06-09 `draft → ready` by PM. User locked 2 dimensions at grooming: **A.1** single page + sticky summary (rejected wizard); **A.2** attachments picked in-form, uploaded post-create, partial failures land on the T-0084b retry surface (rejected pre-create temp upload + block-on-failure). 8 PM-absorbed decisions captured in §C (validator mirrors, shipping-method gating, widget failure degradation, disabled-button idempotency, no client-side pricing, entry guards, `attachmentsFailed` handoff, fixed quantity). **Ready for frontend.**

## Definition of Ready

- [x] User stories linked (US-customer-0010, US-customer-0011) and AC traceable to them
- [x] All backend dependencies merged to master (T-0063, T-0064, T-0070); endpoint shapes verified in `customer-api.v1.ts` / `public-api.v1.ts`
- [x] User-locked decisions captured with rebutted alternatives (deliberation policy)
- [x] No blocking open questions; absorbed defaults flagged in §C for PR review
- [x] i18n keys enumerated; error-code parity confirmed pre-existing
- [x] Test plan stub path agreed (`docs/test-plans/T-0084a.md`); QA surface = Vercel preview
- [x] Owner assigned (`frontend`); size M; bundle position fixed (1 of 3)
