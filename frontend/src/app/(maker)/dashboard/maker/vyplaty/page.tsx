import Link from 'next/link';
import type { Metadata } from 'next';
import { redirect } from 'next/navigation';
import { Alert } from '@/components/ui/alert';
import { Icon } from '@/components/ui/icon';
import {
  getMakerPayouts,
  type MakerPayoutsPage,
} from '@/lib/api-client-helpers/payouts-client';
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

  const result = await getMakerPayouts({ page });

  if (!result.success && result.error.type === 'Unauthorized') {
    redirect(`/login?redirect=${encodeURIComponent(ROUTE_PATH)}`);
  }

  return (
    <section className="bg-surface-primary py-12 lg:py-16">
      <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
        <header className="mb-8">
          <h1 className="text-3xl font-bold tracking-tight text-white sm:text-4xl">
            {t('dashboard.maker.payouts.title')}
          </h1>
          <p className="mt-3 max-w-2xl text-base text-zinc-400">
            {t('dashboard.maker.payouts.subtitle')}
          </p>
        </header>

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
      <p className="mb-6 text-sm text-zinc-500">
        {t('dashboard.maker.payouts.count', { count: data.totalCount })}
      </p>
      <PayoutRows items={data.items} />
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
    <div className="flex flex-col items-center justify-center gap-4 rounded-2xl border border-dashed border-zinc-800 bg-surface-card px-6 py-20 text-center">
      <div className="flex h-14 w-14 items-center justify-center rounded-2xl bg-zinc-800 text-zinc-500">
        <Icon name="creditCard" size={28} />
      </div>
      <div>
        <h2 className="text-lg font-semibold text-zinc-100">
          {t('dashboard.maker.payouts.empty.title')}
        </h2>
        <p className="mt-2 max-w-md text-sm text-zinc-400">
          {t('dashboard.maker.payouts.empty.description')}
        </p>
      </div>
    </div>
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
          className="inline-flex w-fit items-center gap-2 rounded-xl border border-red-800/50 px-4 py-2 text-sm font-semibold text-red-300 transition-colors hover:bg-red-950"
        >
          {t('dashboard.maker.payouts.error.retry')}
        </Link>
      </div>
    </Alert>
  );
}
