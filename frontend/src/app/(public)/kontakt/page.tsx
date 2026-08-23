import type { Metadata } from 'next';
import { t } from '@/lib/i18n';

/**
 * /kontakt — identifikace provozovatele (O4 / §2.8 bod 3 v
 * docs/meetings/dopady-rozhodnuti-na-platformu.md). VOP §1 a GDPR §1
 * odkazují na "kontaktní sekci" pro aktuální identifikační údaje
 * provozovatele.
 *
 * Údaje jsou závazné a ověřené proti ARES / veřejnému rejstříku
 * (IČO 29633443) — placeholder-lock z T-0130 tady padá. /vop a /gdpr
 * zůstávají placeholdery až do schváleného právního textu (Q-0030).
 *
 * Prezentace: iOS grouped list — micro-header nad každou skupinou,
 * key-value řádky oddělené hairline dividery uvnitř jedné karty.
 */

export function generateMetadata(): Metadata {
  return {
    title: t('static.contact.meta_title'),
    description: t('static.contact.meta_description'),
  };
}

function InfoRow({
  label,
  value,
  href,
  breakMode = 'words',
}: {
  readonly label: string;
  readonly value: string;
  readonly href?: string;
  readonly breakMode?: 'words' | 'all';
}) {
  const valueClass = `min-w-0 text-right text-sm text-zinc-100 ${
    breakMode === 'all' ? 'break-all' : 'break-words'
  }`;

  return (
    <div className="flex items-start justify-between gap-3 py-2.5">
      <dt className="shrink-0 text-sm text-zinc-400">{label}</dt>
      <dd className={valueClass}>
        {href ? (
          <a
            href={href}
            className="rounded-sm underline decoration-zinc-600 underline-offset-4 transition-colors duration-150 hover:decoration-zinc-300 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-400/60"
          >
            {value}
          </a>
        ) : (
          value
        )}
      </dd>
    </div>
  );
}

export default function ContactPage() {
  return (
    <section className="py-16 lg:py-24">
      <div className="mx-auto flex max-w-4xl flex-col gap-8 px-4 sm:px-6 lg:px-8">
        <h1 className="text-3xl font-bold tracking-tight text-zinc-50 sm:text-4xl">{t('static.contact.title')}</h1>

        <section className="flex flex-col gap-3">
          <h2 className="text-xs font-semibold uppercase tracking-widest text-zinc-500">
            {t('static.contact.section_operator_title')}
          </h2>
          <div className="rounded-xl border border-zinc-800 bg-surface-card px-4 py-1 sm:px-5">
            <dl className="divide-y divide-zinc-800">
              <InfoRow
                label={t('static.contact.operator_name_label')}
                value={t('static.contact.operator_name')}
              />
              <InfoRow
                label={t('static.contact.operator_ico_label')}
                value={t('static.contact.operator_ico_value')}
              />
              <InfoRow
                label={t('static.contact.operator_vat_label')}
                value={t('static.contact.operator_vat_value')}
              />
              <InfoRow
                label={t('static.contact.operator_address_label')}
                value={t('static.contact.operator_address_value')}
              />
              <InfoRow
                label={t('static.contact.operator_register_label')}
                value={t('static.contact.operator_register_value')}
              />
            </dl>
          </div>
        </section>

        <section className="flex flex-col gap-3">
          <h2 className="text-xs font-semibold uppercase tracking-widest text-zinc-500">
            {t('static.contact.section_contact_title')}
          </h2>
          <div className="rounded-xl border border-zinc-800 bg-surface-card px-4 py-1 sm:px-5">
            <dl className="divide-y divide-zinc-800">
              <InfoRow
                label={t('static.contact.operator_email_label')}
                value={t('static.contact.operator_email_value')}
                href={`mailto:${t('static.contact.operator_email_value')}`}
                breakMode="all"
              />
            </dl>
          </div>
        </section>
      </div>
    </section>
  );
}
