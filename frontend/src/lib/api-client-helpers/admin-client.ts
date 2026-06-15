/**
 * Hand-written wrappers around the authenticated admin read endpoints in
 * the .NET Admin host (T-0105 all-orders, T-0108 audit-log, T-0109
 * all-invoices). Same convention as <c>maker-orders.ts</c> /
 * <c>payouts-client.ts</c> (patterns.md B.16): we call
 * <see cref="apiFetch"/> directly (not the NSwag-generated
 * <c>AdminApi</c> class) because <c>apiFetch</c> returns
 * <c>Result&lt;T, ApiError&gt;</c> and the generated client throws on
 * every non-2xx — that doesn't fit the Result flow used everywhere else.
 *
 * All endpoints require an authenticated admin session; the
 * audience-scoped cookies set by <c>AuthController.login</c> on the admin
 * host ride along automatically (browser: <c>credentials: 'include'</c>;
 * SSR: cookie forwarding per patterns.md B.14 / ADR 0024). A
 * customer/maker JWT replayed against the Admin host 401s at the backend
 * (ADR 0013) — no parallel auth logic here.
 *
 * T-0118a is a READ-ONLY consumer: this module exports only the three
 * list reads. Slices b/c extend it with the write helpers (refund,
 * state, resolve, complete, retry, erase). NO write call lives here.
 */

import {
  type IAdminAuditLogItemDto,
  type IAdminInvoiceListItemDto,
  type IAdminOrderListItemDto,
  OrderState,
} from '../api-client/admin-api.v1';
import { apiFetch } from '../runtime/api-fetch';
import { type ApiError, type Result, ok } from '../runtime/result';

// Leading slash matters: apiFetch concatenates `${baseUrl}${path}` against
// host URLs that have no trailing slash, so an unrooted "api/v1/admin-orders"
// would produce http://localhost:5003api/v1/admin-orders (maker-orders.ts
// precedent).
const OrdersBase = '/api/v1/admin-orders';
const InvoicesBase = '/api/v1/admin-invoices';
const AuditBase = '/api/v1/audit-log';

// ---- Shared constants ----

/**
 * Mirrors of the backend list Validators' DefaultPageSize / MaxPageSize.
 * UX-only duplicates for URL clamping — the backend Validator stays
 * authoritative and 400s on a forced out-of-range value (maker-orders.ts
 * precedent). The three admin lists share the same window.
 */
export const ADMIN_LIST_DEFAULT_PAGE_SIZE = 20;
export const ADMIN_LIST_MAX_PAGE_SIZE = 50;

/** Re-export the order-state enum so route code never imports `lib/api-client/` directly. */
export { OrderState };

// ---- Order list (T-0105 GET /admin-orders) ----

/**
 * Mirror of <c>AdminOrderListItemDto</c> (T-0105). <c>createdAt</c> is
 * overridden to wire-shape <c>string</c> (ISO 8601) — <c>apiFetch</c>
 * returns <c>await response.json()</c> without the generated class's
 * <c>Date</c>-parsing constructor (maker-orders.ts B1 rationale). Unlike
 * the maker DTO, the admin DTO deliberately carries <c>customerEmail</c>
 * (operator-only surface — T-0118a §"Why the admin sees customerEmail").
 */
export type AdminOrderListItem = Readonly<Omit<IAdminOrderListItemDto, 'createdAt'>> & {
  readonly createdAt: string;
};

