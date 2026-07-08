'use client';

import Link from 'next/link';
import { useEffect, useId, useState, useSyncExternalStore } from 'react';
import { Button } from '@/components/ui/button';
import {
  ACCEPT_ALL_CHOICES,
  CONSENT_VERSION,
  NECESSARY_ONLY_CHOICES,
  OPEN_CONSENT_SETTINGS_EVENT,
  type ConsentChoices,
  readStoredConsent,
  useStoredConsent,
  writeStoredConsent,
} from '@/lib/consent';
import { t } from '@/lib/i18n';

type BannerMode = 'summary' | 'customize' | 'closed';

function noopSubscribe(): () => void {
  return () => {};
}

/**
 * Standard hydration-safe "has the client finished mounting" check
 * (a `useSyncExternalStore` identity trick: server snapshot `false`,
 * client snapshot `true` — React reconciles the mismatch with a
 * client-only re-render after hydration, no `setState`-in-`useEffect`
 * needed).
 */
function useHasMounted(): boolean {
  return useSyncExternalStore(
    noopSubscribe,
    () => true,
    () => false,
  );
}

/**
 * Cookie consent popup + customize view (T-0147, dopady §2.8).
 *
 * Mounted once from the root layout so it renders across every route
 * group (AC-1). Rendered as a non-blocking popup anchored to the
 * bottom of the viewport — no backdrop, no scroll lock, no focus
 * trap — so the page stays fully usable while the choice is pending.
 * First visit (no valid stored choice) shows the "summary" view
 * offering "Pouze nezbytné" / "Přijmout vše" / a "Nastavit předvolby"
 * expand into the "customize" view with individual analytics/marketing
 * toggles (`necessary` is always-on, not a toggle). Once a choice is
 * made it is persisted first-party (`writeStoredConsent`) and the
 * popup does not reappear (AC-2/AC-3) until `CONSENT_VERSION` is
 * bumped or storage is cleared.
 *
 * The `CookieSettingsLink` (footer / GDPR page) dispatches
 * `OPEN_CONSENT_SETTINGS_EVENT`, which reopens this component's
 * customize view pre-filled with the current choices (AC-6), even
 * after the initial choice has been made.
 *
 * A Client Component: the popup is inherently interactive (buttons,
 * toggles) — mounting it from the (Server Component) root layout
 * keeps the rest of the tree server-rendered.
 */
