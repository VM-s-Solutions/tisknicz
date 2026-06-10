/**
 * Hand-written wrappers around the authenticated customer order
 * endpoints in .NET <c>OrdersController</c> at <c>/api/v1/orders</c> on
 * the Customer host (T-0063 create, T-0064 attachments, T-0082 detail).
 * Same convention as <c>profile.ts</c> / <c>catalog.ts</c> /
 * <c>maker-products.ts</c> (patterns.md B.16): we call
 * <see cref="apiFetch"/> directly (not the NSwag-generated
 * <c>CustomerApi</c> class) because <c>apiFetch</c> returns
 * <c>Result&lt;T, ApiError&gt;</c> and the generated client throws on
 * every non-2xx — that doesn't fit the Result flow used everywhere else.
 *
 * All endpoints require an authenticated customer session; the
 * audience-scoped cookies set by <c>AuthController.login</c> ride along
 * automatically (browser: <c>credentials: 'include'</c>; SSR: cookie
 * forwarding per patterns.md B.14 / ADR 0024).
 */

import {
  type ICreateOrderRequest,
  type ICreateOrderResponse,
  type ICustomerOrderDetailDto,
  type IOrderAttachmentSummaryDto,
  type IUploadOrderAttachmentResponse,
  OrderState,
  ShippingMethod,
} from '../api-client/customer-api.v1';
import { apiFetch } from '../runtime/api-fetch';
import { type ApiError, type Result, ok } from '../runtime/result';

// Leading slash matters: apiFetch concatenates `${baseUrl}${path}` against
// host URLs that have no trailing slash, so an unrooted "api/v1/orders"
// would produce http://localhost:5001api/v1/orders. T-0036 Copilot review
// (same convention as profile.ts).
const Base = '/api/v1/orders';

/**
 * Per-attachment upload budget (checkout-flow review B-1). The T-0064
 * server cap is 10 MiB per file (`ORDER_ATTACHMENT_MAX_BYTES` mirror);
 * at a worst-tolerable ~0.7 Mbps mobile uplink that is ≈2 minutes of
 * transfer, so 120 s replaces the default 8 s apiFetch ceiling that
 * would deterministically abort large uploads. JSON endpoints keep the
 * 8 s default.
 */
const UPLOAD_TIMEOUT_MS = 120_000;

// ---- DTO re-exports (route code never imports lib/api-client/ directly) ----

/**
 * String-valued enums mirroring <c>Makables.Core.Domain.Orders</c>.
 * Re-exported directly so write requests use the same runtime values
 * and reads narrow on the union.
 */
export { OrderState, ShippingMethod };

/** Mirror of <c>CreateOrderRequest</c> in <c>OrdersController</c> (T-0063). */
export type CreateOrderInput = Readonly<ICreateOrderRequest>;

/** Mirror of <c>CreateOrderResponse</c> — `{ orderId, orderNumber, totalPriceMinor, currency }`. */
export type CreateOrderResult = Readonly<ICreateOrderResponse>;

/**
 * Mirror of <c>UploadOrderAttachmentResponse</c> (T-0064).
 * <c>uploadedOn</c> is overridden to wire-shape <c>string</c> (ISO 8601)
 * — <c>apiFetch</c> returns <c>await response.json()</c> without the
 * generated class's <c>Date</c>-parsing constructor, so at runtime the
 * field is still a string (same rationale as
 * <c>MakerProductListItem.createdOn</c>, T-0049 review B1).
 */
export type OrderAttachmentUploadResult = Readonly<
  Omit<IUploadOrderAttachmentResponse, 'uploadedOn'>
> & {
  readonly uploadedOn: string;
};

/** Mirror of <c>OrderAttachmentSummaryDto</c> (T-0082). */
export type OrderAttachmentSummary = Readonly<IOrderAttachmentSummaryDto>;

