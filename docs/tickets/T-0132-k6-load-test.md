---
id: T-0132
title: k6 load-test script + staging-gated RUN against ADR 0023 perf budgets
status: ready
size: S
owner: nextjs-frontend
created: 2026-06-21
updated: 2026-06-21
depends_on: [T-0046, T-0048, T-0063, T-0065, T-0085]
blocks: []
user_stories: []
adrs: [0023]
phase: 6
manual_steps:
  - actor: Ops/QA (human, against live seeded staging)
    timing: pre-launch (ADR 0023 §6 "one synthetic load run before launch"); NOT a merge gate — the script ships now, the RUN is gated
    step: "Execute the committed k6 script against a live, seeded staging environment: 100 concurrent VUs, 30-min run, mixed catalog browse (GET /katalog, /produkt) + order placement, BASE_URL pointed at staging. Per deploy/load-tests/README.md. Then assess the result against ADR 0023 §1 budgets: catalog SSR p95 400ms/p99 1000ms, product SSR p95 350ms, order API p95 600ms/p99 1500ms. PASS = thresholds met + zero 5xx + DB CPU <70% (the DB-CPU leg is checked OUT-OF-BAND in the Azure Postgres metrics blade — k6 cannot read it). Record the run summary + the budget verdict in the sprint status doc / launch-checklist."
    rollback: "N/A — read-only load generation against staging (never production). If the run melts staging, stop the run; no data rollback needed (staging is a seeded scratch env). A budget MISS becomes a perf follow-up ticket per ADR 0023 §1, not a T-0132 merge blocker."
security_touching: false
layers: [infra]
---

# T-0132 — k6 load-test script + staging-gated RUN against ADR 0023 perf budgets

## Context

T-0132 is the **k6 half of the quality-gates bundle** (`feat/quality-gates-bundle`, T-0132 k6 + T-0133 a11y), user-locked 2026-06-21. Both ship under one PR (Bundle B). T-0132 delivers the synthetic load-test script ADR 0023 §6 mandates ("one synthetic load run before launch") and bakes the ADR 0023 §1 performance budgets directly into the script as k6 `thresholds`.

The split is **codebase-now + staging-gated RUN**: the k6 JS script + a README ship in this PR; the actual 30-minute execution against a live seeded staging environment is a **`manual_step`** (it needs a running, seeded staging — not available at script-authoring time, and not a merge gate). This mirrors how the platform ships background-job code before the timer fires: the artifact is reviewable + version-controlled now; the run is gated on the environment being live.

ADR 0023 §6 (Load testing) specifies it exactly: *"One synthetic load run before launch: 100 concurrent users, mixed catalog browse + order placement, 30 min, k6 script committed to `deploy/load-tests/`. Pass criteria: p95 latency under budget; zero 5xx; database CPU under 70%."* ADR 0023 §1 supplies the per-surface budgets the script asserts. T-0132 implements both: the script + the thresholds + the README + the gated RUN as the `manual_step`.

**Q-0015 fold (no scope creep).** Q-0015 (optimizer, checkout-flow Gate 8) is about the **frontend First-Load-JS bundle budget** — an ADR 0023 §1 gap on the *client-side JS* axis. k6 measures **server-side API/SSR latency**, not JS bundle size. They are orthogonal. T-0132 explicitly does **not** close Q-0015: the bundle-budget number is **N/A** to k6, Q-0015 stays a separate frontend-perf concern, deferred. This ticket notes the distinction so a reviewer doesn't conflate "we ran load tests" with "we have a JS bundle budget" — we don't, and that's Q-0015's job.

This is an **infra/ops** artifact — a standalone k6 JS file + README under `deploy/load-tests/`. It is not application code: no backend change, no frontend change, no NSwag regen, no migration, no new `BusinessErrorMessage` codes, no i18n. It does not run in CI (a 30-min 100-VU run is a pre-launch staging exercise, not a per-PR gate).

## Locked design decisions

