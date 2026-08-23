import Link from 'next/link';
import type { Metadata } from 'next';
import { redirect } from 'next/navigation';
import { PageHeader } from '@/components/shared/page-header';
import { Alert } from '@/components/ui/alert';
import { EmptyState } from '@/components/ui/empty-state';
import { StarRating } from '@/components/ui/star-rating';
import { RATING_BP_PER_STAR } from '@/lib/api-client-helpers/catalog';
import { getMakerReviews, type MakerReviewsPage } from '@/lib/api-client-helpers/reviews-client';
import { t } from '@/lib/i18n';
import { resolveErrorMessage } from '@/lib/runtime/errors';
import type { ApiError } from '@/lib/runtime/result';
import { Pagination } from './pagination';
import { ReviewCard } from './review-card';

/**
 * Maker dashboard review list (T-0117, US-maker-0014). Server Component:
 * `page` lives in URL searchParams (no client store), the SSR fetch
 * forwards the maker audience cookie (patterns.md B.14 / ADR 0024),
 * pagination is `<Link>`-based per B.8. The only client island is the
 * reply form inside each card. The aggregate header reads the maker's
 * authoritative `ratingAverageBp` / `ratingCount` off the response
 * envelope (Q5 — no client math over the paged window, §A.3 / AC-6).
 */

export function generateMetadata(): Metadata {
  return {
    title: t('dashboard.maker.reviews.metadata.title'),
    description: t('dashboard.maker.reviews.metadata.description'),
  };
}

// Always render fresh — a reply submit re-syncs via router.refresh().
export const dynamic = 'force-dynamic';

const ROUTE_PATH = '/dashboard/maker/recenze';

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

export default async function MakerReviewsPage({ searchParams }: PageProps) {
  const sp = await searchParams;
  // Junk (page=0, page=abc) clamps to 1 — backend Validator stays authoritative.
  const page = parsePositiveInt(readString(sp.page), 1);

  const result = await getMakerReviews({ page });

  if (!result.success && result.error.type === 'Unauthorized') {
    redirect(`/login?redirect=${encodeURIComponent(ROUTE_PATH)}`);
  }

  return (
    <section className="py-12 lg:py-16">
      <div className="mx-auto max-w-4xl px-4 sm:px-6 lg:px-8">
        <PageHeader
          title={t('dashboard.maker.reviews.title')}
          subtitle={t('dashboard.maker.reviews.subtitle')}
        />

        <div className="mt-8">
          {result.success ? (
            <ReviewsResults data={result.value} />
          ) : (
            <ReviewsError error={result.error} />
          )}
        </div>
      </div>
    </section>
  );
}

function ReviewsResults({ data }: { readonly data: MakerReviewsPage }) {
  return (
    <>
      <AggregateHeader ratingAverageBp={data.ratingAverageBp} ratingCount={data.ratingCount} />

      {data.totalCount === 0 ? (
        <ReviewsEmpty />
      ) : (
        <>
          <div className="mt-8 overflow-hidden rounded-xl border border-zinc-800 bg-surface-card">
            <header className="flex flex-wrap items-center justify-between gap-3 border-b border-zinc-800 bg-surface-secondary/60 px-4 py-3 sm:px-5">
              <h2 className="text-xs font-semibold uppercase tracking-widest text-zinc-500">
                {t('dashboard.maker.reviews.title')}
              </h2>
              <span className="text-sm text-zinc-500">
                {t('dashboard.maker.reviews.aggregate.count', { count: data.totalCount })}
              </span>
            </header>
            <div className="divide-y divide-zinc-800">
              {data.items.map((review) => (
                <ReviewCard key={review.reviewId} review={review} />
              ))}
            </div>
          </div>
          <Pagination
            page={data.page}
            totalPages={data.totalPages ?? 1}
            hasNext={data.hasNextPage ?? false}
            hasPrevious={data.hasPreviousPage ?? false}
          />
        </>
      )}
    </>
  );
}

/**
 * Live aggregate header — reads the maker's authoritative fields off the
 * envelope (Q5). `ratingAverageBp / RATING_BP_PER_STAR` is the same bp→0–5
 * presentation conversion the existing `Stars` precedent does; NO division
 * over the listed page items (AC-6).
 */
function AggregateHeader({
  ratingAverageBp,
  ratingCount,
}: {
  readonly ratingAverageBp: number;
  readonly ratingCount: number;
}) {
  if (ratingCount === 0) {
    return (
      <p className="text-sm text-zinc-500">{t('dashboard.maker.reviews.aggregate.none')}</p>
    );
  }
  const average = ratingAverageBp / RATING_BP_PER_STAR;
  return (
    <div className="panel flex flex-wrap items-center gap-4 rounded-xl border border-zinc-800 px-5 py-4">
      <StarRating value={average} size="md" />
      <span className="text-xl font-bold text-zinc-50">{average.toFixed(1)}</span>
      <span className="text-sm text-zinc-400">
        {t('dashboard.maker.reviews.aggregate.count', { count: ratingCount })}
      </span>
    </div>
  );
}

function ReviewsEmpty() {
  return (
    <div className="mt-8">
      <EmptyState
        icon="star"
        title={t('dashboard.maker.reviews.empty.title')}
        description={t('dashboard.maker.reviews.empty.description')}
      />
    </div>
  );
}

function ReviewsError({ error }: { readonly error: ApiError }) {
  return (
    <Alert variant="error">
      <div className="flex flex-col gap-3">
        <div>
          <p className="font-semibold">{t('dashboard.maker.reviews.error.title')}</p>
          <p className="mt-1 text-sm opacity-90">{resolveErrorMessage(error)}</p>
        </div>
        <Link
          href={ROUTE_PATH}
          className="inline-flex w-fit items-center gap-2 rounded-lg border border-error/40 px-4 py-2 text-sm font-semibold text-error transition-colors hover:bg-tint-error-strong hover:text-on-tint-error"
        >
          {t('dashboard.maker.reviews.error.retry')}
        </Link>
      </div>
    </Alert>
  );
}
