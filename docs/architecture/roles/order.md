---
role: Order
kind: aggregate
status: accepted
---

# Order

## Responsibility

Capture a customer's intent to purchase from a specific maker and track the state of that intent through to delivery, completion, or cancellation.

## Collaborators

- **Customer** (read: identity, contact info)
- **Maker** (read: identity, fulfillment availability)
- **Product** (read: title, base price, weight, category) — optional; null for custom orders
- **OrderPricing** (asks: compute pricing breakdown)
- **ShippingCarrier** (asks: create shipment when shipped)
- **PaymentProvider** (asks: initiate payment session)
- **OrderNumbering** (asks: next order number)

## Knows

- Order number (immutable, country-namespaced)
- Customer contact snapshot at order time (name, email, phone)
- Pricing snapshot: product price, shipping price, platform fee, maker payout, total, currency, VAT
- Shipping method choice (Zásilkovna pickup point or personal pickup) and Zásilkovna branch id if applicable
- State (see lifecycle) and the timestamp of every transition
- `PaymentProviderRef` once a payment session is created
- `ShippingCarrierRef` once a shipment is created
- `AutoDeliverAt` (set when shipped; null otherwise)
- `CancellationSource` (`Customer | AutoExpiry | Admin`, nullable — stamped by `Cancel(source)`; null on orders cancelled before T-0083). T-0083.
- Message-thread denormalization (T-0079, five columns): `CustomerUnreadMessageCount` + `MakerUnreadMessageCount` (INT NOT NULL DEFAULT 0 — O(1) dashboard badges) and `CustomerPendingNotificationEmailAt` + `MakerPendingNotificationEmailAt` (nullable TIMESTAMPTZ — the 5-min notification-debounce pointers), plus `cancellation_source` above
- Customer-supplied attachments (file paths)
- Customer notes

## Does NOT know

