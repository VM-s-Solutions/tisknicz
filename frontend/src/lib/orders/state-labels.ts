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
