# Checkout-flow bundle — Gate 9 + QA-plan completeness audit

> QA pass per the bundle workflow. Run date: 2026-06-10. Scope: `feat/checkout-flow-bundle`
> (T-0084a → T-0084b → T-0085). No frontend test harness exists — coverage = manual QA
> plans + `tsc` + lint + `next build` (Gate 5 frontend clause, confirmed in all 3 plans).

## Part 1 — Gate 9 mechanical consistency

**Verdict: PASS.** `node scripts/check-consistency.mjs` → exit 0, output
`check-consistency: clean (118 tracked).` Baseline of 118 tracked findings unchanged
(all backend T1/T3/T5 + one backend T4); zero NEW violations from the frontend-only diff
(24 frontend files, no `lib/api-client/` hunks).

- **T4 (`any`):** rule scans `:\s*any` and `as any` in `frontend/src/**/*.{ts,tsx}`
  (`scripts/check-consistency.mjs:223-232`). Zero hits in the bundle — the Packeta
  `declare global` typing and all helpers are concrete.
- **T7 (`useEffect` fetch):** the T-0085 poller
  (`frontend/src/app/(customer)/objednavka/[id]/potvrzeni/payment-poll-client.tsx:49`)
  does **not** register. Why: the T7 body heuristic flags only direct
  `fetch(` / `apiClient.` / `await client.` calls (`check-consistency.mjs:357`); the
  poller invokes the named B.16 helper `getCustomerOrderDetail`, which the rule does not
  trace. This is a heuristic blind spot, not a marker-based suppression — acceptable here
  because the sanction rationale is documented in the poller JSDoc (lines 17–21,
  referencing T-0085 §B + Alternatives Option E), exactly as the draft review's Gate 9
  expectation demanded ("justification comment must reference T-0085 A.1, not suppress
  silently"). No sanctioned-exception comment marker convention change needed. Zero other
  `useEffect` occurrences exist under `(customer)/objednavka/`.

## Part 2 — QA-plan completeness audit

Plans audited: `docs/test-plans/T-0084a.md` (15 TCs + 3 edge), `docs/test-plans/T-0084b.md`
(12 TCs + 2 edge), `docs/test-plans/T-0085.md` (10 TCs + 2 edge). Every TC carries a
concrete route/action/expected triple.

### AC coverage matrix

#### T-0084a (12 ACs) — 10 full, 2 partial → 83% full / ~92% weighted

| AC | Plan coverage | Status |
|---|---|---|
| AC-1 form + prefill + no client fetch | TC-1 (network panel) | FULL |
| AC-2 sticky/responsive | TC-15 | FULL |
| AC-3 entry guards | TC-2 (unauth), TC-3 (missing/unknown id), TC-4 (OnRequest) | FULL (see fold F5 re: inactive product) |
| AC-4 validation mirrors + backend fields | TC-5 (mirror half only) | **PARTIAL** — no TC forces a backend `ApiError.fields` rejection (fold F1) |
| AC-5 widget pick flow | TC-6, TC-7 | FULL |
| AC-6 widget failure degradation | TC-8 + edge (SSR config down) | FULL |
| AC-7 personal-pickup gating | TC-9, TC-10 | FULL |
| AC-8 single POST + sequential uploads | TC-11 (double-click) | FULL |
| AC-9 partial-failure handoff + pre-checks | TC-12, TC-13 | FULL (boundary precision — fold F3) |
| AC-10 error-code alerts | TC-14 + edges (deactivated/notActive) | **PARTIAL** — 3 of 5 named codes stepped (fold F2) |
| AC-11 hygiene | tsc/lint/build clause | FULL (reviewer greps the rest) |
| AC-12 responsive + primitives | TC-15 | FULL |

#### T-0084b (10 ACs) — 10 full → 100%

| AC | Plan coverage | Status |
|---|---|---|
| AC-1 SSR breakdown + deadline | TC-1 | FULL |
| AC-2 zero session on load | TC-2 (network panel) | FULL |
| AC-3 pay click → Comgate, one POST | TC-3 (double-click) | FULL |
| AC-4 back + re-pay (cached session) | TC-4 | FULL |
| AC-5 payment.* alerts + refresh-on-conflict | TC-5, TC-6 + edge (auto-cancel race) | FULL |
| AC-6 attachment list + count gate + pre-checks | TC-7 | FULL |
| AC-7 per-file retry, optimistic append | TC-8 | FULL |
| AC-8 ?attachmentsFailed alert | TC-9 | FULL |
| AC-9 non-PendingPayment banner + 404/login | TC-10, TC-11 | FULL |
| AC-10 hygiene + responsive | TC-12 + tsc/lint/build clause | FULL |

#### T-0085 (8 ACs) — 8 full → 100%

| AC | Plan coverage | Status |
|---|---|---|
| AC-1 failure status, zero polls | TC-3 (network panel) | FULL |
| AC-2 SSR Paid → success, no poller | TC-4 | FULL |
| AC-3 verify → success in place | TC-1 | FULL |
| AC-4 cap expiry, polling stops | TC-2 | FULL |
| AC-5 visibility pause/resume | TC-6 | FULL |
| AC-6 crafted ?status= never success | TC-5 + edge (garbage/uppercase) | FULL |
| AC-7 only detail endpoint; Cancelled poll → failure | TC-7, TC-8, TC-10 | FULL |
| AC-8 hygiene + cleanup + responsive | TC-8, TC-9 + tsc/lint/build clause | FULL |

### Draft-review HIGHs — explicit QA steps (4/4 present)

| HIGH | QA step |
|---|---|
| Session-on-click (HIGH-1) | T-0084b TC-2 — network panel: zero `payment-session` requests on load; TC-3 double-click → one POST |
| Crafted-param attack (HIGH-2) | T-0085 TC-5 — forged `?status=paid` on unpaid order → verifying only; edge rows cover garbage/uppercase |
| Upload partial-failure handoff (HIGH-3) | T-0084a TC-12 (kill network mid-upload → `?attachmentsFailed=1`) + T-0084b TC-9 (one-time alert on arrival) |
| Widget failure degradation (HIGH-6) | T-0084a TC-8 (script blocked → disabled + retry) + edge (SSR widget-config fetch down) |

### Edge-case checklist (audit item 3)

| Required edge | Where | Status |
|---|---|---|
| 11th file rejected | T-0084a TC-13 | PRESENT |
| 10 MiB + 1 rejected client-side | T-0084a TC-13 uses "11-MiB file" | PARTIAL — boundary imprecise (fold F3) |
| Double-click submit | T-0084a TC-11; T-0084b TC-3 | PRESENT |
| Poll cap expiry | T-0085 TC-2 | PRESENT |
| Visibility pause/resume | T-0085 TC-6 | PRESENT |
| Cancelled-state arrival at confirmation | T-0085 TC-10 (poll-observed) only | PARTIAL — SSR-arrival variant missing (fold F4) |
| /objednavka without productId | T-0084a TC-3 | PRESENT |
| Unauthenticated × 3 routes | T-0084a TC-2; T-0084b TC-11; T-0085 TC-7 | PRESENT |

### Manual-step preconditions (audit item 4) — PRESENT

- Packeta widget key: T-0084a preconditions name T-0070 manual step
  `packeta-public-widget-key-secret` and declare QA blocked without it.
- Comgate return URLs: T-0085 preconditions name `comgate-return-urls-config` with the
  exact URL template; T-0084b preconditions pin Comgate sandbox `TestMode = true`.

### Responsive matrix (audit item 5) — PRESENT

375/768/1280 stepped in all three plans (T-0084a TC-15 with layout expectations;
T-0084b TC-12; T-0085 TC-9 across all four frames).

## Fold list (missing scenarios — implementer/QA to add before PR execution)

1. **F1 (T-0084a AC-4, second half):** no case forces a backend field rejection — add a
   TC that bypasses the mirror (devtools-edit a field post-validation or tamper the
   request) and verifies `ApiError.fields` renders inline, camelCase-normalised.
2. **F2 (T-0084a AC-10):** `maker.notVerified`, `maker.personalPickupDisabled`, and
   `order.invalidQuantity` alerts are never stepped — only 3 of the 5+ named codes are.
   Add staging toggles or note why unreachable.
3. **F3 (boundary precision + HIGH-4 interaction):** replace "11-MiB file" with the exact
   pair — 10 MiB accepted / 10 MiB + 1 byte rejected client-side — and add one genuine
   ~10 MiB upload on a throttled uplink to exercise the HIGH-4 timeout-override fix.
4. **F4 (T-0085 decision-matrix row 5):** SSR arrival with state already `Cancelled` and
   a non-failure/absent `?status=` must render the state banner (plan covers only
   poll-observed Cancelled and Refunded/Disputed).
5. **F5 (T-0084a AC-3):** the staged "inactive product id" precondition is never used —
   add an explicit visit with the inactive productId → `notFound()` (TC-3 only steps
   missing/unknown).

## Verdict

Gate 9: **PASS** (exit 0, 118 tracked, zero new T4/T7). QA plans: T-0084b and T-0085
fully cover their ACs and all four draft-review HIGHs with concrete reproduction steps;
T-0084a covers 10/12 fully with 2 partials. Plans are executable once the two manual-step
preconditions land on staging. Recommendation: fold the 5 items above into the plans
before PR-open execution — none blocks implementation, all are plan-edit-only.
