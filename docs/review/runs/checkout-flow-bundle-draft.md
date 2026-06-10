# Checkout-flow bundle — Reviewer preliminary verdict (draft)

> Bundle-scope draft per `docs/process/routing.md` "Bundling related tickets into one PR" §parallel-reviewer. Final verdict happens after the implementer reports done; this is the early-warning pass before any diff exists. Bundle: `feat/checkout-flow-bundle` = T-0084a (order form) → T-0084b (pre-payment page) → T-0085 (confirmation). Sources verified against the working tree on 2026-06-10.

## Bundle scope (T-0084a + T-0084b + T-0085)

Three frontend-only tickets that close the revenue path — the platform's first revenue-path UI. Every consumed backend contract (T-0063 CreateOrder, T-0064 attachments, T-0065 payment-session, T-0066/67 webhook → MarkOrderPaid, T-0070 widget-config, T-0082 detail, T-0083 auto-cancel) is merged and typed in the generated client (verified: `ordersPOST`/`ordersGET2`/`paymentSession`/`attachmentsPOST` at `frontend/src/lib/api-client/customer-api.v1.ts:43-59`; `widgetConfig` at `public-api.v1.ts:48`). **Zero backend changes, zero NSwag regen** — the PR diff must contain no `lib/api-client/` changes at all. New surface: `/objednavka` form (route group `(customer)`), `/objednavka/[id]` pre-payment page with explicit Zaplatit CTA + attachment retry manager, `/objednavka/[id]/potvrzeni` Comgate-return page with optimistic render + capped poll-to-Paid. Five user-locked dimensions (T-0084a Q1/Q2, T-0084b Q3/Q2-retry, T-0085 Q4) are non-negotiable; 23 PM-absorbed §C decisions get verified row-by-row at PR-open.

## Patterns / rules the diff must honour

- **patterns.md B.1** — Server Components by default. `'use client'` exactly six files bundle-wide: `order-form-client.tsx`, `attachment-picker.tsx`, `zasilkovna-widget.tsx` (T-0084a AC-11), `pay-button-client.tsx`, `attachment-manager-client.tsx` (T-0084b AC-10), `payment-poll-client.tsx` (T-0085 AC-8). Any seventh `'use client'` needs explicit justification.
- **patterns.md B.4 + B.16** — every call through `apiFetch` via hand-written helpers in `lib/api-client-helpers/` (`orders-client.ts`, `payments-client.ts`, `shipping.ts`); route code never imports `lib/api-client/` directly; helpers re-export DTO types. Sibling precedent: `auth.ts`, `profile.ts`, `catalog.ts`, `maker-products.ts`.
- **No `useEffect` data fetching — with ONE sanctioned exception.** The T-0085 poller is locked by grooming (A.1) and scoped by ticket §B: "the no-`useEffect`-fetch rule targets initial data loading — initial data here IS server-fetched; the client effect is a verification *timer* re-invoking the existing helper". The exception is valid ONLY in the ticket-pseudocode shape: SSR initial fetch, 3s interval, 30s active cap, visibility-pause with freeze + immediate-poll-on-return, cleanup of interval + listener on unmount. Any other `useEffect` fetch anywhere in the bundle (widget config, profile, detail re-fetch) = request changes.
- **No client-side pricing math.** Summary price from `ProductDetail` via `formatCzk` (B.10); authoritative total only from `CreateOrderResponse.totalPriceMinor` / `CustomerOrderDetailDto.totalAmountMinor`. The only client arithmetic allowed: display-scale conversion (`vatRateBp` → percent, per B.12 expect a named constant, not a magic `/ 100`) and the display-only `createdAt + 24h` deadline (T-0084b §C, named constant + T-0083 source comment expected).
- **patterns.md B.5 + B.14 (i18n)** — all copy via `cs-CZ.ts` keys, vykání. `t()` is `MessageKey`-typed (`cs-CZ.ts:462`) — see HIGH-5 below for the consequence.
- **patterns.md B.7** — `<section>` route wrapper on every `page.tsx` / `loading.tsx` / `not-found.tsx`.
- **patterns.md B.14 / ADR 0024** — SSR detail/profile fetches rely on the audience-cookie forwarding already in `api-fetch.ts:110-116`; no hand-rolled cookie code in pages.
- **patterns.md B.15** — attachment uploads pass `FormData` as raw `body`, no manual `Content-Type`.
- **patterns.md B.17** — `ApiError.fields` drives inline errors; PascalCase → camelCase normalisation at the form layer.
- **ADR 0022** — `lib/api-client/` untouched; pre-commit hook (`scripts/check-api-client-manual-edits.mjs`) enforces.
- **CLAUDE.md payments rule** — "All payments verified server-side. Never trust the client-side redirect params from Comgate alone." This is the bundle's load-bearing rule; T-0085 AC-6 is its test.
- **CLAUDE.md no-mocks rule** — failed SSR fetches render loud error states; the non-PendingPayment banner is intentionally incomplete until T-0086b.

