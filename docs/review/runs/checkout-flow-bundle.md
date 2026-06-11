# Checkout-flow bundle — Final review (T-0084a + T-0084b + T-0085)

> Branch `feat/checkout-flow-bundle`, commits 5590024..acd8bb4 (5 commits, 27 files, +2703/−0).
> Reviewed 2026-06-10 against the tickets, patterns.md §B, CLAUDE.md frontend self-check,
> `docs/process/quality-gates.md`, `docs/review/checklist.md`, and the draft at
> `docs/review/runs/checkout-flow-bundle-draft.md`.

## Verdict: **BLOCK** (request changes — single blocker)

One blocker: **draft HIGH-4 (apiFetch 8 s hard timeout vs 10 MiB uploads) was not addressed.**
Everything else in the bundle is in excellent shape — all five user-locked dimensions are
honoured exactly, HIGH-5 is resolved cleanly, hygiene is spotless, and all four verification
commands pass. Fix the blocker (plus optional folds below) and this approves.

## BLOCKER

### B-1 (draft HIGH-4, confirmed unresolved) — upload requests die at 8 s

- `frontend/src/lib/runtime/api-fetch.ts:120-123` is **untouched in this diff**:
  `AbortSignal.timeout(8000)` composed via `AbortSignal.any` — a caller signal can only
  shorten the budget, never extend it.
- `frontend/src/lib/api-client-helpers/orders-client.ts:137-148` `uploadOrderAttachment`
  passes no timeout override — every attachment POST has a hard 8 s ceiling.
- Consequence: a 10 MiB file needs >10 Mbps sustained uplink to finish in 8 s. On common
  Czech mobile uplinks the upload **deterministically** aborts as `network.timeout`, and the
  T-0084b retry fails identically (same file, same ceiling). This degrades T-0084a AC-8/AC-9
  and T-0084b AC-7 from "flaky-network edge case" to "guaranteed failure for large files" —
  on the platform's first revenue path.
- Expected fix (from the draft, unchanged): add an opt-in per-call timeout to
  `ApiFetchOptions` (hand-written runtime module — editable), consumed **only** by
  `uploadOrderAttachment`; default 8 s behaviour unchanged for every existing call site.
  Do not fork a second fetch path.

## Draft findings — confirmed / refuted

| Draft | Status | Evidence |
|---|---|---|
| HIGH-1 session-on-click (Q3) | **PASS** | `createPaymentSession` has exactly one call site: `pay-button-client.tsx:38` inside the click handler (ref in-flight guard :33-34); success → `window.location.assign(redirectUrl)` :41; no SSR/mount/prefetch session anywhere. |
| HIGH-2 redirect-param trust (Q4) | **PASS** | `potvrzeni/page.tsx:59-146` implements the 6-row matrix top-down exactly as ticketed. `?status=` (trimmed, lowercased :62) only selects the failure frame (:111-117); success requires `isPaidOrLater(detail.state)` (:100, SSR) or a polled `Paid` (`payment-poll-client.tsx:65`). Forged `?status=paid` on `PendingPayment` → row 6 verifying → cap. AC-6 satisfied by construction. |
| HIGH-3 upload orchestration (Q2) | **PASS** | `order-form-client.tsx:142-214`: ref guard before any work (:148), single `createOrder`, sequential never-abort loop (:196-208), `?attachmentsFailed=N` (:212), navigation only after loop settles (:213), button stays disabled through navigation. Manager (`attachment-manager-client.tsx`): count gate = existing + non-failed local (:56-58), per-file retry re-POSTs the in-memory `File` (:114-117). *Code shape correct; runtime contaminated by B-1.* |
| HIGH-4 upload timeout | **CONFIRMED — BLOCKER B-1** | See above. |
| HIGH-5 i18n error-code mapping | **RESOLVED** | `lib/runtime/errors.ts`: `isMessageKey` is a proper type guard (:17-19, zero `as MessageKey` casts); `resolveErrorMessage` falls back to typed per-`ErrorType` keys (:21-31, all nine verified present in `cs-CZ.ts:27-35`); raw `error.message` never rendered. 4 parity keys added (`auth.emailNotConfirmed`, `file.invalid`, `file.tooLarge`, `file.unsupportedType`). All error paths route through it: form (`buildFormError`, order-form-client.tsx:440-447, with `checkout.emailNotConfirmedHint`), pay button (:45), manager (:71). |
| HIGH-6 Packeta widget | **PASS** | `zasilkovna-widget.tsx`: scriptUrl/publicKey/options from SSR config props (no hardcode); load failure clears cached promise + `onError` (:60-64); typed `declare global` (:37-41, no `any`); only the public widget key reaches the bundle. SSR config-fetch failure is a first-class degraded state: `widgetConfig=null` → option disabled + retry via `router.refresh()` (order-form-client.tsx:106-118). Pickup point id flows into `createOrder` payload (:168-169). |
| MEDIUM-1 email gate | PASS | Literal `auth.emailNotConfirmed` key now exists → no generic-403 fallthrough; resend hint appended. |
| MEDIUM-2 poller lifecycle | PASS | In-flight guard (`pollInFlightRef`, poll-client:47,56-59), interval + listener cleanup (:111-115), budget freezes while hidden (interval stopped :96-98), immediate poll + resume on return (:99-103), constants as named exports with grooming-lock comments (:31-34). |
| MEDIUM-3 phone regex drift | PASS | New `CZECH_PHONE_PATTERN` (validation.ts:31) with T-0063 source comment; existing `validatePhone` (:22) untouched. |
| MEDIUM-4 money discipline | PASS w/ LOW nit (N-1) | All rows `formatCzk` from DTO minors; no client totals; `AUTO_CANCEL_WINDOW_MS` named + T-0083 comment (order-breakdown.tsx:23). |
| MEDIUM-5 priceType casing | PASS | DTO literal `'OnRequest'` (objednavka/page.tsx:74); `'From'` products order-able, rendered via `catalog.product.price.from` (order-summary.tsx:79-81). |
| MEDIUM-6 envelope unwrap | PASS | Genuine `ok(result.value.detail)` with a typed envelope interface (orders-client.ts:102-104, 158-167) — no cast. |
| MEDIUM-7 missing primitives | PARTIAL — fold N-5 | Disabled-pickup tooltip = `title` attr + always-visible helper text (a11y-correct). Shipping radios are bespoke styled labels in the route component, not a `components/ui/` primitive — acceptable now, extract when T-0086/87 needs radio rows again (harvest candidate). |
| MEDIUM-8 untrusted params | PASS w/ LOW nit (N-2) | `status` trimmed/lowercased/set-matched; `attachmentsFailed` `parseInt` + `isFinite` + `>0` gate ([id]/page.tsx:76-84); no upper clamp. |
| MEDIUM-9 regen leak | PASS | Zero `lib/api-client/` hunks in the diff (verified `git diff --stat`); branch cut post order-cleanup merge. |

