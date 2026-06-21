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
  return (
    <section className="bg-surface-primary py-16 lg:py-24">
      <div className="mx-auto flex max-w-3xl flex-col gap-8 px-4 sm:px-6 lg:px-8">
        <h1 className="text-3xl font-bold tracking-tight text-white sm:text-4xl">
          {t('static.privacy.title')}
        </h1>
        <Alert variant="warning">
          <p className="font-semibold">{t('static.legal_placeholder.banner')}</p>
        </Alert>
        <p className="text-base leading-relaxed text-zinc-400">
          {t('static.privacy.placeholder_note')}
        </p>
      </div>
    </section>
  );
}
