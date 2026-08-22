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
  type ICustomerOrderListItemDto,
  type IMarkCustomerOrderMessagesAsReadResponse,
  type IMarkOrderDeliveredApiResponse,
  type IOpenCustomerDisputeResponse,
  type IOrderAttachmentSummaryDto,
  type IOrderMessageDto,
  type IPostCustomerOrderMessageResponse,
  type IUploadOrderAttachmentResponse,
  DisputeCategory,
  OrderSort,
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
export { DisputeCategory, OrderSort, OrderState, ShippingMethod };

/**
 * Mirror of <c>GetCustomerOrders.DefaultPageSize</c> (T-0080). The
 * dashboard list never emits <c>pageSize</c> — 20 is the only window
 * size at MVP (T-0086a §Scope); the constant exists for the loading
 * skeleton + display math only.
 */
export const CUSTOMER_ORDERS_DEFAULT_PAGE_SIZE = 20;

/**
 * Mirror of <c>CustomerOrderListItemDto</c> (T-0080 + T-0089's
 * <c>unreadMessageCount</c> projection). <c>createdAt</c> is overridden
 * to wire-shape <c>string</c> (ISO 8601) — same rationale as
 * <see cref="CustomerOrderDetail"/>.
 */
export type CustomerOrderListItem = Readonly<Omit<ICustomerOrderListItemDto, 'createdAt'>> & {
  readonly createdAt: string;
};

/**
 * Wire shape of <c>PagedData&lt;CustomerOrderListItemDto&gt;</c>.
 * <c>totalPages</c> / <c>hasNextPage</c> / <c>hasPreviousPage</c> are
 * optional on the generated interface (T-0049 precedent) — callers
 * provide narrow fallbacks.
 */