## New findings

| # | Sev | Finding | Recommendation |
|---|---|---|---|
| N-1 | LOW | `detail.vatRateBp / 100` magic divisor at `order-breakdown.tsx:26-28` (doc comment present, but B.12 expects a named constant). | **Fold** into the B-1 fix commit: `const BASIS_POINTS_PER_PERCENT = 100`. |
| N-2 | LOW | `?attachmentsFailed=` has no upper clamp ([id]/page.tsx:76) — `?attachmentsFailed=999999` renders verbatim in the alert. Presentational only. | **Fold**: clamp to `ORDER_ATTACHMENT_MAX_FILES`. |
| N-3 | LOW | Test-plan stub drift: `docs/test-plans/T-0084a.md` TC-2 expects `/auth/login?redirect=…` but the code (correctly) redirects to `/login?redirect=…`. | **Fold**: fix the stub route. |
| N-4 | INFO | Rapid tab-visibility toggling fires budget-free immediate polls (poll-client:99-101). Matches the ticket lock verbatim ("one immediate poll … does not consume budget"); user-driven, bounded in practice. | No action. Noted for T-0086 reuse. |
| N-5 | INFO | Bespoke radio-row markup duplicated twice in `order-form-client.tsx` (:286-343, :356-388). | Harvest candidate for a `components/ui/` choice-card primitive when the next radio surface ships. Not this PR. |
| N-6 | INFO | **Pre-existing, out of scope:** `/auth/login` 404s — `middleware.ts:24` + ~10 links (`register-form.tsx:52,99`, `verify-client.tsx:63`, `reset-client.tsx:56,116`, `profile-client.tsx:53`, `pro-makery/page.tsx:159`, `register-maker-form.tsx:62`). The bundle's new code correctly targets `/login` (route table from `next build` confirms). **Not logged in `docs/questions/open.md`** — recommend PM opens a Q-item or a quick-fix ticket (route alias or link sweep). Do not block this PR on it. |

## Implementer deviations — judged

1. `/login?redirect=` instead of ticket's `/auth/login?next=` — **justified**: `(auth)` route group serves login at `/login` (build route table confirms) and `login-form.tsx:24` consumes `?redirect=`. Commit acd8bb4 fixed the bundle's own redirects. Ticket prose was wrong.
2. `ordersGET2` real path `/api/v1/orders/{orderId}` — **justified**: verified in `customer-api.v1.ts:543`; ticket prose (`/api/v1/customer/orders/{id}`) was wrong. DTO-over-prose is the standing rule.
3. 4 error-code parity keys — **justified and required**: the tickets' "full parity exists" premise was wrong for `auth.emailNotConfirmed` + `file.*` (draft HIGH-5).
4. `label` prop on `ZasilkovnaWidgetProps` — **justified**: chosen-point state lives in the form; the label ("Vybrat"/"Změnit") follows it. Cleaner than duplicating state in the widget.
5. Extra i18n keys beyond enumeration (metadata, loadError, validation, shippingMethod, notFound) — **justified**: ticket key lists were estimates ("~30 keys"); every enumerated group is present.
6. Editable email — **not a deviation**: T-0084a §C says "prefilled … editable" verbatim.
7. No frontend test harness — **acceptable under Gate 5's frontend clause**, with one condition: the PR description must state it explicitly and point at the three manual plans (all present in the diff; T-0085 TC-5/TC-6 pin the classifier, T-0084a TC-5/TC-11/TC-12 pin mirrors + orchestration). Silent omission would have been a fail; this is logged.

