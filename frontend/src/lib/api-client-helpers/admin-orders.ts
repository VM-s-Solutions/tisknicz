/**
 * Hand-written wrappers around the authenticated admin order-command +
 * order-scoped audit-trail endpoints in the .NET Admin host (T-0105
 * refund, T-0107 manual state change, T-0106 dispute open/resolve, T-0108
 * audit-log). Same convention as <c>admin-client.ts</c> /
 * <c>maker-orders.ts</c> (patterns.md B.16): we call <see cref="apiFetch"/>
 * directly (not the NSwag-generated <c>AdminApi</c> class) because
 * <c>apiFetch</c> returns <c>Result&lt;T, ApiError&gt;</c> and the
 * generated client throws on every non-2xx.
 *
 * The four command endpoints live under <c>/api/v1/orders/{id}/...</c> on
 * the admin host (NOT under <c>/admin-orders</c> — verified against the
 * generated <c>AdminApi.refund/state/dispute/resolve</c> URL templates).
 * They require an authenticated admin session; the audience-scoped
 * cookies ride along automatically (browser: <c>credentials:'include'</c>;
 * SSR: cookie forwarding per patterns.md B.14 / ADR 0024). A
 * customer/maker JWT replayed against the Admin host 401s at the backend
 * (ADR 0013).
 *
 * T-0118b is a READ-ONLY consumer of <c>lib/api-client/</c>: this module
 * wraps the generated methods' request/response *shapes* (imported as
 * type-only) but never edits the generated file. The refund gate, the
 * manual-transition allow-list and the dispute parenthesis-state
 * semantics are ALL backend business logic — these helpers only post
 * inputs and surface the typed verdict.
 */

import {
  DisputeCategory,
  DisputeResolutionOutcome,
  type IAdminOrderDetailDto,
  type IChangeOrderStateManuallyResponse,
  type IOpenDisputeResponse,
  type IRefundOrderResponse,
  type IResolveDisputeResponse,
  OrderState,
} from '../api-client/admin-api.v1';
import { apiFetch } from '../runtime/api-fetch';
import { type ApiError, type Result, ok } from '../runtime/result';
import {
  type AdminAuditPage,
  ADMIN_LIST_DEFAULT_PAGE_SIZE,
  ADMIN_LIST_MAX_PAGE_SIZE,
  getAdminAuditLog,
  getAdminOrders,
} from './admin-client';

// Leading slash matters: apiFetch concatenates `${baseUrl}${path}` against
// host URLs with no trailing slash (admin-client.ts precedent).
const AdminOrdersBase = '/api/v1/admin-orders';

// Leading slash matters: apiFetch concatenates `${baseUrl}${path}` against
// host URLs with no trailing slash (admin-client.ts precedent).
const OrdersBase = '/api/v1/orders';

/** Re-export the command enums so route code never imports `lib/api-client/` directly. */
export { DisputeCategory, DisputeResolutionOutcome, OrderState };

// ---- Refund (T-0105 POST /orders/{id}/refund) ----

/** Mirror of <c>RefundOrderRequest</c> (T-0105). Amounts are minor units. */
export interface RefundOrderInput {
  /** Minor units; defaults to the order total at the call site, editable down for a partial. */
  readonly amountMinor: number;
  readonly reason: string;
  /** Required only when the order is `Completed` (maker already paid out — T-0105 AC-4). */
  readonly acknowledgePostPayout: boolean;
}

/** Wire shape of <c>RefundOrderResponse</c> (state may flip to `Refunded` on a full refund). */
export type RefundOrderResult = Readonly<IRefundOrderResponse>;

/**
 * Admin refund (T-0105, US-admin-0008). Money moves through Comgate. The
 * remaining-refundable cap, the post-payout gate and the
 * `payment.refund.*` verdicts are ALL backend — this helper only posts
 * the entered body. Failure codes: `payment.refund.invalidState` (409),
 * `payment.refund.amountExceedsRemaining` (422),
 * `payment.refund.postPayoutAckRequired` (422),
 * `payment.refund.noProviderRef` (409), Comgate `Permanent` (503).
 */
export async function refundOrder(
  orderId: string,
  input: RefundOrderInput,
): Promise<Result<RefundOrderResult, ApiError>> {
  return apiFetch<RefundOrderResult>(
    'admin',
    `${OrdersBase}/${encodeURIComponent(orderId)}/refund`,
    {
      method: 'POST',
      json: {
        amountMinor: input.amountMinor,
        reason: input.reason,
        acknowledgePostPayout: input.acknowledgePostPayout,
      },
    },
  );
}

// ---- Manual state change (T-0107 POST /orders/{id}/state) ----

