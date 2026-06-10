---
role: OrderMessage
kind: aggregate-child
status: accepted
---

# OrderMessage

## Responsibility

A single message in the two-party, order-scoped thread between customer and maker (T-0079). Admin has **read-only** visibility via the T-0111 unscoped admin queries — there is no admin write surface and no admin-authored messages at MVP.

## Collaborators

- **Order** (parent aggregate; owns the denormalized unread counters + the 5-minute notification-debounce pointers — see below)
- **User** (`AuthorUserId` audit trail; for a maker post this is the user behind the maker row, resolved via `IMakerRepository.GetByUserIdAsync`)
- **Outbox** (digest notification emails enqueued by the Post handlers, gated by the debounce predicate)

## Knows

- `OrderId` (FK to `orders.id`), `AuthorRole` (`Customer | Maker`), `AuthorUserId` (FK to `users.id`)
- `Body` — trimmed at construction, max 2000 chars (`OrderMessage.MaxBodyLength`)
- `ReadByCounterpartyAt` — read receipt stamped by the MarkAsRead bulk-UPDATE sweep; null until first read; set-once. Internal-only at MVP (no "seen at" badge in the API); feeds analytics + the unread-count denormalization
- `Auditable` base: `CountryCode`, `IsActive`, `CreatedBy/On`, etc.

## Does NOT know

- Attachments (order-level attachments handle file sharing at MVP)
- Email rendering / dispatch (outbox + `EmailSendService` routing own that; the digest email never contains the message body)
- The unread counters and debounce pointers themselves — those live on the parent `Order` (see `order.md`)

## Feature surface (6 per-audience one-file features)

Per the ADR 0013 / T-0082 compile-time IDOR precedent, the surface is split per audience — a maker JWT cannot dispatch a customer command and vice versa. All in `Core.AppServices/Features/OrderMessages/`:

| Feature | Host | What it does |
|---|---|---|
| `PostCustomerOrderMessage` | Customer | Insert message + bump `MakerUnreadMessageCount` + maybe enqueue maker digest email |
| `PostMakerOrderMessage` | Maker | Symmetric: bump `CustomerUnreadMessageCount` + maybe enqueue customer digest email |
| `GetCustomerOrderMessages` | Customer | Paged thread read (PageSize 1–50); cross-tenant probe → empty page, not 404 (T-0080 list-empty contract) |
| `GetMakerOrderMessages` | Maker | Symmetric paged read; makerId resolved via `GetByUserIdAsync` |
| `MarkCustomerOrderMessagesAsRead` | Customer | Bulk-UPDATE sweep + counter reset + pointer clear |
| `MarkMakerOrderMessagesAsRead` | Maker | Symmetric sweep |

Routes: `api/v1/orders/{orderId}/messages` (GET, POST) + `.../messages/mark-read` (POST) on both hosts.

## Notification debounce (5-minute digest, locked decision A.2)

The mechanics live on `Order` as two nullable pointer columns (`CustomerPendingNotificationEmailAt`, `MakerPendingNotificationEmailAt`) and four domain methods:

1. On post, the handler asks `order.ShouldEmitNotificationFor(authorRole, now)` — true when the **recipient's** pointer is null OR strictly older than `now − 5 min` (`Order.NotificationDebounceWindow`). At exactly the 5-minute boundary the pointer still suppresses.
2. If true: enqueue the outbox digest email (`order.message.posted.customerEmail` / `...makerEmail`) and call `order.MarkNotificationEmittedFor(authorRole, now)` to set the pointer. Pointer read + conditional update + message insert + counter bump + outbox row commit in **one** UoW transaction.
3. If false: the 2nd…Nth post inside the window is silenced — digest semantics, max 1 email / 5 min / order / direction.
4. On MarkAsRead, the reader's pointer is cleared unconditionally (`ClearPendingNotificationFor`) so the next message to that reader fires immediately rather than being silenced by a stale window.

Email-spam ceiling: the debounce caps notification volume regardless of posting rate (relevant context for Q-0011 rate limiting).