/**
 * Mirror of <c>CustomerOrderDetailDto</c> (T-0082). Every timestamp is
 * overridden to wire-shape <c>string</c> (ISO 8601) for the same
 * reason as <see cref="OrderAttachmentUploadResult"/> — display helpers
 * wrap <c>new Date(...)</c> themselves (see <c>lib/utils/dates.ts</c>).
 */
export type CustomerOrderDetail = Readonly<
  Omit<
    ICustomerOrderDetailDto,
    | 'paidAt'
    | 'acceptedAt'
    | 'shippedAt'
    | 'deliveredAt'
    | 'cancelledAt'
    | 'createdAt'
    | 'updatedAt'
    | 'attachments'
  >
> & {
  readonly paidAt: string | undefined;
  readonly acceptedAt: string | undefined;
  readonly shippedAt: string | undefined;
  readonly deliveredAt: string | undefined;
  readonly cancelledAt: string | undefined;
  readonly createdAt: string;
  readonly updatedAt: string | undefined;
  readonly attachments: readonly OrderAttachmentSummary[];
};

/**
 * Generated envelope around the detail DTO
 * (<c>GetCustomerOrderDetailsResponse { detail }</c>). Unwrapped at the
 * helper boundary so page code never touches the envelope (T-0084b).
 */
interface GetCustomerOrderDetailsEnvelope {
  readonly detail: CustomerOrderDetail;
}

// ---- Endpoints ----

/**
 * Create an order in <c>PendingPayment</c> (T-0063). Quantity is fixed
 * at 1 per the T-0061 invariant — the caller passes it explicitly so
 * the request mirrors the backend contract verbatim. Validation errors
 * come back as <c>ApiError.type === 'Validation'</c> with a
 * <c>fields</c> map for inline display (patterns.md B.17); the backend
 * stays authoritative over every rule the form mirrors.
 */
export async function createOrder(
  input: CreateOrderInput,
): Promise<Result<CreateOrderResult, ApiError>> {
  return apiFetch<CreateOrderResult>('customer', Base, {
    method: 'POST',
    json: input,
  });
}

/**
 * Upload one attachment to an existing order (T-0064 — attachments are
 * uploaded AFTER order creation by API design, T-0063 Q3 lock). Builds
 * a <c>FormData</c> with the <c>file</c> field expected by the
 * controller's <c>IFormFile</c> binding and passes it as the raw body.
 * We deliberately do NOT set <c>Content-Type</c>: the browser computes
 * the multipart boundary itself (patterns.md B.15).
 *
 * Failure codes from <c>BusinessErrorMessage</c>: <c>file.invalid</c>,
 * <c>file.tooLarge</c>, <c>file.unsupportedType</c>,
 * <c>order.attachmentLimitReached</c>, <c>order.stateForbidsAttachment</c>.
 */
export async function uploadOrderAttachment(
  orderId: string,
  file: File,
): Promise<Result<OrderAttachmentUploadResult, ApiError>> {
  const formData = new FormData();
  formData.append('file', file);
  return apiFetch<OrderAttachmentUploadResult>(
    'customer',
    `${Base}/${encodeURIComponent(orderId)}/attachments`,
    { method: 'POST', body: formData, timeoutMs: UPLOAD_TIMEOUT_MS },
  );
}

/**
 * Customer-scoped order detail (T-0082). The backend returns 404 for
 * foreign orders and unknown ids alike (IDOR-resistant,
 * US-customer-0012 AC-3) — both surface as
 * <c>ApiError.type === 'NotFound'</c>. The
 * <c>GetCustomerOrderDetailsResponse</c> envelope is unwrapped here so
 * callers receive the inner DTO directly.
 */
export async function getCustomerOrderDetail(
  orderId: string,
): Promise<Result<CustomerOrderDetail, ApiError>> {
  const result = await apiFetch<GetCustomerOrderDetailsEnvelope>(
    'customer',
    `${Base}/${encodeURIComponent(orderId)}`,
    { method: 'GET' },
  );
  return result.success ? ok(result.value.detail) : result;
}
