/**
 * Hand-written wrappers around the authenticated maker product CRUD +
 * image upload endpoints in <c>ProductController</c> at
 * <c>/api/v1/products</c> on the Maker host (T-0049). Same convention as
 * <c>profile.ts</c> / <c>catalog.ts</c>: we call <see cref="apiFetch"/>
 * directly (not the NSwag-generated <c>MakerApi</c> class) because
 * <c>apiFetch</c> returns <c>Result&lt;T, ApiError&gt;</c> and the
 * generated client throws on every non-2xx — that doesn't fit the
 * Result flow used everywhere else in the app.
 *
 * <para>
 * All endpoints require an authenticated maker session; the audience-
 * scoped cookies set by <c>AuthController.login</c> ride along
 * automatically thanks to <c>apiFetch</c>'s default
 * <c>credentials: 'include'</c>.
 * </para>
 *
 * <para>
 * DTO types below mirror the NSwag-generated <c>I…</c> interfaces from
 * <c>lib/api-client/maker-api.v1.ts</c> as <c>readonly</c> aliases so
 * route code only imports from this helper module and never from the
 * generated client. Keep them in sync with the C# records when the
 * contract changes — running <c>npm run generate:api</c> regenerates
 * the source interfaces, and parity is the helper author's
 * responsibility.
 * </para>
 */

import {
  type ICreateProductRequest,
  type ICreateProductResponse,
  type IMakerProductDetail,
  type IMakerProductListItem,
  type IPagedDataOfMakerProductListItem,
  type IProductImageItem,
  type IUpdateProductRequest,
  type IUploadProductImageResponse,
  FulfillmentType as FulfillmentTypeEnum,
  PriceType as PriceTypeEnum,
} from '../api-client/maker-api.v1';
import { apiFetch } from '../runtime/api-fetch';
import { type ApiError, type Result, ok } from '../runtime/result';

// Leading slash matters: apiFetch concatenates `${baseUrl}${path}` against
// host URLs that have no trailing slash, so an unrooted "api/v1/products"
// would produce http://localhost:5002api/v1/products. T-0036 Copilot
// review (same convention as profile.ts).
const Base = '/api/v1/products';

// ---- DTO re-exports (mirror Makables.Core.Domain.Products records) ----

/**
 * String-valued enum mirroring <c>Makables.Core.Domain.Products.PriceType</c>.
 * Re-export the generated enum directly so write requests use the same
 * runtime values and the type narrows on the union.
 */
export type PriceType = PriceTypeEnum;
export const PRICE_TYPES = [
  PriceTypeEnum.Fixed,
  PriceTypeEnum.From,
  PriceTypeEnum.OnRequest,
] as const;
export { PriceTypeEnum as PriceTypeValues };

/**
 * String-valued enum mirroring
 * <c>Makables.Core.Domain.Products.FulfillmentType</c> (T-0144). "Na
 * zakázku" vs. "skladem" — drives the maker-form control and (via the
 * public catalog DTOs) the product-detail badge + checkout notice.
 */
export type FulfillmentType = FulfillmentTypeEnum;
export const FULFILLMENT_TYPES = [
  FulfillmentTypeEnum.MadeToOrder,
  FulfillmentTypeEnum.InStock,
] as const;
export { FulfillmentTypeEnum as FulfillmentTypeValues };

/**
 * Mirror of <c>MakerProductListItem</c> — one row in the maker dashboard
 * product list. Includes soft-deleted products (the dashboard surfaces
 * drafts and recently-deactivated items). <c>primaryImageBlobPath</c> is
 * the lowest-<c>sortOrder</c> image, or undefined when there are none
 * yet.
 *
 * <para>
 * <c>createdOn</c> is overridden to <c>string</c> (ISO 8601) — the
 * wire shape. The generated NSwag interface types it as <c>Date</c>
 * because the C# source is <c>DateTimeOffset</c> and NSwag's class
 * constructor would have parsed it to a real <c>Date</c>, but
 * <c>apiFetch</c> returns <c>await response.json()</c> as the value
 * type without instantiating that class, so at runtime it's still a
 * string. Aliasing to <c>I…</c> verbatim would re-create the type lie
 * round 1 of this PR tried to fix. Display helpers wrap
 * <c>new Date(createdOn)</c> themselves (see
 * <c>lib/utils/dates.ts</c>'s <c>formatDate</c>). T-0049 code-quality
 * review B1 + Copilot round-6 M1.
 * </para>
 */
