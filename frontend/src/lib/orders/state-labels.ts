/**
 * Presentational OrderState → i18n-key mapping (T-0084b). Display
 * mapping ONLY — no transition logic lives here or anywhere on the
 * frontend (the order state machine is backend business logic). Shared
 * by /objednavka/[id], the confirmation page (T-0085) and later the
 * order dashboards (T-0086/87).
 */

import { OrderState } from '../api-client-helpers/orders-client';
import type { MessageKey } from '../i18n';

/**
 * Wire-value union for OrderState (T-0087a). The customer- and maker-
 * host NSwag clients each generate their own *nominal* enum with
 * identical string values, so the maker enum is not assignable to the
 * customer enum (or vice versa). The template-literal union accepts
 * members of both — the display maps below stay shared instead of being
 * forked per audience.
 */
export type OrderStateValue = `${OrderState}`;

export function orderStateLabelKey(state: OrderStateValue): MessageKey {
  // String-literal cases (not enum members): TS only narrows the
  // OrderStateValue union — and thus proves exhaustiveness below — when
  // the case labels are literal types. Runtime values are identical.
  switch (state) {
    case 'PendingPayment':
      return 'order.state.pending_payment';
    case 'Paid':
      return 'order.state.paid';
    case 'Accepted':
      return 'order.state.accepted';
    case 'Shipped':
      return 'order.state.shipped';
    case 'Delivered':
      return 'order.state.delivered';
    case 'Completed':
      return 'order.state.completed';
    case 'Cancelled':
      return 'order.state.cancelled';
    case 'Refunded':
      return 'order.state.refunded';
    case 'Disputed':
      return 'order.state.disputed';
    default: {
      // Compile-time exhaustiveness check — adding a 10th state to the
      // backend enum fails tsc here instead of rendering a blank label.
      const exhaustive: never = state;
      return exhaustive;
    }
  }
}

/**
 * Display-only classification used by the T-0085 confirmation page:
 * Paid and every later state render the success/thank-you frame — a
 * customer revisiting the bookmark days later sees the thank-you, not
 * a poller. NOT a transition rule; the state machine stays backend-side.
 */
const PAID_OR_LATER_STATES: ReadonlySet<OrderState> = new Set([
  OrderState.Paid,
  OrderState.Accepted,
  OrderState.Shipped,
  OrderState.Delivered,
  OrderState.Completed,
]);

export function isPaidOrLater(state: OrderState): boolean {
  return PAID_OR_LATER_STATES.has(state);
}

/**
 * Mirror of the `Badge` UI primitive's variant union — kept local so
 * this display map stays importable without pulling a component module
 * into helper code.
 */
export type OrderStateBadgeVariant = 'default' | 'success' | 'warning' | 'error' | 'brand';

/**
 * Per-state badge tone (T-0086a §C) — display-only lookup, NOT a state
 * machine. Shared by the customer order list, the tracking detail and
 * later the maker dashboards (T-0087a/b).
 */
export function orderStateBadgeVariant(state: OrderStateValue): OrderStateBadgeVariant {
  // String-literal cases for union narrowing — see orderStateLabelKey.
  switch (state) {
    case 'PendingPayment':
      return 'warning';
    case 'Paid':
    case 'Accepted':
      return 'brand';
    case 'Shipped':
      return 'default';
    case 'Delivered':
    case 'Completed':
      return 'success';
    case 'Cancelled':
    case 'Disputed':
      return 'error';
    case 'Refunded':
      return 'warning';
    default: {
      const exhaustive: never = state;
      return exhaustive;
    }
  }
}