export interface CustomerOrdersPage {
  readonly items: readonly CustomerOrderListItem[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
  readonly totalPages?: number;
  readonly hasNextPage?: boolean;
  readonly hasPreviousPage?: boolean;
}

/** Filter/sort/page inputs for {@link getCustomerOrders} — 1:1 with the T-0080 GET contract. */
export interface CustomerOrdersInput {
  readonly page?: number;
  readonly state?: OrderState;
  /** ISO `yyyy-MM-dd` from `<input type="date">` — backend binds DateTimeOffset. */
  readonly dateFrom?: string;
  readonly dateTo?: string;
  readonly sort?: OrderSort;
}

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

/**
 * Generated envelope around the list page
 * (<c>GetCustomerOrdersResponse { orders }</c>). Unwrapped at the helper
 * boundary so page code never touches the envelope (T-0086a).
 */
interface GetCustomerOrdersEnvelope {
  readonly orders: CustomerOrdersPage;
}

// ---- Order messages + deliver + downloads (T-0086b) ----

/**
 * Mirror of <c>OrderMessageDto</c> (T-0079). <c>createdAt</c> is
 * overridden to wire-shape <c>string</c> (ISO 8601) — same rationale as
 * <see cref="CustomerOrderDetail"/>.
 */
export type OrderMessageItem = Readonly<Omit<IOrderMessageDto, 'createdAt'>> & {
  readonly createdAt: string;
};

/** Wire shape of <c>PagedData&lt;OrderMessageDto&gt;</c> (newest-first, 50/page per T-0079). */
export interface OrderMessagesPageData {
  readonly items: readonly OrderMessageItem[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
  readonly totalPages?: number;
  readonly hasNextPage?: boolean;
  readonly hasPreviousPage?: boolean;
}

/** Mirror of <c>PostCustomerOrderMessageResponse</c> — wire-shape <c>createdAt</c>. */
export type PostOrderMessageResult = Readonly<
  Omit<IPostCustomerOrderMessageResponse, 'createdAt'>
> & {
  readonly createdAt: string;
};

/** Mirror of <c>MarkCustomerOrderMessagesAsReadResponse { markedCount }</c> (T-0079, idempotent). */
export type MarkMessagesReadResult = Readonly<IMarkCustomerOrderMessagesAsReadResponse>;

/** Mirror of <c>MarkOrderDeliveredApiResponse { orderId, state }</c> (T-0076). */
export type MarkOrderDeliveredResult = Readonly<IMarkOrderDeliveredApiResponse>;

/** Generated envelope around the messages page (<c>GetCustomerOrderMessagesResponse { messages }</c>). */
interface GetOrderMessagesEnvelope {
  readonly messages: OrderMessagesPageData;
}

/**
 * Streaming-download budget (mirrors <see cref="UPLOAD_TIMEOUT_MS"/>):
 * attachments are capped at 10 MiB (T-0064) and invoice PDFs are far
 * smaller, but the same worst-tolerable mobile-downlink math applies,
 * so binary downloads share the 120 s ceiling instead of the 8 s JSON
 * default.
 */
const DOWNLOAD_TIMEOUT_MS = 120_000;

// ---- Endpoints ----

/**
 * Customer dashboard order list (T-0080, consumed by T-0086a). Params
 * are emitted only when they diverge from the backend defaults so
 * canonical URLs and request lines stay clean (patterns.md B.8):
 * `page` only when &gt; 1, `sort` only when not `CreatedAtDesc`,
 * `state`/`dateFrom`/`dateTo` only when set. `pageSize` is never sent —
 * the backend default of 20 is the only dashboard window size at MVP.
 * The backend Validator stays authoritative (page clamps, inverted
 * date range → `ApiError.type === 'Validation'`).
 */
export async function getCustomerOrders(
  input: CustomerOrdersInput,
): Promise<Result<CustomerOrdersPage, ApiError>> {
  const params = new URLSearchParams();
  if (input.page !== undefined && input.page > 1) params.set('page', String(input.page));
  if (input.state !== undefined) params.set('state', input.state);
  if (input.dateFrom !== undefined) params.set('dateFrom', input.dateFrom);
  if (input.dateTo !== undefined) params.set('dateTo', input.dateTo);
  if (input.sort !== undefined && input.sort !== OrderSort.CreatedAtDesc) {
    params.set('sort', input.sort);
  }
  const query = params.toString();
  const result = await apiFetch<GetCustomerOrdersEnvelope>(
    'customer',
    query ? `${Base}?${query}` : Base,
    { method: 'GET' },
  );
  return result.success ? ok(result.value.orders) : result;
}

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

/**
 * Customer confirm-delivery transition <c>Shipped → Delivered</c>
 * (T-0076, US-customer-0013). Silent-Success idempotent on the backend:
 * the common race (carrier/auto path delivered first) returns 200, so
 * <c>order.invalidTransition</c> (409) only surfaces for genuinely
 * ineligible states.
 */
/**
 * The customer cancels their own UNPAID order (T-0181 / Q-0041, audit
 * CUST-M3). `PendingPayment` only — no money has moved, so there is no
 * refund and no window; a paid order answers 409 (that is the maker's
 * "refuse" action). Re-calling on an already-cancelled order is a 200.
 */
export async function cancelPendingOrder(
  orderId: string,
): Promise<Result<CancelPendingOrderResult, ApiError>> {
  return apiFetch<CancelPendingOrderResult>(
    'customer',
    `${Base}/${encodeURIComponent(orderId)}/cancel`,
    { method: 'POST' },
  );
}

/** Mirror of `CancelPendingOrder.CancelPendingOrderResponse`. */
export interface CancelPendingOrderResult {
  readonly orderId: string;
  readonly state: string;
}

export async function markOrderDelivered(
  orderId: string,
): Promise<Result<MarkOrderDeliveredResult, ApiError>> {
  return apiFetch<MarkOrderDeliveredResult>(
    'customer',
    `${Base}/${encodeURIComponent(orderId)}/deliver`,
    { method: 'POST' },
  );
}

/**
 * Paged order-message thread, newest first (T-0079, 50/page backend
 * default — <c>pageSize</c> is never emitted). The
 * <c>GetCustomerOrderMessagesResponse</c> envelope is unwrapped here so
 * callers receive the page directly.
 */
export async function getOrderMessages(
  orderId: string,
  page: number,
): Promise<Result<OrderMessagesPageData, ApiError>> {
  const params = new URLSearchParams();
  if (page > 1) params.set('page', String(page));
  const query = params.toString();
  const path = `${Base}/${encodeURIComponent(orderId)}/messages`;
  const result = await apiFetch<GetOrderMessagesEnvelope>(
    'customer',
    query ? `${path}?${query}` : path,
    { method: 'GET' },
  );
  return result.success ? ok(result.value.messages) : result;
}

/**
 * Post one message to the order thread (T-0079). Failure codes from
 * <c>BusinessErrorMessage</c>: <c>order.message.bodyEmpty</c>,
 * <c>order.message.bodyTooLong</c> (2000-char cap — the UI mirror in
 * <c>lib/utils/validation.ts</c> never replaces the backend rule) and
 * <c>order.message.notAllowedInState</c> (PendingPayment guard).
 */
export async function postOrderMessage(
  orderId: string,
  body: string,
): Promise<Result<PostOrderMessageResult, ApiError>> {
  return apiFetch<PostOrderMessageResult>(
    'customer',
    `${Base}/${encodeURIComponent(orderId)}/messages`,
    { method: 'POST', json: { body } },
  );
}

/** Mirror of <c>OpenCustomerDisputeResponse { orderId, disputeId }</c> (T-0106/T-0145). */
export type OpenCustomerDisputeResult = Readonly<IOpenCustomerDisputeResponse>;

/**
 * "Eskalovat na Makables" (T-0145) — escalates the order-message thread
 * into a platform dispute. Failure codes from <c>BusinessErrorMessage</c>:
 * <c>order.dispute.categoryNotAllowed</c> (carrier-reserved categories are
 * never offered here), <c>order.dispute.windowExpired</c> (14-day
 * from-delivery window elapsed), <c>order.invalidTransition</c>. Silent-
 * Success re-open (§C.4) returns the existing open dispute's id.
 */
export async function openCustomerDispute(
  orderId: string,
  category: DisputeCategory,
  description: string,
): Promise<Result<OpenCustomerDisputeResult, ApiError>> {
  return apiFetch<OpenCustomerDisputeResult>(
    'customer',
    `${Base}/${encodeURIComponent(orderId)}/dispute`,
    { method: 'POST', json: { category, description } },
  );
}

/**
 * Zero the customer's unread counter for this order's thread (T-0079;
 * feeds the T-0086a list badge). Idempotent — a repeat call returns
 * <c>markedCount: 0</c>, so the thread can re-fire it after polls
 * without bookkeeping.
 */
export async function markOrderMessagesRead(
  orderId: string,
): Promise<Result<MarkMessagesReadResult, ApiError>> {
  return apiFetch<MarkMessagesReadResult>(
    'customer',
    `${Base}/${encodeURIComponent(orderId)}/messages/mark-read`,
    { method: 'POST' },
  );
}

/**
 * Authenticated binary download for attachment / invoice paths
 * (T-0086b). The backend emits RELATIVE customer-host paths
 * (<c>/api/v1/orders/{id}/attachments/{attachmentId}</c>,
 * <c>/api/v1/orders/{id}/invoice</c> — T-0082/T-0088; direct blob URLs
 * are never exposed), so the path is passed to <c>apiFetch</c> verbatim
 * and the audience cookie rides along. Callers turn the Blob into a
 * user-visible download via an object URL + programmatic anchor.
 */
export async function downloadOrderFile(path: string): Promise<Result<Blob, ApiError>> {
  return apiFetch<Blob>('customer', path, {
    method: 'GET',
    parse: 'blob',
    timeoutMs: DOWNLOAD_TIMEOUT_MS,
  });
}
