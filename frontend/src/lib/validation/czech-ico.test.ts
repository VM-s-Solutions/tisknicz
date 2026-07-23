import { describe, expect, it } from 'vitest';
import { isValidCzechIco, normalizeIcoInput } from './czech-ico';

describe('isValidCzechIco', () => {
  it.each([
    '27074358', // Avast Software s.r.o. — the backend test fixture
    '00006947', // Ministerstvo financí
    '45274649', // ČEZ, a. s.
  ])('accepts real IČO %s', (ico) => {
    expect(isValidCzechIco(ico)).toBe(true);
  });

  it.each([
    '27074359', // checksum off by one
    '12345678', // checksum fails
    '00000000', // checksum fails (m=0 → expected 1)
    '2707435',  // 7 digits
    '270743580', // 9 digits
    '2707435a', // non-digit
    '',
  ])('rejects invalid input %s', (ico) => {
    expect(isValidCzechIco(ico)).toBe(false);
  });
});

describe('normalizeIcoInput', () => {
  it('strips non-digits and caps at 8', () => {
    expect(normalizeIcoInput('270 743 58')).toBe('27074358');
    expect(normalizeIcoInput('CZ27074358')).toBe('27074358');
    expect(normalizeIcoInput('123456789012')).toBe('12345678');
    expect(normalizeIcoInput('abc')).toBe('');
  });
});
