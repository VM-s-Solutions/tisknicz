import { afterEach, describe, expect, it } from 'vitest';
import { evaluateConsent, hasConsent } from '../consent';
import { readStoredConsent, writeStoredConsent } from '../storage';
import { ACCEPT_ALL_CHOICES, CONSENT_VERSION, NECESSARY_ONLY_CHOICES } from '../types';

/**
 * Pure-logic tests for the consent gating primitive (T-0147, AC-5).
 * The default MUST be fail-closed (`false`) until an explicit choice
 * at the current `CONSENT_VERSION` has been persisted.
 */
function clearConsentCookie() {
  document.cookie = 'makables_cookie_consent=; path=/; max-age=0';
}

describe('consent module', () => {
  afterEach(() => {
    clearConsentCookie();
  });

  it('hasConsent("necessary") is always true, with or without a stored choice', () => {
    expect(hasConsent('necessary')).toBe(true);
    writeStoredConsent(NECESSARY_ONLY_CHOICES);
    expect(hasConsent('necessary')).toBe(true);
  });

  it('AC-5: hasConsent("analytics"/"marketing") default to false before any choice is made', () => {
    expect(readStoredConsent()).toBeNull();
    expect(hasConsent('analytics')).toBe(false);
    expect(hasConsent('marketing')).toBe(false);
  });

  it('hasConsent reflects a persisted "necessary only" choice', () => {
    writeStoredConsent(NECESSARY_ONLY_CHOICES);
    expect(hasConsent('analytics')).toBe(false);
    expect(hasConsent('marketing')).toBe(false);
  });

  it('hasConsent reflects a persisted "accept all" choice', () => {
    writeStoredConsent(ACCEPT_ALL_CHOICES);
    expect(hasConsent('analytics')).toBe(true);
    expect(hasConsent('marketing')).toBe(true);
  });

  it('hasConsent reflects an exact custom combination (analytics only)', () => {
    writeStoredConsent({ analytics: true, marketing: false });
    expect(hasConsent('analytics')).toBe(true);
    expect(hasConsent('marketing')).toBe(false);
  });

  it('evaluateConsent treats a stale-version stored record as no choice made (fail-closed)', () => {
    const stale = {
      version: CONSENT_VERSION - 1,
      updatedAt: new Date().toISOString(),
      choices: { analytics: true, marketing: true },
    };
    expect(evaluateConsent('analytics', stale)).toBe(false);
    expect(evaluateConsent('marketing', stale)).toBe(false);
    expect(evaluateConsent('necessary', stale)).toBe(true);
  });

  it('evaluateConsent treats a null stored record as no choice made', () => {
    expect(evaluateConsent('analytics', null)).toBe(false);
  });

  it('writeStoredConsent persists a record at the current CONSENT_VERSION with a timestamp', () => {
    const before = Date.now();
    const record = writeStoredConsent(ACCEPT_ALL_CHOICES);
    expect(record.version).toBe(CONSENT_VERSION);
    expect(new Date(record.updatedAt).getTime()).toBeGreaterThanOrEqual(before);
    expect(readStoredConsent()).toEqual(record);
  });

  it('readStoredConsent ignores malformed JSON in storage', () => {
    document.cookie = 'makables_cookie_consent=not-json; path=/';
    expect(readStoredConsent()).toBeNull();
    expect(hasConsent('analytics')).toBe(false);
  });
});