## AC traceability — 30/30 traced

**T-0084a (12):** AC-1 page.tsx SSR fetches + email prefill ✓; AC-2 `lg:sticky lg:top-8` (order-summary.tsx:24) + mobile stacking (page.tsx:100-105) ✓ (breakpoints = manual QA); AC-3 guards :46-76 ✓; AC-4 mirrors :120-140 + `normalizeFieldErrors` B.17 ✓; AC-5 widget pick/Změnit/submit gate ✓; AC-6 disable + retry ✓; AC-7 pickup gate + payload ✓; **AC-8 ✓ code / ✗ runtime (B-1)**; **AC-9 ✓ code / ✗ runtime for large files (B-1)**; AC-10 resolveErrorMessage + parity keys ✓; AC-11 hygiene grep clean, 3 sanctioned `'use client'`, `<section>`, zero generated-client hunks, tsc/lint/build green ✓; AC-12 manual plan in diff ✓.

**T-0084b (10):** AC-1 breakdown/badge/VAT %/total/contact/deadline, no client fetch ✓; AC-2 zero non-click session call sites (grep) ✓; AC-3 guard + assign ✓; AC-4 no client caching ✓; AC-5 error map + `router.refresh()` on conflict codes (pay-button:21-24,46-48) ✓; AC-6 seeded list + cap gate + pre-checks ✓; **AC-7 ✓ code / ✗ runtime for large files (B-1)**; AC-8 one-time alert, parsed ✓; AC-9 banner via `orderStateLabelKey` (exhaustive `never` switch), `not-found.tsx`, login redirect ✓; AC-10 hygiene + 2 sanctioned leaves ✓.

**T-0085 (8):** AC-1 failure short-circuit, no poller ✓; AC-2 SSR Paid → success, no poller ✓; AC-3 verifying + 3 s poll + in-place swap ✓; AC-4 cap stops permanently ✓; AC-5 visibility pause/freeze/immediate-poll ✓; AC-6 success only from backend state (by construction) ✓; AC-7 single endpoint, `Cancelled` → failure, 404/login ✓; AC-8 hygiene, 1 `'use client'`, cleanup, `<section>` ✓.

**Verdict: 27 PASS, 3 PASS-in-code/FAIL-at-runtime (T-0084a AC-8, AC-9; T-0084b AC-7 — all via B-1).**

## Gates

| Gate | Verdict | Notes |
|---|---|---|
| 1 — CLAUDE.md self-check | **PASS** | Diff grep: zero `any`/`console.*`/TODO/dead code/inline layout style/arbitrary Tailwind. `'use client'` exactly the 6 sanctioned leaves; single sanctioned `useEffect` (poller, justification comment references T-0085 §B). |
| 2 — AC | **FAIL (B-1)** | 27/30 clean; 3 ACs runtime-degraded by the upload timeout. |
| 3 — Security | **PASS** (not security-touching) | Payments rule verified: session-on-click, success-only-from-backend-state, no secrets in bundle, IDOR → 404. No SecOps ping needed. |
| 4 — Architecture | **PASS** | B.16 helpers only; no extension-point violations; no business logic client-side; generated client untouched. No Architect ping needed. RDD parity N/A (frontend-only, no domain roles). |
| 5 — Tests | **CONDITIONAL PASS** | No harness exists (pre-existing); no after-the-fact pure-logic tests present (TDD HARD-FAIL not triggered). Three manual plans in diff cover the pure-logic behaviours. **Condition:** PR description states the no-harness decision explicitly. |
| 6 — Contract parity | **PASS** | Zero contract change; zero `lib/api-client/` hunks; pre-commit hook unaffected. |
| 7 — Docs | **PASS** | Test plans added; no env/arch changes. Recommendation (non-blocking): log N-6 in `docs/questions/open.md`. |
| 9 — Mechanical | **PASS** | `check-consistency: clean (118 tracked)` — baseline unchanged. |

## Verification re-run (2026-06-10)

- `npx tsc --noEmit` — clean.
- `npm run lint` — clean.
- `npm run build` — clean (route table includes `/objednavka`, `/objednavka/[id]`, `/objednavka/[id]/potvrzeni`, all dynamic; `/login` confirmed as the real login route).
- `node scripts/check-consistency.mjs` — exit 0, 118 tracked, no new violations.

## Required for approval

1. **B-1**: per-call timeout override in `ApiFetchOptions`, consumed by `uploadOrderAttachment` only; default unchanged. (Re-review will check both upload call sites + no behaviour change elsewhere.)
2. Gate-5 condition: PR description states the no-test-harness decision.

Recommended folds into the same fix commit: N-1, N-2, N-3. Recommended for PM: N-6 Q-item.
