# Checkout-flow bundle — Gate 8 (Performance)

> Perf-dimension pass on `feat/checkout-flow-bundle` (T-0084a + T-0084b + T-0085), frontend-only, first revenue-path UI. Measured against the working tree on 2026-06-10 (`npm run build`, Next.js 16.2.6/Turbopack). Budgets: [ADR 0023 §1](../../adr/0023-non-functional-requirements.md) + CLAUDE.md §Performance. ADR 0023 defines **no First Load JS budget and no checkout TTFB budget** — the ~150 kB line below is the Gate 8 convention, not an ADR number.

## Verdict: **GATE8_FOLD**

No PR-introduced blocker. The >150 kB First Load JS flag fires on all three routes, but the breach is inherited from the pre-existing shared baseline (131.8 kB gzip) — `/katalog` already sits at 157.6 kB on the same shared chunks. Two folds + one standalone perf ticket + one open question.

## 1. Measurements — route-level First Load JS (gzip, computed from `.next` manifests; Turbopack no longer prints sizes)

| Route | First Load JS (gzip) | raw | Delta vs /katalog | Route-specific chunk |
|---|---|---|---|---|
| `/objednavka` | **169.6 kB** | 582.4 kB | +12.0 kB | `15-mieb8_sc_q.js` 22.9 kB gz (form + picker + widget wrapper + i18n copy) |
| `/objednavka/[id]` | **162.4 kB** | 560.6 kB | +4.8 kB | `0jx.147f6m6k7.js` 15.7 kB gz (pay button + attachment manager + i18n copy) |
| `/objednavka/[id]/potvrzeni` | **161.0 kB** | 556.9 kB | +3.4 kB | `0.cvcjm6v1y9k.js` 14.3 kB gz (poller + views + i18n copy) |
| `/katalog` (existing reference) | 157.6 kB | 546.4 kB | — | — |
| `/produkt/[productId]` (existing) | 163.4 kB | 560.9 kB | +5.8 kB | — |
| Shared root baseline (all routes) | 131.8 kB | 451.8 kB | — | 6 rootMain chunks; 69.1 + 37.5 kB gz are framework |

Marginal cost of this PR: **+3.4 to +12.0 kB gzip per route** over the existing catalog reference. The ~150 kB line is mathematically unreachable while the shared baseline is 131.8 kB — that is a pre-existing property of the app shell, not of this diff.

## 2. Findings

```
[HIGH] frontend/src/lib/i18n/cs-CZ.ts:1 — F4/F6 (bundle weight; pre-existing pattern — standalone perf ticket, not a PR gate)
What: the full cs-CZ dictionary is inlined into 17 separate client chunks — every client-leaf route chunk carries a private copy (verified: identical checkout AND catalog string literals present in all 17).
Cost: ~35.1 kB raw / ~10 kB gzip per copy; 44-63% of each new checkout route chunk (22.9 / 15.7 / 14.3 kB gz) is dictionary; every cross-route navigation re-downloads ~10 kB gz that a shared chunk would cache once; this PR grew the dictionary +147 lines, inflating all 17 copies app-wide.
Fix: extract lib/i18n into a shared cached chunk (Turbopack chunking config) or namespace-split the dictionary so client leaves import only their slice.
Refs: CLAUDE.md §Performance (lazy-load heavy client modules); patterns.md B.5/B.14; predates this bundle — file as docs/perf ticket, do not gate the PR on it.
```

```
[MEDIUM] frontend/src/app/(customer)/objednavka/page.tsx:53-84 — F-SSR (fetch waterfall)
What: SSR runs 3 serial backend round trips — getMyProfile, then getProductById, then Promise.all(getMakerBySlug, getWidgetConfig). Profile, product and widget-config are mutually independent; only getMakerBySlug needs product.makerSlug.
Cost: cost model — 1 avoidable serial API call (~20-60 ms added TTFB p95 intra-Azure; nearest ADR analog: product page TTFB 350 ms p95) on the revenue-path entry page, every checkout open.
Fix: batch 1 = Promise.all(profile, product, widgetConfig), batch 2 = getMakerBySlug — keeping the auth-redirect check first in result handling; trade-off (wasted anon product/widget fetch for unauthenticated visitors who get redirected — cheap: product endpoint is anonymous, widget-config is Cache-Control 1h) goes in Alternatives Considered on T-0084a.
Refs: ADR 0023 §1 (no checkout TTFB row — see open question below); CLAUDE.md §Performance.
```

