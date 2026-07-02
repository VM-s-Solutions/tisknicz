import type { Metadata } from 'next';
import { Alert } from '@/components/ui/alert';
import { t } from '@/lib/i18n';

/**
 * /gdpr — ochrana osobních údajů (privacy policy). PLACEHOLDER shell
 * only (T-0130 / Q-0030). The approved privacy/cookie text is a
 * blocking pre-launch item supplied by JVM YORE s.r.o.; this page ships
 * the route, the heading, a visible `Alert variant="warning"`
 * placeholder banner, and a keyed note. NO invented privacy/cookie
 * copy. The `static.privacy.*` keys are wired for a drop-in replacement
 * once the text is approved.
 */

export function generateMetadata(): Metadata {
  return {
    title: t('static.privacy.meta_title'),
    description: t('static.privacy.meta_description'),
  };
}

export default function PrivacyPage() {
  const legalSources = [
    'static.privacy.law_gdpr',
    'static.privacy.law_cz_processing',
    'static.privacy.law_eprivacy',
    'static.privacy.law_cz_electronic',
  ] as const;

  return (
    <section className="bg-surface-primary py-16 lg:py-24">
      <div className="mx-auto flex max-w-4xl flex-col gap-8 px-4 sm:px-6 lg:px-8">
        <h1 className="text-3xl font-bold tracking-tight text-white sm:text-4xl">
          {t('static.privacy.title')}
        </h1>

        <Alert variant="info">
          <p className="font-semibold">{t('static.privacy.disclaimer')}</p>
        </Alert>

        <div className="space-y-6 rounded-2xl border border-zinc-800 bg-surface-card p-6 sm:p-8">
          <section className="space-y-3">
            <h2 className="text-xl font-semibold text-white">{t('static.privacy.section_controller_title')}</h2>
            <p className="leading-relaxed text-zinc-300">{t('static.privacy.section_controller_body')}</p>
          </section>

          <section className="space-y-3">
            <h2 className="text-xl font-semibold text-white">{t('static.privacy.section_data_title')}</h2>
            <p className="leading-relaxed text-zinc-300">{t('static.privacy.section_data_body')}</p>
          </section>

          <section className="space-y-3">
            <h2 className="text-xl font-semibold text-white">{t('static.privacy.section_legal_basis_title')}</h2>
            <p className="leading-relaxed text-zinc-300">{t('static.privacy.section_legal_basis_body')}</p>
          </section>

          <section className="space-y-3">
            <h2 className="text-xl font-semibold text-white">{t('static.privacy.section_retention_title')}</h2>
            <p className="leading-relaxed text-zinc-300">{t('static.privacy.section_retention_body')}</p>
          </section>

          <section className="space-y-3">
            <h2 className="text-xl font-semibold text-white">{t('static.privacy.section_rights_title')}</h2>
            <p className="leading-relaxed text-zinc-300">{t('static.privacy.section_rights_body')}</p>
          </section>

          <section className="space-y-3">
            <h2 className="text-xl font-semibold text-white">{t('static.privacy.section_cookies_title')}</h2>
            <p className="leading-relaxed text-zinc-300">{t('static.privacy.section_cookies_body')}</p>
          </section>

          <section className="space-y-3">
            <h2 className="text-xl font-semibold text-white">{t('static.privacy.section_law_title')}</h2>
            <p className="leading-relaxed text-zinc-300">{t('static.privacy.section_law_intro')}</p>
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
