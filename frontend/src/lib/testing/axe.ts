import { configureAxe } from 'jest-axe';

/**
 * Shared axe runner pinned to **WCAG 2.1 Level AA** (T-0133, ADR 0023
 * §5). Every a11y test imports `axeAA` so the AA target is explicit and
 * uniform rather than relying on axe's default ruleset: we restrict the
 * active rules to the `wcag2a`, `wcag2aa`, `wcag21a`, and `wcag21aa`
 * tags. This is the merge-gate ruleset — a regression on any AA rule
 * fails CI.
 *
 * NOTE: the `color-contrast` rule cannot evaluate in jsdom (Tailwind
 * utility classes are not applied to a computed layout, so axe sees no
 * resolved colours). Contrast (4.5:1 body / 3:1 large per ADR 0023 §5)
 * is therefore verified in the MANUAL checklist
 * (`docs/test-plans/a11y-manual-checklist.md`), not here. The automated
 * AA gate covers structure: names, roles, labels, landmarks, heading
 * order, ARIA validity, focusable-content rules.
 *
 * This module lives under `src/lib/testing/` (not `api-client/`, which
 * the vitest config excludes) and is only imported by `*.test.tsx`
 * files, so it ships zero bytes to the production bundle.
 */
export const axeAA = configureAxe({
  runOnly: {
    type: 'tag',
    values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'],
  },
});
