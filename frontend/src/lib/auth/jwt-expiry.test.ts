import { describe, expect, it } from 'vitest';
import { decodeJwtPayload, isJwtExpiredOrInvalid } from './jwt-expiry';

function fakeJwt(payload: Record<string, unknown>): string {
  const body = Buffer.from(JSON.stringify(payload)).toString('base64url');
  return `eyJhbGciOiJIUzI1NiJ9.${body}.c2ln`;
}

describe('decodeJwtPayload', () => {
  it('decodes sub/email/exp from a base64url payload', () => {
    const token = fakeJwt({ sub: 'user-1', email: 'anna@example.cz', exp: 1_800_000_000 });
    expect(decodeJwtPayload(token)).toEqual({
      sub: 'user-1',
      email: 'anna@example.cz',
      exp: 1_800_000_000,
    });
  });

  it('decodes UTF-8 payload content (Czech diacritics)', () => {
    const token = fakeJwt({ sub: 'user-2', email: 'růžena@příklad.cz' });
    expect(decodeJwtPayload(token)?.email).toBe('růžena@příklad.cz');
  });

  it('returns null for garbage', () => {
    expect(decodeJwtPayload('not-a-jwt')).toBeNull();
    expect(decodeJwtPayload('a.%%%%.c')).toBeNull();
    expect(decodeJwtPayload('')).toBeNull();
  });
});

describe('isJwtExpiredOrInvalid', () => {
  it('is false for a token expiring beyond the skew window', () => {
    const token = fakeJwt({ exp: Math.floor(Date.now() / 1000) + 600 });
    expect(isJwtExpiredOrInvalid(token)).toBe(false);
  });

  it('is true for a token already expired', () => {
    const token = fakeJwt({ exp: Math.floor(Date.now() / 1000) - 60 });
    expect(isJwtExpiredOrInvalid(token)).toBe(true);
  });

  it('is true inside the skew window (expiring mid-request counts as expired)', () => {
    const token = fakeJwt({ exp: Math.floor(Date.now() / 1000) + 5 });
    expect(isJwtExpiredOrInvalid(token, 15_000)).toBe(true);
  });

  it('is true for garbage or a missing exp claim', () => {
    expect(isJwtExpiredOrInvalid('garbage')).toBe(true);
    expect(isJwtExpiredOrInvalid(fakeJwt({ sub: 'x' }))).toBe(true);
  });
});
