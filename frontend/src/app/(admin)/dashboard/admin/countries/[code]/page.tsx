import type { Metadata } from 'next';
import { Alert } from '@/components/ui/alert';
import { t } from '@/lib/i18n';
import { CountryConfigForm } from './country-config-form';

/**
 * Admin country-config edit (T-0118c §2, US-admin-0006). Server Component,
 * `force-dynamic`. The route is keyed on the country code path param (CZ at
 * launch). The editable T-0108 set (VAT/fee/providers/invoicing-mode/
 * shipping-price + mandatory reason) is mutated through the PUT
 * `country-configurations/{code}` endpoint; the provider-change retype gate
 * (A.5) + the unregistered-code rejection + the in-flight advisory are all
 * backend.
 *
 * CONTRACT GAP (flagged): there is NO GET on the contract, so the form has
 * no server pre-fill — the operator enters the full editable set. A read
 * endpoint is the clean fix and is logged as a backend follow-up.
 */

export function generateMetadata(): Metadata {
  return {
    title: t('dashboard.admin.ops.country.metadata.title'),
    description: t('dashboard.admin.ops.country.metadata.description'),
  };
}

export const dynamic = 'force-dynamic';

interface PageProps {
  readonly params: Promise<{ readonly code: string }>;
}

export default async function AdminCountryConfigPage({ params }: PageProps) {
  const { code } = await params;
  const countryCode = code.trim().toUpperCase();

  return (
    <section className="py-12 lg:py-16">
      <div className="mx-auto max-w-3xl px-4 sm:px-6 lg:px-8">
        <header className="mb-8">
          <h1 className="text-3xl font-bold tracking-tight text-white sm:text-4xl">
            {t('dashboard.admin.ops.country.title', { code: countryCode })}
          </h1>
          <p className="mt-3 max-w-2xl text-base text-zinc-400">
            {t('dashboard.admin.ops.country.subtitle')}
          </p>
        </header>

        {/* Full-replace hazard banner (review BLOCKER-1 / Gate 4): the PUT is
            full-replace and the form is not pre-filled, so a partial or
            mistyped submit silently overwrites the whole country config.
            warning variant (not info) until the GetCountryConfiguration GET
            follow-up lands. */}
        <Alert variant="warning" className="mb-6">
          <p className="text-sm">{t('dashboard.admin.ops.country.noPrefillNote')}</p>
        </Alert>

        <CountryConfigForm countryCode={countryCode} />
      </div>
    </section>
  );
}
