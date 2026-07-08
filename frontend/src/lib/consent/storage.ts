import { CONSENT_VERSION, type ConsentChoices, type StoredConsent } from './types';

/**
 * First-party storage for the cookie-consent choice (T-0147). The
 * ticket leaves the storage mechanism unprescribed ("cookie or
 * localStorage, implementer's choice"); a plain first-party
 * `document.cookie` entry is used here — it is read/written only from
 * the client (the banner and `ConsentGate` are both Client Components)
 * and never needs a round-trip to the backend. A long `max-age` keeps
 * the choice for repeat visits; bumping `CONSENT_VERSION` (see
 * `./types.ts`) forces re-consent regardless of how long is left.
 */
const COOKIE_NAME = 'makables_cookie_consent';
const COOKIE_MAX_AGE_SECONDS = 60 * 60 * 24 * 365; // ~1 year

/**
 * Fired on `window` whenever the stored consent changes, so any
 * mounted `ConsentGate` / banner instance can react without polling.
 */
export const CONSENT_CHANGE_EVENT = 'makables:cookie-consent-changed';

/** Fired to ask the banner to reopen the customize view (footer link). */
export const OPEN_CONSENT_SETTINGS_EVENT = 'makables:open-cookie-settings';

function isConsentChoices(value: unknown): value is ConsentChoices {
  if (typeof value !== 'object' || value === null) return false;
  const record = value as Record<string, unknown>;
  return typeof record.analytics === 'boolean' && typeof record.marketing === 'boolean';
}

function isStoredConsent(value: unknown): value is StoredConsent {
  if (typeof value !== 'object' || value === null) return false;
  const record = value as Record<string, unknown>;
  return (
    typeof record.version === 'number' &&
    typeof record.updatedAt === 'string' &&
    isConsentChoices(record.choices)
  );
}

function readCookieValue(name: string): string | null {
  const cookies = document.cookie ? document.cookie.split('; ') : [];
  for (const entry of cookies) {
    const separatorIndex = entry.indexOf('=');
    if (separatorIndex === -1) continue;
    if (entry.slice(0, separatorIndex) === name) {
      return decodeURIComponent(entry.slice(separatorIndex + 1));
    }
  }
  return null;
}

// `useSyncExternalStore` (see `./use-stored-consent.ts`) requires
// `getSnapshot` to return a referentially stable value when nothing
// changed, or it re-renders in an infinite loop. `document.cookie`
// gives no change notification of its own, so this tiny cache keyed
// on the raw cookie string is what makes repeated `readStoredConsent`
// calls between actual writes return the *same* object reference.
let cachedRaw: string | null = null;
let cachedParsed: StoredConsent | null = null;

/**
 * Reads the current stored consent, or `null` if nothing is stored,
 * storage is unavailable (SSR), or the stored payload fails to parse.
 * Callers decide how to treat a version mismatch (see `hasConsent`);
 * this function returns the raw record as-is.
 */
export function readStoredConsent(): StoredConsent | null {
  if (typeof document === 'undefined') return null;

  const raw = readCookieValue(COOKIE_NAME);
  if (raw === cachedRaw) return cachedParsed;

  cachedRaw = raw;
  cachedParsed = null;

  if (raw) {
    try {
      const parsed: unknown = JSON.parse(raw);
      cachedParsed = isStoredConsent(parsed) ? parsed : null;
    } catch {
      cachedParsed = null;
    }
  }

  return cachedParsed;
}

/**
 * Persists a consent choice at the current `CONSENT_VERSION` and
 * notifies any mounted listeners (`CONSENT_CHANGE_EVENT`) so
 * `hasConsent()` reflects the new value on the very next call (AC-6).
 */
export function writeStoredConsent(choices: ConsentChoices): StoredConsent {
  const record: StoredConsent = {
    version: CONSENT_VERSION,
    updatedAt: new Date().toISOString(),
    choices,
  };

  if (typeof document !== 'undefined') {
    const value = encodeURIComponent(JSON.stringify(record));
    document.cookie = `${COOKIE_NAME}=${value}; path=/; max-age=${COOKIE_MAX_AGE_SECONDS}; SameSite=Lax`;
    window.dispatchEvent(new CustomEvent(CONSENT_CHANGE_EVENT));
  }

  return record;
}

/** Dispatches the "reopen consent settings" event (footer link). */
export function openConsentSettings(): void {
  if (typeof window === 'undefined') return;
  window.dispatchEvent(new CustomEvent(OPEN_CONSENT_SETTINGS_EVENT));
}
