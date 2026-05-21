---
role: OrderMessage
kind: aggregate
status: accepted
---

# OrderMessage

## Responsibility

A single message in the order-scoped thread between customer and maker (and admin, who can view all).

## Collaborators

- **Order** (parent; messages belong to one order)
- **User** (the sender)

## Knows

- `OrderId`, `SenderUserId`, `Content`
- `CreatedAt`

## Does NOT know

- Read receipts (post-MVP candidate)
- Attachments (post-MVP — order-level attachments handle file sharing at MVP)

## Lifecycle

- **Created by:** `SendMessage.Command` (customer or maker; only valid on `Paid` and later states)
- **Modified by:** never (immutable)
- **Persisted by:** `IOrderMessageRepository`
- **Destroyed by:** never (soft delete only on the parent order's GDPR cascade)

## Invariants

- Messages can only be sent on an order in `Paid` and later states. `PendingPayment` orders don't have messaging.
- A sender must be the customer of the order, the maker fulfilling it, or an admin.

## Implementation pointer

`backend/src/Makables.Core.Domain/Orders/OrderMessage.cs`.

## Related

- Roles: `order`, `user`
- ADRs: 0001 personas (escrow trust model — no pre-purchase messaging)