## Pre-flight risks (HIGH first)

### HIGH

- **HIGH-1: Payment-session-on-click (T-0084b Q3, user-locked).** No Comgate session may be created on page load/render — not SSR, not a mount effect, not a prefetch. The ONLY `createPaymentSession` call site is the `pay-button-client.tsx` click handler. AC-2 pins it via network panel. Also verify: no `<Link prefetch>` or speculative trigger pointing at any session-creating path, and the success path uses `window.location.assign(redirectUrl)` (full document navigation), not `router.push`. Reject on sight if a session is minted anywhere outside the click handler.

- **HIGH-2: Confirmation page must never trust redirect params (T-0085 Q4 + CLAUDE.md).** `?status=` drives ONLY the failure-branch and the optimistic frame choice. The success view is granted exclusively by backend-read `detail.state === OrderState.Paid` (SSR short-circuit row 3 or a poll result). The crafted-param AC (AC-6: forged `?status=paid` on an unpaid order must NOT show success) is the test — I will trace the decision matrix in `potvrzeni/page.tsx` row by row against the ticket table and verify the poller cannot reach `view = 'success'` from anything but `state === Paid`. Failure-status matching must be case-insensitive on the documented set (`cancelled|cancel|failed|error`); unknown values fall to the optimistic path.

- **HIGH-3: Attachment upload orchestration (T-0084a Q2 + T-0084b Q2).** Files held client-side → exactly one `ordersPOST` (disabled-button + in-flight guard; AC-8 rapid double-click proof) → sequential `attachmentsPOST` loop that NEVER aborts on per-file failure → navigate with `?attachmentsFailed=<n>` when ≥1 failed (AC-9) → T-0084b manager is the retry surface (failed `File` kept in memory, per-file "Zkusit znovu", count gate = existing + queued ≤ 10). Race watchpoints: button must stay disabled through navigation after a successful create (re-enabling invites a duplicate order); no navigation before the upload loop settles; mirror constants (`ORDER_ATTACHMENT_MAX_FILES = 10`, `ORDER_ATTACHMENT_MAX_BYTES = 10 MiB`, PDF/JPEG/PNG/WebP) must carry T-0064 source-of-truth comments.

- **HIGH-4: `apiFetch` hard 8-second timeout vs 10 MiB uploads.** Verified at `frontend/src/lib/runtime/api-fetch.ts:120-123`: `AbortSignal.timeout(8000)` is composed with any caller signal via `AbortSignal.any` — whichever fires FIRST aborts, so a caller **cannot extend** the budget. A 10 MiB attachment needs >10 Mbps sustained uplink to finish inside 8 s; on common Czech mobile uplinks the upload deterministically aborts as `network.timeout`, and the T-0084b retry will fail identically (same file, same timeout). This silently degrades T-0084a AC-8/AC-9 and T-0084b AC-7 from "flaky-network edge case" to "guaranteed failure for large files." Expected fix in this PR: add an opt-in per-call timeout override to `ApiFetchOptions` in `api-fetch.ts` (hand-written runtime lib — editable, unlike the generated client), used by the two upload helpers only, default behaviour unchanged for every existing call site. Shipping the bundle without addressing this = request changes.

