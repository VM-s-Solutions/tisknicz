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
 * Mirror of <c>Makables.Core.Domain.Makers.MakerLegalType</c>. Whether a
 * maker trades as a company ("Firma") or as an individual trader
 * ("Živnostník"). Every maker holds an IČO, so
 * <c>NaturalPerson</c> means OSVČ — never "private seller".
 *
 * A maker whose legal form the registry adapter could not classify is
 * NULL on the backend and matches neither value, so it appears only in
 * the unfiltered list. There is deliberately no third "unknown" member
 * to select.
 */
export type MakerLegalType = 'LegalEntity' | 'NaturalPerson';

/**
 * Mirror of <c>PagedData&lt;T&gt;</c> in
 * <c>Makables.Core.Domain.Common</c>. Backend computes
 * <c>TotalPages</c>, <c>HasNextPage</c>, <c>HasPreviousPage</c>; we
 * surface them directly so no client-side pagination math is needed.
 *
 * NOTE the <c>Page</c> suffix on the two booleans — it is the wire name
 * (see the C# computed properties and the NSwag-generated
 * <c>PagedDataOfMakerListItem</c>). This mirror previously declared them
 * as <c>hasNext</c> / <c>hasPrevious</c>, which typed as <c>boolean</c>
 * but resolved to <c>undefined</c> at runtime, so every catalog
 * prev/next control rendered permanently disabled and the sitemap
 * stopped after one page. Keep these names in sync with the C# record.
 */
export interface PagedData<T> {
  readonly items: readonly T[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
  readonly totalPages: number;
  readonly hasNextPage: boolean;
  readonly hasPreviousPage: boolean;
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
  /** Blob path of the maker's logo; use {@link buildMakerLogoUrl}. Null → initial tile. */
  readonly logoBlobPath: string | null;
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
  /** Denormalized product rating in basis points (0..50000); see RATING_BP_PER_STAR. */
  readonly ratingAverageBp: number;
  readonly ratingCount: number;
  readonly primaryImageBlobPath: string | null;
}

/**
 * Mirror of <c>MakerReviewItem</c> (T-0050). The latest 5 active
 * reviews, newest-first, with the maker's reply when one exists (flat
 * nullable fields — one overwritable reply per review).
 */
export interface MakerReviewItem {
  readonly reviewId: string;
  readonly ratingStars: number;
  readonly comment: string | null;
  readonly createdAt: string;
  readonly replyBody: string | null;
  readonly replyCreatedAt: string | null;
  /**
   * The review author's avatar blob path; use {@link buildAvatarUrl}.
   * Null when they never uploaded one, deactivated, or were erased —
   * the only author-identifying field on this DTO by design (see the
   * C# record's remarks).
   */
  readonly authorAvatarBlobPath: string | null;
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
  /** Blob path of the maker's logo; use {@link buildMakerLogoUrl}. Null → initial tile. */
  readonly logoBlobPath: string | null;
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
  /** Denormalized product rating in basis points (0..50000); see RATING_BP_PER_STAR. */
  readonly ratingAverageBp: number;
  readonly ratingCount: number;
  readonly makerId: string;
  readonly makerSlug: string;
  readonly makerCompanyName: string;
  readonly makerIsVerified: boolean;
  readonly makerPersonalPickupEnabled: boolean;
  readonly makerPickupNote: string | null;
  /** Blob path of the maker's logo; use {@link buildMakerLogoUrl}. Null → initial tile. */
  readonly makerLogoBlobPath: string | null;
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
  /**
   * Zero or more category slugs, emitted as a REPEATED `category` query
   * param (`?category=a&category=b`) — what the controller's
   * `string[]? category` binder expects. The backend OR-s them: a maker
   * listed under any selected category matches.
   */
  readonly categories?: readonly string[];
  readonly city?: string;
  readonly minRatingStars?: number;
  /** Restrict to companies or individual traders. Omit for no constraint. */
  readonly legalType?: MakerLegalType;
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

/**
 * Mirror of <c>PublicCategoryItem</c> (T-0119). One active category —
 * feeds the catalog filter dropdown and the maker product-form category
 * picker. <c>id</c> is what <c>Product.CategoryId</c> references;
 * <c>slug</c> is the catalog URL query value.
 */
export interface PublicCategoryItem {
  readonly id: string;
  readonly name: string;
  readonly slug: string;
  readonly icon: string | null;
  readonly description: string | null;
  readonly sortOrder: number;
}

// ---- Endpoints ----

/**
 * Active categories for the country (T-0119). Anonymous reference data —
 * replaces the hardcoded launch list so admin-created categories surface.
 * Callers keep `CATALOG_CATEGORIES` as the degrade-gracefully fallback.
 */
export async function getCatalogCategories(
  country = 'CZ',
): Promise<Result<{ readonly items: readonly PublicCategoryItem[] }, ApiError>> {
  const params = new URLSearchParams({ country });
  return apiFetch<{ readonly items: readonly PublicCategoryItem[] }>(
    'public',
    `${Base}/categories?${params.toString()}`,
    { method: 'GET' },
  );
}

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
  for (const slug of input.categories ?? []) {
    if (slug) params.append('category', slug);
  }
  if (input.city) params.set('city', input.city);
  if (input.minRatingStars !== undefined) {
    params.set('minRating', String(input.minRatingStars));
  }
  if (input.legalType) {
    params.set('legalType', input.legalType);
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
  return buildFileUrl('products', blobPath);
}

/**
 * Build the public URL for a maker's catalog logo. Blob path is
 * <c>{country}/makers/{makerId}/{filename}</c>, served by
 * <c>ProfileImageController.GetMakerLogo</c>. Returns <c>null</c> when
 * the maker has no logo, so callers fall back to the initial tile.
 */
export function buildMakerLogoUrl(blobPath: string | null | undefined): string | null {
  return buildFileUrl('makers', blobPath);
}

/**
 * Build the public URL for a user's avatar. Blob path is
 * <c>{country}/avatars/{userId}/{filename}</c>, served by
 * <c>ProfileImageController.GetAvatar</c>. Returns <c>null</c> when the
 * user has no avatar, so callers fall back to initials.
 */
export function buildAvatarUrl(blobPath: string | null | undefined): string | null {
  return buildFileUrl('avatars', blobPath);
}

/**
 * Shared blob-path → public-URL mapping for the three
 * <c>/api/v1/files/{folder}/…</c> streaming routes. The DTO blob path
 * already carries the folder segment after the country
 * (<c>cz/makers/…</c>), while the route puts the folder BEFORE the
 * country (<c>/files/makers/cz/…</c>) — so the segment is stripped once
 * to avoid doubling it.
 */
function buildFileUrl(
  folder: 'products' | 'makers' | 'avatars',
  blobPath: string | null | undefined,
): string | null {
  if (!blobPath) return null;
  // Defense-in-depth: reject any path segment that could traverse out
  // of the folder (T-0047 security review — non-exploitable because
  // next/image anchors on remotePatterns.hostname, but better to refuse
  // a suspicious blob path than emit a URL the optimizer will normalize
  // to a same-host 404).
  if (/(^|\/)\.\.(\/|$)/.test(blobPath)) return null;
  const baseUrl =
    process.env.NEXT_PUBLIC_API_PUBLIC_BASE_URL?.replace(/\/+$/, '') ??
    'http://localhost:5104';
  const normalised = blobPath
    .replace(/^\/+/, '')
    .replace(new RegExp(`^([^/]+)/${folder}/`), '$1/');
  return `${baseUrl}/api/v1/files/${folder}/${normalised}`;
}
