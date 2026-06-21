# Preliminary review — `feat/quality-gates-bundle` (T-0132 k6 + T-0133 a11y)

**Reviewer:** Code Reviewer (running in parallel with the implementer)
**Status:** PRELIMINARY — written from the tickets + ADR 0023 + quality-gates.md before artifacts land. This is the rubric the final review will apply, not a sign-off.
**Branch:** `feat/quality-gates-bundle` · **Bundle:** B (one PR, two tickets)
**Inputs read:** T-0132, T-0133, ADR 0023 §1/§2/§5/§6, quality-gates.md, ci.yml, frontend/package.json, site-url.ts, check-consistency.mjs, i18n cs-CZ shape.

## Pre-flight state of the tree (as of this read)

The implementer has **not yet committed any bundle artifacts**. Working tree at read time shows only `.claude/settings.json`, an unrelated `docs/runbooks/monitoring.md` edit, and an unrelated draft. Confirmed ABSENT:
- `deploy/load-tests/` (no k6 script, no README) — T-0132 deliverable missing.
- `frontend/vitest.config.ts`, `frontend/vitest.setup.ts` — T-0133 harness missing.
- Any `frontend/src/**/*.test.{ts,tsx}` (only `node_modules` hits) — no tests yet.
- `docs/test-plans/a11y-manual-checklist.md` — missing.
- `frontend/package.json` has **NO** `vitest` / `jest-axe` / `@testing-library/*` devDeps and **NO `"test"` script**. (Current devDeps: tailwind, eslint, husky, nswag, typescript, @types/*.)
- `ci.yml` `frontend` job has steps Install → Typecheck → Lint → Build; **no test step**.

→ Nothing to approve yet. Below is the row-by-row rubric. **None of these can be marked PASS until the artifacts exist and `npm run test:run` is observed green.**

---

## HEADLINE GATE — the harness must be REAL + RUNNING (not theater)

This is the single most important thing. A wired-but-non-running harness is a HARD FAIL. To clear it, the final review must observe ALL of:

1. `frontend/package.json` devDeps include `vitest`, `@vitejs/plugin-react`, `@testing-library/react`, `@testing-library/jest-dom`, `jest-axe`, `@types/jest-axe`, `jsdom` (T-0133 §scope / AC-1).
2. **`package-lock.json` is regenerated and committed in the same PR** (AC-1 explicit: "real lockfile update"). A `package.json` edit with a stale lockfile = HARD FAIL (CI `npm ci` would break).
3. A `"test"` (and `"test:watch"`) script exists. NOTE: tickets reference both `npm run test` (AC-1/AC-5) and `vitest run`. The user's pre-flight names **`npm run test:run`** as the green signal. The implementer must wire whatever script CI invokes so that the command CI runs actually executes vitest in run-once (non-watch) mode. Flag if `"test"` maps to watch mode (would hang CI).
4. `vitest.config.ts` sets `environment: 'jsdom'`, `globals: true`, `setupFiles` registering `@testing-library/jest-dom` + `jest-axe`'s `toHaveNoViolations`, and **`exclude` (and `coverage.exclude`) dropping `src/lib/api-client/**`** (AC-1, mirrors linter `IGNORED_PATH_GLOBS`). Verify the generated client is genuinely excluded.
5. **The tests actually RUN and PASS.** Final review MUST run `npm run test:run` (or the CI command) and observe green. A harness whose tests error/skip/hang is theater → request changes, no approval.

---

## T-0133 — Accessibility (AC-by-AC rubric)

- **AC-1 (harness installed + runnable):** see HEADLINE GATE. PASS only when devDeps + lockfile + config + script present AND `vitest run` executes in jsdom with the matchers registered and api-client excluded. *Status: PENDING.*
- **AC-2 (axe zero-violations on REAL critical pages):** tests must render the **actual presentational components** of catalog list, product detail, checkout/order surfaces, and the four static pages (`/jak-to-funguje`, `/pro-makery`, `/vop`, `/gdpr`) and assert `await axe(container)` → `toHaveNoViolations()`. Anti-theater checks:
  - Reject if a test renders a trivial empty `<div/>` or a stub component — must be the real markup tree (e.g. the catalog `maker-card.tsx` / `katalog/[slug]/product-card.tsx` subtrees, the checkout form).
  - **Path correction for the implementer:** the ticket prose says `produkt/__tests__/...`, but the real tree is `frontend/src/app/(public)/katalog/...` and `katalog/[slug]/...` (there is no `produkt/` route). Tests must target the components that actually exist; a test file under a non-existent route renders nothing. Verify the imports resolve to real components.
  - Server Components that fetch must be tested via their **presentational child with seeded props** (no network in jsdom). Confirm no `apiFetch`/network call fires in a test.
  - *Status: PENDING.*
- **AC-3 (violations FIXED, not suppressed):** any axe finding from AC-2 must be **fixed in markup/i18n** (contrast token, `<label htmlFor>`/`aria-label`, focus order, `alt`, `aria-describedby` on form errors) and listed in the PR description + ticket status log. HARD checks:
  - Reject any `axe(container, { rules: { ... : { enabled: false } } })` that disables an AA rule to make a page pass — that is suppression, not a fix (AC-3: "no AA assertion is weakened").
  - Reject scoping-down that drops a rule globally; a genuine deferral must be a narrow `// TODO(T-NNNN)` on one assertion with a follow-up ticket, per T-0133 §C.
  - WCAG 2.1 **AA** is the target (ADR 0023 §5). Not AAA (ADR rejects AAA); not a weakened subset.
  - *Status: PENDING.*
- **AC-4 (SEO predicate tests — Q-0031 retroactive):** pin `canonicalUrl(path)` and `SITE_URL` default from `frontend/src/lib/seo/site-url.ts`. Confirmed the helper exists with the exact contract AC-4 describes:
  - `canonicalUrl('/')` → `https://makables.cz` (bare origin, no trailing slash); `canonicalUrl('/katalog')` → no double slash; non-leading-slash input normalised.
  - **GOTCHA to verify:** `SITE_URL` is resolved **once at module import** (`resolveSiteUrl()` runs at load). A test asserting the prod-vs-dev default must set `NEXT_PUBLIC_SITE_URL` / `NODE_ENV` and use `vi.resetModules()` + dynamic `import()` (or `vi.stubEnv` before first import). A naive `import { SITE_URL }` at top-of-file will bake whatever env the test runner had at load and the default-resolution assertion becomes a tautology. Flag if the implementer asserts default resolution without env isolation.
  - ~6 assertions expected. *Status: PENDING.*
- **AC-5 (axe-in-CI — regression gate going forward):** `ci.yml` `frontend` job gains a `Test (vitest a11y + SEO)` step running the vitest command after Build, reusing the existing `npm ci` + node 20 setup (no new job). Must be **valid YAML** and must run the FE tests so a future a11y regression fails CI. Verify the step's `run:` matches the actual passing script name (the `npm run test` vs `test:run` consistency point above). *Status: PENDING.*
- **AC-6 (manual a11y checklist doc):** `docs/test-plans/a11y-manual-checklist.md` must list keyboard-nav rows + NVDA/Firefox-Czech screen-reader rows per critical customer path per ADR 0023 §5 (page title, landmark/heading order, label + `aria-describedby` errors, accessible names in Czech, dynamic-state announcements, contrast spot-checks). The RUN is recorded as the ticket `manual_step` (QA actor, pre-launch, read-only rollback). *Status: PENDING.*
- **AC-7 (T8/T9 stay green):** a11y fixes must NOT drop or rename any `cs-CZ` key. Confirmed T8 in `check-consistency.mjs` reads `BusinessErrorMessage.cs` ↔ `cs-CZ.ts` keys; new visually-hidden strings as additive `a11y.*` keys keep T8 green. Final review must run `node scripts/check-consistency.mjs` and observe **exit 0, no new T1–T9 findings**. *Status: PENDING.*
- **AC-8 (build clean):** `npx tsc --noEmit` + `npx eslint src` + `npx next build` green; `npm run test` green; consistency exit 0; **no edits to `frontend/src/lib/api-client/`** (pre-commit hook + linter enforce). Watch that the new test files don't trip eslint (`@testing-library` globals, `jest-axe` types) — `eslint src` covers `src/**`, so test files under `src` must lint clean. *Status: PENDING.*

---

## T-0132 — k6 load-test script (AC-by-AC rubric)

- **AC-1 (valid k6, 100 VU / 30 min / mixed browse+order):** `deploy/load-tests/makables-load.js` must export `options.scenarios` summing to ~100 concurrent VUs over a 30-min `duration`, with a catalog-browse group (GET `/katalog`, GET `/produkt/{id}` — or the real catalog/product paths) AND an order-placement group (order-create API). `BASE_URL` via `__ENV.BASE_URL` (configurable). Verify it's syntactically valid k6 (imports from `k6/http`, `k6`), not pseudo-code. *Status: PENDING.*
- **AC-2 (budget FIDELITY — must match ADR 0023 §1 EXACTLY):** `options.thresholds`, per-surface tagged, must be:
  - catalog SSR: `p(95)<400` **and** `p(99)<1000` ms
  - product SSR: `p(95)<350` ms (ADR §1 also lists product p99 1000; T-0132 §B names it — include if tagged)
  - order API: `p(95)<600` **and** `p(99)<1500` ms
  - zero-5xx: `http_req_failed: ['rate==0']` PLUS per-request `check(res, { 'no 5xx': r => r.status < 500 })`
  - **HARD FAIL on any drift** (e.g. 500ms catalog, p95 only with no p99, softened/invented budgets). ADR 0023 §1 is the source of truth; quote the row in any reject comment. *Status: PENDING.*
- **AC-3 (README completeness):** `deploy/load-tests/README.md` documents install/seed/run/read-output, the **out-of-band DB-CPU <70%** check (Azure Postgres metrics blade — k6 cannot read it; ADR 0023 §6 names <70%), the **staging-only / pre-launch / never-production** rule, and the **Q-0015 note** (k6 = API latency, NOT JS bundle; FE bundle budget is a separate deferred concern). *Status: PENDING.*
- **AC-4 (RUN as manual_step, correctly flagged):** the 30-min staging RUN must be present as the ticket's `manual_step` with actor (Ops/QA human + seeded staging), timing (pre-launch, NOT a merge gate), rollback (read-only; budget MISS → perf follow-up ticket, not a blocker). Confirmed already present in the T-0132 frontmatter. *Status: PASS (ticket frontmatter); re-confirm nothing in the PR claims it was executed.*
- **AC-5 (consistency clean, no app code):** artifact is non-application JS under `deploy/load-tests/` (outside `frontend/src` + `backend/src`); `check-consistency.mjs` exit 0; no NSwag regen, no migration, no `lib/api-client/` edits. **Confirmed:** the linter's candidate filter is `.(cs|ts|tsx|mjs|cjs)$` and per-rule globs are all `backend/src/**` or `frontend/src/**`, so a `deploy/load-tests/*.js` file is never a candidate → won't produce findings. AC-5 claim holds. *Status: structurally PASS pending the file actually living there.*

---

## MANUAL-RUN HONESTY (cross-bundle, critical)

Both gated RUNs ship the **artifact**, not a claimed execution:
- T-0132: the bundle ships the k6 **script + README**. The 30-min 100-VU run is the `manual_step`. **HARD FAIL if the PR/status log claims "load test passed" / "p95 met" / any executed-run result** without an actual staging run. There is no live seeded staging at authoring time — any "ran it, green" claim is fabricated. Verify the README and status log say the RUN is gated/pending, not done.
- T-0133: the bundle ships the **NVDA/keyboard checklist doc**. The NVDA-on-Firefox-Czech pass is the `manual_step`. **HARD FAIL if the PR claims the manual screen-reader pass was performed.** Only the AUTOMATED axe gate is the merge gate; the manual pass is pre-launch.
- Distinguish clearly: the **automated** axe tests + SEO predicates ARE expected to have been run and shown green in this PR (that's AC-2/AC-4/AC-5/AC-8). The **manual** legs are not.

## Quality-gates.md cross-checks

- **Gate 5 (Tests / pure-logic TDD):** the SEO predicates (`canonicalUrl`, `SITE_URL`) are **pure logic** → T-0067+ TDD mandate applies. Per Gate 5, the test commit must precede (or red→green in the status log) the implementation. The SEO helper already shipped in T-0131 (commit `834de75`) **untested** — Q-0031's whole premise. These predicate tests are explicitly a **retroactive** pin of already-shipped T-0131 code, which T-0133 names as the Q-0031 close-out. Treat as the sanctioned retro-fix (not a fresh after-the-fact violation), BUT the axe tests + any NEW pure helper introduced in this PR must follow TDD. Flag any brand-new pure logic that got tests written after its implementation in this same PR.
- **Gate 1 (FE self-check):** new test files live under `frontend/src` → subject to no-`any`, no `console.*`, no unused imports, typed. jest-axe sometimes tempts an `any` cast on `expect`; require `@types/jest-axe` typing instead.
- **Gate 6 (contract parity):** N/A — no backend/NSwag change in either ticket. Confirm `lib/api-client/` untouched.
- **Gate 8 (Optimizer):** new npm packages (vitest + jest-axe + testing-library + jsdom) are **devDependencies only** → no client-bundle impact, no SSR runtime path. Optimizer ping NOT required (not a hot path; dev-only deps). Note in the final review that the bundle-size gate is unaffected (these never ship to the client).

## Verdict

**PRELIMINARY: NOT APPROVABLE YET — artifacts absent.** No blocking design objection to the bundle plan; the ADR/AC mapping is sound and the linter/T8 claims check out. Final approval is contingent on, in priority order:
1. The harness being REAL + RUNNING: devDeps + regenerated lockfile + config (api-client excluded) + a non-watch test script, with `npm run test:run` observed **green**.
2. axe tests on the **real** critical components (correct `katalog`/`[slug]` paths, not the non-existent `produkt/` path) asserting `toHaveNoViolations` AA, with surfaced violations **fixed** (not rule-disabled).
3. k6 thresholds matching ADR 0023 §1 **exactly** (400/1000, 350, 600/1500, rate==0 + status<500 check).
4. **No claimed execution** of either manual RUN (k6 30-min, NVDA pass) anywhere in the PR/status log.
5. T8 green + `check-consistency.mjs` exit 0; tsc/eslint/next build green.
