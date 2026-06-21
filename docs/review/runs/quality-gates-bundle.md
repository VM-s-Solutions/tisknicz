# Final review — `feat/quality-gates-bundle` (T-0132 k6 + T-0133 a11y)

**Reviewer:** Code Reviewer · **Branch:** `feat/quality-gates-bundle` · **Bundle:** B (one PR, two tickets)
**Scope reviewed:** commits `f1a6ddf..57e301c` (79d0e45 + 3a42d36 + 57e301c). The `f1a6ddf` ops-runbooks commit is a branch-base ride-along from Bundle C — its 4 files (`docs/launch-checklist.md`, `docs/questions/open.md`, `docs/review/runs/ops-runbooks-draft.md`, `docs/runbooks/monitoring.md`) are **excluded** from this review.
**Verified against:** the draft rubric, ADR 0023 §1/§5/§6, T-0132/T-0133 ACs, quality-gates.md Gates 1/5/6/8, CLAUDE.md.

## VERDICT: APPROVED

The harness is real and running, the axe pass is genuine (not suppressed), the k6 budgets match ADR 0023 §1 exactly, and there are **no false "executed/passed" claims** for either manual leg. Every checklist row passes. No blockers.

---

## Harness real + axe genuine (observed, not asserted)

- **`npm run test:run` observed GREEN:** `7 passed (7) · 26 passed (26)`, vitest 3.0.x, 2.84s. Non-watch (`"test"` and `"test:run"` both → `vitest run`; `"test:watch"` is the watch variant). Not theater.
- **Lockfile genuinely regenerated:** `frontend/package-lock.json` +3679/−663; `node_modules/vitest` pinned. Not a stale-lockfile `package.json` edit — `npm ci` will resolve.
- **Config sound** (`vitest.config.ts`): `environment: 'jsdom'`, `globals: true`, `setupFiles: ['./vitest.setup.ts']`, `include: src/**/*.{test,spec}.{ts,tsx}`, **`exclude` drops `src/lib/api-client/**`** (run + coverage). Setup registers jest-dom + `expect.extend(toHaveNoViolations)`. `vitest.d.ts` augments the matcher onto vitest's `Assertion` (tsc passes without jest globals). `tsc --noEmit` clean; `eslint` clean on every new test/harness file.
- **axe meaningful + NOT suppressed:** tests import the **real** components — `MakerCard`, `ProductGallery`, `OrderSummary` (all on disk) and the 4 static page default exports (`jak-to-funguje`/`pro-makery`/`vop`/`gdpr`) — rendered with seeded props, asserting `toHaveNoViolations()` via `axeAA`. `axeAA` (`src/lib/testing/axe.ts`) uses `configureAxe({ runOnly: { type: 'tag', values: ['wcag2a','wcag2aa','wcag21a','wcag21aa'] } })` — a **whitelist to AA**, the correct non-suppressive scoping. **Grep for `disableRules` / `rules:{...:off}` / `enabled:false` / `.skip` / `.todo` across `frontend/src`: ZERO matches.** No AA rule was disabled to make a page pass. The clean pass is genuine (components were already AA-clean — honestly stated; zero markup churn, see i18n below).
- **color-contrast deferral — ACCEPTABLE:** `color-contrast` is a `wcag2aa` rule so it is *not* tag-excluded; axe attempts it and emits a benign jsdom `HTMLCanvasElement.getContext` not-implemented warning, then degrades the rule to incomplete (not a violation) because jsdom resolves no pixels. Tests still PASS. Contrast is correctly routed to the manual checklist §C (live page). The stderr warning is noise, not a failure.

## Manual-RUN honesty (the integrity check) — CLEAN

