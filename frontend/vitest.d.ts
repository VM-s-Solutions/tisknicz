/**
 * Type augmentation wiring the `jest-axe` `toHaveNoViolations` matcher
 * into vitest's `expect` (T-0133).
 *
 * `@types/jest-axe` ships its matcher declaration against jest's global
 * `expect`, not vitest's. This module re-declares the same matcher on
 * vitest's `Assertion`/`AsymmetricMatchersContaining` interfaces so
 * `expect(await axe(container)).toHaveNoViolations()` type-checks under
 * `tsc --noEmit` without importing jest's globals. The runtime matcher is
 * registered in `vitest.setup.ts` via `expect.extend(toHaveNoViolations)`.
 */
import 'vitest';

interface AxeMatchers<R = unknown> {
  toHaveNoViolations(): R;
}

declare module 'vitest' {
  // eslint-disable-next-line @typescript-eslint/no-empty-object-type
  interface Assertion<T = unknown> extends AxeMatchers<T> {}
  // eslint-disable-next-line @typescript-eslint/no-empty-object-type
  interface AsymmetricMatchersContaining extends AxeMatchers {}
}
