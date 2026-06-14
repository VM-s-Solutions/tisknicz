import Link from 'next/link';
import type { Metadata } from 'next';
import { redirect } from 'next/navigation';
import { Alert } from '@/components/ui/alert';
import { Icon } from '@/components/ui/icon';
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
    <section className="bg-surface-primary py-12 lg:py-16">
      <div className="mx-auto max-w-4xl px-4 sm:px-6 lg:px-8">
        <header className="mb-8">
          <h1 className="text-3xl font-bold tracking-tight text-white sm:text-4xl">
            {t('dashboard.maker.reviews.title')}
          </h1>
          <p className="mt-3 max-w-2xl text-base text-zinc-400">
            {t('dashboard.maker.reviews.subtitle')}
          </p>
        </header>

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
          <div className="mt-8 flex flex-col gap-4">
            {data.items.map((review) => (
              <ReviewCard key={review.reviewId} review={review} />
            ))}
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
    <div className="flex flex-wrap items-center gap-4 rounded-2xl border border-zinc-800 bg-surface-card px-5 py-4">
      <StarRating value={average} size="md" />
      <span className="text-lg font-semibold text-white">{average.toFixed(1)}</span>
      <span className="text-sm text-zinc-400">
        {t('dashboard.maker.reviews.aggregate.count', { count: ratingCount })}
      </span>
    </div>
  );
}

function ReviewsEmpty() {
  return (
    <div className="mt-8 flex flex-col items-center justify-center gap-4 rounded-2xl border border-dashed border-zinc-800 bg-surface-card px-6 py-20 text-center">
      <div className="flex h-14 w-14 items-center justify-center rounded-2xl bg-zinc-800 text-zinc-500">
        <Icon name="star" size={28} />
      </div>
      <div>
        <h2 className="text-lg font-semibold text-zinc-100">
          {t('dashboard.maker.reviews.empty.title')}
        </h2>
        <p className="mt-2 max-w-md text-sm text-zinc-400">
          {t('dashboard.maker.reviews.empty.description')}
        </p>
      </div>
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
          className="inline-flex w-fit items-center gap-2 rounded-xl border border-red-800/50 px-4 py-2 text-sm font-semibold text-red-300 transition-colors hover:bg-red-950"
        >
          {t('dashboard.maker.reviews.error.retry')}
        </Link>
      </div>
    </Alert>
  );
}