## Unread counts (denormalized, locked decision A.3)

`Order.CustomerUnreadMessageCount` / `Order.MakerUnreadMessageCount` give O(1) badge reads on the dashboard lists (T-0080 customer / T-0081 maker projections). `IncrementUnreadFor(authorRole)` bumps the **opposite** party's counter on post; `ResetUnreadFor(readerRole)` zeroes the reader's counter on MarkAsRead — an unconditional reset, never a decrement, so the counter cannot drift positive.

## Lifecycle

- **Created by:** `PostCustomerOrderMessage.Command` / `PostMakerOrderMessage.Command`
- **Modified by:** only `ReadByCounterpartyAt`, via the repository's bulk `ExecuteUpdateAsync` sweep (set-once)
- **Persisted by:** `IOrderMessageRepository` (writes) + `IOrderMessageQueries` (owner-scoped paged reads)
- **Destroyed by:** never at MVP (soft delete via `Auditable`; hard delete only on the GDPR right-to-erasure cascade, T-0110 — FK to `orders.id` cascades)

## Invariants

- **State guard:** posting is blocked on `PendingPayment` **only** (user ruling 2026-06-09 on review MEDIUM-2) → `order.message.notAllowedInState`. All other states — **including `Cancelled`**, `Disputed`, `Refunded` — stay open for post-cancel coordination between the parties. Reading and mark-as-read are never state-gated.
- The author must be the customer of the order or the maker fulfilling it. Enforced two ways: compile-time (per-audience features) and runtime (ownership `EXISTS` predicate baked into every SQL read AND the MarkAsRead bulk UPDATE). Cross-tenant POST/mark-read → generic `order.notFound`; cross-tenant GET → empty page. No enumeration oracle.
- `Body` is 1–2000 chars; validated at the command boundary (`order.message.bodyEmpty` / `order.message.bodyTooLong`) and re-checked in `OrderMessage.Create`.
- Messages are immutable after creation (no edit, no per-message delete).

## Known commit-boundary wrinkle (review LOW-2, accepted)

The MarkAsRead sweep is a **two-commit shape**: `ExecuteUpdateAsync` executes immediately in its own implicit transaction, while the `Order` side effects (counter reset + pointer clear) commit later via the UoW pipeline's `SaveChangesAsync`. A crash between the two commits can leave messages stamped read while the counter is still > 0 — transient drift that **self-heals on the next MarkAsRead** because `ResetUnreadFor` is unconditional. Documented in the handler XML docs (Gate 8 M-2 fold).

## Implementation pointer

- `backend/src/Makables.Core.Domain/OrderMessages/OrderMessage.cs` — entity + `Create` factory (+ `OrderMessageAuthorRole`, `OrderMessageDto`, `IOrderMessageRepository`, `IOrderMessageQueries` alongside)
- `backend/src/Makables.Core.Domain/Orders/Order.cs` — counters, pointers, and the five message-thread domain methods
- `backend/src/Makables.Core.AppServices/Features/OrderMessages/` — the 6 features
- `backend/src/Makables.Infra.Database/OrderMessages/` — repository (bulk sweeps) + queries (paged, `AsNoTracking`)
- `backend/src/Makables.Web.Customer/Controllers/OrderMessagesController.cs` + `backend/src/Makables.Web.Maker/Controllers/OrderMessagesController.cs`
- Migration: `backend/src/Makables.Infra.Database/Migrations/20260609174208_OrderCleanupBundle.cs` (table + FKs + the two thread indexes)

## Related

- Roles: `order` (parent; owns counters/pointers/methods), `user`, `maker`, `outbox`
- ADRs: 0001 personas (escrow trust model — no pre-purchase messaging), 0005 (per-audience hosts), 0013 (data scoping), 0014 (UoW atomicity), 0015 (this role file)
- Tickets: T-0079 (this surface), T-0080/T-0081 (unread-count list exposure), T-0111 (admin read-only), T-0110 (GDPR cascade)