- **HIGH-5: i18n error-code parity gap — the tickets' premise is wrong for two code families.** T-0084a claims "Error-code surface already has full i18n parity (… `auth.emailNotConfirmed`, `file.*` …). This ticket adds UI copy keys only — no error-code keys." Verified against `cs-CZ.ts`: parity EXISTS for `order.invalidQuantity`, `product.notActive`, `maker.deactivated`, `maker.notVerified`, `maker.personalPickupDisabled`, `order.attachmentLimitReached`, `order.stateForbidsAttachment`, all five `payment.*`, `order.invalidStateForPayment`, `order.paymentAlreadyCaptured`, and all nine `order.state.*` (lines 356-388). Parity does NOT exist as literal keys for **`auth.emailNotConfirmed`** (only the UI key `auth.login.email_not_confirmed` at line 49) and **`file.invalid` / `file.tooLarge` / `file.unsupportedType`** (only `dashboard.maker.products.images.error.*` UI variants; zero `'file.*'` keys in the catalog). Compounding this, `t()` is typed `t(key: MessageKey)` where `MessageKey = keyof typeof messages` (`cs-CZ.ts:452,462`) — a literal `t(error.code)` does not compile. The implementer must follow the established precedent: a typed mapping function per surface (`mapRegisterError` in `register-form.tsx:107`, `mapUploadErrorCode` in `image-manager.tsx:195`) returning `MessageKey` with a safe generic fallback — and must add checkout-appropriate copy keys for the email-not-confirmed alert (with resend hint, T-0084a AC-10 + `.emailNotConfirmedHint`) and the attachment-upload error rows. A `t(code as MessageKey)` cast = request changes (unsafe; renders `undefined` for unknown codes). No raw `error.message` leak for the named codes (AC-10).

- **HIGH-6: Packeta widget = third-party script in the revenue path.** Script URL + public key must come from the SSR-fetched `PickupPointWidgetConfig` (verified shape at `public-api.v1.ts:1871-1873`) — NOT hardcoded. Failure degrades, never breaks: script-load error OR runtime error → Zásilkovna radio disabled + error notice + retry affordance that re-attempts script load (AC-6); if the maker also lacks personal pickup, submit stays disabled with an explanatory alert. The `<script>` injection is the one sanctioned third-party exception (official widget UI; Comgate/Packeta REST stay backend-only). Packeta global typed via minimal `declare global` — no `any`. No secrets in the bundle: the widget `publicKey` is public by design (served by an anonymous endpoint); verify nothing else from config leaks. Third failure mode the ticket §C does not spell out: the **SSR `widgetConfig` fetch itself failing** — must degrade the same way (disabled option + notice), not crash the form render (no-mocks rule: loud, not broken).

### MEDIUM

- **MEDIUM-1: Email-confirmed gate surfaced, not pre-checked.** T-0063 middleware returns 403 `auth.emailNotConfirmed`; the form maps it to a form-level alert with resend hint (AC-10). No client-side pre-check (rejected Option G — no endpoint exposes confirmation state). Watch that the 403 doesn't fall into a generic "K této akci nemáte oprávnění" path.
- **MEDIUM-2: Poller lifecycle leaks.** Interval + `visibilitychange` listener cleared on unmount (T-0085 AC-8; navigate-away-mid-poll is in the test plan). Additional shape concern: a naive `setInterval` + async fetch can overlap in-flight polls (3 s interval < the 8 s fetch timeout) — expect a chained `setTimeout` or an in-flight guard. Budget (`activeElapsedMs`) accrues only while visible; constants `POLL_INTERVAL_MS = 3000` / `POLL_CAP_MS = 30_000` as named exports with the grooming-lock comment.
- **MEDIUM-3: Czech phone regex mirror drift — concrete, not hypothetical.** `lib/utils/validation.ts:22` already has `validatePhone` with the LAXER `^(\+420)?\s?\d{3}\s?\d{3}\s?\d{3}$` (allows leading 0–5; used by maker flows). The new `CZECH_PHONE_PATTERN` must be the exact T-0063 regex `^(\+420\s?)?[6-9]\d{2}\s?\d{3}\s?\d{3}$` as a SEPARATE constant with a source-of-truth comment. The order form must use the new constant; the existing `validatePhone` must NOT be modified (different backend validator, different consumers).
- **MEDIUM-4: Money display discipline.** Breakdown rows render backend-provided minors via `formatCzk` only. Watch for any client-side addition/subtraction of breakdown lines (e.g., recomputing total from parts), shipping-price computation in the T-0084a summary (locked: i18n note instead), or `vatRateBp / 100` as an unnamed magic number (B.12 wants a shared named constant).
- **MEDIUM-5: `priceType` casing drift in ticket §C.** T-0084a says guard on `priceType == 'on_request'`; the actual DTO literal is `'OnRequest'` (precedent: `produkt/[productId]/page.tsx:160`). Implementer follows the DTO, not the ticket prose. Also decide-and-show: `'From'`-priced products are NOT excluded by the ticket — only `OnRequest` redirects; verify against the T-0063 validator behaviour rather than inventing a frontend gate.
- **MEDIUM-6: `getCustomerOrderDetail` envelope unwrap.** Helper must genuinely map `ok(value.detail)` out of `GetCustomerOrderDetailsResponse { detail }` — not cast `Result<{detail}>` to `Result<CustomerOrderDetail>`. Page code never touches the envelope (T-0084b §Helpers).
- **MEDIUM-7: Missing UI primitives.** `components/ui/` currently has alert, badge, button, card, icon, input, select, spinner, textarea — **no Modal, no Tooltip, no Radio**. Packeta v6 renders its own overlay (so a Modal primitive may be unnecessary — verify), but the personal-pickup disabled "tooltip" (AC-7) and the shipping radio need primitives. New primitives belong in `components/ui/`, not inline in route components (checklist §E). A11y note: disabled inputs don't reliably fire hover — visible helper text beats a hover-only tooltip.
- **MEDIUM-8: `?attachmentsFailed=` and `?status=` are untrusted input.** Parse + clamp (`parsePositiveInt` precedent, B.8) before interpolating into i18n copy; presentational only — no behaviour beyond the alert/branch.
- **MEDIUM-9: Branch hygiene / regen leak.** The working tree currently sits on `feat/order-cleanup-bundle` with UNCOMMITTED `lib/api-client/customer-api.v1.ts` + `maker-api.v1.ts` + `.spec-hashes.json` modifications (T-0079 regen). The checkout bundle must branch from master AFTER order-cleanup merges (T-0084b ticket depends_on T-0082/T-0083). If the implementer branches off the in-flight order-cleanup branch, the regen diffs leak into this PR and violate the "zero `lib/api-client/` changes" invariant all three tickets pin. Flag for PM if the diff contains ANY `lib/api-client/` hunks.

