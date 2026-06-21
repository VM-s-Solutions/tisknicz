/**
 * Global vitest setup (T-0133 / Q-0031). Loaded once per test file via
 * `vitest.config.ts` `setupFiles`.
 *
 * - `@testing-library/jest-dom` adds DOM-aware matchers
 *   (`toBeInTheDocument`, `toHaveAttribute`, ...).
 * - `jest-axe`'s `toHaveNoViolations` is the matcher every a11y test
 *   asserts against (`expect(await axe(container)).toHaveNoViolations()`),
 *   enforcing zero WCAG 2.1 AA violations per ADR 0023 §5.
 */
import '@testing-library/jest-dom/vitest';
import { toHaveNoViolations } from 'jest-axe';
import { expect } from 'vitest';

expect.extend(toHaveNoViolations);
