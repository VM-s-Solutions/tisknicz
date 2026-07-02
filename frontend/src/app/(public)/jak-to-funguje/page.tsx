import Link from 'next/link';
import type { Metadata } from 'next';
import { Icon } from '@/components/ui/icon';
import { t } from '@/lib/i18n';

/**
 * /jak-to-funguje — public "how it works" page (T-0130). Server
 * Component, zero client JS. Marketing content harvested from
 * PROJEKT-VIZE.md §"Co je Tiskni.cz" + §"Jak to funguje — objednávkový
 * flow" (the customer-visible milestones). Vykání (V form). All copy
 * via `static.how_it_works.*` cs-CZ keys.
 *
 * generateMetadata carries title/description (T-0130) + OG/Twitter/
 * canonical (T-0131).
 */

const STEPS = [
  {
    icon: 'search',
    titleKey: 'static.how_it_works.step1_title',
    bodyKey: 'static.how_it_works.step1_body',
    detailKeys: [
      'static.how_it_works.step1_detail1',
      'static.how_it_works.step1_detail2',
      'static.how_it_works.step1_detail3',
      'static.how_it_works.step1_detail4',
    ],
  },
  {
    icon: 'upload',
    titleKey: 'static.how_it_works.step2_title',
    bodyKey: 'static.how_it_works.step2_body',
    detailKeys: [
      'static.how_it_works.step2_detail1',
      'static.how_it_works.step2_detail2',
    ],
  },
  {
    icon: 'creditCard',
    titleKey: 'static.how_it_works.step3_title',
    bodyKey: 'static.how_it_works.step3_body',
    detailKeys: ['static.how_it_works.step2_detail4'],
  },
  {
    icon: 'check',
    titleKey: 'static.how_it_works.step4_title',
    bodyKey: 'static.how_it_works.step4_body',
    detailKeys: ['static.how_it_works.step6_detail1', 'static.how_it_works.step6_detail2'],
  },
  { icon: 'truck', titleKey: 'static.how_it_works.step5_title', bodyKey: 'static.how_it_works.step5_body' },
  {
    icon: 'package',
    titleKey: 'static.how_it_works.step6_title',
    bodyKey: 'static.how_it_works.step6_body',
    detailKeys: [
      'static.how_it_works.step6_detail3',
      'static.how_it_works.step6_detail4',
    ],
  },
] as const;

export function generateMetadata(): Metadata {
  return {
    title: t('static.how_it_works.meta_title'),
    description: t('static.how_it_works.meta_description'),
  };
}

export default function HowItWorksPage() {
  return (
    <section className="bg-surface-primary py-20 lg:py-24">
      <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
        <header className="max-w-4xl">
          <h1 className="text-4xl font-bold tracking-tight text-white sm:text-5xl">
            {t('static.how_it_works.title')}
          </h1>
          <p className="mt-6 max-w-3xl text-lg leading-relaxed text-zinc-400">
            {t('static.how_it_works.intro')}
          </p>
        </header>

        <div className="mt-16">
          <h2 className="text-sm font-semibold uppercase tracking-[0.18em] text-brand-400">
            {t('static.how_it_works.steps_heading')}
          </h2>
          <ol className="mt-10 bg-zinc-900/25">
            {STEPS.map((step, index) => (
              <li
                key={step.titleKey}
                className="px-4 py-5 first:border-t-0 sm:px-6 sm:py-6 [&:not(:first-child)]:border-t [&:not(:first-child)]:border-zinc-800"
              >
                <div className="flex items-start gap-4">
                  <span className="mt-0.5 inline-flex h-7 min-w-7 items-center justify-center rounded-md bg-zinc-800 px-2 text-xs font-bold text-brand-400">
                    {index + 1}
                  </span>
                  <div className="min-w-0 flex-1">
                    <div className="flex items-center gap-3">
                      <span className="flex h-7 w-7 items-center justify-center text-brand-400">
                        <Icon name={step.icon} size={16} />
                      </span>
                      <h3 className="text-lg font-semibold text-white">{t(step.titleKey)}</h3>
                    </div>
                    <p className="mt-2 text-sm leading-relaxed text-zinc-400">{t(step.bodyKey)}</p>
                    {'detailKeys' in step ? (
                      <ul className="mt-3 space-y-1.5">
                        {step.detailKeys.map((detailKey) => (
                          <li key={detailKey} className="flex items-start gap-2 text-sm leading-relaxed text-zinc-300">
                            <span className="mt-0.5 text-brand-400">
                              <Icon name="check" size={14} />
                            </span>
                            {t(detailKey)}
                          </li>
                        ))}
                      </ul>
                    ) : null}
                  </div>
                </div>
              </li>
            ))}
          </ol>
        </div>

        <div className="mt-16 flex flex-col items-center gap-5 py-10 text-center">
          <p className="text-xs font-semibold uppercase tracking-[0.24em] text-brand-400/80">
            {t('static.how_it_works.steps_heading')}
          </p>
          <h2 className="text-2xl font-bold tracking-tight text-white sm:text-3xl">
            {t('static.how_it_works.cta_heading')}
          </h2>
          <Link
            href="/katalog"
            className="group inline-flex items-center gap-2 border-b border-brand-500/70 pb-1 text-base font-semibold text-brand-300 transition-colors hover:border-brand-300 hover:text-brand-200"
          >
            {t('static.how_it_works.cta')}
            <Icon name="arrowRight" size={18} className="transition-transform group-hover:translate-x-1" />
          </Link>
        </div>
      </div>
    </section>
  );
}
