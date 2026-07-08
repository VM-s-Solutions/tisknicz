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
 * Mirror of <c>Makables.Core.Domain.Products.FulfillmentType</c>
 * (T-0144). "Na zakázku" vs. "skladem" — drives the product-detail
 * badge and the checkout withdrawal-right notice.
 */
export type FulfillmentType = 'MadeToOrder' | 'InStock';

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

/**
 * Mirror of <c>MakerProductItem</c> — an active product as shown on the
 * maker's profile and (forward-compat) other product-list surfaces.
 * <c>priceType</c> is <c>"Fixed" | "From" | "OnRequest"</c>;
 * <c>fulfillmentType</c> is <c>"MadeToOrder" | "InStock"</c> (T-0144);
 * <c>primaryImageBlobPath</c> is the blob storage path (use
 * <see cref="buildProductImageUrl"/> to build a renderable URL).
 */
export interface MakerProductItem {
  readonly productId: string;
  readonly title: string;
  readonly priceAmountMinor: number;
  readonly priceCurrency: string;
  readonly priceType: 'Fixed' | 'From' | 'OnRequest';
  readonly fulfillmentType: FulfillmentType;
  readonly primaryImageBlobPath: string | null;
}

/**
 * Mirror of <c>MakerReviewItem</c>. Empty list until T-0050 ships the
 * review producer; kept on the contract so the maker-profile page is
 * forward-compatible.
 */
export interface MakerReviewItem {
  readonly reviewId: string;
  readonly ratingStars: number;
  readonly comment: string | null;
  readonly createdAt: string;
}

/**
 * Mirror of <c>MakerProfile</c> (US-customer-0008). Header fields +
 * active products newest-first + (deferred) recent reviews.
 */
export interface MakerProfile {
  readonly makerId: string;
  readonly slug: string;
  readonly companyName: string;
  readonly bio: string | null;
  readonly legalForm: string | null;
  readonly city: string;
  readonly isVerified: boolean;
  readonly personalPickupEnabled: boolean;
  readonly pickupNote: string | null;
  readonly ratingAverageBp: number;
  readonly ratingCount: number;
  readonly totalOrders: number;
  readonly products: readonly MakerProductItem[];
  readonly reviews: readonly MakerReviewItem[];
}

/**
 * Mirror of <c>ProductImageItem</c>. One image on the product detail
 * page; the backend orders the list by <c>sortOrder</c> ascending, so
 * the consumer renders the list as-is (initial index 0 = primary).
 */
export interface ProductImageItem {
  readonly imageId: string;
  readonly blobPath: string;
  readonly sortOrder: number;
}

/**
 * Mirror of <c>ProductDetail</c> (US-customer-0009). Product fields +
 * all images + the owning maker's display info ("by {maker}" link back
 * to the profile page). The backend gates inactive products and
 * non-publicly-listable makers at the query level — a 404 from the
 * controller maps to <see cref="ApiError"/> with <c>type === 'NotFound'</c>
 * regardless of which condition failed (no oracle leakage).
 */
export interface ProductDetail {
  readonly productId: string;
  readonly title: string;
  readonly description: string | null;
  readonly priceAmountMinor: number;
  readonly priceCurrency: string;
  readonly priceType: 'Fixed' | 'From' | 'OnRequest';
  readonly fulfillmentType: FulfillmentType;
  readonly weightGrams: number;
  readonly categoryId: string;
  readonly makerId: string;
  readonly makerSlug: string;
  readonly makerCompanyName: string;
  readonly makerIsVerified: boolean;
  readonly images: readonly ProductImageItem[];
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
 * Basis-points-per-star scale used by the backend's denormalized
 * <c>Maker.RatingAverageBp</c> field. Mirrors
 * <c>CatalogQueries.BpPerStar</c> (Infra/Catalog) — one star = 10 000
 * basis points, so the 0..5-star display value is
 * <c>RatingAverageBp / RATING_BP_PER_STAR</c>. Centralised here so
 * every catalog surface (list cards, profile header, future product
 * detail) computes the display the same way.
 */
export const RATING_BP_PER_STAR = 10_000;

// ---- Endpoints ----

/**
 * Paged maker list for the public catalog (US-customer-0007). Anonymous
 * — no session required. Backend filters inactive / unconfirmed makers;
 * do not re-filter on the client.
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

/**
 * Public maker profile by slug (US-customer-0008). The backend returns
 * 404 for inactive / unconfirmed makers and unknown slugs alike — the
 * caller decides whether to <c>notFound()</c> or render a soft error.
 */
export async function getMakerBySlug(
  slug: string,
): Promise<Result<MakerProfile, ApiError>> {
  return apiFetch<MakerProfile>(
    'public',
    `${Base}/makers/${encodeURIComponent(slug)}`,
    { method: 'GET' },
  );
}

/**
 * Public product detail by id (US-customer-0009). The backend returns
 * 404 for inactive products and products whose owning maker isn't
 * publicly-listable alike — the caller decides whether to
 * <c>notFound()</c> or render a soft error. <paramref name="productId"/>
 * is URL-encoded before being placed on the path; the backend route
 * accepts the raw id (no slug shape).
 */
export async function getProductById(
  productId: string,
): Promise<Result<ProductDetail, ApiError>> {
  return apiFetch<ProductDetail>(
    'public',
    `${Base}/products/${encodeURIComponent(productId)}`,
    { method: 'GET' },
  );
}

/**
 * Build the public image URL for a product's primary image. The blob
 * path on the DTO is <c>{country}/products/{productId}/{filename}</c>;
 * the controller route is
 * <c>/api/v1/files/products/{country}/{productId}/{filename}</c>
 * (see <c>Makables.Web.Public.Controllers.ProductImageController</c>) —
 * the blob path already carries the <c>products/</c> segment so we
 * strip it once to avoid doubling.
 *
 * Returns <c>null</c> for missing paths so callers can render a
 * placeholder.
 */
export function buildProductImageUrl(blobPath: string | null | undefined): string | null {
  if (!blobPath) return null;
  // Defense-in-depth: reject any path segment that could traverse out
  // of /products/ (T-0047 security review — non-exploitable because
  // next/image anchors on remotePatterns.hostname, but better to refuse
  // a suspicious blob path than emit a URL the optimizer will normalize
  // to a same-host 404).
  if (/(^|\/)\.\.(\/|$)/.test(blobPath)) return null;
  const baseUrl =
    process.env.NEXT_PUBLIC_API_PUBLIC_BASE_URL?.replace(/\/+$/, '') ??
    'http://localhost:5104';
  const normalised = blobPath
    .replace(/^\/+/, '')
    .replace(/^([^/]+)\/products\//, '$1/');
  return `${baseUrl}/api/v1/files/products/${normalised}`;
}
