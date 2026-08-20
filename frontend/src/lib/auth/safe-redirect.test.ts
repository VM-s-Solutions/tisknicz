import { describe, expect, it } from 'vitest';
import { safeRedirectTarget } from './safe-redirect';

/**
 * Open-redirect guard for `?redirect=`. Extracted from the two login
 * forms, so these cases pin the behaviour all three consumers now share.
 */
describe('safeRedirectTarget', () => {
  it('accepts path-only targets', () => {
    expect(safeRedirectTarget('/objednavka?productId=p1')).toBe('/objednavka?productId=p1');
    expect(safeRedirectTarget('/dashboard/maker/objednavky')).toBe('/dashboard/maker/objednavky');
    expect(safeRedirectTarget('/')).toBe('/');
  });

  it('rejects protocol-relative and absolute targets', () => {
    expect(safeRedirectTarget('//evil.example')).toBeNull();
    expect(safeRedirectTarget('/\\evil.example')).toBeNull();
    expect(safeRedirectTarget('https://evil.example')).toBeNull();
    expect(safeRedirectTarget('javascript:alert(1)')).toBeNull();
  });

  it('rejects a missing or empty value', () => {
    expect(safeRedirectTarget(null)).toBeNull();
    expect(safeRedirectTarget(undefined)).toBeNull();
    expect(safeRedirectTarget('')).toBeNull();
    expect(safeRedirectTarget('katalog')).toBeNull();
  });
});