/** Mirror of <c>ChangeOrderStateRequest</c> (T-0107). Reason is mandatory, min 10 chars (backend). */
export interface ChangeOrderStateInput {
  readonly targetState: OrderState;
  readonly reason: string;
}

/** Wire shape of <c>ChangeOrderStateManuallyResponse</c> (the resulting state). */
export type ChangeOrderStateResult = Readonly<IChangeOrderStateManuallyResponse>;

/**
 * Admin manual state change (T-0107, US-admin-0010). The transition
 * allow-list (`ManualOrderTransitionPolicy`) is Core.Domain — the UI
 * offers candidate targets and surfaces the policy's verdict. Failure
 * codes (409): `order.manualTransition.notAllowed` and the named-command
 * variants (`useRefundOrder`, `useOpenDispute`, `useResolveDispute`,
 * `useMarkPayoutBatchCompleted`, `paidRequiresProviderRef`).
 */
export async function changeOrderState(
  orderId: string,
  input: ChangeOrderStateInput,
): Promise<Result<ChangeOrderStateResult, ApiError>> {
  return apiFetch<ChangeOrderStateResult>(
    'admin',
    `${OrdersBase}/${encodeURIComponent(orderId)}/state`,
    {
      method: 'POST',
      json: { targetState: input.targetState, reason: input.reason },
    },
  );
}

// ---- Dispute open (T-0106 POST /orders/{id}/dispute) ----

/** Mirror of <c>OpenAdminDisputeRequest</c> (T-0106). All six categories allowed on the admin endpoint. */
export interface OpenAdminDisputeInput {
  readonly category: DisputeCategory;
  readonly description: string;
}

/** Wire shape of <c>OpenDisputeResponse</c>. */
export type OpenAdminDisputeResult = Readonly<IOpenDisputeResponse>;

/**
 * Admin dispute open (T-0106, US-admin-0011 — admin transcribing a
 * phoned-in dispute). Failure codes: `order.dispute.categoryNotAllowed`
 * (defensive — the admin endpoint allows all six), `order.invalidTransition`.
 */
export async function openAdminDispute(
  orderId: string,
  input: OpenAdminDisputeInput,
): Promise<Result<OpenAdminDisputeResult, ApiError>> {
  return apiFetch<OpenAdminDisputeResult>(
    'admin',
    `${OrdersBase}/${encodeURIComponent(orderId)}/dispute`,
    {
      method: 'POST',
      json: { category: input.category, description: input.description },
    },
  );
}

// ---- Dispute resolve (T-0106 POST /orders/{id}/dispute/resolve) ----

/** Mirror of <c>ResolveDisputeRequest</c> (T-0106). `resolutionNotes` is customer-visible. */
export interface ResolveDisputeInput {
  readonly outcome: DisputeResolutionOutcome;
  readonly resolutionNotes: string;
}

/** Wire shape of <c>ResolveDisputeResponse</c> (restored/terminal state + applied outcome). */
export type ResolveDisputeResult = Readonly<IResolveDisputeResponse>;

/**
 * Admin dispute resolve (T-0106, US-admin-0011). Failure codes:
 * `order.dispute.notOpen` (409), `order.invalidTransition`.
 */
export async function resolveDispute(
  orderId: string,
  input: ResolveDisputeInput,
): Promise<Result<ResolveDisputeResult, ApiError>> {
  return apiFetch<ResolveDisputeResult>(
    'admin',
    `${OrdersBase}/${encodeURIComponent(orderId)}/dispute/resolve`,
    {
      method: 'POST',
      json: { outcome: input.outcome, resolutionNotes: input.resolutionNotes },
    },
  );
}

// ---- Admin order detail (T-0127 GET /admin-orders/{orderId}) ----

/**
 * Mirror of <c>AdminOrderDetailDto</c> (T-0127, Q-0024). The full
 * privileged order header — number, state, all amounts/breakdown, country,
 * maker, `customerEmail`, contact snapshot, lifecycle timestamps. Admin is
 * privileged → NO GDPR redaction. <c>Date</c> fields are overridden to
 * wire-shape <c>string</c> (apiFetch returns raw JSON without the generated
 * class's Date-parsing constructor — admin-client.ts B1 rationale); the
 * page formats them with the cs-CZ date utils.
 */
export type AdminOrderDetail = Readonly<
  Omit<
    IAdminOrderDetailDto,
    | 'createdAt'
    | 'paidAt'
    | 'acceptedAt'
    | 'shippedAt'
    | 'deliveredAt'
    | 'completedAt'
    | 'cancelledAt'
    | 'refundedAt'
    | 'disputedAt'
  >