/** Wire shape of <c>PagedData&lt;AdminOrderListItemDto&gt;</c> (totalPages/hasNext/hasPrevious optional). */
export interface AdminOrdersPage {
  readonly items: readonly AdminOrderListItem[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
  readonly totalPages?: number;
  readonly hasNextPage?: boolean;
  readonly hasPreviousPage?: boolean;
}

/** Filter/page inputs for {@link getAdminOrders} — 1:1 with the T-0105 GET contract. */
export interface AdminOrdersInput {
  readonly page?: number;
  /** Clamped to {@link ADMIN_LIST_MAX_PAGE_SIZE} by the page; backend authoritative. */
  readonly pageSize?: number;
  readonly state?: OrderState;
  /** ISO 3166-1 alpha-2 (e.g. `CZ`). */
  readonly country?: string;
  readonly makerId?: string;
  readonly customerEmail?: string;
}

/** Generated envelope around the list page (<c>GetAllOrdersResponse { orders }</c>). */
interface GetAllOrdersEnvelope {
  readonly orders: AdminOrdersPage;
}

/**
 * Admin all-orders list (T-0105, consumed by T-0118a). Params are emitted
 * only when they diverge from the backend defaults so canonical URLs and
 * request lines stay clean (patterns.md B.8): `page` only when &gt; 1,
 * `pageSize` only when not the default, the rest only when set. Sorted
 * `CreatedAt DESC` server-side. The backend Validator stays authoritative.
 */
export async function getAdminOrders(
  input: AdminOrdersInput,
): Promise<Result<AdminOrdersPage, ApiError>> {
  const params = new URLSearchParams();
  if (input.page !== undefined && input.page > 1) params.set('page', String(input.page));
  if (input.pageSize !== undefined && input.pageSize !== ADMIN_LIST_DEFAULT_PAGE_SIZE) {
    params.set('pageSize', String(input.pageSize));
  }
  if (input.state !== undefined) params.set('state', input.state);
  if (input.country !== undefined && input.country !== '') params.set('country', input.country);
  if (input.makerId !== undefined && input.makerId !== '') params.set('makerId', input.makerId);
  if (input.customerEmail !== undefined && input.customerEmail !== '') {
    params.set('customerEmail', input.customerEmail);
  }
  const query = params.toString();
  const result = await apiFetch<GetAllOrdersEnvelope>(
    'admin',
    query ? `${OrdersBase}?${query}` : OrdersBase,
    { method: 'GET' },
  );
  return result.success ? ok(result.value.orders) : result;
}

// ---- Invoice list (T-0109 GET /admin-invoices) ----

/**
 * Mirror of <c>AdminInvoiceListItemDto</c> (T-0109). <c>createdAt</c>
 * wire-shape <c>string</c>; <c>type</c> is the <c>InvoiceType</c> ordinal
 * (0 = Customer, 1 = Fee — the generated client surfaces it as a raw
 * number, no nominal enum). The label map lives in the row component.
 */
export type AdminInvoiceListItem = Readonly<Omit<IAdminInvoiceListItemDto, 'createdAt'>> & {
  readonly createdAt: string;
};

/** Wire shape of <c>PagedData&lt;AdminInvoiceListItemDto&gt;</c>. */
export interface AdminInvoicesPage {
  readonly items: readonly AdminInvoiceListItem[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
  readonly totalPages?: number;
  readonly hasNextPage?: boolean;
  readonly hasPreviousPage?: boolean;
}

/** Filter/page inputs for {@link getAdminInvoices} — 1:1 with the T-0109 GET contract. */
export interface AdminInvoicesInput {
  readonly page?: number;
  readonly pageSize?: number;
  /** <c>InvoiceType</c> ordinal: 0 = Customer, 1 = Fee. */
  readonly type?: number;
  readonly country?: string;
  readonly recipient?: string;
  /** ISO `yyyy-MM-dd` from `<input type="date">` — backend binds DateTimeOffset. */
  readonly dateFrom?: string;
  readonly dateTo?: string;
}

/** Generated envelope around the list page (<c>GetAllInvoicesResponse { invoices }</c>). */
interface GetAllInvoicesEnvelope {
  readonly invoices: AdminInvoicesPage;
}

/**
 * Admin all-invoices list (T-0109, consumed by T-0118a). Sorted
 * `IssueDate DESC` server-side. Date params are passed as ISO strings —
 * the backend binds them to DateTimeOffset (the generated method types
 * them as `Date`, but apiFetch sends raw query strings the same way the
 * maker order list does). Params emitted only when set (B.8).
 */
export async function getAdminInvoices(
  input: AdminInvoicesInput,
): Promise<Result<AdminInvoicesPage, ApiError>> {
  const params = new URLSearchParams();
  if (input.page !== undefined && input.page > 1) params.set('page', String(input.page));
  if (input.pageSize !== undefined && input.pageSize !== ADMIN_LIST_DEFAULT_PAGE_SIZE) {
    params.set('pageSize', String(input.pageSize));
  }
  if (input.type !== undefined) params.set('type', String(input.type));
  if (input.country !== undefined && input.country !== '') params.set('country', input.country);
  if (input.recipient !== undefined && input.recipient !== '') {
    params.set('recipient', input.recipient);
  }
  if (input.dateFrom !== undefined && input.dateFrom !== '') params.set('dateFrom', input.dateFrom);
  if (input.dateTo !== undefined && input.dateTo !== '') params.set('dateTo', input.dateTo);
  const query = params.toString();
  const result = await apiFetch<GetAllInvoicesEnvelope>(
    'admin',
    query ? `${InvoicesBase}?${query}` : InvoicesBase,
    { method: 'GET' },
  );
  return result.success ? ok(result.value.invoices) : result;
}

// ---- Audit-log list (T-0108 GET /audit-log) ----

/**
 * Mirror of <c>AdminAuditLogItemDto</c> (T-0108). <c>createdAt</c>
 * wire-shape <c>string</c>; <c>notes</c> / <c>ipAddress</c> optional.
 * The before/after diff detail is slice c (US-admin-0015 AC-2) — this
 * list carries only the summary columns.
 */
export type AdminAuditLogItem = Readonly<Omit<IAdminAuditLogItemDto, 'createdAt'>> & {
  readonly createdAt: string;
};

/** Wire shape of <c>PagedData&lt;AdminAuditLogItemDto&gt;</c>. */
export interface AdminAuditPage {
  readonly items: readonly AdminAuditLogItem[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
  readonly totalPages?: number;
  readonly hasNextPage?: boolean;
  readonly hasPreviousPage?: boolean;
}

/** Filter/page inputs for {@link getAdminAuditLog} — 1:1 with the T-0108 GET contract. */
export interface AdminAuditInput {
  readonly page?: number;
  readonly pageSize?: number;
  readonly adminUserId?: string;
  readonly actionCode?: string;
  readonly targetEntity?: string;
  readonly dateFrom?: string;
  readonly dateTo?: string;
}

/** Generated envelope around the list page (<c>GetAdminAuditLogResponse { entries }</c>). */
interface GetAdminAuditLogEnvelope {
  readonly entries: AdminAuditPage;
}

/**
 * Admin audit-log list (T-0108, consumed by T-0118a). Sorted
 * `created_at DESC` server-side. Params emitted only when set (B.8).
 */
export async function getAdminAuditLog(
  input: AdminAuditInput,
): Promise<Result<AdminAuditPage, ApiError>> {
  const params = new URLSearchParams();
  if (input.page !== undefined && input.page > 1) params.set('page', String(input.page));
  if (input.pageSize !== undefined && input.pageSize !== ADMIN_LIST_DEFAULT_PAGE_SIZE) {
    params.set('pageSize', String(input.pageSize));
  }
  if (input.adminUserId !== undefined && input.adminUserId !== '') {
    params.set('adminUserId', input.adminUserId);
  }
  if (input.actionCode !== undefined && input.actionCode !== '') {
    params.set('actionCode', input.actionCode);
  }
  if (input.targetEntity !== undefined && input.targetEntity !== '') {
    params.set('targetEntity', input.targetEntity);
  }
  if (input.dateFrom !== undefined && input.dateFrom !== '') params.set('dateFrom', input.dateFrom);
  if (input.dateTo !== undefined && input.dateTo !== '') params.set('dateTo', input.dateTo);
  const query = params.toString();
  const result = await apiFetch<GetAdminAuditLogEnvelope>(
    'admin',
    query ? `${AuditBase}?${query}` : AuditBase,
    { method: 'GET' },
  );
  return result.success ? ok(result.value.entries) : result;
}

// ---- Invoice PDF download (BACKEND FOLLOW-UP — endpoint absent) ----
//
// The admin invoice-PDF-download endpoint is NOT on the current admin
// contract (`admin-api.v1.ts` exposes only the payout `csv(id)` bank-file
// method, not an invoice stream). Per T-0118a §Technical-notes + Option E,
// the "Stáhnout fakturu" button ships disabled-with-tooltip and the gap is
// logged as a thin backend follow-up — NOT wired to a guessed path. When
// the endpoint lands, the blob helper goes here:
//
//   export async function downloadAdminInvoice(
//     invoiceId: string,
//   ): Promise<Result<Blob, ApiError>> {
//     return apiFetch<Blob>('admin', `/api/v1/admin-invoices/${invoiceId}/pdf`,
//       { method: 'GET', parse: 'blob', timeoutMs: 120_000 });
//   }
//
// and the row swaps the disabled button for the blob-island download
// button (the maker `fee-invoice-download.tsx` mechanism). Until then no
// download path exists and none is faked.
