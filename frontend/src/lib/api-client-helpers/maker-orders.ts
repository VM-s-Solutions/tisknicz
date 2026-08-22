/**
 * Hand-written wrappers around the authenticated maker order endpoints
 * in .NET <c>OrdersController</c> at <c>/api/v1/orders</c> on the Maker
 * host (T-0081 list; T-0087b extends with detail + actions + label +
 * messages). Same convention as <c>maker-products.ts</c> /
 * <c>orders-client.ts</c> (patterns.md B.16): we call
 * <see cref="apiFetch"/> directly (not the NSwag-generated
 * <c>MakerApi</c> class) because <c>apiFetch</c> returns
 * <c>Result&lt;T, ApiError&gt;</c> and the generated client throws on
 * every non-2xx — that doesn't fit the Result flow used everywhere else.
 *
 * All endpoints require an authenticated maker session; the
 * audience-scoped cookies set by <c>AuthController.login</c> ride along
 * automatically (browser: <c>credentials: 'include'</c>; SSR: cookie
 * forwarding per patterns.md B.14 / ADR 0024). A customer JWT replayed
 * against the Maker host 401s at the backend (ADR 0013) — no parallel
 * auth logic here.
 */

import {
  type IAcceptOrderResponse,
  type IHandOverOrderResponse,
  type IMakerOrderDetailDto,
  type IMakerOrderListItemDto,
  type IMarkMakerOrderMessagesAsReadResponse,
  type IOrderAttachmentSummaryDto,
  type IOrderMessageDto,
  type IPostMakerOrderMessageResponse,
  type IShipOrderResponse,
  OrderSort,
  OrderState,
  ShippingMethod,
} from '../api-client/maker-api.v1';
import { apiFetch } from '../runtime/api-fetch';
import { type ApiError, type Result, ok } from '../runtime/result';

// Leading slash matters: apiFetch concatenates `${baseUrl}${path}` against
// host URLs that have no trailing slash, so an unrooted "api/v1/orders"
// would produce http://localhost:5002api/v1/orders. T-0036 Copilot review
// (same convention as maker-products.ts).
const Base = '/api/v1/orders';

// ---- DTO re-exports (route code never imports lib/api-client/ directly) ----

/**
 * String-valued enums mirroring <c>Makables.Core.Domain.Orders</c>.
 * Re-exported directly so reads narrow on the union. NOTE: the maker
 * client generates its own nominal enums (identical string values to the
 * customer client's) — maker route code must import these, never the
 * customer helper's.
 */
export { OrderSort, OrderState, ShippingMethod };

/**
 * Mirrors of <c>GetMakerOrders.DefaultPageSize</c> / <c>MaxPageSize</c>
 * (T-0081 Validator). UX-only duplicates for URL clamping — the backend
 * Validator stays authoritative and 400s on a forced out-of-range value.
 */
export const MAKER_ORDERS_DEFAULT_PAGE_SIZE = 20;
export const MAKER_ORDERS_MAX_PAGE_SIZE = 50;

/**
 * Mirror of <c>MakerOrderListItemDto</c> (T-0081 + T-0089's
 * <c>unreadMessageCount</c> projection). <c>createdAt</c> is overridden
 * to wire-shape <c>string</c> (ISO 8601) — <c>apiFetch</c> returns
 * <c>await response.json()</c> without the generated class's
 * <c>Date</c>-parsing constructor (T-0049 review B1 rationale). The DTO
 * deliberately carries no customer email and no platform fee (T-0081
 * §A.2 GDPR lock + §C payout-only lock).
 */
export type MakerOrderListItem = Readonly<Omit<IMakerOrderListItemDto, 'createdAt'>> & {
  readonly createdAt: string;
};

/**
 * Wire shape of <c>PagedData&lt;MakerOrderListItemDto&gt;</c>.
 * <c>totalPages</c> / <c>hasNextPage</c> / <c>hasPreviousPage</c> are
 * optional on the generated interface (T-0049 precedent) — callers
 * provide narrow fallbacks.
 */
