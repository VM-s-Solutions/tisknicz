'use client';

import { openConsentSettings } from '@/lib/consent';
import { t } from '@/lib/i18n';

/**
 * "Cookie settings" entry point (T-0147). Reopens the consent banner's
 * customize view, pre-filled with the visitor's current choices
 * (AC-6). Used from the footer and the GDPR page's cookies section,
 * both of which promise a "nastavení souhlasu" mechanism.
 *
 * A Client Component because it dispatches a DOM event the banner
 * (also client-side) listens for — there is no server round-trip.
 */
export function CookieSettingsLink({ className = '' }: { readonly className?: string }) {
  return (
    <button
      type="button"
      onClick={() => openConsentSettings()}
      className={`text-sm text-zinc-300 transition-colors hover:text-zinc-50 ${className}`}
    >
      {t('cookieConsent.settingsLinkLabel')}
    </button>
  );
}
