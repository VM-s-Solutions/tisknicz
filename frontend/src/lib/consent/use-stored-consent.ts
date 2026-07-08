'use client';

import { useSyncExternalStore } from 'react';
import { CONSENT_CHANGE_EVENT, readStoredConsent } from './storage';
import type { StoredConsent } from './types';

function subscribe(onChange: () => void): () => void {
  window.addEventListener(CONSENT_CHANGE_EVENT, onChange);
  return () => window.removeEventListener(CONSENT_CHANGE_EVENT, onChange);
}

function getServerSnapshot(): StoredConsent | null {
  return null;
}

/**
 * Reactive read of the stored consent record. Used by `ConsentGate`
 * and the banner so they re-render immediately when the visitor makes
 * or changes a choice (AC-6), without polling or a `useEffect`
 * data-fetch. `useSyncExternalStore` keeps the server-rendered and
 * first client-rendered output identical (both read `null`), avoiding
 * a hydration mismatch.
 */
export function useStoredConsent(): StoredConsent | null {
  return useSyncExternalStore(subscribe, readStoredConsent, getServerSnapshot);
}
