---
id: T-0133
title: Accessibility — vitest + jest-axe frontend harness, axe-core a11y tests in CI, manual NVDA checklist
status: ready
size: M
owner: nextjs-frontend
created: 2026-06-21
updated: 2026-06-21
depends_on: [T-0046, T-0047, T-0048, T-0084a, T-0084b, T-0085, T-0086a, T-0086b, T-0130, T-0131]
blocks: []
user_stories: []
adrs: [0023]
phase: 6
manual_steps:
  - actor: QA (human + screen reader)
    timing: pre-launch (before production go-live; not a merge gate)
    step: "Run the manual a11y pass in docs/test-plans/a11y-manual-checklist.md — keyboard-only navigation of every critical customer path (catalog, product, checkout/order, static pages) + an NVDA-on-Firefox Czech screen-reader pass (ADR 0023 §5). Record pass/fail per row; file any finding as a follow-up ticket. This is the human/assistive-tech leg the automated axe tests cannot cover (reading order, focus announcements, label clarity in Czech)."
    rollback: "N/A — read-only verification. Findings become follow-up tickets; they do not block the T-0133 merge (the automated axe gate is the merge gate; the manual pass is a pre-launch gate)."
security_touching: false
layers: [web-frontend, config]
---

# T-0133 — Accessibility: vitest + jest-axe harness, axe-core a11y tests in CI, manual NVDA checklist

## Context

T-0133 is the **a11y half of the quality-gates bundle** (`feat/quality-gates-bundle`, T-0132 k6 + T-0133 a11y), user-locked 2026-06-21. Both ship under one PR (Bundle B). T-0133 stands up the frontend test harness the platform has lacked the whole build phase, then uses it to put WCAG 2.1 AA enforcement into CI.

The bundle answers **Q-0031** (the standing "frontend has no test harness" gap): stand up **vitest + @testing-library/react + jest-axe** as the real harness — not a standalone axe-only script. This is the harness decision the user locked; it does three jobs at once:

