/**
 * Presentational OrderState → i18n-key mapping (T-0084b). Display
 * mapping ONLY — no transition logic lives here or anywhere on the
 * frontend (the order state machine is backend business logic). Shared
 * by /objednavka/[id], the confirmation page (T-0085) and later the
 * order dashboards (T-0086/87).
 */

import { OrderState } from '../api-client-helpers/orders-client';
import type { MessageKey } from '../i18n';

export function orderStateLabelKey(state: OrderState): MessageKey {
  switch (state) {
    case OrderState.PendingPayment:
      return 'order.state.pending_payment';
    case OrderState.Paid:
      return 'order.state.paid';
    case OrderState.Accepted:
      return 'order.state.accepted';
    case OrderState.Shipped:
      return 'order.state.shipped';
    case OrderState.Delivered:
      return 'order.state.delivered';
    case OrderState.Completed:
      return 'order.state.completed';
    case OrderState.Cancelled:
      return 'order.state.cancelled';
    case OrderState.Refunded:
      return 'order.state.refunded';
    case OrderState.Disputed:
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
export function orderStateBadgeVariant(state: OrderState): OrderStateBadgeVariant {
  switch (state) {
    case OrderState.PendingPayment:
      return 'warning';
    case OrderState.Paid:
    case OrderState.Accepted:
      return 'brand';
    case OrderState.Shipped:
      return 'default';
    case OrderState.Delivered:
    case OrderState.Completed:
      return 'success';
    case OrderState.Cancelled:
    case OrderState.Disputed:
      return 'error';
    case OrderState.Refunded:
      return 'warning';
    default: {
      const exhaustive: never = state;
      return exhaustive;
    }
  }
}
