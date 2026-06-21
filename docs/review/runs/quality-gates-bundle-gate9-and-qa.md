---
bundle: quality-gates-bundle
branch: feat/quality-gates-bundle
tickets: [T-0132, T-0133]
role: Tester (QA)
date: 2026-06-21
kind: gate-9 + QA verification audit note
---

# Quality-gates bundle — Gate 9 + QA verification

This bundle's deliverables **are** QA infrastructure (a vitest+jest-axe a11y
harness, a k6 load-test script, and two manual checklists). The test plans are
the harness itself, so this thin audit note records the gate run, the harness
result, and the gated pre-launch manual steps in lieu of per-ticket T-plans.

## Task 1 — Gate 9 (consistency linter)

- `node scripts/check-consistency.mjs` → **exit 0**, `check-consistency: clean
  (151 tracked)`. Matches expectation (151; bundle adds no T-row — the harness +
  k6 + docs introduce no new feature-file violations, and `deploy/load-tests/*.js`
  is outside the linter's candidate roots).
- **T8 (i18n parity) + T9 (unique-index translator): green** (the run is clean;
  both hard-fail checks passed). No cs-CZ key dropped; any a11y i18n additions
  are additive.
- `docs/audits/consistency-violations.md` **UNCHANGED by this bundle**: `git diff
  66bd766..feat/quality-gates-bundle -- docs/audits/consistency-violations.md` is
  empty. (The non-empty `git diff master -- …` is pre-existing base drift from
  already-merged bundles since the branch point — Admin/Payouts/Reviews/
  CountryConfigurations/Outbox rows — NOT introduced here.)

**Gate 9 verdict: PASS.**

## Task 2 — Harness + artifact verification

- `npm run test:run` → **GREEN: 7 files / 26 tests passed**, 2.82 s. (jsdom
  stderr noise — `HTMLCanvasElement.getContext` not implemented, a `LinkComponent`
  act() warning — is non-fatal; the `color-contrast` rule is intentionally
  deferred to the manual pass and excluded from the jsdom gate.)
- **a11y tests are real** (read product-a11y + checkout-a11y): they import real
  components (`ProductGallery`, `OrderSummary`), render with seeded data, and
  assert `expect(await axeAA(container)).toHaveNoViolations()`. `axeAA`
  (`src/lib/testing/axe.ts`) pins `runOnly` to `wcag2a/wcag2aa/wcag21a/wcag21aa`
  — **AA-tagged**, structural-only, the merge gate.
- **k6 script structurally valid** (`deploy/load-tests/makables-load.js`):
  `node --check` exit 0; thresholds (per-surface p95/p99 + `http_req_failed
  rate==0` + `checks rate==1`), two `ramping-vus` scenarios summing to 100 VUs
  (85 browse + 15 order) over a 30-min profile (2m ramp + 26m hold + 2m down),
  full `__ENV` config, custom `Trend`s tagged per surface. k6 binary NOT
  installed (expected — staging-only manual run, not CI).
- **Manual artifacts are CHECKLISTS, not results:**
  - `docs/test-plans/a11y-manual-checklist.md` — KB/SR/CT rows with empty
    `Actual`/`P/F` columns; `gate: pre-launch (NOT a merge gate)`; "runs once
    before launch, executed by QA with a screen reader." Does not claim execution.
  - `deploy/load-tests/README.md` — install/seed/configure/run/read-output
    instructions; "the execution is the gated manual step." Does not claim
    execution.

## Task 3 — Gated pre-launch manual RUNs (manual_step)

These two RUNs are the gated pre-launch items; the script + checklist ship now,
the execution is owner-tracked, both cross-referenced from
`docs/launch-checklist.md`:

1. **k6 30-min load run** (Ops/QA, staging only) — execute
   `makables-load.js` against seeded staging; PASS iff all thresholds green
   (catalog p95<400, product p95<350, order p95<600, zero 5xx, checks==1) **AND**
   out-of-band Postgres CPU < 70%. Cross-ref: launch-checklist Performance/Infra
   sections + `docs/runbooks/monitoring.md` / `backup-restore.md`.
2. **NVDA + Firefox (Czech) screen-reader pass + keyboard + contrast spot-check**
   (QA) — run `a11y-manual-checklist.md` against a deployed seeded build; any Fail
   → follow-up ticket, not a merge blocker.

## Gaps / notes

- No functional gap. The two jsdom stderr lines are environmental, not test
  failures (the suite is GREEN). The `color-contrast` AA leg is correctly
  excluded from the automated gate and routed to the live-page manual spot-check
  (CT-1..CT-6) — this is an honest coverage boundary, documented in `axe.ts` and
  the checklist, not a silent skip.
