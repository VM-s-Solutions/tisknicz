'use client';

import { useSyncExternalStore } from 'react';
import { Icon, type IconName } from '@/components/ui/icon';
import { t, type MessageKey } from '@/lib/i18n';
import { nextThemePreference, type ThemePreference } from '@/lib/theme/theme';
import {
  readPreference,
  readServerPreference,
  subscribe,
  writePreference,
} from '@/lib/theme/theme-store';

const PRESENTATION: Record<ThemePreference, { icon: IconName; labelKey: MessageKey }> = {
  system: { icon: 'monitor', labelKey: 'theme.system' },
  light: { icon: 'sun', labelKey: 'theme.light' },
  dark: { icon: 'moon', labelKey: 'theme.dark' },
};

/**
 * Cycles system → light → dark. Three states rather than a two-way flip: a
 * plain light/dark switch has no way back to "follow the OS" once it is
 * touched, so a visitor who tries the toggle once could never return to the
 * default they arrived with.
 *
 * The applied theme lives in `data-theme` on `<html>`, written before first
 * paint by the bootstrap script in `app/layout.tsx`. This component only
 * *changes* it — it never owns the initial value, which is why there is no
 * flash and no server/client palette disagreement.
 */
export function ThemeToggle({ className = '' }: { className?: string }) {
  const preference = useSyncExternalStore(subscribe, readPreference, readServerPreference);

  const current = PRESENTATION[preference];
  const upcoming = PRESENTATION[nextThemePreference(preference)];

  return (
    <button
      type="button"
      onClick={() => writePreference(nextThemePreference(preference))}
      title={t('theme.toggle_title', { next: t(upcoming.labelKey) })}
      aria-label={t('theme.toggle_aria', {
        current: t(current.labelKey),
        next: t(upcoming.labelKey),
      })}
      className={`inline-flex h-8 w-8 shrink-0 items-center justify-center rounded-lg border border-zinc-700 text-zinc-300 transition-colors hover:border-zinc-600 hover:bg-zinc-800 hover:text-zinc-50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-400/60 ${className}`}
    >
      <Icon name={current.icon} size={15} strokeWidth={1.75} />
    </button>
  );
}
