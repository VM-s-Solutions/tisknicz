import { describe, expect, it } from 'vitest';
import { catalogEmptyKind } from '../page';

/**
 * T-0170 (audit PUB-H3): an empty item list is three different
 * situations — a stale out-of-range page, a filter matching nothing,
 * and a genuinely empty catalog. The old UI showed "no makers match
 * your filter + clear filters" for all three.
 */
describe('catalogEmptyKind', () => {
  it('is none while items render', () => {
    expect(
      catalogEmptyKind({ itemCount: 3, totalCount: 40, hasActiveFilters: false }),
    ).toBe('none');
  });

  it('flags an out-of-range page when results exist but this page is empty', () => {
    expect(
      catalogEmptyKind({ itemCount: 0, totalCount: 40, hasActiveFilters: false }),
    ).toBe('out_of_range');
    expect(
      catalogEmptyKind({ itemCount: 0, totalCount: 40, hasActiveFilters: true }),
    ).toBe('out_of_range');
  });

  it('distinguishes filtered-to-zero from a genuinely empty catalog', () => {
    expect(
      catalogEmptyKind({ itemCount: 0, totalCount: 0, hasActiveFilters: true }),
    ).toBe('filtered');
    expect(
      catalogEmptyKind({ itemCount: 0, totalCount: 0, hasActiveFilters: false }),
    ).toBe('no_makers');
  });
});
