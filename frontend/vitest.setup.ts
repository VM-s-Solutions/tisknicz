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

/**
 * jsdom ships `AbortSignal` without the static `any` combinator that
 * every modern browser and Node 20+ provide. `lib/runtime/api-fetch.ts`
 * uses it to compose a caller's signal with its own timeout budget, so
 * without this shim any test that passes `signal` throws
 * "AbortSignal.any is not a function" in the harness while working fine
 * in production. Shim only when absent — a future jsdom that implements
 * it must win.
 */
if (typeof AbortSignal.any !== 'function') {
  AbortSignal.any = (signals: readonly AbortSignal[]): AbortSignal => {
    const controller = new AbortController();
    const alreadyAborted = signals.find((signal) => signal.aborted);
    if (alreadyAborted) {
      controller.abort(alreadyAborted.reason);
      return controller.signal;
    }
    for (const signal of signals) {
      signal.addEventListener('abort', () => controller.abort(signal.reason), {
        once: true,
        signal: controller.signal,
      });
    }
    return controller.signal;
  };
}