Captured per `docs/process/deliberation.md`. The user locked the quality-gates bundle 2026-06-21 (the two gated RUNs as manual_steps; ADR 0023 budgets as the pass criteria). PM-absorbed decisions follow from ADR 0023 §1/§6.

### A. User-locked at bundle lock 2026-06-21 (non-negotiable)

1. **The k6 RUN (30-min, 100-VU, staging) is a `manual_step`, not a merge gate.** The script ships now; the execution + the budget assessment are human/staging-gated (needs a live, seeded staging env). **Rejected:** running k6 in CI (a 30-min 100-VU run per PR is absurd cost + needs a live env CI doesn't have); blocking the bundle merge on a staging run (staging may not be seeded at merge time).

2. **Pass criteria = ADR 0023 §1 budgets as k6 thresholds + zero 5xx + DB CPU <70%.** The script bakes the budgets as `thresholds` so k6 itself reports pass/fail; the DB-CPU leg is **checked out-of-band** (k6 cannot read Azure Postgres CPU — the operator reads the metrics blade). **Rejected:** softer or invented budgets (ADR 0023 §1 is the source of truth); dropping the DB-CPU criterion (ADR 0023 §6 names it explicitly — it just lives out-of-band).

3. **Q-0015 bundle-budget is N/A here — k6 is API latency, not JS bundle.** Q-0015 stays a separate frontend-bundle concern, deferred. **Rejected:** folding a JS-bundle assertion into the k6 ticket (different axis; k6 has no view of client bundle size).

### B. ADR-locked (no relitigation)

- **ADR 0023 §1 (performance budgets).** The thresholds the script asserts: Catalog page TTFB (SSR) p95 **400 ms** / p99 **1000 ms**; Product page TTFB (SSR) p95 **350 ms** / p99 **1000 ms**; Order creation API p95 **600 ms** / p99 **1500 ms**. (Payment-redirect-receipt p95 1500ms/p99 3000ms is included if order placement drives a payment-session call in the scenario.)
- **ADR 0023 §2 (scale assumptions).** The load shape mirrors MVP scale: 100 concurrent users (the §2 "concurrent users" ceiling), catalog browse RPS up to 50, orders/day up to 200. The 100-VU / 30-min run is the §6-specified synthetic run at the §2 ceiling.
- **ADR 0023 §6 (load testing).** Script committed to `deploy/load-tests/`; 100 concurrent users; mixed catalog browse + order placement; 30 min; pass = p95 under budget + zero 5xx + DB CPU <70%.

### C. PM-absorbed (no user input needed)

- **Location:** `deploy/load-tests/` (ADR 0023 §6 names this exact path). A `makables-load.js` (or `catalog-order-mix.js`) k6 script + a `README.md`.
- **k6 script shape:** `export const options = { scenarios: {...}, thresholds: {...} }`. Scenarios: a **catalog-browse** VU group (GET `/katalog`, GET `/produkt/{id}` with a seeded id pool) carrying the bulk of the 100 VUs (catalog is the dominant traffic per §2), and an **order-placement** VU group (the order-create API + payment-session call) at a lower rate matching the ~200 orders/day shape. `BASE_URL` is read from an env var (`__ENV.BASE_URL`) — **configurable** so the same script points at dev/staging.
- **Thresholds (baked budgets):** per-scenario / per-tag `http_req_duration` thresholds — `p(95)` and `p(99)` set to the ADR 0023 §1 numbers per surface (tag catalog requests, product requests, order-API requests distinctly so each gets its own budget). A `http_req_failed` rate threshold `== 0` enforces **zero 5xx** (5xx counts as a failed check; the script also explicitly checks `status < 500`). The DB-CPU <70% criterion is documented in the README as the out-of-band leg (k6 has no DB visibility).
- **Seeded-id strategy:** the script reads a small pool of seeded product ids / maker slugs from an env var or a `SharedArray` JSON, so catalog/product requests hit real seeded rows on staging. The README documents the seeding precondition.
- **Order-placement realism:** the order scenario creates an order via the customer order-create API against a seeded test customer (auth handled per the README — a pre-issued staging JWT or a login leg). At MVP the scenario can run order-create as the headline order-path latency measurement; payment-session is included if the seeded flow allows it. The README flags any leg that needs live external providers (Comgate) vs. a staging stub.
- **README:** `deploy/load-tests/README.md` — how to install k6, how to seed staging, how to set `BASE_URL` + the seeded-id pool + auth, how to run (`k6 run makables-load.js`), how to read the threshold pass/fail output, and the **out-of-band DB-CPU check** (Azure Postgres metrics blade, <70% during the run). States plainly: the RUN is pre-launch, against staging only, never production.
- **Not in CI:** the script is not wired into `.github/workflows/ci.yml` (a 30-min 100-VU run is a pre-launch staging exercise). T-0133 owns the CI test step (vitest a11y); T-0132 is the offline load artifact.
- **Q-0015 note in the README + this ticket:** k6 measures API/SSR latency, NOT JS bundle size; the frontend First-Load-JS budget (Q-0015) is a separate, deferred concern.
- **No application code, no NSwag, no migration, no i18n, no error codes.**

## Scope

### Load-test artifact (`deploy/load-tests/`)

- **`deploy/load-tests/makables-load.js`** — NEW. The k6 script:
  - `options.scenarios`: a catalog-browse scenario (constant-VU or ramping-VU summing to ~100 VUs over a 30-min `duration`, GET `/katalog` + GET `/produkt/{seededId}`) + an order-placement scenario (lower-rate order-create API calls against a seeded customer).
  - `options.thresholds`: per-tag `http_req_duration` `p(95)`/`p(99)` set to the ADR 0023 §1 budgets (catalog SSR 400/1000, product SSR 350/1000, order API 600/1500), plus `http_req_failed: ['rate==0']` for the zero-5xx criterion. Per-request `check(res, { 'no 5xx': r => r.status < 500 })`.
  - `BASE_URL` via `__ENV.BASE_URL`; seeded id/slug pool via `__ENV` or a `SharedArray`.
- **`deploy/load-tests/README.md`** — NEW. Install k6, seed staging, set env (`BASE_URL`, seeded ids, auth), run, read threshold output, the out-of-band DB-CPU <70% check, the staging-only / pre-launch / never-production rule, and the Q-0015-is-not-this note.

### The RUN (manual_step)

- Pre-launch, Ops/QA actor, against live seeded staging: 100 VUs / 30 min / mixed browse+order. Assess against ADR 0023 §1 budgets + zero 5xx + DB CPU <70% (out-of-band). Record the verdict in the sprint status / launch-checklist. A MISS → perf follow-up ticket (ADR 0023 §1), not a T-0132 merge blocker.

## Out of scope

- **The 30-min execution + budget assessment** — that is the `manual_step` (pre-launch, staging, human). The script + README ship now; the run is gated.
- **CI wiring** — the k6 run does not go in CI (30-min, 100-VU, needs a live env). T-0133 owns the CI vitest step.
- **Frontend JS bundle budget (Q-0015)** — explicitly N/A to k6 (API latency ≠ JS bundle). Q-0015 stays a separate, deferred frontend-perf concern.
- **DB-CPU automation** — k6 cannot read Azure Postgres CPU; the <70% criterion is an out-of-band metrics-blade check by the operator.
- **Production load testing** — staging only, never production (ADR 0023 §6 is a pre-launch staging run).
- **a11y / vitest harness** — T-0133 owns it (same bundle PR).
- **App code, NSwag, migrations, error codes, i18n** — none.

## Acceptance criteria

- **AC-1** Given `deploy/load-tests/makables-load.js`, when inspected, then it is a valid k6 script with `options.scenarios` implementing **100 concurrent VUs over a 30-min run** with a **mixed catalog-browse (GET /katalog, GET /produkt/{id}) + order-placement** workload, and `BASE_URL` read from `__ENV.BASE_URL` (configurable per environment).
- **AC-2** Given the script's `options.thresholds`, when inspected, then the **ADR 0023 §1 budgets are baked as k6 thresholds**: catalog SSR `p(95)<400ms` / `p(99)<1000ms`, product SSR `p(95)<350ms`, order API `p(95)<600ms` / `p(99)<1500ms` (tagged per surface), plus a `http_req_failed` `rate==0` threshold enforcing **zero 5xx** (with a per-request `status < 500` check).
- **AC-3** Given `deploy/load-tests/README.md`, when inspected, then it documents: install/seed/run/read-output, the **out-of-band DB-CPU <70%** check (Azure Postgres metrics blade — k6 cannot read it), the staging-only / pre-launch / never-production rule, and the **Q-0015 note** (k6 = API latency, not JS bundle; the FE bundle budget is a separate deferred concern).
- **AC-4** Given the staging RUN, when recorded as this ticket's `manual_step`, then it carries actor (Ops/QA, human + seeded staging), timing (pre-launch, not a merge gate), and rollback (read-only against staging; a budget MISS → perf follow-up ticket, not a merge blocker).
- **AC-5** Build/consistency clean: the artifact is non-application JS under `deploy/load-tests/` (outside `frontend/src` and `backend/src`); `node scripts/check-consistency.mjs` exit 0 (no new T1–T9 findings — the script lives outside the linter's `frontend/src`/`backend/src` candidate roots); no NSwag regen, no migration, no edits to `frontend/src/lib/api-client/`.

## Test plan reference

The k6 thresholds ARE the pass/fail spec; the RUN procedure is `deploy/load-tests/README.md`. No `docs/test-plans/T-0132.md` — the README is the runbook and the RUN is the gated `manual_step`.

## Status log

- 2026-06-21 `draft` by PM. Created as the k6 half of the quality-gates bundle (`feat/quality-gates-bundle`, T-0132 k6 + T-0133 a11y), user-locked 2026-06-21. Bundle B = one PR. codebase-now + staging-gated RUN: the k6 script (`deploy/load-tests/makables-load.js` per ADR 0023 §6 path) + README ship now; the 30-min 100-VU staging execution + budget assessment are the `manual_step`. Budgets baked from ADR 0023 §1 (catalog SSR 400/1000, product SSR 350, order API 600/1500); pass = thresholds met + zero 5xx + DB CPU <70% (out-of-band). Q-0015 fold: bundle-budget N/A (k6 = API latency, not JS bundle); Q-0015 stays a separate deferred FE-perf concern. ADR 0023 §1/§6. No app code, no NSwag, no migration, no error codes, no i18n.
- 2026-06-21 `draft → ready` by PM. DoR confirmed: (1) not-duplicate — no prior load-test artifact exists (`deploy/load-tests/` absent); first k6 script. (2) observable AC — 5 ACs with measurable proof (script shape, baked thresholds, README sections, manual_step fields, consistency exit 0). (3) sized S — a standalone k6 JS file + README + the gated RUN; the script is bounded (<4h to author + review; the RUN is human-gated, not counted in the size). (4) depends_on — the surfaces the script exercises must exist: catalog (`T-0046`), product (`T-0048`), order-create (`T-0063`), payment-session (`T-0065`), confirmation (`T-0085`) — all done/ready; "All Phase 1-5" in the INDEX row is the aggregate-dependency shorthand (the full order+catalog path must be live before a meaningful load run). (5) manual_steps — populated (Ops/QA, pre-launch, the 30-min staging RUN + out-of-band DB-CPU check; read-only rollback). (6) security_touching: false — an offline load-test artifact; runs against staging, touches no auth/payment/PII code path in the repo. (7) layers — `infra` (a `deploy/` ops artifact). User-locked decisions in §A: the RUN is a manual_step; ADR 0023 budgets as thresholds; Q-0015 is N/A. **Ready for nextjs-frontend** (owns the bundle PR; the k6 script is standalone JS, order-independent from T-0133's harness). Ships in the bundle PR with T-0133.
