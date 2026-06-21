import Link from 'next/link';
import type { Metadata } from 'next';
import { Card } from '@/components/ui/card';
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
    <section className="bg-surface-primary py-16 lg:py-24">
      <div className="mx-auto max-w-5xl px-4 sm:px-6 lg:px-8">
        <header className="mx-auto max-w-3xl text-center">
          <h1 className="text-4xl font-bold tracking-tight text-white sm:text-5xl">
            {t('static.for_makers.title')}
          </h1>
          <p className="mt-6 text-lg leading-relaxed text-zinc-400">
            {t('static.for_makers.intro')}
          </p>
        </header>

        <div className="mt-16">
          <h2 className="text-center text-sm font-semibold uppercase tracking-widest text-brand-400">
            {t('static.for_makers.benefits_heading')}
          </h2>
          <div className="mt-10 grid grid-cols-1 gap-6 md:grid-cols-2 lg:grid-cols-3">
            {BENEFITS.map((benefit) => (
              <Card key={benefit.titleKey} padding="lg" hover className="flex h-full flex-col gap-4">
                <span className="flex h-11 w-11 items-center justify-center rounded-xl bg-zinc-800 text-brand-400">
                  <Icon name={benefit.icon} size={22} />
                </span>
                <h3 className="text-lg font-semibold text-white">{t(benefit.titleKey)}</h3>
                <p className="text-sm leading-relaxed text-zinc-400">{t(benefit.bodyKey)}</p>
              </Card>
            ))}
          </div>
        </div>

        <div className="mx-auto mt-16 max-w-2xl">
          <Card padding="lg" className="flex flex-col gap-4">
            <h2 className="text-xl font-semibold text-white">
              {t('static.for_makers.example_heading')}
            </h2>
            <p className="text-sm text-zinc-400">{t('static.for_makers.example_intro')}</p>
            <ul className="flex flex-col gap-3">
              {EXAMPLE_LINES.map((lineKey) => (
                <li key={lineKey} className="flex items-start gap-3 text-sm text-zinc-300">
                  <span className="mt-0.5 text-brand-400">
                    <Icon name="arrowRight" size={16} />
                  </span>
                  {t(lineKey)}
                </li>
              ))}
            </ul>
            <p className="text-xs text-zinc-500">{t('static.for_makers.example_note')}</p>
          </Card>
        </div>

        <div className="mt-16 flex flex-col items-center gap-6 rounded-2xl border border-zinc-800 bg-surface-secondary px-6 py-12 text-center">
          <h2 className="text-2xl font-bold tracking-tight text-white sm:text-3xl">
            {t('static.for_makers.cta_heading')}
          </h2>
          <Link
            href="/register/maker"
            className="group inline-flex items-center gap-2 rounded-xl border border-brand-400 bg-brand-400/10 px-8 py-4 text-base font-semibold text-brand-400 transition-all duration-200 hover:bg-brand-400/20"
          >
            {t('static.for_makers.cta')}
            <Icon name="arrowRight" size={18} className="transition-transform group-hover:translate-x-1" />
          </Link>
        </div>
      </div>
    </section>
  );
}