## AC traceability matrix (30 ACs: 12 + 10 + 8)

### T-0084a — /objednavka order form

| AC | How I verify in the diff |
|---|---|
| AC-1 | `(customer)/objednavka/page.tsx` SSR-fetches product + maker profile + widget config + customer profile (cookie-forwarded); email prefill passed as prop into `order-form-client.tsx`; no fetch-on-mount effect in any client file. Preview network panel for the no-client-fetch proof. |
| AC-2 | `order-summary.tsx`: `lg:sticky` (or equivalent ≥1280 class) + mobile-first single-column stacking order in `page.tsx` markup. Preview at 375/768/1280. |
| AC-3 | `page.tsx` guards: `Unauthorized` → `redirect('/auth/login?next=' + encodeURIComponent(originalUrl))`; missing/blank `productId` → invalid-link state + catalog CTA (`checkout.invalidLink.*`); `NotFound` → `notFound()`. |
| AC-4 | Mirror validation in `order-form-client.tsx` using the new `validation.ts` constants (no network on mirror failure); `ApiError.fields` → inline errors with PascalCase→camelCase normalisation (B.17). |
| AC-5 | `zasilkovna-widget.tsx`: lazy single `<script>` injection from `scriptUrl` prop, `Packeta.Widget.pick(publicKey, cb, options)`, `onPick` → chosen-point display + "Změnit"; submit gate blocks when Zásilkovna selected without a point. |
| AC-6 | `onError` wiring → radio disabled + `checkout.widget.error` notice + retry re-attempting script load; submit blocked while no shipping method is selectable. |
| AC-7 | `personalPickupEnabled === false` → disabled radio + tooltip copy; enabled+selected → `pickupNote` + `city` render, payload `shippingMethod: PersonalPickup`, `zasilkovnaPickupPointId` undefined. |
| AC-8 | Submit handler: in-flight guard + disabled button; one `createOrder`; sequential `uploadOrderAttachment` loop with per-file status; `router.push` last. Double-click proof in manual plan. |
| AC-9 | Loop has no early-abort on failure; `?attachmentsFailed=<n>` appended when n>0; pre-checks (type/size/count) reject before any network call with Czech copy (`checkout.attachments.rejected*`). |
| AC-10 | Typed code→`MessageKey` map (per HIGH-5) covering the five named codes; alert renders i18n copy; no raw `error.message` for known codes. |
| AC-11 | Grep diff: zero `any`/`console.*`/fetch-effects; `'use client'` exactly on the 3 named files; `<section>` wrappers; zero `lib/api-client/` hunks; CI lint + tsc + build. |
| AC-12 | Manual preview QA per `docs/test-plans/T-0084a.md` (stub must exist in diff); primitives from `components/ui/`; no arbitrary Tailwind values. |

