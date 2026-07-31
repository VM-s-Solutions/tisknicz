import type { ReactNode } from 'react';
import { Icon } from '@/components/ui/icon';
import { t } from '@/lib/i18n';

interface AuthShellProps {
  readonly title: string;
  readonly subtitle: string;
  readonly children: ReactNode;
}

const HIGHLIGHTS: ReadonlyArray<{ icon: 'check' | 'truck' | 'messageCircle'; key: 'auth.shared.point_verified' | 'auth.shared.point_shipping' | 'auth.shared.point_support' }> = [
  { icon: 'check', key: 'auth.shared.point_verified' },
  { icon: 'truck', key: 'auth.shared.point_shipping' },
  { icon: 'messageCircle', key: 'auth.shared.point_support' },
];

const STATS: ReadonlyArray<{ valueKey: 'auth.shared.stat_makers_value' | 'auth.shared.stat_categories_value' | 'auth.shared.stat_orders_value'; labelKey: 'auth.shared.stat_makers_label' | 'auth.shared.stat_categories_label' | 'auth.shared.stat_orders_label' }> = [
  { valueKey: 'auth.shared.stat_makers_value', labelKey: 'auth.shared.stat_makers_label' },
  { valueKey: 'auth.shared.stat_categories_value', labelKey: 'auth.shared.stat_categories_label' },
  { valueKey: 'auth.shared.stat_orders_value', labelKey: 'auth.shared.stat_orders_label' },
];

/**
 * Centered auth shell: eyebrow + heading above a `.panel` surface that
 * holds the form, with the trust points and marketplace stats as
 * hairline-divided strips below it. The (auth) layout provides the
 * fullscreen backdrop and ambient glows.
 */
export function AuthShell({ title, subtitle, children }: AuthShellProps) {
  return (
    <section className="flex flex-col">
      <header className="text-center">
        <p className="text-xs font-semibold uppercase tracking-widest text-brand-300">{t('auth.shared.eyebrow')}</p>
        <h1 className="mt-3 text-2xl font-semibold tracking-tight text-white sm:text-3xl md:text-4xl">{title}</h1>
        <p className="mx-auto mt-3 max-w-md text-sm leading-relaxed text-zinc-400">{subtitle}</p>
      </header>

      <div className="panel mt-8 rounded-xl border border-zinc-800 p-6 sm:p-8">{children}</div>

      <ul className="mt-10 space-y-2.5 border-t border-zinc-800/80 pt-6">
        {HIGHLIGHTS.map((item) => (
          <li key={item.key} className="flex items-start justify-center gap-2.5 text-xs text-zinc-500">
            <span className="mt-0.5 text-brand-400">
              <Icon name={item.icon} size={14} />
            </span>
            <span>{t(item.key)}</span>
          </li>
        ))}
      </ul>

      <dl className="mt-6 grid grid-cols-3 divide-x divide-zinc-800/80 border-t border-zinc-800/80 pt-6 text-center">
        {STATS.map((stat) => (
          <div key={stat.labelKey} className="px-2">
            <dt className="text-xs leading-tight text-zinc-500">{t(stat.labelKey)}</dt>
            <dd className="mt-1 text-sm font-semibold text-white">{t(stat.valueKey)}</dd>
          </div>
        ))}
      </dl>
    </section>
  );
}