export type MakerProductListItem = Readonly<
  Omit<IMakerProductListItem, 'createdOn'>
> & {
  readonly createdOn: string;
};

/**
 * Mirror of <c>MakerProductDetail</c> — the maker-scoped product-detail
 * view for the dashboard edit form. Includes <c>isActive</c> so the edit
 * UI can show a "this product is deactivated" banner.
 *
 * <para><c>createdOn</c> is overridden to wire-shape <c>string</c> — see
 * the <c>MakerProductListItem</c> remark for the rationale.</para>
 */
export type MakerProductDetail = Readonly<
  Omit<IMakerProductDetail, 'createdOn' | 'images'>
> & {
  readonly createdOn: string;
  readonly images: readonly ProductImageItem[];
};

/** Mirror of <c>ProductImageItem</c>; identical shape to the public DTO. */
export type ProductImageItem = Readonly<IProductImageItem>;

/** Mirror of <c>CreateProductRequest</c> in <c>ProductController</c>. */
export type CreateProductRequest = Readonly<ICreateProductRequest>;

/** Mirror of <c>UpdateProductRequest</c> in <c>ProductController</c>. */
export type UpdateProductRequest = Readonly<IUpdateProductRequest>;

/** Mirror of <c>CreateProductResponse</c> — wraps the new product id. */
export type CreateProductResponse = Readonly<ICreateProductResponse>;

/** Mirror of <c>UploadProductImageResponse</c> — wraps the new image id. */
export type UploadProductImageResponse = Readonly<IUploadProductImageResponse>;

/**
 * Maker-scoped paged list. Re-exported as <c>MakerProductsPage</c> for
 * ergonomics at the call site.
 */
export type MakerProductsPage = Readonly<
  Omit<IPagedDataOfMakerProductListItem, 'items'> & {
    readonly items: readonly MakerProductListItem[];
  }
>;

// Backend default + cap mirror (see <c>GetMyProducts.DefaultPageSize</c>).
// Re-stated here so URL builders stay typed without a round-trip; the
// backend remains the source of truth.
export const MAKER_PRODUCTS_DEFAULT_PAGE_SIZE = 24;
/** Matches the backend's <c>GetMyProducts.MaxPageSize</c>. */
export const MAKER_PRODUCTS_MAX_PAGE_SIZE = 48;

// ---- Endpoints ----

/**
 * Paged list of the authenticated maker's products (active + soft-deleted).
 * Sorted active-first then newest-first by the backend.
 */
export async function getMyProducts(
  input: { readonly page?: number; readonly pageSize?: number } = {},
): Promise<Result<MakerProductsPage, ApiError>> {
  const params = new URLSearchParams();
  params.set('page', String(input.page ?? 1));
  params.set('pageSize', String(input.pageSize ?? MAKER_PRODUCTS_DEFAULT_PAGE_SIZE));
  return apiFetch<MakerProductsPage>('maker', `${Base}?${params.toString()}`, {
    method: 'GET',
  });
}

/**
 * Single product detail for the dashboard edit form. The backend
 * IDOR-shields by returning 404 for products owned by another maker, so
 * the helper surfaces both "doesn't exist" and "belongs to someone else"
 * as <c>ApiError.type === 'NotFound'</c>.
 */
export async function getMyProductById(
  productId: string,
): Promise<Result<MakerProductDetail, ApiError>> {
  return apiFetch<MakerProductDetail>(
    'maker',
    `${Base}/${encodeURIComponent(productId)}`,
    { method: 'GET' },
  );
}

