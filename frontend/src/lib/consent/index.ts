/**
 * Public surface of the cookie-consent module (T-0147). Any future
 * script-loading code should import `hasConsent` (or the
 * `ConsentGate` component, re-exported here for convenience) from
 * `@/lib/consent` rather than reaching into `./storage` or `./types`
 * directly.
 */
export { ConsentGate } from '@/components/shared/consent-gate';
export { evaluateConsent, hasConsent } from './consent';
export {
  CONSENT_CHANGE_EVENT,
  OPEN_CONSENT_SETTINGS_EVENT,
  openConsentSettings,
  readStoredConsent,
  writeStoredConsent,
} from './storage';
export {
  ACCEPT_ALL_CHOICES,
  CONSENT_VERSION,
  NECESSARY_ONLY_CHOICES,
} from './types';
export type {
  ConsentCategory,
  ConsentChoices,
  StoredConsent,
  ToggleableConsentCategory,
} from './types';
export { useStoredConsent } from './use-stored-consent';