export interface MakerOrdersPage {
  readonly items: readonly MakerOrderListItem[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
  readonly totalPages?: number;
  readonly hasNextPage?: boolean;
  readonly hasPreviousPage?: boolean;
}

/** Filter/sort/page inputs for {@link getMakerOrders} — 1:1 with the T-0081 GET contract. */
export interface MakerOrdersInput {
  readonly page?: number;
  /** Clamped to {@link MAKER_ORDERS_MAX_PAGE_SIZE} by the page; backend authoritative. */
  readonly pageSize?: number;
  /** At most ONE state per request (T-0081 §A.3 — no client-side multi-state merging). */
  readonly state?: OrderState;
  /** ISO `yyyy-MM-dd` from `<input type="date">` — backend binds DateTimeOffset. */
  readonly dateFrom?: string;
  readonly dateTo?: string;
  readonly sort?: OrderSort;
}

/** Generated envelope around the list page (<c>GetMakerOrdersResponse { orders }</c>). */
interface GetMakerOrdersEnvelope {
  readonly orders: MakerOrdersPage;
}

// ---- Endpoints ----

/**
 * Maker dashboard order list (T-0081, consumed by T-0087a). Params are
 * emitted only when they diverge from the backend defaults so canonical
 * URLs and request lines stay clean (patterns.md B.8): `page` only when
 * &gt; 1, `pageSize` only when not 20, `sort` only when not
 * `CreatedAtDesc`, `state`/`dateFrom`/`dateTo` only when set. The
 * backend Validator stays authoritative (page clamps, inverted date
 * range → `ApiError.type === 'Validation'`).
 */
export async function getMakerOrders(
  input: MakerOrdersInput,
): Promise<Result<MakerOrdersPage, ApiError>> {
  const params = new URLSearchParams();
  if (input.page !== undefined && input.page > 1) params.set('page', String(input.page));
  if (input.pageSize !== undefined && input.pageSize !== MAKER_ORDERS_DEFAULT_PAGE_SIZE) {
    params.set('pageSize', String(input.pageSize));
  }
  if (input.state !== undefined) params.set('state', input.state);
  if (input.dateFrom !== undefined) params.set('dateFrom', input.dateFrom);
  if (input.dateTo !== undefined) params.set('dateTo', input.dateTo);
  if (input.sort !== undefined && input.sort !== OrderSort.CreatedAtDesc) {
    params.set('sort', input.sort);
  }
  const query = params.toString();
  const result = await apiFetch<GetMakerOrdersEnvelope>(
    'maker',
    query ? `${Base}?${query}` : Base,
    { method: 'GET' },
  );
  return result.success ? ok(result.value.orders) : result;
}

// ---- Order detail + state-machine actions (T-0087b) ----

/** Mirror of <c>OrderAttachmentSummaryDto</c> (T-0082) — `downloadUrl` is a relative maker-host path. */
export type OrderAttachmentSummary = Readonly<IOrderAttachmentSummaryDto>;

/**
 * Mirror of <c>MakerOrderDetailDto</c> (T-0082). Every timestamp is
 * overridden to wire-shape <c>string</c> (ISO 8601) — display helpers
 * wrap <c>new Date(...)</c> themselves (see <c>lib/utils/dates.ts</c>).
 * The DTO deliberately has NO customer email field (T-0082 AC-4
 * compile-time GDPR lock) and no platform-fee figure (T-0081/T-0082
 * payout-only lock).
 */
export type MakerOrderDetail = Readonly<
  Omit<
    IMakerOrderDetailDto,
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

/** Generated envelope around the detail DTO (<c>GetMakerOrderDetailsResponse { detail }</c>). */
interface GetMakerOrderDetailsEnvelope {
  readonly detail: MakerOrderDetail;
}

/** Mirror of <c>AcceptOrderResponse { orderId }</c> (T-0071). */
export type AcceptOrderResult = Readonly<IAcceptOrderResponse>;

/** Mirror of <c>ShipOrderResponse { orderId, carrierRef, trackingUrl }</c> (T-0072). */
export type ShipOrderResult = Readonly<IShipOrderResponse>;

/** Mirror of <c>HandOverOrderResponse { orderId }</c> (T-0073). */
export type HandOverOrderResult = Readonly<IHandOverOrderResponse>;

/**
 * Maker-scoped order detail (T-0082). The backend returns 404 for
 * foreign orders and unknown ids alike (single `order.notFound` shape —
 * no IDOR oracle); both surface as <c>ApiError.type === 'NotFound'</c>.
 * The envelope is unwrapped here so callers receive the inner DTO.
 */
export async function getMakerOrderDetail(
  orderId: string,
): Promise<Result<MakerOrderDetail, ApiError>> {
  const result = await apiFetch<GetMakerOrderDetailsEnvelope>(
    'maker',
    `${Base}/${encodeURIComponent(orderId)}`,
    { method: 'GET' },
  );
  return result.success ? ok(result.value.detail) : result;
}

/**
 * Maker accept transition <c>Paid → Accepted</c> (T-0071,
 * US-maker-0006). The backend re-validates the transition — stale calls
 * return 409 <c>order.invalidTransition</c>.
 */
/**
 * The maker REFUSES a paid order they cannot fulfil (T-0181 / Q-0041).
 * Paid-only and only within the configured window of `paidAt` — past it
 * the backend answers 409 `order.refusalWindowExpired` and the maker must
 * go through admin support. The full amount is refunded before the order
 * is cancelled, so a gateway failure leaves the order untouched.
 */
export async function refuseOrder(
  orderId: string,
  reason: string,
): Promise<Result<RefuseOrderResult, ApiError>> {
  return apiFetch<RefuseOrderResult>(
    'maker',
    `${Base}/${encodeURIComponent(orderId)}/refuse`,
    { method: 'POST', json: { reason } },
  );
}

/** Mirror of `RefuseOrder.RefuseOrderResponse`. */
export interface RefuseOrderResult {
  readonly orderId: string;
  readonly state: string;
  readonly refundedAmountMinor: number;
}

export async function acceptOrder(
  orderId: string,
): Promise<Result<AcceptOrderResult, ApiError>> {
  return apiFetch<AcceptOrderResult>(
    'maker',
    `${Base}/${encodeURIComponent(orderId)}/accept`,
    { method: 'POST' },
  );
}

/**
 * Maker ship transition <c>Accepted → Shipped</c> for Zásilkovna orders
 * (T-0072, US-maker-0007). Creates a REAL carrier shipment + label —
 * irreversible from the UI, hence the confirm dialog at the call site
 * (T-0087b §C). Failure codes: <c>order.invalidTransition</c> (409),
 * <c>shipping.methodNotEligible</c> (400, personal-pickup order),
 * <c>shipping.carrierUnavailable</c> (503).
 */
export async function shipOrder(orderId: string): Promise<Result<ShipOrderResult, ApiError>> {
  return apiFetch<ShipOrderResult>(
    'maker',
    `${Base}/${encodeURIComponent(orderId)}/ship`,
    { method: 'POST' },
  );
}

/**
 * Maker handover transition <c>Accepted → Shipped</c> for personal-
 * pickup orders (T-0073, US-maker-0008) — starts the auto-deliver
 * timer; no carrier side effect, so no confirm dialog (T-0087b §C).
 * Failure codes mirror {@link shipOrder} minus the carrier 503.
 */
export async function handOverOrder(
  orderId: string,
): Promise<Result<HandOverOrderResult, ApiError>> {
  return apiFetch<HandOverOrderResult>(
    'maker',
    `${Base}/${encodeURIComponent(orderId)}/handover`,
    { method: 'POST' },
  );
}

/**
 * Streaming-download budget (orders-client.ts precedent, checkout-flow
 * review B-1 math): label/attachment/invoice PDFs are small but the
 * worst-tolerable mobile-downlink case needs far more than the 8 s JSON
 * default, so binary downloads share the 120 s ceiling.
 */
const DOWNLOAD_TIMEOUT_MS = 120_000;

/**
 * Shipping-label PDF download (T-0075, US-maker-0009). Deliberately
 * does NOT use the generated <c>MakerApi.label()</c>: NSwag types the
 * file response as <c>Promise&lt;void&gt;</c> and discards the PDF body
 * (T-0087b §C / Option F lock). Fetching as a blob through
 * <see cref="apiFetch"/> keeps the audience cookie, the timeout budget
 * and RFC7807 error parsing; callers turn the Blob into a named
 * download via an object URL + programmatic anchor. 503 surfaces as
 * <c>shipping.carrierUnavailable</c>.
 */
export async function downloadShippingLabel(orderId: string): Promise<Result<Blob, ApiError>> {
  return apiFetch<Blob>(
    'maker',
    `/api/v1/maker/files/orders/${encodeURIComponent(orderId)}/label`,
    { method: 'GET', parse: 'blob', timeoutMs: DOWNLOAD_TIMEOUT_MS },
  );
}

/**
 * Authenticated binary download for attachment / invoice paths
 * (T-0064/T-0088 maker-scoped endpoints). The backend emits RELATIVE
 * maker-host paths (<c>/api/v1/orders/{id}/attachments/{attachmentId}</c>,
 * <c>/api/v1/orders/{id}/invoice</c>; direct blob URLs are never
 * exposed), so the path is passed to <c>apiFetch</c> verbatim and the
 * audience cookie rides along.
 */
export async function downloadMakerOrderFile(path: string): Promise<Result<Blob, ApiError>> {
  return apiFetch<Blob>('maker', path, {
    method: 'GET',
    parse: 'blob',
    timeoutMs: DOWNLOAD_TIMEOUT_MS,
  });
}

// ---- Order messages trio (T-0079, maker host — T-0087b thread wiring) ----

/**
 * Mirror of <c>OrderMessageDto</c> (T-0079). <c>createdAt</c> is
 * overridden to wire-shape <c>string</c> (ISO 8601) — same rationale as
 * <see cref="MakerOrderDetail"/>.
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

/** Mirror of <c>PostMakerOrderMessageResponse</c> — wire-shape <c>createdAt</c>. */
export type PostOrderMessageResult = Readonly<
  Omit<IPostMakerOrderMessageResponse, 'createdAt'>
> & {
  readonly createdAt: string;
};

/** Mirror of <c>MarkMakerOrderMessagesAsReadResponse { markedCount }</c> (T-0079, idempotent). */
export type MarkMessagesReadResult = Readonly<IMarkMakerOrderMessagesAsReadResponse>;

/** Generated envelope around the messages page (<c>GetMakerOrderMessagesResponse { messages }</c>). */
interface GetMakerOrderMessagesEnvelope {
  readonly messages: OrderMessagesPageData;
}

/**
 * Paged order-message thread, newest first (T-0079, 50/page backend
 * default — <c>pageSize</c> is never emitted). Envelope unwrapped so
 * the thread adapter receives the page directly.
 */
export async function getMakerOrderMessages(
  orderId: string,
  page: number,
): Promise<Result<OrderMessagesPageData, ApiError>> {
  const params = new URLSearchParams();
  if (page > 1) params.set('page', String(page));
  const query = params.toString();
  const path = `${Base}/${encodeURIComponent(orderId)}/messages`;
  const result = await apiFetch<GetMakerOrderMessagesEnvelope>(
    'maker',
    query ? `${path}?${query}` : path,
    { method: 'GET' },
  );
  return result.success ? ok(result.value.messages) : result;
}

/**
 * Post one message to the order thread (T-0079). Failure codes from
 * <c>BusinessErrorMessage</c>: <c>order.message.bodyEmpty</c>,
 * <c>order.message.bodyTooLong</c>, <c>order.message.notAllowedInState</c>.
 */
export async function postMakerOrderMessage(
  orderId: string,
  body: string,
): Promise<Result<PostOrderMessageResult, ApiError>> {
  return apiFetch<PostOrderMessageResult>(
    'maker',
    `${Base}/${encodeURIComponent(orderId)}/messages`,
    { method: 'POST', json: { body } },
  );
}

/**
 * Zero the maker's unread counter for this order's thread (T-0079;
 * clears the T-0087a list badge). Idempotent — a repeat call returns
 * <c>markedCount: 0</c>, so the thread re-fires it after polls without
 * bookkeeping.
 */
export async function markMakerOrderMessagesRead(
  orderId: string,
): Promise<Result<MarkMessagesReadResult, ApiError>> {
  return apiFetch<MarkMessagesReadResult>(
    'maker',
    `${Base}/${encodeURIComponent(orderId)}/messages/mark-read`,
    { method: 'POST' },
  );
}