> & {
  readonly createdAt: string;
  readonly paidAt: string | undefined;
  readonly acceptedAt: string | undefined;
  readonly shippedAt: string | undefined;
  readonly deliveredAt: string | undefined;
  readonly completedAt: string | undefined;
  readonly cancelledAt: string | undefined;
  readonly refundedAt: string | undefined;
  readonly disputedAt: string | undefined;
};

/** Generated envelope around the detail (<c>GetAdminOrderDetailResponse { order }</c>). */
interface GetAdminOrderDetailEnvelope {
  readonly order: AdminOrderDetail;
}

/**
 * Admin order detail (T-0127, US-admin-0009 AC-2). The real privileged
 * header — replaces T-0118b's list-row scan. Unscoped (cross-tenant) over
 * `GetByIdUnscopedAsync`, AsNoTracking. 404 (unknown / inactive id) →
 * `OrderNotFound` (reused code). The detail header + the existing audit-log
 * trail together cover the route; line items / message thread stay out of
 * scope (ticket §Out of scope).
 */
export async function getAdminOrderDetail(
  orderId: string,
): Promise<Result<AdminOrderDetail, ApiError>> {
  const result = await apiFetch<GetAdminOrderDetailEnvelope>(
    'admin',
    `${AdminOrdersBase}/${encodeURIComponent(orderId)}`,
    { method: 'GET' },
  );
  return result.success ? ok(result.value.order) : result;
}

// ---- Per-user in-flight signal (T-0127, the delete-user pre-disable) ----

/**
 * The order states that BLOCK a GDPR hard-delete (§A.3) — T-0110's
 * interlock set plus `Disputed`. A user with any order in one of these
 * states cannot be erased; the backend T-0110 gate
 * (`user.cannotDeleteWithInFlightOrders`) stays authoritative. This list
 * drives the PROACTIVE pre-disable only (UX layer).
 */
const IN_FLIGHT_ORDER_STATES: readonly OrderState[] = [
  OrderState.PendingPayment,
  OrderState.Paid,
  OrderState.Accepted,
  OrderState.Shipped,
  OrderState.Disputed,
];

/**
 * Per-user in-flight signal (T-0127, Q-0024 §A.3). Probes the filtered
 * admin-orders read once per in-flight state for the target user; the
 * FIRST non-empty page means the user has a blocking order. Drives the
 * delete-user proactive pre-disable (AC-12). On a read failure returns
 * `false` (do NOT pre-disable on a transient error — the backend gate is
 * authoritative and re-checks at submit). The admin-orders read takes a
 * single `state` per call (1:1 with T-0105's contract), so the few in-
 * flight states are probed with `pageSize: 1` — emptiness is the signal,
 * no rows are rendered.
 */
export async function userHasInFlightOrders(
  customerUserId: string,
): Promise<Result<boolean, ApiError>> {
  for (const state of IN_FLIGHT_ORDER_STATES) {
    const result = await getAdminOrders({
      page: 1,
      pageSize: 1,
      state,
      customerUserId,
    });
    if (!result.success) return result;
    if (result.value.totalCount > 0) return ok(true);
  }
  return ok(false);
}

// ---- Order-scoped audit trail (T-0108 GET /audit-log filtered to the order) ----

/**
 * The order's audit trail (the audit read for the detail route — §C.2).
 * Paired with the privileged header from {@link getAdminOrderDetail}
 * (T-0127). The generated `auditLog` query has no `targetId` filter, so we
 * fetch the `targetEntity: "order"` slice and narrow to this order client-
 * side (on the server) before paginating — the same shape the global audit
 * list (T-0118a) consumes.
 *
 * NOTE: because the backend filter is entity-coarse, a busy log could
 * page past this order's rows; the gap is bounded and logged with the
 * detail-DTO follow-up. A `targetId` query param is the clean fix.
 */
export async function getAdminOrderAuditTrail(
  orderId: string,
  page: number,
  pageSize: number,
): Promise<Result<AdminAuditPage, ApiError>> {
  const clampedPageSize = Math.min(
    Math.max(pageSize, 1),
    ADMIN_LIST_MAX_PAGE_SIZE,
  );
  const result = await getAdminAuditLog({
    page: page > 0 ? page : 1,
    pageSize: clampedPageSize,
    targetEntity: 'order',
  });
  if (!result.success) return result;

  const items = result.value.items.filter((entry) => entry.targetId === orderId);
  return ok({ ...result.value, items });
}

export { ADMIN_LIST_DEFAULT_PAGE_SIZE, ADMIN_LIST_MAX_PAGE_SIZE };
export type { AdminAuditPage } from './admin-client';
