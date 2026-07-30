import type { Metadata } from 'next';
import { Alert } from '@/components/ui/alert';
import { Icon } from '@/components/ui/icon';
import { t } from '@/lib/i18n';

/**
 * /kontakt — identifikace provozovatele (O4 / §2.8 bod 3 v
 * docs/meetings/dopady-rozhodnuti-na-platformu.md). VOP §1 a GDPR §1
 * odkazují na "kontaktní sekci" pro aktuální identifikační údaje
 * provozovatele; do teď žádná neexistovala. IČO/sídlo/e-mail jsou
 * PLACEHOLDER (legal-placeholder lock, stejný vzor jako /vop a /gdpr,
 * T-0130) — žádné vymyšlené údaje, doplní provozovatel před launchem.
 */

export function generateMetadata(): Metadata {
  return {
    title: t('static.contact.meta_title'),
    description: t('static.contact.meta_description'),
  };
}

export default function ContactPage() {
  return (
    <section className="py-16 lg:py-24">
      <div className="mx-auto flex max-w-4xl flex-col gap-8 px-4 sm:px-6 lg:px-8">
        <h1 className="text-3xl font-bold tracking-tight text-white sm:text-4xl">{t('static.contact.title')}</h1>

        <Alert variant="warning">
          <p className="font-semibold">{t('static.contact.disclaimer')}</p>
        </Alert>

        <div className="space-y-6 rounded-2xl border border-zinc-800 bg-surface-card p-6 sm:p-8">
          <section className="space-y-3">
            <div className="flex items-center gap-3">
              <span className="icon-tile h-9 w-9" aria-hidden="true">
                <Icon name="building" size={16} />
              </span>
              <h2 className="text-xl font-semibold text-white">{t('static.contact.section_operator_title')}</h2>
            </div>
            <p className="leading-relaxed text-zinc-300">{t('static.contact.operator_name')}</p>
            <dl className="grid grid-cols-1 gap-3 sm:grid-cols-2">
              <div>
                <dt className="text-xs uppercase tracking-wide text-zinc-500">{t('static.contact.operator_ico_label')}</dt>
                <dd className="mt-1 flex items-center gap-2 text-zinc-300">
                  <span className="text-zinc-500" aria-hidden="true">
                    <Icon name="tag" size={14} />
                  </span>
                  {t('static.contact.operator_ico_value')}
                </dd>
              </div>
              <div>
                <dt className="text-xs uppercase tracking-wide text-zinc-500">{t('static.contact.operator_address_label')}</dt>
                <dd className="mt-1 flex items-center gap-2 text-zinc-300">
                  <span className="text-zinc-500" aria-hidden="true">
                    <Icon name="mapPin" size={14} />
                  </span>
                  {t('static.contact.operator_address_value')}
                </dd>
              </div>
            </dl>
          </section>

          <section className="space-y-3">
            <div className="flex items-center gap-3">
              <span className="icon-tile h-9 w-9" aria-hidden="true">
                <Icon name="messageCircle" size={16} />
              </span>
              <h2 className="text-xl font-semibold text-white">{t('static.contact.section_contact_title')}</h2>
            </div>
            <dl>
              <dt className="text-xs uppercase tracking-wide text-zinc-500">{t('static.contact.operator_email_label')}</dt>
              <dd className="mt-1 flex items-center gap-2 text-zinc-300">
                <span className="text-zinc-500" aria-hidden="true">
                  <Icon name="mail" size={14} />
                </span>
                {t('static.contact.operator_email_value')}
              </dd>
            </dl>
          </section>
        </div>
      </div>
    </section>
  );
}
