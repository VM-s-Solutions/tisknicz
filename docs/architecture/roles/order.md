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
  - Most states → `Cancelled` (with rules)
  - `Paid` and later → `Refunded` (admin action only)
  - `Shipped`+ → `Disputed` (customer or maker can open)
- **Persisted by:** `IOrderRepository`
- **Destroyed by:** never (soft delete only, and only by admin via `DeleteUserPermanently` for GDPR — and even then, the order persists in anonymized form for legal retention)

## Invariants

- An order's `OrderNumber` is set at creation and never changes.
- An order's pricing snapshot is set at creation and never recalculated. If a product's price changes later, existing orders are unaffected.
- A state transition is only valid if the target state is in the allowed-next-states list for the current state.
- `PaymentProviderRef` is set at most once via `MarkAsPaid` (the T-0065 `ReservePaymentSession` path may pre-stamp and re-stamp on session retry; `MarkAsPaid` refuses a DIFFERENT non-null incoming ref).
- `PaymentMethod` is set at most once on `MarkAsPaid` — a different non-null incoming value vs. the existing non-null value is refused with `OrderInvalidTransition`. T-0067.
- `ShippingCarrierRef` is set at most once (on first `Shipped`).
- `AutoDeliverAt` = `ShippedAt + 7 days`. Set atomically with `ShippedAt`.

## Implementation pointer

`backend/src/Makables.Core.Domain/Orders/Order.cs`. State machine logic encapsulated in Order methods (`MarkAsPaid`, `Accept`, `Ship`, `MarkDelivered`, etc.) that return `BusinessResult` on invalid transitions.

## Related

- ADRs: 0002, 0003, 0004, 0009, 0016, 0017, 0020
- Stories: most customer + maker stories
- Roles: `customer`, `maker`, `product`, `order-pricing`, `payment-provider`, `shipping-carrier`, `order-numbering`
