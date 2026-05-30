/**
 * Hand-written wrappers around the anonymous public catalog endpoints
 * exposed by .NET <c>CatalogController</c> at <c>/api/v1/catalog/*</c>.
 *
 * Same convention as <c>profile.ts</c>: we call <see cref="apiFetch"/>
 * directly (not the NSwag-generated <c>PublicApi</c> class) because
 * <c>apiFetch</c> returns <c>Result&lt;T, ApiError&gt;</c> and the
 * generated client throws on every non-2xx — that doesn't fit the
 * Result flow used everywhere else in the app. The DTOs below mirror
 * the records in
 * <c>backend/src/Makables.Core.Domain/Catalog/ICatalogQueries.cs</c>;
 * keep them in sync with the C# records when adding new fields.
 *
 * The catalog endpoints are anonymous — no auth header required.
 */

import { apiFetch } from '../runtime/api-fetch';
import type { ApiError, Result } from '../runtime/result';

const Base = '/api/v1/catalog';

// ---- DTOs (mirror Makables.Core.Domain.Catalog records) ----

/**
 * Mirror of <c>PagedData&lt;T&gt;</c> in
 * <c>Makables.Core.Domain.Common</c>. Backend computes
 * <c>TotalPages</c>, <c>HasNext</c>, <c>HasPrevious</c>; we surface them
 * directly so no client-side pagination math is needed.
 */
export interface PagedData<T> {
  readonly items: readonly T[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
  readonly totalPages: number;
  readonly hasNext: boolean;
  readonly hasPrevious: boolean;
}

/**
 * Mirror of <c>MakerListItem</c>. <c>ratingAverageBp</c> is basis-points
 * (0..50000); divide by 1000 for a 0.0–5.0 star display.
 */
export interface MakerListItem {
  readonly makerId: string;
  readonly slug: string;
  readonly companyName: string;
  readonly bio: string | null;
  readonly city: string;
  readonly isVerified: boolean;
  readonly ratingAverageBp: number;
  readonly ratingCount: number;
  readonly totalOrders: number;
}

// ---- Input ----

/**
 * Frontend-side filter input for <see cref="getPagedMakers"/>. Mirrors
 * the query-string contract of <c>CatalogController.GetMakers</c>; all
 * fields are optional except <c>page</c> / <c>pageSize</c> which default
 * to <c>1</c> / <c>24</c>.
 */
export interface CatalogFilterInput {
  readonly country?: string;
  readonly category?: string;
  readonly city?: string;
  readonly minRatingStars?: number;
  readonly page?: number;
  readonly pageSize?: number;
}

/**
 * Backend default + cap (see <c>GetPagedMakers.DefaultPageSize</c> /
 * <c>MaxPageSize</c>). Re-stated here so URL builders stay typed without
 * a round-trip; if the backend changes these the parity check between
 * controller and DTO contract still owns the truth.
 */
export const CATALOG_DEFAULT_PAGE_SIZE = 24;
export const CATALOG_MAX_PAGE_SIZE = 48;

/**
 * Paged maker list for the public catalog. Anonymous — no session
 * required. Backend filters inactive / unconfirmed makers; do not
 * re-filter on the client.
 */
export async function getPagedMakers(
  input: CatalogFilterInput,
): Promise<Result<PagedData<MakerListItem>, ApiError>> {
  const params = new URLSearchParams();
  params.set('country', input.country ?? 'CZ');
  if (input.category) params.set('category', input.category);
  if (input.city) params.set('city', input.city);
  if (input.minRatingStars !== undefined) {
    params.set('minRating', String(input.minRatingStars));
  }
  params.set('page', String(input.page ?? 1));
  params.set('pageSize', String(input.pageSize ?? CATALOG_DEFAULT_PAGE_SIZE));

  return apiFetch<PagedData<MakerListItem>>(
    'public',
    `${Base}/makers?${params.toString()}`,
    { method: 'GET' },
  );
}