export function CookieConsentBanner() {
  const stored = useStoredConsent();
  const alreadyDecided = stored !== null && stored.version === CONSENT_VERSION;

  // Consent lives in client-only storage; the server (and the very
  // first client render, per `useStoredConsent`'s server snapshot)
  // has no way to know a returning visitor already decided. Gating on
  // a post-mount flag — rather than rendering the popup on the
  // server and hiding it after hydration — avoids a visible flash
  // for every returning visitor on every page load.
  const mounted = useHasMounted();

  const [manualMode, setManualMode] = useState<BannerMode | null>(null);
  const [analyticsChecked, setAnalyticsChecked] = useState(false);
  const [marketingChecked, setMarketingChecked] = useState(false);

  const mode: BannerMode = manualMode ?? (alreadyDecided ? 'closed' : 'summary');
  const dismissable = alreadyDecided;

  const titleId = useId();
  const descriptionId = useId();

  function openCustomize() {
    // Read fresh rather than closing over the `stored` value from
    // this render — the window-event listener below subscribes once
    // (`[]` deps) and must still see the latest choice if it fires
    // long after mount (e.g. reopened via the footer's settings link).
    const current = readStoredConsent();
    setAnalyticsChecked(current?.choices.analytics ?? false);
    setMarketingChecked(current?.choices.marketing ?? false);
    setManualMode('customize');
  }

  function close() {
    setManualMode('closed');
  }

  function acceptAll() {
    writeStoredConsent(ACCEPT_ALL_CHOICES);
    close();
  }

  function necessaryOnly() {
    writeStoredConsent(NECESSARY_ONLY_CHOICES);
    close();
  }

  function saveCustomChoices() {
    const choices: ConsentChoices = { analytics: analyticsChecked, marketing: marketingChecked };
    writeStoredConsent(choices);
    close();
  }

  function backOrClose() {
    if (dismissable) {
      close();
    } else {
      setManualMode('summary');
    }
  }

  // Reopen from the footer / GDPR page "cookie settings" entry point,
  // pre-filled with the current stored choices (AC-6).
  useEffect(() => {
    function onOpenSettings() {
      openCustomize();
    }
    window.addEventListener(OPEN_CONSENT_SETTINGS_EVENT, onOpenSettings);
    return () => window.removeEventListener(OPEN_CONSENT_SETTINGS_EVENT, onOpenSettings);
  }, []);

  // Escape dismisses the popup once a choice already exists
  // (first-visit stays open until a choice is made).
  useEffect(() => {
    if (!mounted || mode === 'closed' || !dismissable) return;

    function onKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') close();
    }

    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [mounted, mode, dismissable]);

  if (!mounted || mode === 'closed') {
    return null;
  }

  return (
    <div className="pointer-events-none fixed inset-x-0 bottom-0 z-50 px-4 pb-4 sm:px-6 sm:pb-6">
      <div
        role="dialog"
        aria-labelledby={titleId}
        aria-describedby={descriptionId}
        className="glass pointer-events-auto mx-auto w-full max-w-3xl rounded-2xl border border-zinc-800 p-5 shadow-2xl motion-safe:animate-slide-up sm:p-6"
      >
        {mode === 'summary' ? (
          <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:gap-6">
            <div className="min-w-0 flex-1">
              <h2 id={titleId} className="text-sm font-semibold text-white">
                {t('cookieConsent.title')}
              </h2>
              <p id={descriptionId} className="mt-1 text-xs leading-relaxed text-zinc-400">
                {t('cookieConsent.description')}{' '}
                <Link href="/gdpr" className="underline hover:text-white">
                  {t('cookieConsent.privacyLinkText')}
                </Link>
                .
              </p>
            </div>

            <div className="flex shrink-0 flex-col-reverse gap-2 sm:flex-row sm:items-center">
              <Button type="button" variant="ghost" size="sm" onClick={openCustomize}>
                {t('cookieConsent.customize')}
              </Button>
              <Button type="button" variant="outline" size="sm" onClick={necessaryOnly}>
                {t('cookieConsent.necessaryOnly')}
              </Button>
              <Button type="button" variant="primary" size="sm" onClick={acceptAll}>
                {t('cookieConsent.acceptAll')}
              </Button>
            </div>
          </div>
        ) : (
          <div className="flex flex-col gap-4">
            <div>
              <h2 id={titleId} className="text-sm font-semibold text-white">
                {t('cookieConsent.customizeTitle')}
              </h2>
              <p id={descriptionId} className="mt-1 text-xs leading-relaxed text-zinc-400">
                {t('cookieConsent.customizeDescription')}
              </p>
            </div>

            <div className="flex flex-col gap-2">
              <ConsentCategoryRow
                label={t('cookieConsent.categoryNecessaryLabel')}
                description={t('cookieConsent.categoryNecessaryDescription')}
                checked
                alwaysOn
              />
              <ConsentCategoryRow
                label={t('cookieConsent.categoryAnalyticsLabel')}
                description={t('cookieConsent.categoryAnalyticsDescription')}
                checked={analyticsChecked}
                onChange={setAnalyticsChecked}
              />
              <ConsentCategoryRow
                label={t('cookieConsent.categoryMarketingLabel')}
                description={t('cookieConsent.categoryMarketingDescription')}
                checked={marketingChecked}
                onChange={setMarketingChecked}
              />
            </div>

            <div className="flex flex-col-reverse gap-2 sm:flex-row sm:items-center sm:justify-end">
              <Button type="button" variant="ghost" size="sm" onClick={backOrClose}>
                {dismissable ? t('cookieConsent.close') : t('cookieConsent.back')}
              </Button>
              <Button type="button" variant="primary" size="sm" onClick={saveCustomChoices}>
                {t('cookieConsent.save')}
              </Button>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}

function ConsentCategoryRow({
  label,
  description,
  checked,
  alwaysOn = false,
  onChange,
}: {
  readonly label: string;
  readonly description: string;
  readonly checked: boolean;
  readonly alwaysOn?: boolean;
  readonly onChange?: (checked: boolean) => void;
}) {
  return (
    <div className="flex items-start justify-between gap-4 border-b border-zinc-800/60 pb-2 last:border-b-0 last:pb-0">
      <div className="flex flex-col gap-0.5">
        <span className="text-xs font-semibold text-white">{label}</span>
        <span className="text-xs leading-relaxed text-zinc-500">{description}</span>
      </div>
      {alwaysOn ? (
        <span className="shrink-0 rounded-full border border-zinc-700 px-3 py-1 text-xs font-medium text-zinc-400">
          {t('cookieConsent.alwaysOn')}
        </span>
      ) : (
        <label className="flex shrink-0 items-center gap-2 text-sm text-zinc-200">
          <span className="sr-only">{label}</span>
          <input
            type="checkbox"
            checked={checked}
            onChange={(event) => onChange?.(event.target.checked)}
            className="h-4 w-4 rounded border-zinc-700 bg-zinc-900 text-brand-400 focus:ring-brand-400/40"
          />
        </label>
      )}
    </div>
  );
}
