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
  { icon: 'search', titleKey: 'static.how_it_works.step1_title', bodyKey: 'static.how_it_works.step1_body' },
  { icon: 'upload', titleKey: 'static.how_it_works.step2_title', bodyKey: 'static.how_it_works.step2_body' },
  { icon: 'creditCard', titleKey: 'static.how_it_works.step3_title', bodyKey: 'static.how_it_works.step3_body' },
  { icon: 'check', titleKey: 'static.how_it_works.step4_title', bodyKey: 'static.how_it_works.step4_body' },
  { icon: 'truck', titleKey: 'static.how_it_works.step5_title', bodyKey: 'static.how_it_works.step5_body' },
  { icon: 'package', titleKey: 'static.how_it_works.step6_title', bodyKey: 'static.how_it_works.step6_body' },
] as const;

export function generateMetadata(): Metadata {
  return {
    title: t('static.how_it_works.meta_title'),
    description: t('static.how_it_works.meta_description'),
  };
}

export default function HowItWorksPage() {
  return (
    <section className="bg-surface-primary py-16 lg:py-24">
      <div className="mx-auto max-w-5xl px-4 sm:px-6 lg:px-8">
        <header className="mx-auto max-w-3xl text-center">
          <h1 className="text-4xl font-bold tracking-tight text-white sm:text-5xl">
            {t('static.how_it_works.title')}
          </h1>
          <p className="mt-6 text-lg leading-relaxed text-zinc-400">
            {t('static.how_it_works.intro')}
          </p>
        </header>

        <div className="mt-16">
          <h2 className="text-center text-sm font-semibold uppercase tracking-widest text-brand-400">
            {t('static.how_it_works.steps_heading')}
          </h2>
          <ol className="mt-10 grid grid-cols-1 gap-6 md:grid-cols-2 lg:grid-cols-3">
            {STEPS.map((step, index) => (
              <li key={step.titleKey}>
                <div className="hover-glow relative flex h-full flex-col gap-4 rounded-2xl border border-zinc-800 bg-surface-card p-7">
                  <div className="flex items-center gap-3">
                    <span className="flex h-11 w-11 items-center justify-center rounded-xl bg-zinc-800 text-brand-400">
                      <Icon name={step.icon} size={22} />
                    </span>
                    <span className="text-3xl font-bold text-brand-400">{index + 1}</span>
                  </div>
                  <h3 className="text-lg font-semibold text-white">{t(step.titleKey)}</h3>
                  <p className="text-sm leading-relaxed text-zinc-400">{t(step.bodyKey)}</p>
                </div>
              </li>
            ))}
          </ol>
        </div>

        <div className="mt-16 flex flex-col items-center gap-6 rounded-2xl border border-zinc-800 bg-surface-secondary px-6 py-12 text-center">
          <h2 className="text-2xl font-bold tracking-tight text-white sm:text-3xl">
            {t('static.how_it_works.cta_heading')}
          </h2>
          <Link
            href="/katalog"
            className="group inline-flex items-center gap-2 rounded-xl border border-brand-400/50 px-8 py-4 text-base font-semibold text-brand-400 transition-all duration-200 hover:border-brand-400 hover:bg-brand-400/10"
          >
            {t('static.how_it_works.cta')}
            <Icon name="arrowRight" size={18} className="transition-transform group-hover:translate-x-1" />
          </Link>
        </div>
      </div>
    </section>
  );
}