- **k6 30-min run:** README frames the execution as the **gated manual step** ("the execution is the gated manual step (Ops/QA, against a live seeded staging env)"; "not a T-0132 merge blocker"). The "Read the output" section describes how to judge a future run, not a recorded result.
- **NVDA pass:** `a11y-manual-checklist.md` frontmatter `gate: pre-launch (NOT a merge gate)`; it is a checklist with empty `Actual`/`P/F` columns, not a results report.
- **Grep of bundle-B artifacts** for executed/passed claims surfaced only 3 benign hits: README line 30 is a *negation* ("do not read 'we ran k6' as…"), and 2 checklist lines are the NVDA section header / precondition. **No "load test passed" / "p95 met" / "NVDA passed" fabrication anywhere.** Only the automated axe + SEO + CI is presented as the merge gate.

## AC matrix

| Ticket | AC | Status | Evidence |
|---|---|---|---|
| T-0133 | 1 harness installed+runnable | PASS | devDeps + lockfile + config + scripts; `test:run` green |
| T-0133 | 2 axe zero-violations on real critical pages | PASS | MakerCard/ProductGallery/OrderSummary + 4 static pages, seeded props, 26 green |
| T-0133 | 3 violations fixed not suppressed | PASS | zero suppression patterns; clean pass genuine (no markup change needed) |
| T-0133 | 4 SEO predicate tests (Q-0031) | PASS | site-url (env-isolated via resetModules+stubEnv), truncate-for-meta, landing-metadata |
| T-0133 | 5 axe-in-CI gate | PASS | ci.yml frontend job `Test (vitest a11y + SEO): npm run test:run`, no continue-on-error |
| T-0133 | 6 manual a11y checklist | PASS | KB/SR/CT rows, NVDA+Firefox-Czech, pre-launch, not a gate |
| T-0133 | 7 T8/T9 green | PASS | consistency exit 0 (151 tracked); zero cs-CZ key deletions |
| T-0133 | 8 build clean | PASS | tsc + eslint + test green; api-client untouched |
| T-0132 | 1 valid k6 100VU/30min/mixed | PASS | ramping-vus 85 browse + 15 order = 100; 2m+26m+2m; `__ENV` BASE_URL/JWT |
| T-0132 | 2 budget fidelity vs ADR 0023 §1 | PASS | catalog p95<400/p99<1000, product p95<350/p99<1000, order p95<600/p99<1500, http_req_failed rate==0 |
| T-0132 | 3 README completeness | PASS | install/seed/run/read, DB-CPU<70% out-of-band, staging-only, Q-0015 note |
| T-0132 | 4 RUN as manual_step | PASS | gated, no claimed execution |
| T-0132 | 5 consistency clean, no app code | PASS | `deploy/load-tests/*.js` non-candidate; exit 0; no NSwag/migration |

## Gates

- **Gate 1 (FE self-check):** PASS — no `any`/`!`, typed, no `console.*`, tsc + eslint clean on test files.
- **Gate 5 (Tests / TDD):** PASS — SEO predicates are the **sanctioned Q-0031 retroactive** pin of already-shipped T-0131 code (not a fresh after-the-fact violation). No brand-new pure logic was introduced in this PR that got tests written after implementation; the a11y tests pin pre-existing components. No HARD-FAIL.
- **Gate 6 (contract parity):** N/A — no backend/NSwag change; `lib/api-client/` untouched.
- **Gate 8 (Optimizer):** Not required — new packages are devDependencies only (vitest/jest-axe/testing-library/jsdom), zero client-bundle/SSR-runtime impact; k6 script is offline ops. No hot path.

## Deviations — all acceptable

- `vitest.d.ts` + `src/lib/testing/axe.ts`: test-only, zero prod bytes (axe.ts imported only by `*.test.tsx`; api-client excluded). Justified by tsc matcher-typing + AA-runner centralisation. ACCEPT.
- Zero a11y fixes needed (clean pass): honestly stated, corroborated by zero i18n key deletions and zero markup diff. ACCEPT.
- color-contrast → manual checklist: jsdom limitation, documented in axe.ts + checklist §C. ACCEPT.
- Draft predicted `produkt/` route was non-existent; the actual tree DOES have `(public)/produkt/[productId]/` and `product-gallery.tsx`. The product a11y test imports the real component — no issue. Draft prediction superseded by reality.

## Harvest

No 3rd-hit recurring finding in this bundle. No append to `recurring-findings.md`; no Architect ping required.