1. **Unblocks automated a11y** (this ticket's axe-core tests + axe-in-CI).
2. **Retroactively unblocks the T-0131 SEO predicate tests** (`canonicalUrl` and the other pure SEO/format predicates the SEO ticket left unpinned because no harness existed). T-0133 writes those tests too, while the harness exists — closing the Q-0031 retroactive item.
3. **Establishes the FE pure-logic test seam** every future frontend ticket builds on (the first `*.test.ts` / `*.test.tsx` in the repo).

ADR 0023 §5 sets the accessibility target: **WCAG 2.1 Level AA for customer-facing surfaces** (catalog, product, order placement, account pages), `axe-core` automated checks in frontend CI, manual keyboard-nav per major release, and **NVDA + Firefox Czech-language screen-reader testing once before launch**. ADR 0023 §6 names the harness explicitly: "Component tests: vitest + Testing Library." T-0133 implements exactly that.

The frontend stays a pure presentation layer (CLAUDE.md): the tests assert rendered DOM has zero axe violations + pin pure predicates; they introduce no business logic. Any a11y violation the axe tests surface gets a **small, noted fix** (contrast token, missing `aria-label`/`<label>`, focus order, `alt` text) — these touch presentation markup and i18n strings only. **i18n keys are preserved** (a11y fixes must not drop or rename `cs-CZ` keys — T8 stays green); if a fix needs a new visually-hidden string it gets a new `a11y.*` key.

## Locked design decisions

Captured per `docs/process/deliberation.md`. The user locked the quality-gates bundle 2026-06-21 (Q-0031 answer + the two gated RUNs as manual_steps). PM-absorbed decisions follow from ADR 0023 §5/§6 and CLAUDE.md frontend rules.

### A. User-locked at bundle lock 2026-06-21 (non-negotiable)

1. **Q-0031 → vitest + @testing-library/react + jest-axe is THE frontend harness** (real harness, not a standalone axe script). Added as `devDependencies` in `frontend/package.json` with a real lockfile update + a `vitest.config.ts` + a `"test"` script. **Rejected:** standalone Playwright-free axe CI script (option 3 of Q-0031 — solves only T-0133, leaves T-0131 SEO predicates unpinned and gives the platform no general FE unit harness); status-quo tsc/lint/build/manual-QA only (option 2 — leaves both T-0131 and T-0133 blocked).

2. **axe-core a11y tests assert zero violations against WCAG 2.1 AA** (ADR 0023 §5) on the critical customer paths. **Rejected:** AAA (ADR 0023 explicitly rejects AAA); a subset of rules (AA is the named target; do not weaken it).

3. **The manual a11y RUN (keyboard + NVDA/Firefox Czech screen reader) is a `manual_step`**, not a merge gate — it needs a human + assistive tech. The automated axe gate is the merge gate; the manual pass is a **pre-launch** gate (ADR 0023 §5 "once before launch"). **Rejected:** blocking the bundle merge on the manual pass (can't automate a screen-reader human; would stall the PR indefinitely).

### B. ADR-locked (no relitigation)

- **ADR 0023 §5 (accessibility).** WCAG 2.1 AA for customer-facing surfaces; `axe-core` in frontend CI; manual keyboard-nav per major release; NVDA + Firefox Czech screen-reader once before launch; 4.5:1 body / 3:1 large-text contrast; visible focus on every interactive element; form errors via `aria-describedby`, never color-only. These are the assertions the tests + the checklist verify.
- **ADR 0023 §6 (testing strategy / frontend).** "Component tests: vitest + Testing Library for pure logic and visual components. Coverage target 60%." T-0133 stands up exactly this harness. (60% is an aspirational target, not a merge gate at MVP — the gate is "axe zero-violation on the critical paths" + "the pinned predicates pass"; coverage grows ticket-by-ticket.)
- **CLAUDE.md frontend rules.** No business logic in tests-under-test; Server Components default; all strings via `cs-CZ` i18n. The generated `lib/api-client/` is never edited and is **excluded from the vitest run** (it is generated; testing it is noise — mirrors the consistency-linter `IGNORED_PATH_GLOBS`).

### C. PM-absorbed (no user input needed)

- **Harness wiring:** `vitest` + `@testing-library/react` + `@testing-library/jest-dom` + `jest-axe` + `jsdom` (or `happy-dom`) + `@vitejs/plugin-react` as `devDependencies`. `vitest.config.ts` sets `environment: 'jsdom'`, `globals: true`, a `setupFiles` that registers `@testing-library/jest-dom` + `jest-axe`'s `toHaveNoViolations` matcher, and an **`exclude`/`coverage.exclude` that drops `src/lib/api-client/**`** (generated) plus the default `node_modules`/`.next`/`dist`. `"test": "vitest run"` + `"test:watch": "vitest"` scripts in `package.json`.
- **Test file convention:** `*.test.ts` / `*.test.tsx` colocated next to the unit under test (or under a `__tests__/` sibling). First tests in the repo; sets the pattern.
- **axe-test surface (critical customer paths per ADR 0023 §5):** render each critical surface (or its largest pure presentational subtree) and assert `await axe(container)` → `toHaveNoViolations()`. Surfaces: catalog list, product detail, the order/checkout surfaces (the checkout form + pre-payment + confirmation presentational pieces), and the static pages (`/jak-to-funguje`, `/pro-makery`, `/vop`, `/gdpr`). Where a page is a Server Component that fetches, the test renders the **presentational child component** with seeded props (no network in jsdom) — the a11y of the rendered markup is what axe checks.
- **SEO predicate tests (Q-0031 retroactive):** pin `canonicalUrl(path)` from `lib/seo/site-url.ts` (leading-slash join, no double slash, absolute against `SITE_URL`) + any other pure SEO/format predicate T-0131 left untested (e.g. the sitemap static-route list shape, `SITE_URL` default resolution). These are the 6-ish SEO unit tests Q-0031 named.
- **axe-in-CI:** add a **frontend test step** to `.github/workflows/ci.yml` in the existing `frontend` job — `npm run test` after `Build` (or as a sibling step). A11y regressions then fail CI going forward. No new job; reuse the `frontend` job's `npm ci` + node setup.
- **a11y fixes are small + noted:** any violation axe surfaces gets fixed in the same PR (contrast, label, focus, alt) and listed in the PR description + this ticket's status log. No large refactors; if a fix is non-trivial it becomes a follow-up ticket and the axe test for that exact rule is scoped down with a `// TODO(T-NNNN)` note rather than weakening the AA assertion globally.
- **i18n preserved:** a11y fixes must not drop/rename `cs-CZ` keys (T8 gate). New visually-hidden strings → new `a11y.*` cs-CZ keys.
- **Manual checklist doc:** `docs/test-plans/a11y-manual-checklist.md` — keyboard-nav rows + NVDA/Firefox Czech screen-reader rows per critical path (ADR 0023 §5). The RUN is the `manual_step`.
- **No backend change, no NSwag regen, no new `BusinessErrorMessage` codes, no migration.**

## Scope

### Frontend harness (devDeps + config)

- **`frontend/package.json`** — add `devDependencies`: `vitest`, `@vitejs/plugin-react`, `@testing-library/react`, `@testing-library/jest-dom`, `jest-axe`, `@types/jest-axe`, `jsdom`. Add `"test": "vitest run"` + `"test:watch": "vitest"` scripts. **Real lockfile update** (`package-lock.json` regenerated by `npm install`) committed in the same PR.
- **`frontend/vitest.config.ts`** — NEW. `environment: 'jsdom'`, `globals: true`, `setupFiles: ['./vitest.setup.ts']`, `exclude: [...configDefaults.exclude, 'src/lib/api-client/**']`, `coverage.exclude` likewise drops the generated client. React plugin wired for `.tsx`.
- **`frontend/vitest.setup.ts`** — NEW. Imports `@testing-library/jest-dom`; `expect.extend(toHaveNoViolations)` from `jest-axe`.

### axe-core a11y tests (critical customer paths, WCAG AA)

- **`frontend/src/app/(public)/katalog/__tests__/catalog-a11y.test.tsx`** — render the catalog list presentational component with seeded maker rows; `await axe(container)` → zero violations.
- **`frontend/src/app/(public)/produkt/__tests__/product-a11y.test.tsx`** — product detail presentational subtree with a seeded product; zero violations.
- **Order/checkout a11y tests** — the checkout form (`/objednavka`), the pre-payment surface (`/objednavka/[id]`), and the confirmation surface presentational pieces, each with seeded props; zero violations. (Client components; render in jsdom with mocked client helpers — no network.)
- **Static-page a11y tests** — `/jak-to-funguje`, `/pro-makery`, `/vop`, `/gdpr` presentational content; zero violations (these are mostly static prose + the placeholder `Alert` — a clean axe pass is cheap insurance the headings/landmarks are right).

### SEO predicate tests (Q-0031 retroactive unblock of T-0131)

- **`frontend/src/lib/seo/__tests__/site-url.test.ts`** — pin `canonicalUrl(path)` (leading-slash join; no double slash; absolute against `SITE_URL`); pin `SITE_URL` default (`https://makables.cz` when env unset in production-mode; localhost only in dev). Pin the sitemap static-route list shape if `sitemap.ts` exposes a testable pure helper; otherwise note it. ~6 assertions (the T-0131 SEO unit tests Q-0031 named).

### axe-in-CI

- **`.github/workflows/ci.yml`** — in the existing `frontend` job, add a `Test (vitest a11y + SEO)` step running `npm run test` (after `Install` + `Typecheck`/`Lint`/`Build`, reusing the same `npm ci` + node 20 setup). A11y regressions fail CI from here on.

### Manual a11y checklist (the RUN is a manual_step)

- **`docs/test-plans/a11y-manual-checklist.md`** — NEW. Per ADR 0023 §5:
  - **Keyboard nav** rows: tab order, visible focus on every interactive element, no keyboard trap, skip-to-content, Enter/Space activation, Esc closes modals (Packeta widget), focus return on modal close — per critical path (catalog → product → checkout → confirmation; static pages; auth forms).
  - **NVDA + Firefox (Czech)** rows: page title announced, landmark/heading structure read in order, form labels + error messages announced (`aria-describedby`), buttons/links have accessible names in Czech, dynamic state changes announced (e.g. payment "verifying" poll). 
  - Contrast spot-check rows (4.5:1 body / 3:1 large) for the brand dark theme.
  - The RUN (human + NVDA) is the `manual_step` — pre-launch, QA actor.

### a11y fixes (small, noted)

- Whatever the axe tests surface — contrast token bump, missing `<label htmlFor>` / `aria-label`, focus-order fix, `alt` text, `aria-describedby` wiring on form errors. Each listed in the PR + this ticket's status log. New visually-hidden strings → `a11y.*` cs-CZ keys (T8 preserved).

## Out of scope

- **k6 load test** — T-0132 owns the k6 script + the staging RUN. Same bundle PR, separate ticket.
- **E2E / Playwright** — ADR 0023 §6 defers E2E to post-MVP. T-0133 is component + a11y + predicate tests only.
- **Visual regression** — ADR 0023 §6 excludes it from MVP.
- **60% coverage as a merge gate** — ADR 0023 §6 names 60% as a target; the merge gate at MVP is "axe zero-violation on critical paths + pinned predicates pass." Coverage grows per-ticket.
- **Maker/admin dashboard AA perfection** — ADR 0023 §5 accepts one-off issues in maker/admin data tables/complex forms as backlog. Critical CUSTOMER paths are the AA-enforced surface; maker/admin axe tests are welcome but not gating.
- **The manual NVDA RUN execution** — it is the `manual_step` (pre-launch, human + screen reader); the checklist doc ships now, the RUN is gated.
- **Backend tests / NSwag / migrations** — none. Frontend + CI + docs only.

## Acceptance criteria

- **AC-1** Given the harness is installed, when `npm run test` runs in `frontend/`, then vitest executes with the jsdom environment, `@testing-library/jest-dom` + `jest-axe` matchers registered, and `src/lib/api-client/**` excluded from the run. `package.json` carries the new devDeps + the `"test"` script; `package-lock.json` is updated in the same commit.
- **AC-2** Given the catalog list, product detail, the order/checkout surfaces, and the four static pages, when their a11y tests render the presentational markup and call `await axe(container)`, then each asserts `toHaveNoViolations()` against WCAG 2.1 AA — all green.
- **AC-3** Given any automated axe violation surfaced during AC-2, when the PR lands, then it is fixed (contrast/label/focus/alt/aria-describedby) and the fix is listed in this ticket's status log + the PR description; no AA assertion is weakened to pass.
- **AC-4** Given the SEO predicate tests, when they run, then `canonicalUrl(path)` is pinned (leading-slash join, no double slash, absolute against `SITE_URL`) and `SITE_URL` default resolution is pinned — the Q-0031 retroactive T-0131 unblock is satisfied (~6 SEO assertions).
- **AC-5** Given `.github/workflows/ci.yml`, when CI runs on a PR, then the `frontend` job runs `npm run test` and **fails the build on any a11y or predicate test failure** going forward.
- **AC-6** Given `docs/test-plans/a11y-manual-checklist.md`, when inspected, then it lists keyboard-nav + NVDA/Firefox-Czech screen-reader rows per critical customer path per ADR 0023 §5, and the manual RUN is recorded as the ticket's `manual_step` (QA actor, pre-launch timing, read-only rollback).
- **AC-7** Given the full a11y fix set, when T8 (BusinessErrorMessage ↔ cs-CZ parity) and T9 run via `node scripts/check-consistency.mjs`, then both stay green — no `cs-CZ` key dropped or renamed; any new visually-hidden string is a new `a11y.*` key.
- **AC-8** Build clean: `npx tsc --noEmit` + `npx eslint src` + `npx next build` all green; `npm run test` green; `node scripts/check-consistency.mjs` exit 0 (no new T1–T9 findings). No edits to `frontend/src/lib/api-client/` (pre-commit hook enforces).

## Test plan reference

The harness IS the test plan for the automated surface (inline above). The manual surface is `docs/test-plans/a11y-manual-checklist.md` (the gated RUN).

## Status log

- 2026-06-21 `draft` by PM. Created as the a11y half of the quality-gates bundle (`feat/quality-gates-bundle`, T-0132 k6 + T-0133 a11y), user-locked 2026-06-21. Bundle B = one PR. Carries the Q-0031 answer: stand up vitest + @testing-library/react + jest-axe as the real frontend harness (devDeps + lockfile + `vitest.config.ts` + `"test"` script; api-client/ excluded). Scope: the harness + axe-core AA tests on catalog/product/checkout/static paths + the Q-0031-retroactive T-0131 SEO predicate tests + axe-in-CI (`ci.yml` frontend test step) + the manual NVDA/keyboard checklist (`docs/test-plans/a11y-manual-checklist.md`, the RUN gated as a manual_step). ADR 0023 §5/§6. No backend/NSwag/migration.
- 2026-06-21 `draft → ready` by PM. DoR confirmed: (1) not-duplicate — first FE test harness; no prior ticket stands one up (Q-0031 was the open gap). (2) observable AC — 8 ACs, all with measurable proof (test green, CI fail-on-regression, checklist rows, consistency exit 0). (3) sized M — harness + ~8 axe tests + ~6 SEO assertions + CI step + checklist + small noted fixes (4–16h). (4) depends_on — all the customer-facing FE pages this audits (`T-0046/0047/0048` catalog/product, `T-0084a/0084b/0085` checkout, `T-0086a/0086b` dashboards, `T-0130/0131` static+SEO) are `ready`/done on the bundle path; harness install itself has no code dependency. (5) manual_steps — populated (QA, pre-launch, the NVDA+keyboard RUN; read-only rollback). (6) security_touching: false — test harness + a11y markup fixes, no auth/payment/PII surface. (7) layers — `web-frontend` + `config` (CI workflow). User-locked decisions in §A: Q-0031 = vitest+jest-axe harness; axe asserts WCAG AA; the manual RUN is a manual_step. **Ready for nextjs-frontend.** Ships in the bundle PR with T-0132; T-0133 lands the harness first (T-0132's k6 script is standalone and order-independent).
