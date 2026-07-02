import Link from 'next/link';
import type { Metadata } from 'next';
import { Icon } from '@/components/ui/icon';
import { t } from '@/lib/i18n';

/**
 * /pro-makery — public maker value-proposition page (T-0130). Server
 * Component, zero client JS. Content harvested from PROJEKT-VIZE.md
 * §"Proč to děláme" (bod 3) + §"6 kategorií služeb" (intro) and
 * §"Byznys model" (benefit cards + the illustrative "Příklad
 * kalkulace" — static keyed copy, never computed client-side; pricing
 * math is backend-owned). Vykání (V form per the public-acquisition
 * tone lock A.3). All copy via `static.for_makers.*` cs-CZ keys.
 */

const BENEFITS = [
  { icon: 'check', titleKey: 'static.for_makers.benefit_free_title', bodyKey: 'static.for_makers.benefit_free_body' },
  { icon: 'creditCard', titleKey: 'static.for_makers.benefit_commission_title', bodyKey: 'static.for_makers.benefit_commission_body' },
  { icon: 'clock', titleKey: 'static.for_makers.benefit_payouts_title', bodyKey: 'static.for_makers.benefit_payouts_body' },
  { icon: 'file', titleKey: 'static.for_makers.benefit_invoicing_title', bodyKey: 'static.for_makers.benefit_invoicing_body' },
  { icon: 'truck', titleKey: 'static.for_makers.benefit_shipping_title', bodyKey: 'static.for_makers.benefit_shipping_body' },
  { icon: 'shoppingBag', titleKey: 'static.for_makers.benefit_no_minimum_title', bodyKey: 'static.for_makers.benefit_no_minimum_body' },
] as const;

const EXAMPLE_LINES = [
  'static.for_makers.example_customer_pays',
  'static.for_makers.example_commission',
  'static.for_makers.example_loyal_commission',
  'static.for_makers.example_maker_gets',
] as const;

export function generateMetadata(): Metadata {
  return {
    title: t('static.for_makers.meta_title'),
    description: t('static.for_makers.meta_description'),
  };
}

export default function ForMakersPage() {
  return (
    <div className="bg-surface-primary">
      <section className="border-b border-zinc-800 bg-surface-primary py-20 sm:py-24">
        <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
          <header className="max-w-4xl">
            <p className="text-sm font-semibold uppercase tracking-[0.18em] text-brand-400">Makables</p>
            <h1 className="mt-5 text-4xl font-bold tracking-tight text-white sm:text-5xl lg:text-6xl">
              {t('static.for_makers.title')}
            </h1>
            <p className="mt-6 max-w-3xl text-lg leading-relaxed text-zinc-300">{t('static.for_makers.intro')}</p>
            <div className="mt-10 flex flex-wrap items-center gap-6">
              <Link
                href="/register/maker"
                className="inline-flex items-center gap-2 rounded-lg border border-brand-500 bg-brand-500 px-6 py-3 text-sm font-semibold text-zinc-950 transition-colors hover:bg-brand-400"
              >
                {t('static.for_makers.cta')}
                <Icon name="arrowRight" size={16} />
              </Link>
              <Link
                href="/jak-to-funguje"
                className="group inline-flex items-center gap-2 border-b border-brand-500/70 pb-1 text-sm font-semibold text-brand-300 transition-colors hover:border-brand-300 hover:text-brand-200"
              >
                {t('nav.how_it_works')}
                <Icon name="arrowRight" size={16} className="transition-transform group-hover:translate-x-1" />
              </Link>
            </div>
          </header>
        </div>
      </section>

      <section className="bg-surface-secondary py-16 sm:py-20">
        <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
          <h2 className="text-sm font-semibold uppercase tracking-[0.18em] text-brand-400">
            {t('static.for_makers.benefits_heading')}
          </h2>
          <div className="mt-10">
            <ol className="border-y border-zinc-800">
            {BENEFITS.map((benefit, index) => (
              <li
                key={benefit.titleKey}
                className="px-4 py-5 first:border-t-0 sm:px-6 sm:py-6 [&:not(:first-child)]:border-t [&:not(:first-child)]:border-zinc-800"
              >
                <div className="relative flex items-start gap-4">
                  <span className="relative z-10 mt-0.5 inline-flex h-7 min-w-7 items-center justify-center rounded-md border border-zinc-700 bg-surface-secondary px-2 text-xs font-bold text-brand-400">
                    {index + 1}
                  </span>
                  <div className="min-w-0 flex-1">
                    <div className="flex items-center gap-3">
                      <span className="flex h-7 w-7 items-center justify-center text-brand-400">
                        <Icon name={benefit.icon} size={16} />
                      </span>
                      <h3 className="text-lg font-semibold text-white">{t(benefit.titleKey)}</h3>
                    </div>
                    <p className="mt-2 text-sm leading-relaxed text-zinc-400">{t(benefit.bodyKey)}</p>
                  </div>
                </div>
              </li>
            ))}
            </ol>
          </div>
        </div>
      </section>

      <section className="bg-surface-primary py-16 sm:py-20">
        <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
          <div className="max-w-3xl">
            <h2 className="text-xl font-semibold text-white">{t('static.for_makers.example_heading')}</h2>
            <p className="mt-2 text-sm text-zinc-400">{t('static.for_makers.example_intro')}</p>
            <ul className="mt-6 border-y border-zinc-800">
              {EXAMPLE_LINES.map((lineKey) => (
                <li
                  key={lineKey}
                  className="flex items-start gap-3 px-1 py-3 text-sm text-zinc-300 [&:not(:first-child)]:border-t [&:not(:first-child)]:border-zinc-800"
                >
                  <span className="mt-0.5 text-brand-400">
                    <Icon name="arrowRight" size={16} />
                  </span>
                  {t(lineKey)}
                </li>
              ))}
            </ul>
            <p className="mt-3 text-xs text-zinc-500">{t('static.for_makers.example_note')}</p>
          </div>
        </div>
      </section>

      <section className="border-t border-zinc-800 bg-surface-secondary py-16 sm:py-20">
        <div className="mx-auto flex max-w-5xl flex-col gap-5 px-4 text-center sm:px-6 lg:px-8">
          <p className="text-xs font-semibold uppercase tracking-[0.24em] text-brand-400/80">{t('static.for_makers.benefits_heading')}</p>
          <h2 className="text-2xl font-bold tracking-tight text-white sm:text-3xl">{t('static.for_makers.cta_heading')}</h2>
          <Link
            href="/register/maker"
            className="group mx-auto inline-flex items-center gap-2 border-b border-brand-500/70 pb-1 text-base font-semibold text-brand-300 transition-colors hover:border-brand-300 hover:text-brand-200"
          >
            {t('static.for_makers.cta')}
            <Icon name="arrowRight" size={18} className="transition-transform group-hover:translate-x-1" />
          </Link>
        </div>
      </section>
    </div>
  );
}
