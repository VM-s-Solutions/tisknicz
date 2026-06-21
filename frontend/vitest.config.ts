import { fileURLToPath } from 'node:url';
import react from '@vitejs/plugin-react';
import { configDefaults, defineConfig } from 'vitest/config';

/**
 * Vitest harness for the Makables frontend (T-0133 / Q-0031).
 *
 * This is the platform's first frontend test runner: the real harness the
 * build phase lacked, standing up the pure-logic + a11y test seam every
 * future frontend ticket builds on. ADR 0023 §6 names it explicitly:
 * "Component tests: vitest + Testing Library."
 *
 * - `environment: 'jsdom'` so React components render to a DOM that
 *   `@testing-library/react` + `jest-axe` can inspect (no browser, no
 *   network — components are rendered with seeded props).
 * - `globals: true` so `describe`/`it`/`expect` are available without an
 *   import, matching the Testing Library / jest-dom ergonomics.
 * - `setupFiles` registers the `@testing-library/jest-dom` matchers and
 *   `jest-axe`'s `toHaveNoViolations` matcher once for every test file.
 * - The generated NSwag client (`src/lib/api-client/**`) is excluded from
 *   the run AND from coverage — it is generated, never hand-edited
 *   (CLAUDE.md), so testing it is noise. Mirrors the consistency-linter
 *   `IGNORED_PATH_GLOBS`.
 * - `@/*` is resolved here to match the tsconfig path alias the app uses,
 *   so test imports read the same way as production imports.
 */
export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./vitest.setup.ts'],
    css: false,
    include: ['src/**/*.{test,spec}.{ts,tsx}'],
    exclude: [...configDefaults.exclude, 'src/lib/api-client/**'],
    coverage: {
      exclude: [
        ...(configDefaults.coverage.exclude ?? []),
        'src/lib/api-client/**',
        '**/*.config.*',
        'vitest.setup.ts',
      ],
    },
  },
});
