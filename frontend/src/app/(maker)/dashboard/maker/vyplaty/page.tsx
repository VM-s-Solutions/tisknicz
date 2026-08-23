import Link from 'next/link';
import type { Metadata } from 'next';
import { redirect } from 'next/navigation';
import { PageHeader } from '@/components/shared/page-header';
import { Alert } from '@/components/ui/alert';
import { EmptyState } from '@/components/ui/empty-state';
import {
  getMakerPayouts,
  type MakerPayoutsPage,
} from '@/lib/api-client-helpers/payouts-client';
import { getMyMakerProfile } from '@/lib/api-client-helpers/profile';
import { t } from '@/lib/i18n';
import { resolveErrorMessage } from '@/lib/runtime/errors';
import type { ApiError } from '@/lib/runtime/result';
import { Pagination } from './pagination';
import { PayoutRows } from './payout-row';

/**
 * Maker dashboard payout list (T-0116, US-maker-0012). Server Component:
 * `page` lives in URL searchParams (no client store), the SSR fetch forwards
 * the maker audience cookie (patterns.md B.14 / ADR 0024), pagination is
 * `<Link>`-based per B.8. Sort is fixed server-side (CompletedAt DESC) — no
 * UI control (Q5 lock). Two-value state mapping (no Pending). NO CSV
 * affordance anywhere (A.1 / AC-10).
 */

export function generateMetadata(): Metadata {
  return {
    title: t('dashboard.maker.payouts.metadata.title'),
    description: t('dashboard.maker.payouts.metadata.description'),
  };
}

// Always render fresh — the list reflects settlements that just happened.
export const dynamic = 'force-dynamic';

const ROUTE_PATH = '/dashboard/maker/vyplaty';

interface PageProps {
  readonly searchParams: Promise<Record<string, string | string[] | undefined>>;
}

function readString(value: string | string[] | undefined): string {
  if (Array.isArray(value)) return value[0] ?? '';
  return value ?? '';
}

function parsePositiveInt(raw: string, fallback: number): number {
  const parsed = Number.parseInt(raw, 10);
  if (!Number.isFinite(parsed) || parsed < 1) return fallback;
  return parsed;
}

export default async function MakerPayoutsPage({ searchParams }: PageProps) {
  const sp = await searchParams;
  // Junk (page=0, page=abc) clamps to 1 — backend Validator stays authoritative.
  const page = parsePositiveInt(readString(sp.page), 1);

  // The payout page never said what a maker most needs to know: that we
  // cannot pay them at all without a bank account (T-0173, audit MAKER-M4).
  // Read alongside the batches — a failed profile read must not break the
  // page, so the banner simply doesn't render.
  const [result, profileResult] = await Promise.all([
    getMakerPayouts({ page }),
    getMyMakerProfile('maker'),
  ]);
  const bankAccountMissing =
    profileResult.success && (profileResult.value.bankAccount ?? '').trim() === '';

  if (!result.success && result.error.type === 'Unauthorized') {
    redirect(`/login?redirect=${encodeURIComponent(ROUTE_PATH)}`);
  }

  return (
    <section className="py-12 lg:py-16">
      <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
        <PageHeader
          title={t('dashboard.maker.payouts.title')}
          subtitle={t('dashboard.maker.payouts.subtitle')}
        />

        {bankAccountMissing ? (
          <div className="mt-6">
            <Alert variant="warning">
              <div className="flex flex-col gap-2">
                <div>
                  <p className="font-semibold">
                    {t('dashboard.maker.payouts.bank_missing.title')}
                  </p>
                  <p className="mt-1 text-sm">
                    {t('dashboard.maker.payouts.bank_missing.body')}
                  </p>
                </div>
                <Link
                  href="/dashboard/maker/profil"
                  className="w-fit text-sm font-semibold underline underline-offset-2"
                >
                  {t('dashboard.maker.payouts.bank_missing.cta')}
                </Link>
              </div>
            </Alert>
          </div>
        ) : null}

        {/* MAKER-M3 (copy half): the page listed batches that already exist
            but never explained the cadence, so "how much am I owed and when"
            had no answer at all. The accrued-balance READ is T-0179. */}
        <p className="mt-6 text-sm text-zinc-500">
          {t('dashboard.maker.payouts.cadence_note')}
        </p>

        <div className="mt-8">
          {result.success ? (
            <PayoutsResults data={result.value} />
          ) : (
            <PayoutsError error={result.error} />
          )}
        </div>
      </div>
    </section>
  );
}

function PayoutsResults({ data }: { readonly data: MakerPayoutsPage }) {
  if (data.totalCount === 0) {
    return <PayoutsEmpty />;
  }

  const totalPages = data.totalPages ?? 1;
  const hasNext = data.hasNextPage ?? false;
  const hasPrevious = data.hasPreviousPage ?? false;

  return (
    <>
      <PayoutRows items={data.items} totalCount={data.totalCount} />
      <Pagination
        page={data.page}
        totalPages={totalPages}
        hasNext={hasNext}
        hasPrevious={hasPrevious}
      />
    </>
  );
}

function PayoutsEmpty() {
  return (
    <EmptyState
      icon="wallet"
      title={t('dashboard.maker.payouts.empty.title')}
      description={t('dashboard.maker.payouts.empty.description')}
    />
  );
}

function PayoutsError({ error }: { readonly error: ApiError }) {
  return (
    <Alert variant="error">
      <div className="flex flex-col gap-3">
        <div>
          <p className="font-semibold">{t('dashboard.maker.payouts.error.title')}</p>
          <p className="mt-1 text-sm opacity-90">{resolveErrorMessage(error)}</p>
        </div>
        <Link
          href={ROUTE_PATH}
          className="inline-flex w-fit items-center gap-2 rounded-lg border border-error/40 px-4 py-2 text-sm font-semibold text-error transition-colors hover:bg-error-fill-soft"
        >
          {t('dashboard.maker.payouts.error.retry')}
        </Link>
      </div>
    </Alert>
  );
}
