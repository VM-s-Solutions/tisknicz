import type { Metadata } from 'next';
import { Alert } from '@/components/ui/alert';
import { t } from '@/lib/i18n';

/**
 * /vop — obchodní podmínky (terms of service). PLACEHOLDER shell only
 * (T-0130 / Q-0030). The approved legal text is a blocking pre-launch
 * item supplied by JVM YORE s.r.o.; this page ships the route, the
 * heading, a visible `Alert variant="warning"` placeholder banner, and
 * a keyed note. NO invented legal clauses. The `static.terms.*` keys
 * are wired for a drop-in replacement once the text is approved.
 */

export function generateMetadata(): Metadata {
  return {
    title: t('static.terms.meta_title'),
    description: t('static.terms.meta_description'),
  };
}

export default function TermsPage() {
  const legalSources = [
    'static.terms.law_cz_civil',
    'static.terms.law_cz_consumer',
    'static.terms.law_cz_info_society',
    'static.terms.law_eu_consumer_rights',
    'static.terms.law_eu_dsa',
  ] as const;

  return (
    <section className="bg-surface-primary py-16 lg:py-24">
      <div className="mx-auto flex max-w-4xl flex-col gap-8 px-4 sm:px-6 lg:px-8">
        <h1 className="text-3xl font-bold tracking-tight text-white sm:text-4xl">
          {t('static.terms.title')}
        </h1>

        <Alert variant="info">
          <p className="font-semibold">{t('static.terms.disclaimer')}</p>
        </Alert>

        <div className="space-y-6 rounded-2xl border border-zinc-800 bg-surface-card p-6 sm:p-8">
          <section className="space-y-3">
            <h2 className="text-xl font-semibold text-white">{t('static.terms.section_operator_title')}</h2>
            <p className="leading-relaxed text-zinc-300">{t('static.terms.section_operator_body')}</p>
          </section>

          <section className="space-y-3">
            <h2 className="text-xl font-semibold text-white">{t('static.terms.section_scope_title')}</h2>
            <p className="leading-relaxed text-zinc-300">{t('static.terms.section_scope_body')}</p>
          </section>

          <section className="space-y-3">
            <h2 className="text-xl font-semibold text-white">{t('static.terms.section_contracts_title')}</h2>
            <p className="leading-relaxed text-zinc-300">{t('static.terms.section_contracts_body')}</p>
          </section>

          <section className="space-y-3">
            <h2 className="text-xl font-semibold text-white">{t('static.terms.section_payments_title')}</h2>
            <p className="leading-relaxed text-zinc-300">{t('static.terms.section_payments_body')}</p>
          </section>

          <section className="space-y-3">
            <h2 className="text-xl font-semibold text-white">{t('static.terms.section_withdrawal_title')}</h2>
            <p className="leading-relaxed text-zinc-300">{t('static.terms.section_withdrawal_body')}</p>
          </section>

          <section className="space-y-3">
            <h2 className="text-xl font-semibold text-white">{t('static.terms.section_claims_title')}</h2>
            <p className="leading-relaxed text-zinc-300">{t('static.terms.section_claims_body')}</p>
          </section>

          <section className="space-y-3">
            <h2 className="text-xl font-semibold text-white">{t('static.terms.section_law_title')}</h2>
            <p className="leading-relaxed text-zinc-300">{t('static.terms.section_law_intro')}</p>
            <ul className="list-disc space-y-2 pl-5 text-zinc-300">
              {legalSources.map((law) => (
                <li key={law}>{t(law)}</li>
              ))}
            </ul>
          </section>
        </div>
      </div>
    </section>
  );
}
