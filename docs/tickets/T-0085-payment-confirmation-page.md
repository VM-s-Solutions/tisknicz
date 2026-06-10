---
id: T-0085
title: Payment confirmation page /objednavka/[id]/potvrzeni — optimistic render + poll-to-Paid
status: ready
size: S
owner: frontend
created: 2026-06-09
updated: 2026-06-09
depends_on: [T-0084b, T-0066, T-0067]
blocks: [T-0086a]
user_stories: [US-customer-0010]
adrs: [0005, 0016, 0022, 0024]
phase: 4
manual_steps: [vercel-preview-manual-qa, comgate-return-urls-config]
security_touching: false
layers: [frontend]
---

# T-0085 — Payment confirmation page at /objednavka/[id]/potvrzeni

## Context

T-0085 is the **third and final ticket in the checkout-flow bundle** (`feat/checkout-flow-bundle`: T-0084a order form → T-0084b pre-payment page → **T-0085 confirmation**). It is the Comgate returnUrl target: after the customer finishes (or abandons) the gateway flow, Comgate sends the browser back to `/objednavka/<id>/potvrzeni?status=...`. Shipping this page closes the revenue loop end-to-end and unblocks realistic QA data for bundle 2 (`feat/order-dashboards-bundle` — dashboards need genuinely Paid orders to render).

The hard constraint is CLAUDE.md security rule: **"All payments verified server-side. Never trust the client-side redirect params from Comgate alone."** The redirect params are a UX hint; the truth is the T-0066 webhook → T-0067 `MarkOrderPaid` transition, observable to the frontend only as `detail.state === OrderState.Paid` on the existing `ordersGET2` endpoint. The page therefore renders optimistically and **polls the detail endpoint** until the webhook lands or a time cap is hit. No new backend contract is introduced — `ordersGET2` is the only call (frontend-only ticket, no NSwag regen).

