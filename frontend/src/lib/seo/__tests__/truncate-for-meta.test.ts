import { describe, expect, it } from 'vitest';
import { truncateForMeta } from '../truncate-for-meta';

/**
 * SEO predicate tests (T-0133 / Q-0031 retroactive unblock of T-0131).
 *
 * `truncateForMeta` is the shared `<meta name="description">` slicer used
 * by `generateMetadata` across the public route group (maker profile,
 * product detail). Pure string logic — pin the whitespace collapse, the
 * pass-through under the limit, the word-boundary cut, and the ellipsis.
 */
describe('truncateForMeta', () => {
  it('collapses internal whitespace runs and trims', () => {
    expect(truncateForMeta('  hello   world \n\t foo  ')).toBe('hello world foo');
  });

  it('returns the input unchanged when within the limit', () => {
    const short = 'Krátký popis produktu.';
    expect(truncateForMeta(short, 160)).toBe(short);
  });

  it('cuts back to the last word boundary and appends an ellipsis', () => {
    const text = 'alpha beta gamma delta epsilon zeta';
    const out = truncateForMeta(text, 20);
    expect(out.endsWith('…')).toBe(true);
    expect(out.length).toBeLessThanOrEqual(21);
    // The cut lands on a word boundary, so no partial trailing word.
    expect(out).toBe('alpha beta gamma…');
  });

  it('hard-slices when no acceptable word boundary exists within the budget', () => {
    const longToken = 'a'.repeat(50);
    const out = truncateForMeta(longToken, 10);
    expect(out).toBe('aaaaaaaaaa…');
  });
});
