import Link from 'next/link';
import type { Metadata } from 'next';
import { notFound, redirect } from 'next/navigation';
import { Alert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Card } from '@/components/ui/card';
import { Icon } from '@/components/ui/icon';
import { getAdminMakerDetail } from '@/lib/api-client-helpers/admin-makers';
import { getCountryConfig } from '@/lib/api-client-helpers/admin-ops-client';
import { t } from '@/lib/i18n';
import { MakerAdminActions } from './maker-admin-actions';
import { MakerFeeOverrideForm } from './maker-fee-override-form';

/**
 * Admin maker detail (T-0119b closes the T-0140 read gap). Server
 * Component, `force-dynamic`. Renders the privileged maker header from
 * the real `GET /makers/{id}` read (the T-0140 read-gap banner is gone
 * — the effective fee rate `override ?? country default` displays for
 * real now), the T-0034 judgment-call actions (verify / deactivate /
 * refresh-ARES), and the T-0140 fee-override form. The detail READ is
 * PII-audited server-side (`maker.detail.view`, T-0137).
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

function formatPercent(bp: number): string {
  return `${(bp / 100).toString().replace('.', ',')} %`;
}

export default async function AdminMakerDetailPage({ params, searchParams }: PageProps) {
  const { id } = await params;
  const { country } = await searchParams;
  const makerId = decodeURIComponent(id);
  const countryCode = (country ?? DEFAULT_COUNTRY_CODE).trim().toUpperCase();

  const detailResult = await getAdminMakerDetail(makerId);
  if (!detailResult.success) {
    if (detailResult.error.type === 'Unauthorized') {
      redirect(
        `/admin/login?redirect=${encodeURIComponent(`/dashboard/admin/makers/${encodeURIComponent(makerId)}`)}`,
      );
    }
    if (detailResult.error.type === 'NotFound') {
      notFound();
    }
    return (
      <section className="py-12 lg:py-16">
        <div className="mx-auto max-w-3xl px-4 sm:px-6 lg:px-8">
          <Alert variant="error">{t('dashboard.admin.ops.makers.detail.loadError')}</Alert>
        </div>
      </section>
    );
  }

  const maker = detailResult.value.maker;

  const configResult = await getCountryConfig(countryCode);
  const countryDefaultBp = configResult.success
    ? configResult.value.platformFeeRateBp
    : undefined;
  const effectiveBp = maker.feeRateOverrideBp ?? countryDefaultBp;

  return (
    <section className="py-12 lg:py-16">
      <div className="mx-auto flex max-w-3xl flex-col gap-6 px-4 sm:px-6 lg:px-8">
        <div>
          <Link
            href="/dashboard/admin/makers"
            className="inline-flex items-center gap-2 text-sm font-medium text-zinc-400 transition-colors hover:text-white"
          >
            <Icon name="arrowLeft" size={16} />
            {t('dashboard.admin.ops.makers.detail.back')}
          </Link>
        </div>

        <header>
          <div className="flex flex-wrap items-center gap-3">
            <h1 className="text-3xl font-bold tracking-tight text-white sm:text-4xl">
              {maker.companyName}
            </h1>
            {maker.isVerified ? (
              <Badge variant="success">{t('dashboard.admin.ops.makers.badge.verified')}</Badge>
            ) : (
              <Badge variant="warning">{t('dashboard.admin.ops.makers.badge.unverified')}</Badge>
            )}
            {!maker.isActive ? (
              <Badge variant="error">{t('dashboard.admin.ops.makers.badge.inactive')}</Badge>
            ) : null}
            {maker.snapshotIsStale ? (
              <Badge variant="warning">{t('dashboard.admin.ops.makers.badge.staleSnapshot')}</Badge>
            ) : null}
          </div>
          <p className="mt-2 text-sm text-zinc-400">{maker.userEmail}</p>
        </header>

        <Card className="grid grid-cols-2 gap-x-6 gap-y-3 sm:grid-cols-3">
          <HeaderField label={t('dashboard.admin.ops.makers.detail.ico')} value={maker.registrationNumber} />
          <HeaderField label={t('dashboard.admin.ops.makers.detail.dic')} value={maker.vatId ?? '—'} />
          <HeaderField label={t('dashboard.admin.ops.makers.detail.legalForm')} value={maker.legalForm ?? '—'} />
          <HeaderField label={t('dashboard.admin.ops.makers.detail.city')} value={maker.city} />
          <HeaderField label={t('dashboard.admin.ops.makers.detail.slug')} value={`/${maker.slug}`} />
          <HeaderField
            label={t('dashboard.admin.ops.makers.detail.orders')}
            value={String(maker.totalOrders)}
          />
          <HeaderField
            label={t('dashboard.admin.ops.makers.detail.rating')}
            value={
              maker.ratingCount > 0
                ? `${(maker.ratingAverageBp / 10000).toFixed(1)} (${maker.ratingCount})`
                : '—'
            }
          />
          <HeaderField
            label={t('dashboard.admin.ops.makers.detail.effectiveFee')}
            value={effectiveBp !== undefined ? formatPercent(effectiveBp) : '—'}
          />
          <HeaderField
            label={t('dashboard.admin.ops.makers.detail.feeOverride')}
            value={
              maker.feeRateOverrideBp !== null
                ? formatPercent(maker.feeRateOverrideBp)
                : t('dashboard.admin.ops.makers.detail.feeOverrideNone')
            }
          />
        </Card>

        {!maker.isActiveInRegistry ? (
          <Alert variant="warning">{t('dashboard.admin.ops.makers.detail.dissolvedWarning')}</Alert>
        ) : null}

        <MakerAdminActions makerId={maker.makerId} isVerified={maker.isVerified} isActive={maker.isActive} />

        {!configResult.success ? (
          <Alert variant="error">
            <p className="text-sm">{t('dashboard.admin.ops.makers.detail.countryDefaultUnavailable')}</p>
          </Alert>
        ) : null}

        <MakerFeeOverrideForm
          makerId={maker.makerId}
          countryCode={countryCode}
          countryDefaultBp={countryDefaultBp}
        />
      </div>
    </section>
  );
}

function HeaderField({ label, value }: { readonly label: string; readonly value: string }) {
  return (
    <div>
      <p className="text-xs font-semibold uppercase tracking-wide text-zinc-500">{label}</p>
      <p className="mt-0.5 truncate text-sm text-zinc-200">{value}</p>
    </div>
  );
}