### T-0084b — /objednavka/[id] pre-payment page

| AC | How I verify in the diff |
|---|---|
| AC-1 | `[id]/page.tsx` SSR `getCustomerOrderDetail`; `order-breakdown.tsx` renders number, badge, product/shipping/VAT/total rows via `formatCzk`, contact snapshot, `createdAt + 24h` deadline via `dates.ts`. No client fetch on render. |
| AC-2 | Zero `createPaymentSession` references outside `pay-button-client.tsx` click handler (HIGH-1). Network panel proof in manual plan. |
| AC-3 | Click handler: in-flight guard → `createPaymentSession(orderId)` → `window.location.assign(redirectUrl)`; button disabled click-to-navigation/error. |
| AC-4 | No client-side session caching/freshness logic anywhere (rejected Option C); the retry is the identical call. |
| AC-5 | Error map: `payment.*` → alert + re-enable; `order.invalidStateForPayment` / `order.paymentAlreadyCaptured` → alert + `router.refresh()`. |
| AC-6 | `attachment-manager-client.tsx` seeded from `initialAttachments` prop; count gate existing+queued ≤ `ORDER_ATTACHMENT_MAX_FILES`; pre-checks reuse T-0084a mirrors. |
| AC-7 | Failed rows keep in-memory `File`; "Zkusit znovu" re-POSTs same file; success appends optimistically (no `router.refresh()` per rejected Option E). |
| AC-8 | `searchParams.attachmentsFailed` → one-time alert (`order.page.attachments.failedHandoffAlert`); parsed + clamped (MEDIUM-8). |
| AC-9 | Non-PendingPayment → banner via `orderStateLabelKey` (exhaustive switch w/ `never` check in `lib/orders/state-labels.ts`) + "detail připravujeme" + catalog link; no pay CTA, no manager. `NotFound` → `notFound()` (+ sibling `not-found.tsx`); `Unauthorized` → login redirect with `next`. |
| AC-10 | Grep diff: hygiene; `'use client'` exactly on the 2 named files; `<section>`; zero `lib/api-client/` hunks; CI green; 3 breakpoints in manual plan. |

### T-0085 — /objednavka/[id]/potvrzeni confirmation

| AC | How I verify in the diff |
|---|---|
| AC-1 | Decision-matrix row 4: failure statuses (case-insensitive `cancelled|cancel|failed|error`) → failure frame, `payment-poll-client` NOT rendered. Failure frame: 24h-hold note + retry CTA → `/objednavka/<id>` + catalog link. |
| AC-2 | Row 3: SSR `Paid` (or later success states) → success frame from `confirmation-views.tsx`, no poller in the tree. |
| AC-3 | Row 6: `PendingPayment` + non-failure status → verifying frame + poller; poll via `getCustomerOrderDetail` every ~3 s; `Paid` → in-place swap to success (shared `confirmation-views.tsx` keeps SSR/client frames identical). |
| AC-4 | Cap logic: `activeElapsedMs >= POLL_CAP_MS` → `capReached`, timer stopped permanently; cap frame with email note + detail link. |
| AC-5 | `visibilitychange` handler: hidden → pause + freeze budget; visible → one immediate poll + resume. Instrumented network panel in manual plan (test row 6). |
| AC-6 | The `?status=` parse result is consumed ONLY by the failure branch / frame choice; no code path sets `view = 'success'` from the param (HIGH-2). Manual plan row 5 (crafted `status=paid`). |
| AC-7 | Only `getCustomerOrderDetail` referenced; no new helpers; zero `lib/api-client/` hunks; poll returning `Cancelled` → failure view; `NotFound` → `notFound()`; `Unauthorized` → login redirect. |
| AC-8 | Grep diff: hygiene; `'use client'` only `payment-poll-client.tsx`; cleanup (interval + listener) on unmount; `<section>`; CI green; 4 frames × 3 breakpoints in manual plan. |

## Gate 5 — tests (frontend clause)

