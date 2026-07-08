import type { Metadata } from 'next';
import { redirect } from 'next/navigation';
import { Alert } from '@/components/ui/alert';
import { getCountryConfig } from '@/lib/api-client-helpers/admin-ops-client';
import { t } from '@/lib/i18n';
import { MakerFeeOverrideForm } from './maker-fee-override-form';

/**
 * Admin maker fee-rate override (T-0140, US-admin-0018). Server Component,
 * `force-dynamic`. Reached from the `/dashboard/admin/makers` id-lookup
 * (there is no maker LIST/detail READ endpoint yet — see the read-gap
 * banner below) or by pasting a known maker id directly into the URL.
 *
 * The backend shipped ONLY the write side of this ticket
 * (`SetMakerFeeOverride` — set/clear the override, audited). There is no
 * admin `GetMaker` read endpoint to pre-fill "is an override currently
 * active, and what is it". Per CLAUDE.md's "no mocks" rule this page does
 * NOT fabricate that read: it fetches the real
 * `CountryConfiguration.PlatformFeeRateBp` (the CZ-launch default, T-0127's
 * existing `getCountryConfig`) so the admin can see the rate ceiling the
 * backend Validator enforces, and it surfaces a visible gap banner instead
 * of a fake "current rate" — the same shape as the `users` lookup gap note
 * for the missing find-by-email/name read. A follow-up ticket adding an
 * admin maker-detail read should replace this banner with the real
 * effective-rate display (override ?? country default).
 */

export function generateMetadata(): Metadata {
  return {
    title: t('dashboard.admin.ops.makers.metadata.title'),
    description: t('dashboard.admin.ops.makers.metadata.description'),
  };
}

export const dynamic = 'force-dynamic';

const DEFAULT_COUNTRY_CODE = 'CZ';

interface PageProps {
  readonly params: Promise<{ readonly id: string }>;
  readonly searchParams: Promise<{ readonly country?: string }>;
}

export default async function AdminMakerFeeOverridePage({ params, searchParams }: PageProps) {
  const { id } = await params;
  const { country } = await searchParams;
  const makerId = decodeURIComponent(id);
  const countryCode = (country ?? DEFAULT_COUNTRY_CODE).trim().toUpperCase();

  const result = await getCountryConfig(countryCode);
  if (!result.success && result.error.type === 'Unauthorized') {
    redirect(
      `/admin/login?redirect=${encodeURIComponent(`/dashboard/admin/makers/${encodeURIComponent(makerId)}`)}`,
    );
  }

  const countryDefaultBp = result.success ? result.value.platformFeeRateBp : undefined;

  return (
    <section className="py-12 lg:py-16">
      <div className="mx-auto max-w-3xl px-4 sm:px-6 lg:px-8">
        <header className="mb-8">
          <h1 className="text-3xl font-bold tracking-tight text-white sm:text-4xl">
            {t('dashboard.admin.ops.makers.detail.title', { makerId })}
          </h1>
          <p className="mt-3 max-w-2xl text-base text-zinc-400">
            {t('dashboard.admin.ops.makers.detail.subtitle')}
          </p>
        </header>

        <Alert variant="warning" className="mb-6">
          <p className="text-sm">{t('dashboard.admin.ops.makers.detail.readGapNote')}</p>
        </Alert>

        {!result.success && result.error.type !== 'Unauthorized' ? (
          <Alert variant="error" className="mb-6">
            <p className="text-sm">{t('dashboard.admin.ops.makers.detail.countryDefaultUnavailable')}</p>
          </Alert>
        ) : null}

        <MakerFeeOverrideForm
          makerId={makerId}
          countryCode={countryCode}
          countryDefaultBp={countryDefaultBp}
        />
      </div>
    </section>
  );
}