/**
 * Create a new product. <paramref name="input"/> mirrors the backend's
 * <c>CreateProductRequest</c>; the helper performs no validation — the
 * backend FluentValidation rules are the source of truth, and validation
 * errors come back as <c>ApiError.type === 'Validation'</c> with a
 * <c>fields</c> map for inline display.
 */
export async function createProduct(
  input: CreateProductRequest,
): Promise<Result<CreateProductResponse, ApiError>> {
  return apiFetch<CreateProductResponse>('maker', Base, {
    method: 'POST',
    json: input,
  });
}

/**
 * Update an existing product. The backend returns 200 OK with no
 * meaningful body (handlers without a typed Response use <c>Ok()</c>);
 * <c>apiFetch</c> folds the empty-but-OK response to <c>ok(undefined)</c>.
 */
export async function updateProduct(
  productId: string,
  input: UpdateProductRequest,
): Promise<Result<void, ApiError>> {
  const result = await apiFetch<unknown>(
    'maker',
    `${Base}/${encodeURIComponent(productId)}`,
    { method: 'PUT', json: input },
  );
  return result.success ? ok(undefined) : result;
}

/**
 * Soft-delete a product. The backend returns 200 OK with no
 * meaningful body; <c>apiFetch</c> folds the empty-but-OK response to
 * <c>ok(undefined)</c>. The product still appears in
 * <see cref="getMyProducts"/> (the dashboard shows drafts and
 * recently-deactivated items) with <c>isActive === false</c>.
 */
export async function deleteProduct(
  productId: string,
): Promise<Result<void, ApiError>> {
  const result = await apiFetch<unknown>(
    'maker',
    `${Base}/${encodeURIComponent(productId)}`,
    { method: 'DELETE' },
  );
  return result.success ? ok(undefined) : result;
}

/**
 * Upload a product image. Builds a <c>FormData</c> with the
 * <c>file</c> field expected by the controller's <c>IFormFile</c>
 * binding, and passes it as <c>body</c> on <c>apiFetch</c>. We
 * deliberately do NOT set the <c>Content-Type</c> header: the browser
 * computes the multipart boundary itself and writes the matching
 * header — overriding it would corrupt the request. <c>apiFetch</c>
 * only injects <c>application/json</c> when the <c>json</c> option is
 * provided, so a raw <c>body</c> passes through unmodified.
 *
 * <para>
 * Validation surfaces from the backend as <c>ApiError</c> with one of
 * the codes from <c>BusinessErrorMessage</c>:
 * <c>file.tooLarge</c>, <c>file.unsupportedType</c>, <c>file.invalid</c>,
 * or <c>product.imageLimitReached</c> (409 from the 10-image cap).
 * </para>
 */
export async function uploadProductImage(
  productId: string,
  file: File,
): Promise<Result<UploadProductImageResponse, ApiError>> {
  const formData = new FormData();
  formData.append('file', file);
  return apiFetch<UploadProductImageResponse>(
    'maker',
    `${Base}/${encodeURIComponent(productId)}/images`,
    // T-0174 (audit MAKER-H2): a multi-MB photo on a normal uplink blows
    // the 8 s apiFetch default and the abort used to surface as "invalid
    // file". Multipart uploads get the documented long budget (mirrors
    // UPLOAD_TIMEOUT_MS in profile.ts).
    { method: 'POST', body: formData, timeoutMs: 120_000 },
  );
}

/**
 * Remove a product image by id. The backend returns 200 OK with no
 * meaningful body; <c>apiFetch</c> folds the empty-but-OK response to
 * <c>ok(undefined)</c>.
 */
export async function removeProductImage(
  productId: string,
  imageId: string,
): Promise<Result<void, ApiError>> {
  const result = await apiFetch<unknown>(
    'maker',
    `${Base}/${encodeURIComponent(productId)}/images/${encodeURIComponent(imageId)}`,
    { method: 'DELETE' },
  );
  return result.success ? ok(undefined) : result;
}
