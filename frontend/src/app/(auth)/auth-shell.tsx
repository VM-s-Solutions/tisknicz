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

export function AuthShell({ title, subtitle, children }: AuthShellProps) {
  return (
    <section className="relative overflow-hidden rounded-2xl border border-zinc-800 bg-zinc-900/70 shadow-2xl">
      <div className="pointer-events-none absolute -left-24 -top-24 h-64 w-64 rounded-full bg-sky-500/10 blur-3xl" />
      <div className="pointer-events-none absolute -bottom-24 -right-24 h-64 w-64 rounded-full bg-emerald-500/10 blur-3xl" />

      <div className="relative grid lg:grid-cols-[1.15fr_1fr]">
        <aside className="border-b border-zinc-800 p-6 lg:border-b-0 lg:border-r lg:p-8">
          <p className="text-xs font-semibold uppercase tracking-widest text-sky-300">{t('auth.shared.eyebrow')}</p>
          <h1 className="mt-4 text-3xl font-semibold tracking-tight text-white">{title}</h1>
          <p className="mt-3 text-sm leading-relaxed text-zinc-300">{subtitle}</p>

          <ul className="mt-6 space-y-3 border-y border-zinc-800 py-5">
            {HIGHLIGHTS.map((item) => (
              <li key={item.key} className="flex items-start gap-3 text-sm text-zinc-300">
                <span className="mt-0.5 text-emerald-300">
                  <Icon name={item.icon} size={16} />
                </span>
                <span>{t(item.key)}</span>
              </li>
            ))}
          </ul>

          <dl className="mt-6 grid grid-cols-3 gap-3">
            {STATS.map((stat) => (
              <div key={stat.labelKey} className="rounded-xl border border-zinc-800 bg-zinc-950/70 px-3 py-3">
                <dt className="text-[11px] leading-tight text-zinc-500">{t(stat.labelKey)}</dt>
                <dd className="mt-1 text-lg font-semibold text-white">{t(stat.valueKey)}</dd>
              </div>
            ))}
          </dl>
        </aside>

        <div className="p-4 sm:p-6">{children}</div>
      </div>
    </section>
  );
}