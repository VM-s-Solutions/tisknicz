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
  return (
    <section className="bg-surface-primary py-16 lg:py-24">
      <div className="mx-auto flex max-w-3xl flex-col gap-8 px-4 sm:px-6 lg:px-8">
        <h1 className="text-3xl font-bold tracking-tight text-white sm:text-4xl">
          {t('static.terms.title')}
        </h1>
        <Alert variant="warning">
          <p className="font-semibold">{t('static.legal_placeholder.banner')}</p>
        </Alert>
        <p className="text-base leading-relaxed text-zinc-400">
          {t('static.terms.placeholder_note')}
        </p>
      </div>
    </section>
  );
}
