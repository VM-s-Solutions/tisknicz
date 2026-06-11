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
  type IMakerOrderListItemDto,
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
