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

/**
 * jsdom implements no CSSOM media queries at all, so `window.matchMedia` is
 * simply absent — anything that reads a media query throws rather than
 * returning a sensible default. The theme store subscribes to
 * `(prefers-color-scheme: dark)` from the navbar, which put it in the path of
 * every layout test.
 *
 * Reports "does not match" and accepts listeners without ever firing them:
 * a test that cares about a specific query stubs `window.matchMedia` itself
 * (as `hero-scene-wrapper.test.tsx` does). Shim only when absent, so a future
 * jsdom that implements it wins.
 */
if (typeof window !== 'undefined' && typeof window.matchMedia !== 'function') {
  window.matchMedia = (query: string): MediaQueryList =>
    ({
      matches: false,
      media: query,
      onchange: null,
      addEventListener: () => {},
      removeEventListener: () => {},
      addListener: () => {},
      removeListener: () => {},
      dispatchEvent: () => false,
    }) as unknown as MediaQueryList;
}

/**
 * `window.localStorage` arrives here as a bare `{}` — Node 25 exposes its own
 * experimental `localStorage` global (hence the `--localstorage-file` warning
 * on every run) and what survives into the jsdom window implements none of
 * the Storage interface. Reading a preference therefore throws
 * "getItem is not a function" instead of returning null.
 *
 * Production code already treats a throwing Storage as "no preference" (see
 * `lib/theme/theme-store.ts`), so the app degrades correctly either way — but
 * a test cannot assert what was *persisted* without a working implementation.
 * In-memory, per-file, replaced only when the real thing is missing.
 */
if (typeof window !== 'undefined' && typeof window.localStorage?.getItem !== 'function') {
  const entries = new Map<string, string>();
  const storage: Storage = {
    get length() {
      return entries.size;
    },
    key: (index: number) => [...entries.keys()][index] ?? null,
    getItem: (key: string) => entries.get(key) ?? null,
    setItem: (key: string, value: string) => {
      entries.set(key, String(value));
    },
    removeItem: (key: string) => {
      entries.delete(key);
    },
    clear: () => {
      entries.clear();
    },
  };
  Object.defineProperty(window, 'localStorage', {
    configurable: true,
    value: storage,
  });
}
