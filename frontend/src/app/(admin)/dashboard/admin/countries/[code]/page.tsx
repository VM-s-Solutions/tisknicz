import type { Metadata } from 'next';
import { redirect } from 'next/navigation';
import { Alert } from '@/components/ui/alert';
import { Icon } from '@/components/ui/icon';
import { type CountryConfig, getCountryConfig } from '@/lib/api-client-helpers/admin-ops-client';
import { t } from '@/lib/i18n';
import { CountryConfigForm } from './country-config-form';

/**
 * Admin country-config edit (T-0118c §2 / T-0127 re-wire, US-admin-0006).
 * Server Component, `force-dynamic`. The route is keyed on the country code
 * path param (CZ at launch). The editable T-0108 set (VAT/fee/providers/
 * invoicing-mode/shipping-price + mandatory reason) is mutated through the
 * PUT `country-configurations/{code}` endpoint; the provider-change retype
 * gate (A.5) + the unregistered-code rejection + the in-flight advisory are
 * all backend.
 *
 * T-0127 closed the read gap: the page now fetches GetCountryConfiguration
 * SSR (cookie forwarding per patterns.md B.14 / ADR 0024) and pre-fills the
 * form with the current row. This REMOVES the PR-2 silent-overwrite hazard
 * (the operator edits the loaded values, not a blank slate), so the prior
 * WARNING banner downgrades to a brief INFO note that the PUT is still a
 * full-replace. A 404 (no config for the code) → the blank-form + warning-
 * banner fallback (graceful). A non-404 read failure surfaces an error
 * alert and the blank form (the operator can still retype the full set).
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

/** SSR read outcome: the loaded config, a 404 (no row → blank fallback), or a transient error. */
type ConfigResolution =
  | { readonly kind: 'found'; readonly config: CountryConfig }
  | { readonly kind: 'notFound' }
  | { readonly kind: 'error' };

export default async function AdminCountryConfigPage({ params }: PageProps) {
  const { code } = await params;
  const countryCode = code.trim().toUpperCase();

  const result = await getCountryConfig(countryCode);
  if (!result.success && result.error.type === 'Unauthorized') {
    redirect(
      `/admin/login?redirect=${encodeURIComponent(`/dashboard/admin/countries/${encodeURIComponent(code)}`)}`,
    );
  }

  const resolution: ConfigResolution = result.success
    ? { kind: 'found', config: result.value }
    : result.error.code === 'countryConfiguration.notFound'
      ? { kind: 'notFound' }
      : { kind: 'error' };

  const initial = resolution.kind === 'found' ? resolution.config : undefined;

  return (
    <section className="py-12 lg:py-16">
      <div className="mx-auto max-w-3xl px-4 sm:px-6 lg:px-8">
        <header className="mb-8">
          <div className="flex items-center gap-3">
            <span className="icon-tile h-10 w-10 shrink-0" aria-hidden="true">
              <Icon name="globe" size={18} />
            </span>
            <h1 className="text-3xl font-bold tracking-tight text-white sm:text-4xl">
              {t('dashboard.admin.ops.country.title', { code: countryCode })}
            </h1>
          </div>
          <p className="mt-3 max-w-2xl text-base text-zinc-400">
            {t('dashboard.admin.ops.country.subtitle')}
          </p>
        </header>

        {resolution.kind === 'found' ? (
          // Pre-fill removes the silent-overwrite hazard: the PUT is still a
          // full-replace, so a brief INFO note (not a warning) is enough.
          <Alert variant="info" className="mb-6">
            <p className="text-sm">{t('dashboard.admin.ops.country.fullReplaceNote')}</p>
          </Alert>
        ) : resolution.kind === 'notFound' ? (
          // No config row for this code → blank form. Restore the original
          // full-replace WARNING fence (no pre-fill to protect the operator).
          <Alert variant="warning" className="mb-6">
            <p className="text-sm">{t('dashboard.admin.ops.country.noPrefillNote')}</p>
          </Alert>
        ) : (
          // Transient read failure → the operator may still retype the full
          // set; keep the full-replace warning so they verify every field.
          <>
            <Alert variant="error" className="mb-6">
              <p className="text-sm">{t('dashboard.admin.ops.country.loadError')}</p>
            </Alert>
            <Alert variant="warning" className="mb-6">
              <p className="text-sm">{t('dashboard.admin.ops.country.noPrefillNote')}</p>
            </Alert>
          </>
        )}

        <CountryConfigForm countryCode={countryCode} initialConfig={initial} />
      </div>
    </section>
  );
}