No frontend test harness exists today (zero `*.test.ts(x)` under `frontend/src/` — verified). Gate 5's frontend clause: manual test plan executed against preview; "automated tests only where pure logic exists (money formatting, validation mirrors)." New pure logic in this bundle that qualifies: the validation mirrors / file pre-check predicate, `orderStateLabelKey` (exhaustive switch), and the `?status=` failure classifier. Expectation: if the implementer adds automated tests for these, commit order MUST show test-before-implementation (T-0067+ TDD mandate — after-the-fact pure-logic tests are HARD FAIL); if no harness is stood up, the PR description must say so explicitly and the three test-plan stubs (`docs/test-plans/T-0084a.md`, `T-0084b.md`, `T-0085.md` — all must exist in the diff) must pin those behaviours manually (T-0085 plan rows 5/6 cover the classifier; T-0084a AC-9 covers the pre-checks). Silent omission of both = request changes.

## Mechanical-check expectations (Gate 9)

- **T4 `any`**: 0 new. (`[key: string]: any` in the generated client is baseline, untouched.) `declare global` Packeta typing must be concrete.
- **T7 `useEffect` fetch**: the T-0085 poller is the single sanctioned hit — verify its shape matches the ticket pseudocode exactly; if the consistency checker flags it, the justification comment must reference T-0085 A.1, not suppress silently. Zero other hits.
- **`console.*`**: 0.
- **i18n**: ~64 new UI keys per ticket enumerations (~30 `checkout.*` + ~20 `order.page.*` + ~14 `checkout.confirm.*`), plus the HIGH-5 additions (email-not-confirmed alert copy, attachment-error copy) — all vykání, plural-neutral `Label: N` shape for any `{count}` (B.18). L10n parity ping on the new keys.
- **CI**: `tsc --noEmit` + lint + `next build` green; pre-commit `check-api-client-manual-edits.mjs` passes (zero generated-client hunks).
- **Consistency baseline**: 118 (post order-cleanup) — unchanged. Frontend files add no backend T1s; HARD FAIL on any NEW violation.

## Open items the implementer should confirm before/while coding

1. **HIGH-4 timeout override** — extend `ApiFetchOptions` with an opt-in per-call timeout (upload helpers only), default 8 s untouched. Do not fork a second fetch path.
2. **HIGH-5 error-code mapping** — typed map functions per `mapRegisterError` / `mapUploadErrorCode` precedent + the two missing copy families; no `as MessageKey` casts.
3. **Branch base** — `feat/checkout-flow-bundle` cut from master AFTER order-cleanup merges; verify zero `lib/api-client/` diff before opening the PR (MEDIUM-9).
4. **`CZECH_PHONE_PATTERN` is a new constant**; existing `validatePhone` at `validation.ts:22` stays untouched (MEDIUM-3).
5. **DTO literals over ticket prose** — `priceType === 'OnRequest'` (MEDIUM-5); `OrderState`/`ShippingMethod` are string enums in the generated client (verified) — the `state-labels.ts` exhaustive switch should compile-check via `never`.
6. **Envelope unwrap** in `getCustomerOrderDetail` maps `ok(value.detail)` — no cast (MEDIUM-6).
7. **Poller**: chained `setTimeout` or in-flight guard against overlapping polls; constants as named exports with grooming-lock comments (MEDIUM-2).
8. **Packeta widget**: handle the SSR config-fetch failure as a first-class degraded state, same as script-load failure (HIGH-6).

## Preliminary verdict

**STRUCTURALLY_SOUND_PENDING_DIFF** — with **HIGH-4 (apiFetch 8 s timeout vs 10 MiB uploads)** and **HIGH-5 (i18n error-code parity gap + `t()` MessageKey typing)** as the two named pre-flight concerns the implementer must resolve inside this PR.

Rationale: all three tickets satisfy DoR; every consumed contract is verified present in the generated client on the working tree; the five user-locked dimensions are clear, internally consistent, and match the CLAUDE.md payments rule exactly; the helper/page/component decomposition follows established B.1–B.17 precedent (catalog/product/register/image-manager surfaces). Neither HIGH-4 nor HIGH-5 requires ticket revision — both are implementation-level: HIGH-4 is a one-option extension to a hand-written runtime module; HIGH-5 has two in-repo precedents to copy. The remaining HIGHs (payment-session-on-click, redirect-param trust, upload orchestration, Packeta degradation) are enforcement matters the final diff review will verify against the AC matrix above. Hold the line on: exactly six `'use client'` files, zero generated-client hunks, success-only-from-backend-state, and session-only-on-click.