Webhook latency is normally sub-second to a few seconds; the ~30s cap covers the slow tail, after which the customer is told verification continues and the T-0067 order-paid email confirms (US-customer-0010's notification path). The cancelled/failed redirect maps to T-0065's 24h retry window: the order is still `PendingPayment`, so the retry CTA points back to T-0084b's pay button.

## Locked design decisions

### A. User-locked (2026-06-09 grooming, non-negotiable)

1. **Optimistic render + poll until `Paid`, ~30s cap (Q4).** On arrival with a non-failure `?status=`, render an optimistic "Děkujeme — ověřujeme platbu" view immediately, then poll `ordersGET2` every ~3s. On `State == Paid` → success view (order number, what-happens-next, CTA to the order detail). On cap (~30s of active polling) → "platbu ověřujeme, potvrzení pošleme e-mailem" + link to the order detail. The redirect param NEVER produces the success view by itself — only the backend-read `Paid` state does. **Rejected:** trust-the-redirect (Option A), block-until-webhook with no optimistic frame (Option B), push transport (Option C).

### B. ADR + pattern-locked (no relitigation)

- **CLAUDE.md payments rule** — webhook is truth; redirect params alone never flip the UI to success.
- **patterns.md B.1** — the page itself is a Server Component; the SSR pass does the initial detail fetch (cookie-forwarded per B.14 / ADR 0024). The poller is a `'use client'` leaf. The no-`useEffect`-fetch rule targets initial data loading — initial data here IS server-fetched; the client effect is a verification *timer* re-invoking the existing helper, which is the locked design (see Alternatives, Option E).
- **patterns.md B.4 + B.16** — polling reuses `getCustomerOrderDetail` from `orders-client.ts` (T-0084b); no direct generated-client imports.
- **patterns.md B.5 + B.7 + B.10** — i18n keys (vykání), `<section>` wrapper, `formatCzk` where money shows.
- **ADR 0022** — generated client untouched; no contract change.

### C. PM-absorbed (no user input needed)

- **`?status=` interpretation (defensive):** values indicating cancellation/failure (`cancelled`, `cancel`, `failed`, `error` — matched case-insensitively) → immediate failure view, **no poll**. Any other value (including absent/unknown) → optimistic verifying path. Unknown-as-optimistic is safe because success is only ever granted by the backend state.
- **Failure view:** "Platba nebyla dokončena" + explanation that the order is held for 24h (T-0083) + primary CTA back to `/objednavka/<id>` (T-0084b pay button = the retry surface per T-0065's 24h window) + secondary catalog link.
- **SSR short-circuits:** if the SSR detail read already shows `Paid` (webhook won the race) → success view immediately, zero client polling. If it shows a terminal/unexpected state (`Cancelled`, `Refunded`, ...) → the T-0084b-style state banner pattern (label via `state-labels.ts`). `NotFound` → `notFound()`; `Unauthorized` → login redirect with `next`.
- **Poll mechanics:** interval ~3s, cap ~30s of *active* polling (≈10 attempts). 3s balances perceived responsiveness (webhook is usually sub-second; the first poll catches most payments) against backend load (≤10 indexed, customer-scoped reads per checkout, worst case). **Visibility-pause:** when `document.hidden`, the timer pauses and the elapsed budget freezes; on return to visible, one immediate poll fires and the interval resumes — a customer who tabs away to check their banking app gets a fresh verdict the moment they come back, without having silently burned the budget. Poll responses that show `Cancelled` flip to the failure view; transient poll errors are swallowed silently (next tick retries) — the cap bounds total effort either way.
- **Success view content:** checkmark, order number, "co bude dál" steps (maker přijme → vyrobí a odešle → potvrdíte doručení — mirrors the order state machine, display-only), primary CTA to `/objednavka/<id>`, secondary catalog link. Reaching success swaps the client view in place (no full reload needed).
- **Return-URL configuration is a manual step:** the Comgate merchant return URLs must point at `https://<domain>/objednavka/{refId}/potvrzeni?status=<paid|cancelled>` per environment. Owner: user; blocker for production + staging QA, not for the PR. The exact param values Comgate emits are validated on staging during QA; the defensive parsing above makes the page correct under any value.

## Scope

### Page + components

- **`frontend/src/app/(customer)/objednavka/[id]/potvrzeni/page.tsx`** — Server Component. Decision matrix (rows evaluated top-down):

  | # | SSR `detail.state` | `?status=` (case-insensitive) | Render |
  |---|---|---|---|
  | 1 | fetch `NotFound` | — | `notFound()` |
  | 2 | fetch `Unauthorized` | — | `redirect('/auth/login?next=…')` |
  | 3 | `Paid` (or later: `Accepted`/`Shipped`/`Delivered`/`Completed`) | any | success view, **no poller** |
  | 4 | any | `cancelled` \| `cancel` \| `failed` \| `error` | failure view, **no poller** |
  | 5 | `Cancelled` \| `Refunded` \| `Disputed` | other | state banner (label via `state-labels.ts`) |
  | 6 | `PendingPayment` | absent / unknown / non-failure | verifying frame + `<payment-poll-client>` |

  Row 3 deliberately treats post-`Paid` states as success — a customer revisiting the bookmark days later sees the thank-you, not a poller.
- **`frontend/src/app/(customer)/objednavka/[id]/potvrzeni/payment-poll-client.tsx`** — `'use client'`. Props: `orderId`, `orderNumber`. Behaviour:

  ```
  state: view = 'verifying' | 'success' | 'failure' | 'capReached'
         activeElapsedMs (only accrues while document is visible)

  every POLL_INTERVAL_MS (3000) while view == 'verifying' && !document.hidden:
    result = getCustomerOrderDetail(orderId)
    result.success && state === Paid       → view = 'success'   (stop timer)
    result.success && state === Cancelled  → view = 'failure'   (stop timer)
    result failure (transient)             → ignore; next tick retries
    activeElapsedMs >= POLL_CAP_MS (30000) → view = 'capReached' (stop timer)

  on visibilitychange:
    hidden  → pause interval, freeze activeElapsedMs
    visible → fire one immediate poll, resume interval

  cleanup on unmount: clear interval + visibilitychange listener
  ```

  The timer effect is the one sanctioned effect in the bundle: initial data was server-fetched; the effect is a verification timer re-invoking the existing B.16 helper, per locked decision A.1 (see §B note + Alternatives Option E).
- **`frontend/src/app/(customer)/objednavka/[id]/potvrzeni/confirmation-views.tsx`** — presentational frames (verifying / success / failure / cap-reached), each a pure function of props (order number, CTA hrefs). Shared by the page's SSR short-circuits and the poller's client transitions so both paths render pixel-identical views. Success frame: checkmark icon (`components/ui/icon`), order number, three "co bude dál" steps, primary CTA → `/objednavka/<id>`, secondary → `/katalog`. Failure frame: explanation + 24h-hold note + primary CTA → `/objednavka/<id>` (the T-0084b pay button), secondary → `/katalog`.
- **`frontend/src/app/(customer)/objednavka/[id]/potvrzeni/loading.tsx`** — skeleton.

### Helpers / i18n

- No new helpers — reuses `orders-client.ts:getCustomerOrderDetail` and `orders/state-labels.ts` (both shipped earlier in this bundle by T-0084a/b). Poll constants (`POLL_INTERVAL_MS = 3000`, `POLL_CAP_MS = 30_000`) live as named exports next to the poller, commented with the grooming lock.
- **`frontend/src/lib/i18n/cs-CZ.ts`** — add `checkout.confirm.*` UI keys (vykání; ~14 keys, wording drafted by implementer, PM/UX reviews on PR):

  | Group | Keys |
  |---|---|
  | Verifying | `checkout.confirm.verifying.title` ("Děkujeme! Ověřujeme platbu…"), `.verifying.subtitle` |
  | Success | `checkout.confirm.success.title`, `.success.orderNumber` (placeholder), `.success.step1` (maker přijme), `.success.step2` (vyrobí a odešle), `.success.step3` (potvrdíte doručení), `.success.detailCta`, `.success.catalogCta` |
  | Cap | `checkout.confirm.pendingTitle`, `.pendingEmailNote`, `.pendingDetailLink` |
  | Failure | `checkout.confirm.failed.title`, `.failed.heldNote` (24h), `.failed.retryCta` |

  No new error-code keys.

## Alternatives Considered

- **Option A — Trust `?status=paid` and render success immediately.** *Rejected per A.1 + CLAUDE.md* — redirect params are attacker-editable and race the webhook; showing "zaplaceno" for an order the backend may never mark Paid is the exact failure mode the security rule exists to prevent.
- **Option B — No optimistic frame; spinner until the webhook confirms.** *Rejected per A.1* — the customer just paid and stares at an anonymous spinner; the optimistic "děkujeme, ověřujeme" frame communicates progress honestly without claiming success.
- **Option C — SSE / WebSocket push for the Paid transition.** *Rejected* — new backend infrastructure (endpoint, connection management, Azure scaling concerns) for a single page whose tail case is already covered by email; ~10 polls of an existing indexed read is the cheapest correct thing. Revisit post-MVP if checkout volume warrants.
- **Option D — Poll forever until Paid.** *Rejected per A.1* — unbounded timers burn battery and backend cycles for webhooks that may be hours late; the cap + "potvrdíme e-mailem" fallback (T-0067 email) is the graceful tail.
- **Option E — `router.refresh()` loop instead of a client fetch poll.** *Rejected* — re-runs the entire server tree every 3s (full RSC render + detail fetch) versus one lightweight helper call checking a single field; also makes visibility-pause and the in-place success transition awkward. The client poll via the existing B.16 helper keeps the chokepoint intact.
- **Option F — A dedicated lightweight `GET /orders/{id}/payment-status` endpoint.** *Rejected* — grooming locked "no new backend calls beyond the existing detail endpoint"; `ordersGET2` is indexed, scoped, and already typed in the client. A slimmer payload saves negligible bytes for ≤10 calls and costs a backend ticket + NSwag regen + a second order-read surface to keep consistent.
- **Option G — Auto-redirect to `/objednavka/<id>` on success instead of an in-place success view.** *Rejected* — the customer just completed payment; yanking them to a different page mid-read is disorienting, and the success frame's "co bude dál" steps are the one moment to set delivery expectations. The detail CTA gives the same navigation under the customer's control.

## Out of scope

- **Backend changes of any kind** — no new endpoints, no webhook changes, no NSwag regen.
- **Full order tracking view** — the success CTA links to `/objednavka/<id>` (T-0084b view now; T-0086b tracking view later).
- **Review prompt / cross-sell on the success view** — post-MVP.
- **Push transport (SSE/WebSocket)** — rejected Option C; revisit post-MVP.
- **Retry-payment button ON this page** — retry lives on T-0084b (single payment surface); this page only links there.
- **Analytics / conversion events** — separate instrumentation ticket when the analytics stack lands.

## Acceptance criteria

- **AC-1** Given the customer lands on `/objednavka/<id>/potvrzeni?status=cancelled` (or `failed`), when the page renders, then the failure view shows immediately — "platba nebyla dokončena" + 24h-hold note + retry CTA linking to `/objednavka/<id>` — and **no polling starts** (network panel: zero detail re-fetches after the SSR pass).
- **AC-2** Given the webhook already processed (SSR detail reads `State == Paid`), when the page renders, then the success view (order number, what-happens-next, detail CTA) shows immediately with zero client polling.
- **AC-3** Given the SSR detail reads `PendingPayment` and `?status=` is absent or non-failure, when the page renders, then the optimistic verifying view shows and the poller calls the detail endpoint every ~3s; when a poll returns `Paid`, the view swaps to success in place — no full page reload.
- **AC-4** Given polls return `PendingPayment` for the full ~30s active budget, when the cap is reached, then polling stops permanently and the cap-reached view renders ("platbu ověřujeme, potvrzení pošleme e-mailem") with a link to `/objednavka/<id>`.
- **AC-5** Given the tab is hidden mid-poll, when `visibilitychange` fires, then the interval pauses and the 30s budget freezes; on return to visible, one immediate poll fires and the interval resumes (verifiable via instrumented network panel).
- **AC-6** Given any crafted `?status=` value (e.g. `status=paid`, `status=ok`, garbage), when the backend state is not `Paid`, then the success view is never shown — only verifying / cap-reached / failure frames. Success is granted exclusively by `detail.state === OrderState.Paid` read from the backend.
- **AC-7** Given the whole flow, when the network is inspected, then the only API call this page makes is `GET /api/v1/customer/orders/{id}` via the existing `orders-client.ts` helper — no new contracts, `lib/api-client/` untouched (pre-commit hook passes). Foreign/unknown order → `notFound()`; no session → login redirect; a poll returning `Cancelled` flips to the failure view.
- **AC-8** Hygiene + responsive: zero `any`/`console.*`; `'use client'` only on `payment-poll-client.tsx`; timer cleaned up on unmount; `<section>` wrapper; all copy from `cs-CZ.ts` (vykání); lint + `tsc --noEmit` + `next build` clean; views verified at 375/768/1280 on the Vercel preview.

## Risk / mitigation

- **Webhook slower than 30s (Comgate slow tail / Functions cold start)** → cap-reached view + T-0067 email is the designed fallback; the order detail link shows the eventual truth. No customer-visible inconsistency: we never claimed success.
- **Comgate return-param contract differs from assumptions** → defensive parsing (§C: unknown = optimistic, success only from backend state) makes every param value safe; staging QA pins the actual values; `comgate-return-urls-config` manual step documents the merchant-portal setup.
- **Customer bookmarks/revisits the confirmation URL later** → SSR short-circuits handle it: `Paid` → success, `Cancelled` → failure-style banner, still `PendingPayment` → verifying + poll (harmless, capped).
- **Poll hammering under retries** → fixed 3s spacing, ~10-call cap, visibility-pause; the detail read is indexed + customer-scoped (T-0082).
- **Double-render drift between SSR and client views** → shared `confirmation-views.tsx` presentational components keep the frames pixel-identical.
- **Auto-cancel races a very late payment (customer pays at hour 23:59, T-0083 cancels before the webhook lands)** → backend concern (T-0066/T-0083 ordering owns it); this page renders whatever state the backend settles on — a poll returning `Cancelled` shows the failure view, which is honest. No frontend mitigation needed beyond not lying.
- **Timer leaks on fast navigation away** → AC-8 pins cleanup on unmount; the Playwright-style plan includes a navigate-away-mid-poll step.

## Test plan reference

Manual Playwright-style plan at **`docs/test-plans/T-0085.md`** (stub — filled before bundle PR review). QA surface: Vercel preview against staging backend + Comgate sandbox end-to-end. Pre-condition: `comgate-return-urls-config` manual step applied on staging. The stub must cover at minimum:

| # | Scenario | Expected |
|---|---|---|
| 1 | Pay with sandbox test card, webhook fast | verifying frame → success swap within ~1–2 polls, no reload |
| 2 | Pay, webhook delayed >30s (throttle / pause Function) | cap-reached view at ~30s; polling stops; order-paid email still arrives |
| 3 | Cancel at the gateway | failure view immediately on return; zero polls; retry CTA lands on T-0084b pay button |
| 4 | Revisit confirmation URL after order already `Paid` | success view from SSR, no poller |
| 5 | Craft `?status=paid` on an unpaid order | verifying frame only — success never renders from the param |
| 6 | Hide the tab mid-poll, return after 1 min | polls paused while hidden; immediate poll on return; budget not consumed while hidden |
| 7 | Foreign order id / logged out | 404 page / login redirect with `next` |
| 8 | Viewports 375 / 768 / 1280 on all four frames | layout holds; CTAs reachable |

## Files touched (expected)

### New
- `frontend/src/app/(customer)/objednavka/[id]/potvrzeni/page.tsx`
- `frontend/src/app/(customer)/objednavka/[id]/potvrzeni/payment-poll-client.tsx`
- `frontend/src/app/(customer)/objednavka/[id]/potvrzeni/confirmation-views.tsx`
- `frontend/src/app/(customer)/objednavka/[id]/potvrzeni/loading.tsx`
- `docs/test-plans/T-0085.md` (stub)

### Modified
- `frontend/src/lib/i18n/cs-CZ.ts` — `checkout.confirm.*` UI keys

## Commits hint

1. `feat(T-0085): confirmation views + i18n keys`
2. `feat(T-0085): /objednavka/[id]/potvrzeni page + capped visibility-aware payment poller`

## Status log

- 2026-06-09 `draft` by PM. Created as the third ticket in the checkout-flow bundle (after T-0084a + T-0084b; one PR, sequential). Consumes the merged T-0082 detail endpoint only; depends on T-0066/T-0067 webhook → MarkOrderPaid being live for the poll to ever observe `Paid`. Frontend-only — no contract change, no regen. Shipping this closes the revenue path and unblocks realistic QA data for bundle 2 (`feat/order-dashboards-bundle`).
- 2026-06-09 `draft → ready` by PM. User locked 1 dimension at grooming: **A.1** optimistic render + poll-to-Paid with ~3s interval and ~30s cap; success only from backend state, never from redirect params (rejected trust-the-redirect, spinner-only, push transport). 7 PM-absorbed decisions captured in §C (defensive `?status=` parsing, failure view + retry CTA to T-0084b, SSR short-circuits, poll mechanics + visibility-pause, success-view content, in-place view swap, Comgate return-URL manual step). **Ready for frontend** after T-0084b lands in the bundle branch.

## Definition of Ready

- [x] User story linked (US-customer-0010 AC-2/AC-3 closure) and AC traceable
- [x] All dependencies merged (T-0066, T-0067, T-0082 backend) or sequenced in-bundle (T-0084b)
- [x] User-locked decision captured with rebutted alternatives (deliberation policy)
- [x] No blocking open questions; the Comgate return-param uncertainty is neutralised by defensive parsing + staging QA (§C, Risk)
- [x] i18n keys enumerated; no new error codes
- [x] Test plan stub path agreed (`docs/test-plans/T-0085.md`); QA surface = Vercel preview + Comgate sandbox; manual step `comgate-return-urls-config` recorded
- [x] Owner assigned (`frontend`); size S; bundle position fixed (3 of 3)
