'use client';

import Link from 'next/link';
import { useEffect, useId, useRef, useState, useSyncExternalStore } from 'react';
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

const FOCUSABLE_SELECTOR =
  'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

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
 * Cookie consent banner + customize view (T-0147, dopady §2.8).
 *
 * Mounted once from the root layout so it renders across every route
 * group (AC-1). First visit (no valid stored choice) shows the
 * "summary" view offering "Pouze nezbytné" / "Přijmout vše" / a
 * "Nastavit předvolby" expand into the "customize" view with
 * individual analytics/marketing toggles (`necessary` is always-on,
 * not a toggle). Once a choice is made it is persisted first-party
 * (`writeStoredConsent`) and the banner does not reappear (AC-2/AC-3)
 * until `CONSENT_VERSION` is bumped or storage is cleared.
 *
 * The `CookieSettingsLink` (footer / GDPR page) dispatches
 * `OPEN_CONSENT_SETTINGS_EVENT`, which reopens this component's
 * customize view pre-filled with the current choices (AC-6), even
 * after the initial choice has been made.
 *
 * A Client Component: the banner is inherently interactive (buttons,
 * toggles, a focus trap) — mounting it from the (Server Component)
 * root layout keeps the rest of the tree server-rendered.
 */
export function CookieConsentBanner() {
  const stored = useStoredConsent();
  const alreadyDecided = stored !== null && stored.version === CONSENT_VERSION;

  // Consent lives in client-only storage; the server (and the very
  // first client render, per `useStoredConsent`'s server snapshot)
  // has no way to know a returning visitor already decided. Gating on
  // a post-mount flag — rather than rendering the banner on the
  // server and hiding it after hydration — avoids a visible flash of
  // the dialog for every returning visitor on every page load.
  const mounted = useHasMounted();

  const [manualMode, setManualMode] = useState<BannerMode | null>(null);
  const [analyticsChecked, setAnalyticsChecked] = useState(false);
  const [marketingChecked, setMarketingChecked] = useState(false);

  const mode: BannerMode = manualMode ?? (alreadyDecided ? 'closed' : 'summary');
  const dismissable = alreadyDecided;

  const containerRef = useRef<HTMLDivElement>(null);
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

  // Body scroll lock + keyboard focus trap while the banner is open
  // (AC-8): Tab/Shift+Tab cycle within the panel, Escape closes it
  // only once a choice already exists (first-visit cannot be
  // dismissed without a choice).
  useEffect(() => {
    if (!mounted || mode === 'closed') return;

    const previouslyFocused = document.activeElement as HTMLElement | null;
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';

    const focusables = () =>
      containerRef.current
        ? Array.from(containerRef.current.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR))
        : [];

    focusables()[0]?.focus();

    function onKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        if (dismissable) close();
        return;
      }
      if (event.key !== 'Tab') return;

      const nodes = focusables();
      if (nodes.length === 0) return;
      const firstEl = nodes[0];
      const lastEl = nodes[nodes.length - 1];

      if (event.shiftKey && document.activeElement === firstEl) {
        event.preventDefault();
        lastEl.focus();
      } else if (!event.shiftKey && document.activeElement === lastEl) {
        event.preventDefault();
        firstEl.focus();
      }
    }

    window.addEventListener('keydown', onKeyDown);
    return () => {
      document.body.style.overflow = previousOverflow;
      window.removeEventListener('keydown', onKeyDown);
      previouslyFocused?.focus();
    };
  }, [mounted, mode, dismissable]);

  if (!mounted || mode === 'closed') {
    return null;
  }

  return (
    <div className="fixed inset-0 z-50 flex items-end justify-center p-4 sm:items-center">
      <div
        aria-hidden="true"
        onClick={() => {
          if (dismissable) close();
        }}
        className="absolute inset-0 bg-black/70"
      />

      <div
        ref={containerRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        aria-describedby={descriptionId}
        className="relative z-10 w-full max-w-lg rounded-2xl border border-zinc-800 bg-surface-card p-6 shadow-2xl"
      >
        {mode === 'summary' ? (
          <div className="flex flex-col gap-4">
            <h2 id={titleId} className="text-lg font-semibold text-white">
              {t('cookieConsent.title')}
            </h2>
            <p id={descriptionId} className="text-sm leading-relaxed text-zinc-300">
              {t('cookieConsent.description')}{' '}
              <Link href="/gdpr" className="underline hover:text-white">
                {t('cookieConsent.privacyLinkText')}
              </Link>
              .
            </p>

            <div className="flex flex-col-reverse gap-3 sm:flex-row sm:flex-wrap sm:items-center sm:justify-end">
              <Button type="button" variant="ghost" onClick={openCustomize}>
                {t('cookieConsent.customize')}
              </Button>
              <Button type="button" variant="outline" onClick={necessaryOnly}>
                {t('cookieConsent.necessaryOnly')}
              </Button>
              <Button type="button" variant="primary" onClick={acceptAll}>
                {t('cookieConsent.acceptAll')}
              </Button>
            </div>
          </div>
        ) : (
          <div className="flex flex-col gap-4">
            <h2 id={titleId} className="text-lg font-semibold text-white">
              {t('cookieConsent.customizeTitle')}
            </h2>
            <p id={descriptionId} className="text-sm leading-relaxed text-zinc-300">
              {t('cookieConsent.customizeDescription')}
            </p>

            <div className="flex flex-col gap-3">
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

            <div className="flex flex-col-reverse gap-3 sm:flex-row sm:items-center sm:justify-end">
              <Button type="button" variant="ghost" onClick={backOrClose}>
                {dismissable ? t('cookieConsent.close') : t('cookieConsent.back')}
              </Button>
              <Button type="button" variant="primary" onClick={saveCustomChoices}>
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
    <div className="flex items-start justify-between gap-4 rounded-xl border border-zinc-800 bg-surface-secondary p-4">
      <div className="flex flex-col gap-1">
        <span className="text-sm font-semibold text-white">{label}</span>
        <span className="text-xs leading-relaxed text-zinc-400">{description}</span>
      </div>
      {alwaysOn ? (
        <span className="shrink-0 rounded-full border border-zinc-700 bg-zinc-800 px-3 py-1 text-xs font-medium text-zinc-300">
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