```
[MEDIUM] route group (customer)/objednavka/** — F6 (bundle gate line)
What: all three routes exceed the ~150 kB First Load JS gate line (169.6 / 162.4 / 161.0 kB gz).
Cost: measured above; driver is the 131.8 kB shared baseline + the i18n duplication (HIGH above), not this diff (+3.4 to +12.0 kB marginal vs /katalog, which is itself at 157.6 kB).
Fix: resolve via the i18n shared-chunk ticket + ratify an actual First Load JS budget in ADR 0023 (open question — do not invent a number per perf-charter constraints).
Refs: Gate 8 convention ~150 kB; ADR 0023 §1 has no bundle row — docs/questions/open.md entry: "First Load JS budget per route + checkout TTFB budget".
```

```
[NIT] frontend/src/app/(customer)/objednavka/[id]/potvrzeni/payment-poll-client.tsx:95-104 — poller budget
What: the immediate poll on hidden-to-visible transition does not consume the 30 s budget — pathological tab flipping issues unbounded budget-free requests (1 per resume).
Cost: cost model — bounded by human behaviour and the pollInFlightRef stacking guard; worst realistic case a handful of extra GETs per confirmation.
Fix: count resume polls against activeElapsedMsRef (or cap resume polls at N) so the max-10-request design cap is strict.
Refs: T-0085 Q4/Q5 lock (3 s interval, 30 s cap).
```

HIGH: 1 (pre-existing, ticket-routed) · MEDIUM: 2 · NIT: 1 · BLOCKER: 0

## 3. Checklist results

| Check | Result |
|---|---|
| 1. Bundle weight | Flag fires on all 3 routes but breach is inherited (findings 1+3). Marginal PR cost +3.4 to +12.0 kB gz/route. **Packeta widget script: PASS** — never in the bundle; `<script>` injected at runtime only on first "choose point" click (`zasilkovna-widget.tsx:48-68`), module-level promise cache, `async`, error clears cache for genuine retry, no re-injection on re-render. |
| 2. Server-Component-first | **PASS.** `'use client'` in exactly the 6 sanctioned leaves (order-form-client, attachment-picker, zasilkovna-widget, pay-button-client, attachment-manager-client, payment-poll-client). Pages, order-summary, order-breakdown, confirmation-views, loading x3, not-found are all Server Components. No demotable client component found. |
| 3. SSR fetch efficiency | `/objednavka`: partial waterfall — **MEDIUM finding 2**. `/objednavka/[id]`: single detail fetch — PASS. `/potvrzeni`: single detail fetch — PASS. |
| 4. Images | **PASS.** `order-summary.tsx:30-36` uses `next/image` with explicit `width={96} height={72}` + sized container; zero raw `<img>` in the diff. |
| 5. Poller discipline | **PASS** (one Nit). Pre-incremented budget means the cap fires at tick 10 *without* a request: max 9 poll GETs + 1 SSR fetch = max 10. Visibility-pause freezes budget; cleanup stops interval + removes listener; `intervalId` null-guard + `[view, orderId]` deps prevent double-intervals on re-render; `pollInFlightRef` prevents overlap. Nit: resume polls bypass the cap (finding 4). |
| 6. Attachment upload | **PASS.** Sequential per ticket lock. State granularity fine: max 3 state updates per file (status patch x2 + progress), spaced by network awaits — at most ~30 cheap re-renders over a max-10-file sequence, no storm. Retry manager patches single rows by id over a max-10-item array. |
| 7. Widget script | **PASS** — see check 1. Loaded once, cached, lazy, retry-safe. |
| 8. loading.tsx | **PASS.** Present for all 3 routes with layout-mirroring skeletons. |
| 9. No client data libs | **PASS.** `package.json`/lockfile untouched on the branch (`git diff master...HEAD` shows zero package files); no SWR/React Query/Zustand imports; single sanctioned `useEffect` (poller timer), grep-verified across the diff. |

## 4. Folds handed to the reviewer

1. **Fold into this PR (cheap, revenue path):** parallelize the `/objednavka` SSR batch (finding 2) — one restructure, removes ~20-60 ms TTFB.
2. **Standalone perf ticket (pre-existing, do not gate):** i18n shared-chunk extraction (finding 1) — largest measurable win available (~10 kB gz per route transition, shrinks all 17 client chunks).
3. **Open question for docs/questions/open.md:** ADR 0023 has no First Load JS budget and no checkout-surface TTFB row; ratify both before the next perf gate so the ~150 kB convention becomes a real budget.

— Performance Optimizer, 2026-06-10