- How the invoice is rendered (that's `InvoiceService`)
- How the maker is paid (that's `PayoutBatch` and `PayoutService`)
- How emails are sent (outbox events fire)
- How disputes are adjudicated (separate role)
- Whether the maker has accepted other orders or has capacity (the maker dashboard surfaces that)

## Lifecycle

- **Created by:** `CreateOrder.Command` after customer pays — never speculatively
- **States** (see ADR 0002 for the state machine constraints):
  - `PendingPayment` → `Paid` (via `MarkOrderPaid.Command`, dispatched by Comgate webhook). T-0067: also persists `PaymentMethod` + Comgate-authoritative `PaidAt`, and emits two outbox events — `order.paid.customerEmail` (customer "thanks for your order") and `order.placed.makerEmail` (maker "new order arrived"). `invoice.generate` enqueue lands with T-0068.
  - `Paid` → `Accepted` (`AcceptOrder.Command`, maker action)
  - `Accepted` → `Shipped` (`ShipOrder.Command`, maker action; creates Packeta shipment)
  - `Shipped` → `Delivered` (`MarkOrderDelivered.Command`, customer action OR auto-deliver after 7 days OR carrier-confirmed)
  - `Delivered` → `Completed` (`CompletePayout.Command`, when paid out)
  - `PendingPayment | Paid | Accepted` → `Cancelled` via `Cancel(IClock clock, OrderCancellationSource source = OrderCancellationSource.Customer)` — the entity exposes the edge; which audience may take it is enforced by the command layer. T-0083's `CancelExpiredOrder.Command` (daily `CancelExpiredPendingPaymentOrdersFunction`, 02:00 UTC) cancels `PendingPayment` orders older than 24 h with `source: AutoExpiry` and Silent-Success no-ops when the order already left `PendingPayment`; T-0105/T-0107 pass their own source explicitly. The default `Customer` exists only for pre-T-0083 caller compatibility.
  - `Paid` and later → `Refunded` (admin action only)
  - `Shipped`+ → `Disputed` (customer or maker can open)
- **Persisted by:** `IOrderRepository`
- **Destroyed by:** never (soft delete only, and only by admin via `DeleteUserPermanently` for GDPR — and even then, the order persists in anonymized form for legal retention)

## Message-thread surface (T-0079)

`Order` owns the denormalized state for the order-message thread (see `order-message.md` for the thread itself) via five domain methods:

- `IncrementUnreadFor(authorRole)` — on a posted message, bump the **opposite** party's unread counter (the sender never increments their own; clamped at `int.MaxValue`).
- `ResetUnreadFor(readerRole)` — zero the reader's counter on a MarkAsRead sweep. Unconditional reset, not a decrement — idempotent and self-healing.
- `ShouldEmitNotificationFor(authorRole, now)` — the 5-min debounce predicate: true when the recipient's pending pointer is null OR strictly older than `now − NotificationDebounceWindow` (5 min); at exactly the boundary the pointer still suppresses.
- `MarkNotificationEmittedFor(authorRole, now)` — set the recipient's pointer after the outbox digest row is enqueued.
- `ClearPendingNotificationFor(readerRole)` — null the reader's pointer on MarkAsRead so the next message to them notifies immediately.

## Invariants

- An order's `OrderNumber` is set at creation and never changes.
- An order's pricing snapshot is set at creation and never recalculated. If a product's price changes later, existing orders are unaffected.
- A state transition is only valid if the target state is in the allowed-next-states list for the current state.
- `PaymentProviderRef` is set at most once via `MarkAsPaid` (the T-0065 `ReservePaymentSession` path may pre-stamp and re-stamp on session retry; `MarkAsPaid` refuses a DIFFERENT non-null incoming ref).
- `PaymentMethod` is set at most once on `MarkAsPaid` — a different non-null incoming value vs. the existing non-null value is refused with `OrderInvalidTransition`. T-0067.
- `ShippingCarrierRef` is set at most once (on first `Shipped`).
- `AutoDeliverAt` = `ShippedAt + 7 days`. Set atomically with `ShippedAt`.
- `CancellationSource` is stamped exactly once, at `Cancel` time, atomically with `CancelledAt`. Nullable only for orders cancelled before T-0083.
- The unread counters never go negative — `ResetUnreadFor` is an unconditional zero, `IncrementUnreadFor` only ever adds.

## Dispute surface (T-0106)

`Disputed` is a **parenthesis state** (patterns §A.22), not a terminal one:

- **Disputable allow-list = `Paid | Accepted | Shipped | Delivered`** (§C.1 — Paid/Accepted are the "maker silent / never ships" escalation lanes; `Completed` is OUT: payout settled, nothing to freeze; `PendingPayment`/`Cancelled`/`Refunded` have nothing in escrow).
- `OpenDispute(clock)` stamps `PreDisputeState = State` before flipping; `ResolveDispute(clock, restoreTo)` restores and clears the pointer; `DisputedAt` is KEPT as a historical marker. Invariant: `PreDisputeState` non-null ⇔ `State == Disputed`.
- **`Dispute` child entity** (Q2): category (`DisputeCategory`, carrier-reserved values gated at the party Validators), description (opener's own words, ≤2000), source (`Customer | Maker | Carrier | Admin`, always server-stamped), resolution outcome + customer-visible notes. At most one OPEN dispute per order (`ux_disputes_order_open UNIQUE (order_id) WHERE resolved_at IS NULL`); re-open is Silent-Success returning the existing id; re-resolve is a loud 409 (`order.dispute.notOpen`).
- **Resolution outcomes** (`ResolveDispute.Command`, admin host): `Resumed` → restore only; `Refunded` → nested `RefundOrder.Command` for the full remaining amount; `Cancelled` → `Cancel(clock, Admin)`, only legal from a Paid/Accepted restore.
- **Sweep exclusion by definition:** the auto-deliver + carrier sweeps select `State == Shipped`, so a disputed order drops out without predicate changes (pinned by integration test). The T-0079 message thread stays open in `Disputed` — it IS the evidence channel.
- The state flip + dispute row + `order.disputed.adminEmail` outbox row commit atomically; the admin recipient resolves at send time from `EmailOptions.AdminNotificationAddress` (`ADMIN_NOTIFICATION_EMAIL`).

## Refund surface (T-0105)

Full + partial refunds per user-locked Q1; pure predicate + mutator split:

- `RefundedAmountMinor` (cumulative, `BIGINT NOT NULL DEFAULT 0`) + computed `RemainingRefundableMinor = TotalAmountMinor − RefundedAmountMinor`.
- `ValidateRefund(amountMinor, acknowledgePostPayout)` — pure, no mutation. Gates: state ∈ {Paid, Accepted, Shipped, Delivered, Completed} (`payment.refund.invalidState`); amount ≤ remaining (`payment.refund.amountExceedsRemaining`); `Completed` requires the explicit acknowledgement flag (`payment.refund.postPayoutAckRequired`, Q5 — maker payout already settled, platform fronts the refund until T-0102's negative-balance ledger).
- `Refund(clock, amountMinor, acknowledgePostPayout)` — calls the predicate, accumulates; flips to `Refunded` + stamps `RefundedAt` only when cumulative == total. A partial refund changes NO state — the order stays live.
- **Sanctioned-command interlock:** `RefundOrder.Command` (admin host) is the ONLY path into `Refunded` — it calls Comgate `/v1.0/refund` BEFORE mutating (locked A.5). T-0107's manual state change blocks `→ Refunded` with `order.manualTransition.useRefundOrder`; T-0106's `ResolveDispute(Refunded)` restores `PreDisputeState` first, then dispatches `RefundOrder` for the full remaining amount — `Disputed` is never refunded directly.

## Manual state change (T-0107)

`ManualOrderTransitionPolicy.Evaluate(from, to, hasPaymentProviderRef)` — pure Domain class next to `Order` — is the strict allow-list (user-locked Q4) behind the admin escape hatch `ChangeOrderStateManually.Command` (mandatory ≥10-char reason → audit notes; no outbox/emails). Deterministic precedence: same-state NoOp → from-Refunded `notAllowed` → from-Disputed `useResolveDispute` → to-Refunded `useRefundOrder` → to-Disputed `useOpenDispute` → Delivered→Completed `useMarkPayoutBatchCompleted` → Paid|Accepted→Cancelled `useRefundOrder` → allow-list pair → `notAllowed`.

| From | Permitted manual targets | Routing |
|---|---|---|
| PendingPayment | Paid (only with `PaymentProviderRef` — lost-webhook recovery); Cancelled (manual expiry) | `MarkAsPaid(clock, existing ref)`; `Cancel(clock, Admin)` |
| Paid | Accepted | `Accept(clock)` |
| Accepted | Paid (undo mis-click; ref required) | `RevertAcceptance(clock)` — clears `AcceptedAt` |
| Shipped | Delivered (carrier-blind delivery) | `MarkAsDelivered(clock, AdminManual)` |
| Delivered / Completed / Cancelled / Refunded / Disputed | — | blocked; the error code names the sanctioned command |

`RevertAcceptance(IClock)` is the one new entity edge (Accepted → Paid, clears `AcceptedAt` so a re-accept stamps fresh); `OrderDeliverySource.AdminManual = 3` appends for the manual delivery stamp.

## Implementation pointer

`backend/src/Makables.Core.Domain/Orders/Order.cs`. State machine logic encapsulated in Order methods (`MarkAsPaid`, `Accept`, `RevertAcceptance`, `Ship`, `MarkDelivered`, `Cancel(clock, source)`, `Refund(clock, amount, ack)`, `OpenDispute`, `ResolveDispute(clock, restoreTo)`) that return `BusinessResult` on invalid transitions. `OrderCancellationSource`, the dispute enums + `Dispute` entity, and `ManualOrderTransitionPolicy` live alongside in `backend/src/Makables.Core.Domain/Orders/`. The T-0079/T-0083 columns ship in migration `20260609174208_OrderCleanupBundle.cs`; the T-0105 refund column in `20260612115151_AddOrderRefundedAmountAndRefundEmailTemplate.cs`; the T-0106 disputes table + `pre_dispute_state` in `20260612121152_AddDisputeTableAndPreDisputeState.cs`.

## Related

- ADRs: 0002, 0003, 0004, 0009, 0016, 0017, 0020
- Stories: most customer + maker stories
- Roles: `customer`, `maker`, `product`, `order-pricing`, `payment-provider`, `shipping-carrier`, `order-numbering`, `order-message`
