---
id: T-0079
title: OrderMessage two-party thread (customer ↔ maker async messaging)
status: ready
size: M
owner: dotnet-backend
created: 2026-06-09
updated: 2026-06-09
depends_on: [T-0060, T-0081]
blocks: [T-0086, T-0087]
user_stories: [US-customer-0014, US-maker-0011]
adrs: [0013, 0014, 0017, 0019, 0020, 0023]
phase: 4
manual_steps: []
security_touching: true
layers: [domain, appservices, infra-database, infra-email, web-customer, web-maker]
---

# T-0079 — OrderMessage two-party thread (customer ↔ maker async messaging)

## Context

T-0079 ships the **two-party async message thread** on every Order. The thread is the **single coordination channel** between customer and maker post-payment: per the bundle's GDPR data-minimization stance (locked in T-0081 §A.2), the customer's email is NEVER exposed in any maker-facing response — makers coordinate by posting on this thread, and the recipient receives a debounced "you have a new message" email via the existing outbox+EmailSendService pipeline (ADR 0019). Admin gets read-only access via the existing T-0111 admin tooling; T-0079 does NOT ship admin endpoints or a moderator role.

This ticket directly satisfies **US-customer-0014 — Message the maker about an order** (AC-1 ≥1 ≤2000 char persistence + outbox notify, AC-3 5-min digest debounce) and **US-maker-0011 — Message the customer** (AC-1 persistence + outbox notify, AC-2 5-min debounce). It also closes the forward-compat field already shipped on `MakerOrderListItemDto.UnreadMessageCount: int?` (T-0081 §C.7 "populated as `null` until T-0079 ships") — the projection now reads `o.MakerUnreadMessageCount` instead of returning null. T-0080's `CustomerOrderListItemDto` did NOT reserve a forward-compat field for unread counts (the messages bundle hadn't been groomed at T-0080 ship time); this ticket adds the field and triggers a contract addition on the customer host as well.

The unread-count source of truth is **denormalized onto the Order entity** via two new columns: `customer_unread_message_count INT NOT NULL DEFAULT 0` and `maker_unread_message_count INT NOT NULL DEFAULT 0`. Reads stay O(1) per row at list time (no per-row subquery, no JOIN to a count). Writes are O(1) per posted message + O(1) per mark-as-read action (set the counterparty's counter to 0 in one UPDATE). The alternative — computing the unread count via `SELECT COUNT(*) FROM order_messages WHERE order_id = ? AND author_role = ? AND read_by_counterparty_at IS NULL` at every list-row projection — was rejected per the bundle's flat-DTO + no-N+1 stance (see Alternatives §K).

The notification side is a **5-minute digest debounce**, NOT a per-message email storm. When the recipient party has unread messages, the outbox emits ONE email per recipient per 5-minute window — not per posted message. Implementation: a `pending_notification_email_at` pointer per party on the Order entity (two timestamps: `customer_pending_notification_email_at` + `maker_pending_notification_email_at`). On `PostMessage`, the handler checks the **recipient's** pointer — if null OR if the pointer is older than 5 minutes ago, emit the outbox event and update the pointer to "now". If the pointer is newer than 5 minutes ago, suppress the emit (the previously-emitted email will cover the new message when the recipient opens the thread). On `MarkAsRead`, the handler clears the recipient's pointer (next message will fire again immediately, not be silenced by a stale debounce window). The 5-min window matches the existing outbox retry rhythm (ADR 0020) and the digest semantics already named in US-customer-0014 AC-3 / US-maker-0011 AC-2.

Per ADR 0013 + T-0082 precedent, the feature surface is **split per-audience at compile time**: separate `PostCustomerOrderMessage` + `PostMakerOrderMessage`, separate `GetCustomerOrderMessages` + `GetMakerOrderMessages`, separate `MarkCustomerOrderMessagesAsRead` + `MarkMakerOrderMessagesAsRead`. Each command is wired into a single host. A customer JWT cannot dispatch the maker command — the type itself is not registered on the customer host. The IDOR shield is the WHERE-predicate baked into the scoped repository read methods AND the per-host MediatR registration. No conditional `if (authorRole == X)` branching inside a single shared handler; the audience split IS the branch.

No background Azure Function ships in T-0079. The debounce is a **post-write predicate in the PostMessage handler**, not a separate scheduled digest job. This is intentional per ADR 0020: defer creating Functions until the work is truly periodic OR truly off-thread. Per-message debounce-check fits comfortably in the post + UoW commit pipeline; adding a Function for this would introduce job-scheduling complexity for a one-line predicate.

## Locked design decisions

Captured per `docs/process/deliberation.md`. The user locked 4 dimensions at `/feature` step 3 (two-party-only with admin read-only via T-0111; 5-minute digest debounce; unread count denormalized as 2 new Order columns; outbox event per posted message routed by recipient party). 14 PM-absorbed decisions follow from T-0080 / T-0081 / T-0082 precedents + bundle conventions.

### A. User-locked at /feature step 3 (non-negotiable)

1. **Two-party only — customer + maker.** Admin gets READ-ONLY access via the existing T-0111 admin tooling (out of scope for THIS ticket). No moderator-post role. No system-event auto-posts on state transitions (e.g., no auto "Order paid" message). The thread is a human conversation between two parties. **Rejected:** three-party with admin moderator-post role (admin pollution of the human channel + needs special "admin" badge UI + complicates audit posture); system-event auto-posts on every state change (noise — the customer/maker already see the state transitions in the order header; the message thread should carry only what a human chose to write).

2. **Notification debounce = 5-minute digest.** When the recipient has unread messages, the outbox emits ONE email per recipient per 5-minute window — NOT per posted message. Implementation hint: per-party pending pointer on Order (`{customer,maker}_pending_notification_email_at`); PostMessage handler emits only if the recipient's pointer is null OR older than 5 min, then sets the pointer to now. MarkAsRead clears the pointer. **Rejected:** per-message email (storm risk if a maker types 8 short messages in a minute — recipient gets 8 emails; bad UX + outbox cost); 1-minute window (too short to actually batch typical typing bursts); 15-minute window (too slow to feel responsive); separate Azure Function digest job (over-engineered per ADR 0020 — one-line predicate doesn't justify a job).

3. **Unread count denormalized on Order — 2 new INT columns.** `customer_unread_message_count INT NOT NULL DEFAULT 0` + `maker_unread_message_count INT NOT NULL DEFAULT 0`. The `customer_*` column counts messages authored by the **maker** that the **customer** has NOT marked as read (i.e., messages the customer hasn't yet seen). Symmetric for `maker_*`. T-0081's `MakerOrderListItemDto.UnreadMessageCount` (already shipped, currently returns null) now reads `o.MakerUnreadMessageCount`. T-0080's `CustomerOrderListItemDto` gains an `UnreadMessageCount` field as a NEW contract addition (T-0080 didn't reserve one because the messages bundle hadn't been groomed). **Rejected:** computed via subquery at list projection time (per-row N+1 OR a correlated subquery — both unacceptable at list scale per the bundle's flat-DTO stance); separate `order_message_read_state` table (extra JOIN per list row + extra write per mark-as-read; the 2-column denormalization is the simpler shape and matches how every list view consumes the count); count messages-since-last-seen timestamp (timestamp semantics are slipperier than an integer counter that domain methods clamp at zero).

4. **Outbox event per posted message — routed by recipient party.** Two new event types: `order.message.posted.customerEmail` (fired when the maker posted → customer is the recipient) + `order.message.posted.makerEmail` (fired when the customer posted → maker is the recipient). The sender is NEVER notified about their own post. The 5-min debounce (A.2) determines whether the event actually emits or is suppressed. Format mirrors existing email outbox routing per ADR 0019. **Rejected:** single generic `order.message.posted` event routed downstream in EmailSendService (loses the recipient-party signal at the outbox-table level + complicates retry semantics + makes debounce harder to reason about — the debounce IS per-recipient-party); skip outbox entirely and send email inline from the handler (violates ADR 0017 — every cross-aggregate side effect goes through the outbox).

### B. ADR-locked (no relitigation)

- **ADR 0013 (per-audience JWT enforcement + scoped repo split).** The customer endpoints run under the `Web.Customer` host audience; the maker endpoints run under `Web.Maker`. A customer JWT cannot dispatch the maker command — the type isn't registered on the customer host. The scoped repository's 4 read methods + 4 write methods all bake the audience predicate into the WHERE clause: `GetByOrderForCustomerAsync(orderId, customerUserId, ...)` and `MarkAsReadForCustomerAsync(orderId, customerUserId)` both `Where(o => o.CustomerUserId == customerUserId)`. The predicate IS the IDOR shield — a customer literally cannot post to nor read another customer's thread because the SQL never selects nor updates the row.
- **ADR 0014 (UoW pipeline).** `PostMessage` + `MarkAsRead` are commands → `UnitOfWorkPipelineBehavior` commits per request. Handlers NEVER call `SaveChangesAsync()`. `GetMessages` is a query — no UoW behavior; AsNoTracking projection only. ValidationPipelineBehavior runs on all 6 features (body length, page-size clamp, enum range).
- **ADR 0017 (outbox).** `OrderMessagePostedCustomerEmail` + `OrderMessagePostedMakerEmail` events go through the outbox table per ADR 0017. The PostMessage handler inserts the outbox row in the same DbContext as the OrderMessage insert + Order unread-count increment + pending-pointer update — all atomic per UoW commit. `MarkAsRead` does NOT emit an outbox event (the recipient's app session is already in the thread; there's nothing to notify) but DOES clear the pending pointer so the next post can fire immediately.
- **ADR 0019 (email).** `EmailSendService` (existing) gets a new `IsOrderMessagePosted` method + handler branch. The handler renders the appropriate template (2 new templates: customer-recipient + maker-recipient), resolves the recipient address via the existing `IUserRepository`/`IMakerRepository` lookup, and dispatches through the configured email provider. The email body links back to the order detail page on the appropriate host (customer host for customer recipient, maker host for maker recipient).
- **ADR 0020 (background jobs / outbox).** **NO new Azure Function ships.** The 5-min debounce is enforced **at PostMessage handler time** — the handler reads the recipient's `pending_notification_email_at` pointer, decides whether to emit the outbox event, and updates the pointer in the same UoW commit. The existing outbox dispatcher Function (already shipped) picks up the emitted event on its normal cadence. This intentionally avoids a job-scheduling layer for a one-line predicate.
- **ADR 0023 (read-side queries split from write-side repositories + paging NFRs).** New `IOrderMessageQueries` interface (read-side, AsNoTracking projection-only) co-exists with the new `IOrderMessageRepository` (write-side: AddAsync + MarkAsRead). Reads are paginated `50/page` (cap, default 50 — message threads are read top-to-bottom; one page covers the typical thread; pagination is for the long-tail). Index `(order_id, created_at DESC)` on `order_messages` table per AC-12.

### C. PM-absorbed (no user input needed)

1. **New entity `OrderMessage : Auditable`** in `Core.Domain/OrderMessages/OrderMessage.cs`:
   - `Id: string` (PK, ULID per project convention)
   - `OrderId: string` (FK → Order, indexed)
   - `AuthorRole: OrderMessageAuthorRole` (enum `Customer = 1 | Maker = 2`; stored as INT)
   - `AuthorUserId: string` (FK → User; the actual posting user — denormalized identity for the audit trail even though `AuthorRole + Order` is theoretically sufficient)
   - `Body: string` (NOT NULL, max 2000 chars)
   - `ReadByCounterpartyAt: DateTimeOffset?` (nullable; set when the counterparty's `MarkAsRead` call sweeps this message)
   - `CreatedAt` / `CreatedBy` / `UpdatedAt` / `DeactivatedAt` inherited from `Auditable`. Soft-delete via `DeactivatedAt`; deactivated rows excluded by the global Auditable query filter.

2. **New enum `OrderMessageAuthorRole`** in `Core.Domain/OrderMessages/OrderMessageAuthorRole.cs`:
   ```csharp
   public enum OrderMessageAuthorRole
   {
       Customer = 1,
       Maker = 2,
   }
   ```
   Explicit numeric values are stable wire codes per project convention.

3. **EF migration `Add_OrderMessage_table_and_Order_unread_counts_and_pending_pointers`**:
   - New table `order_messages` with PK + FK to `orders.id` + FK to `users.id` + index `(order_id, created_at DESC)` + index `(order_id, read_by_counterparty_at)` for the unread sweep (partial index `WHERE read_by_counterparty_at IS NULL` if Postgres dialect supports — implementer's call).
   - Alter table `orders`:
     - `customer_unread_message_count INT NOT NULL DEFAULT 0`
     - `maker_unread_message_count INT NOT NULL DEFAULT 0`
     - `customer_pending_notification_email_at TIMESTAMPTZ NULL`
     - `maker_pending_notification_email_at TIMESTAMPTZ NULL`
   - Backfill: all existing orders default to 0 / NULL — no historical message data to migrate.

4. **New repository interface `IOrderMessageRepository`** (write-side, ADR 0013-scoped) in `Core.Domain/OrderMessages/IOrderMessageRepository.cs`:
   - `Task AddAsync(OrderMessage message, CancellationToken ct);`
   - `Task<int> MarkAsReadForCustomerAsync(string orderId, string customerUserId, CancellationToken ct);` — bulk UPDATE of all `author_role = Maker AND read_by_counterparty_at IS NULL` rows for the order; returns the count swept. Predicate enforces customer scope.
   - `Task<int> MarkAsReadForMakerAsync(string orderId, string makerId, CancellationToken ct);` — symmetric.

5. **New query interface `IOrderMessageQueries`** (read-side, ADR 0023, AsNoTracking projection-only) in `Core.Domain/OrderMessages/IOrderMessageQueries.cs`:
   - `Task<PagedData<OrderMessageDto>> GetByOrderForCustomerAsync(string orderId, string customerUserId, int page, int pageSize, CancellationToken ct);`
   - `Task<PagedData<OrderMessageDto>> GetByOrderForMakerAsync(string orderId, string makerId, int page, int pageSize, CancellationToken ct);`
   - Both methods: `Where(o => o.Id == orderId && o.CustomerUserId == customerUserId)` (or maker symmetric) JOIN to order_messages — IDOR shield baked into the EF predicate. Sort `CreatedAt DESC` (newest first); tiebreak `Id DESC`.

6. **`OrderMessageDto`** (NEW, in `Core.AppServices/Features/OrderMessages/DTOs/OrderMessageDto.cs`):
   ```csharp
   public sealed record OrderMessageDto(
       string Id,
       string OrderId,
       OrderMessageAuthorRole AuthorRole,
       string AuthorName,
       string Body,
       DateTimeOffset CreatedAt,
       bool IsMine);
   ```
   `AuthorName` is denormalized at projection time (joined from `users.full_name` OR `makers.display_name` depending on author role; implementer picks per T-0081 §C.5 MakerName precedent). `IsMine` computed at projection: `o.AuthorRole == Customer && o.AuthorUserId == sessionUserId` (and maker symmetric — passed into the projection as a parameter).

7. **Six one-file features per the per-audience compile-time IDOR shield convention** (mirrors T-0082):
   - `Core.AppServices/Features/OrderMessages/PostCustomerOrderMessage.cs` — `Command(string OrderId, string Body)` + Validator + Handler. Resolves `customerUserId` from session. Inserts `OrderMessage(AuthorRole=Customer, ...)`. Increments `Order.MakerUnreadMessageCount`. Checks `Order.MakerPendingNotificationEmailAt`: if null OR > 5 min ago → enqueue `OrderMessagePostedMakerEmail` outbox row + set pointer to now. Returns `PostCustomerOrderMessageResponse(MessageId, CreatedAt)`.
   - `Core.AppServices/Features/OrderMessages/PostMakerOrderMessage.cs` — symmetric. Author = Maker. Increments `Order.CustomerUnreadMessageCount`. Outbox event = `OrderMessagePostedCustomerEmail`. Pointer = `Order.CustomerPendingNotificationEmailAt`.
   - `Core.AppServices/Features/OrderMessages/GetCustomerOrderMessages.cs` — `Query(string OrderId, int Page=1, int PageSize=50)` + Validator + Handler. Calls `IOrderMessageQueries.GetByOrderForCustomerAsync`. Returns `GetCustomerOrderMessagesResponse(PagedData<OrderMessageDto>)`.
   - `Core.AppServices/Features/OrderMessages/GetMakerOrderMessages.cs` — symmetric.
   - `Core.AppServices/Features/OrderMessages/MarkCustomerOrderMessagesAsRead.cs` — `Command(string OrderId)` + Handler. Calls `IOrderMessageRepository.MarkAsReadForCustomerAsync` → resets `Order.CustomerUnreadMessageCount = 0` + clears `Order.CustomerPendingNotificationEmailAt` (so the maker's next post fires immediately, not silenced by a stale debounce). Returns `MarkCustomerOrderMessagesAsReadResponse(int MarkedCount)`.
   - `Core.AppServices/Features/OrderMessages/MarkMakerOrderMessagesAsRead.cs` — symmetric.

8. **Domain methods on Order** (clamped + tested-first per the TDD-red-first surface): `IncrementUnreadForCustomer()`, `IncrementUnreadForMaker()`, `ResetUnreadForCustomer()`, `ResetUnreadForMaker()`. All increments clamp at MAX_INT (defensive — never overflow). All resets clamp at zero (defensive — never go negative). The pending-pointer logic also lives on Order: `bool ShouldEmitNotificationForCustomer(DateTimeOffset now)` and `MarkNotificationEmittedForCustomer(DateTimeOffset now)` (and maker symmetric). Predicate: `pointer == null || pointer < now - 5min`. The 5-minute constant lives as a `TimeSpan` constant on Order (`NotificationDebounceWindow = TimeSpan.FromMinutes(5);`).

9. **Controllers** — new actions on existing per-host OrdersController OR new dedicated `OrderMessagesController` (implementer's call; mirror existing controller granularity):
   - Customer host: `POST /api/v1/customer/orders/{orderId}/messages`, `GET /api/v1/customer/orders/{orderId}/messages`, `POST /api/v1/customer/orders/{orderId}/messages/mark-read`.
   - Maker host: `POST /api/v1/maker/orders/{orderId}/messages`, `GET /api/v1/maker/orders/{orderId}/messages`, `POST /api/v1/maker/orders/{orderId}/messages/mark-read`.
   - All `[Authorize]` with the host's audience scheme. Controllers are one-liners that dispatch via `mediator.Send`.

10. **Outbox event type constants** added to `OutboxEventTypes` (existing constants class): `OrderMessagePostedCustomerEmail`, `OrderMessagePostedMakerEmail`. Routing branch added to `EmailSendService` (per ADR 0019).

11. **Two new email templates + cs-CZ keys**:
    - `order-message-posted-customer.html` + cs-CZ key `email.orderMessagePostedCustomer.{subject,body,cta}`.
    - `order-message-posted-maker.html` + cs-CZ key `email.orderMessagePostedMaker.{subject,body,cta}`.
    - Subject line: "Nová zpráva k objednávce {orderNumber}". Body: short notice + CTA linking to the order detail page on the appropriate host.

12. **Globally-unique response naming** (NSwag CI fix per PR #38): `PostCustomerOrderMessageResponse`, `GetCustomerOrderMessagesResponse`, `MarkCustomerOrderMessagesAsReadResponse`, and the same three for the Maker host. Each is a sealed record-typed wrapper.

13. **BusinessErrorMessage codes** to ADD:
    - `OrderMessageBodyEmpty` — body whitespace-only or empty (Validator).
    - `OrderMessageBodyTooLong` — body > 2000 chars (Validator).
    - `OrderNotFound` — REUSED from the existing Order surface for the IDOR-mismatch path (handler dispatches against the scoped repo; null returns surface as `OrderNotFound` not a distinct "access denied" code — leaks no information about whether the order exists for another tenant).

14. **DTO contract addition on T-0080's `CustomerOrderListItemDto`** — add nullable `int? UnreadMessageCount` field (populated from `o.CustomerUnreadMessageCount`). T-0080 did not reserve this field (the messages bundle hadn't been groomed at T-0080 ship time); T-0079 backfills the addition. NSwag regen on customer host. The maker host already has the field per T-0081 §C.7 — only the projection logic flips from `null` to `o.MakerUnreadMessageCount`.

15. **NSwag regen scope:** BOTH customer + maker hosts. 6 new endpoints + 2 DTO additions. Admin / Public hosts untouched.

## Scope

### Domain layer

- **`Core.Domain/OrderMessages/OrderMessage.cs`** — NEW entity. `Auditable` base. Fields per §C.1. Configured via `OrderMessageConfiguration : IEntityTypeConfiguration<OrderMessage>` in `Infra.Database/Configurations/`. Indexes: `(order_id, created_at DESC)` for thread paging; partial index `(order_id) WHERE read_by_counterparty_at IS NULL` for the mark-as-read sweep (Postgres-specific; implementer confirms dialect supports).
- **`Core.Domain/OrderMessages/OrderMessageAuthorRole.cs`** — NEW enum per §C.2.
- **`Core.Domain/OrderMessages/IOrderMessageRepository.cs`** — NEW write-side interface per §C.4.
- **`Core.Domain/OrderMessages/IOrderMessageQueries.cs`** — NEW read-side interface per §C.5.
- **`Core.Domain/Orders/Order.cs`** — MODIFY existing entity:
  - Add `int CustomerUnreadMessageCount` (default 0) + `int MakerUnreadMessageCount` (default 0).
  - Add `DateTimeOffset? CustomerPendingNotificationEmailAt` + `DateTimeOffset? MakerPendingNotificationEmailAt`.
  - Add domain methods per §C.8: `IncrementUnreadForCustomer`, `IncrementUnreadForMaker`, `ResetUnreadForCustomer`, `ResetUnreadForMaker`, `ShouldEmitNotificationForCustomer(now)`, `ShouldEmitNotificationForMaker(now)`, `MarkNotificationEmittedForCustomer(now)`, `MarkNotificationEmittedForMaker(now)`, `ClearPendingNotificationForCustomer()`, `ClearPendingNotificationForMaker()`.
  - Add constant `public static readonly TimeSpan NotificationDebounceWindow = TimeSpan.FromMinutes(5);`.
- **`Core.Domain/Errors/BusinessErrorMessage.cs`** — MODIFY: add `OrderMessageBodyEmpty` + `OrderMessageBodyTooLong` constants per §C.13.

### AppServices layer

- **`Core.AppServices/Features/OrderMessages/DTOs/OrderMessageDto.cs`** — NEW per §C.6.
- **`Core.AppServices/Features/OrderMessages/PostCustomerOrderMessage.cs`** — NEW one-file feature per §C.7. Handler steps:
  1. Resolve `customerUserId` from `IUserSessionProvider.RequireUserId()`.
  2. Load Order via `IOrderRepository.GetByIdForCustomerAsync(orderId, customerUserId, ct)`. Null → `BusinessResult.Failure(OrderNotFound)`.
  3. Build `OrderMessage(AuthorRole=Customer, AuthorUserId=customerUserId, Body=command.Body, ...)`.
  4. `IOrderMessageRepository.AddAsync(message, ct)`.
  5. `order.IncrementUnreadForMaker()`.
  6. If `order.ShouldEmitNotificationForMaker(clock.UtcNow)` → enqueue `OrderMessagePostedMakerEmail` outbox row (payload: `{ orderId, messageId }`); call `order.MarkNotificationEmittedForMaker(clock.UtcNow)`.
  7. Return `BusinessResult.Success(new PostCustomerOrderMessageResponse(message.Id, message.CreatedAt))`.
  - Validator: `Body.NotEmpty().WithErrorCode(OrderMessageBodyEmpty).MaximumLength(2000).WithErrorCode(OrderMessageBodyTooLong)`. `OrderId.NotEmpty()`.
- **`Core.AppServices/Features/OrderMessages/PostMakerOrderMessage.cs`** — NEW per §C.7, symmetric to the customer version. Resolves `makerId` from `IMakerRepository.GetByUserIdAsync(sessionUserId)`. Increments `CustomerUnreadMessageCount`. Outbox event = `OrderMessagePostedCustomerEmail`.
- **`Core.AppServices/Features/OrderMessages/GetCustomerOrderMessages.cs`** — NEW per §C.7. Query handler calls `IOrderMessageQueries.GetByOrderForCustomerAsync(orderId, customerUserId, page, pageSize, ct)`. Validator: `Page >= 1`, `PageSize` in `[1, 50]`, `OrderId.NotEmpty()`. Response wraps `PagedData<OrderMessageDto>`.
- **`Core.AppServices/Features/OrderMessages/GetMakerOrderMessages.cs`** — NEW per §C.7, symmetric. Resolves maker scope.
- **`Core.AppServices/Features/OrderMessages/MarkCustomerOrderMessagesAsRead.cs`** — NEW per §C.7. Handler steps:
  1. Resolve `customerUserId`.
  2. Load Order via `IOrderRepository.GetByIdForCustomerAsync` (IDOR shield). Null → `OrderNotFound`.
  3. `var marked = await IOrderMessageRepository.MarkAsReadForCustomerAsync(orderId, customerUserId, ct);`
  4. `order.ResetUnreadForCustomer();` — domain method clamps at zero.
  5. `order.ClearPendingNotificationForCustomer();` — so the maker's next post fires immediately, not silenced by a stale debounce window.
  6. Return `BusinessResult.Success(new MarkCustomerOrderMessagesAsReadResponse(marked));`.
  - No outbox event on mark-as-read (the recipient is the one reading; nothing to notify).
- **`Core.AppServices/Features/OrderMessages/MarkMakerOrderMessagesAsRead.cs`** — NEW per §C.7, symmetric.
- **`Core.AppServices/Features/Orders/DTOs/CustomerOrderListItemDto.cs`** — MODIFY: add `int? UnreadMessageCount` field per §C.14. T-0080's projection (`OrderQueries.GetCustomerOrdersPagedAsync`) updates to populate `o.CustomerUnreadMessageCount`.
- **`Infra.Database/Orders/OrderQueries.cs`** — MODIFY: T-0080's customer-list projection populates the new `UnreadMessageCount` field from `o.CustomerUnreadMessageCount`. T-0081's maker-list projection (`GetMakerOrdersPagedAsync`) flips from `null` to `o.MakerUnreadMessageCount`. Both stay one-round-trip per page.
- **`Core.AppServices/Infrastructure/Email/EmailSendService.cs`** — MODIFY per ADR 0019: add `IsOrderMessagePosted` predicate + routing branch + template resolution for both customer + maker recipient variants.
- **`Core.AppServices/Infrastructure/Outbox/OutboxEventTypes.cs`** — MODIFY: add `OrderMessagePostedCustomerEmail` + `OrderMessagePostedMakerEmail` constants.

### Infrastructure / Database layer

- **`Infra.Database/OrderMessages/OrderMessageRepository.cs`** — NEW write-side impl of `IOrderMessageRepository`. `AddAsync` enqueues via `DbContext.OrderMessages.Add`. `MarkAsReadForCustomerAsync` runs a single bulk UPDATE (`ExecuteUpdateAsync`) against `order_messages` joined to `orders` with the customer-scope predicate; returns affected row count.
- **`Infra.Database/OrderMessages/OrderMessageQueries.cs`** — NEW read-side impl of `IOrderMessageQueries`. AsNoTracking + IgnoreAutoIncludes projection. Two queries per call (CountAsync + Skip/Take) per the bundle's standard paged-list shape (T-0080 precedent).
- **`Infra.Database/Configurations/OrderMessageConfiguration.cs`** — NEW EF configuration. Indexes + constraints + FK relationships.
- **`Infra.Database/Migrations/<timestamp>_AddOrderMessageTableAndOrderUnreadCountsAndPendingPointers.cs`** — NEW migration per §C.3.
- **`Config/Extensions/AddMakablesInfrastructure.cs`** — register `IOrderMessageRepository → OrderMessageRepository` + `IOrderMessageQueries → OrderMessageQueries`. Both scoped lifetime.

### Web.Customer host

- **`Web.Customer/Controllers/OrdersController.cs`** OR NEW **`Web.Customer/Controllers/OrderMessagesController.cs`** (implementer's call):
  - `POST /api/v1/customer/orders/{orderId}/messages` → dispatches `PostCustomerOrderMessage.Command`.
  - `GET /api/v1/customer/orders/{orderId}/messages?page=1&pageSize=50` → dispatches `GetCustomerOrderMessages.Query`.
  - `POST /api/v1/customer/orders/{orderId}/messages/mark-read` → dispatches `MarkCustomerOrderMessagesAsRead.Command`.
  - All `[Authorize]` with customer audience. `[ProducesResponseType]` for NSwag.

### Web.Maker host

- **`Web.Maker/Controllers/OrdersController.cs`** OR NEW **`Web.Maker/Controllers/OrderMessagesController.cs`**:
  - `POST /api/v1/maker/orders/{orderId}/messages`.
  - `GET /api/v1/maker/orders/{orderId}/messages?page=1&pageSize=50`.
  - `POST /api/v1/maker/orders/{orderId}/messages/mark-read`.

### Tests

#### Pure-logic predicate tests (TDD red→green; commit FIRST per `## Commits hint`)

- **`Tests/Domain/Orders/OrderUnreadCountTests.cs`** (NEW, ~4 unit):
  1. `IncrementUnreadForCustomer_clamps_at_MaxInt` — start at int.MaxValue; increment; assert still MaxValue (no overflow).
  2. `ResetUnreadForCustomer_clamps_at_zero` — start at 0; reset; assert still 0.
  3. `IncrementUnreadForMaker_increments_by_one` — start at 5; increment; assert 6.
  4. `ResetUnreadForMaker_sets_to_zero_regardless_of_starting_value` — start at 17; reset; assert 0.
- **`Tests/Domain/Orders/OrderNotificationDebounceTests.cs`** (NEW, ~4 unit):
  1. `ShouldEmitNotificationForCustomer_returns_true_when_pointer_null` — pointer null; predicate returns true.
  2. `ShouldEmitNotificationForCustomer_returns_true_when_pointer_older_than_5_min` — pointer = now - 6min; predicate returns true.
  3. `ShouldEmitNotificationForCustomer_returns_false_when_pointer_within_5_min_window` — pointer = now - 3min; predicate returns false.
  4. `MarkNotificationEmittedForCustomer_sets_pointer_to_provided_now` — call with fixed clock; assert pointer == provided now.

#### Handler tests (NSubstitute mocks; ~14 unit)

- **`Tests/AppServices/Features/OrderMessages/PostCustomerOrderMessageHandlerTests.cs`** (NEW, ~3): happy-path persists + increments maker counter + emits outbox event; suppresses outbox emit when within debounce window; rejects OrderNotFound for cross-tenant order.
- **`Tests/AppServices/Features/OrderMessages/PostMakerOrderMessageHandlerTests.cs`** (NEW, ~3): symmetric set.
- **`Tests/AppServices/Features/OrderMessages/GetCustomerOrderMessagesHandlerTests.cs`** (NEW, ~2): happy-path returns paged data; empty result for order with no messages.
- **`Tests/AppServices/Features/OrderMessages/GetMakerOrderMessagesHandlerTests.cs`** (NEW, ~2): symmetric set.
- **`Tests/AppServices/Features/OrderMessages/MarkCustomerOrderMessagesAsReadHandlerTests.cs`** (NEW, ~2): happy-path resets unread + clears pending pointer; OrderNotFound for cross-tenant.
- **`Tests/AppServices/Features/OrderMessages/MarkMakerOrderMessagesAsReadHandlerTests.cs`** (NEW, ~2): symmetric set.
- Validator carve-outs (covered inline per handler tests): body empty → `OrderMessageBodyEmpty`; body > 2000 chars → `OrderMessageBodyTooLong`; page/pageSize clamps.

#### Integration tests (Testcontainers Postgres + WebApplicationFactory; ~6)

- **`IntegrationTests/OrderMessages/PostOrderMessageCrossTenantIsolationTests.cs`** — customer A posts to customer B's order via the customer host → 404 (OrderNotFound). Maker A posts to maker B's order → 404. Confirms the WHERE-predicate IDOR shield at the SQL level.
- **`IntegrationTests/OrderMessages/DebounceSemanticsTests.cs`** — customer posts 3 messages in 4 minutes; outbox table has exactly 1 `OrderMessagePostedMakerEmail` row (first post triggered emit; 2nd + 3rd silenced by debounce). Maker calls MarkAsRead; customer posts again; outbox now has 2 rows (the post-mark-as-read clear cleared the debounce).
- **`IntegrationTests/OrderMessages/UnreadCountDenormalizationTests.cs`** — customer posts 3 messages → `Order.MakerUnreadMessageCount == 3`; maker calls MarkAsRead → counter resets to 0 AND all 3 OrderMessage rows have `ReadByCounterpartyAt` set.
- **`IntegrationTests/OrderMessages/ListOrdersExposesUnreadCountTests.cs`** — customer's list endpoint (T-0080) returns `UnreadMessageCount` populated from `customer_unread_message_count`; maker's list endpoint (T-0081) returns it populated from `maker_unread_message_count`. Cross-checks the contract addition.
- **`IntegrationTests/OrderMessages/MarkAsReadIsIdempotentTests.cs`** — call mark-as-read twice in a row; 2nd call returns `MarkedCount == 0`; counter stays at 0; pending pointer stays cleared.
- **`IntegrationTests/OrderMessages/PagedThreadOrderingTests.cs`** — seed 60 messages; GET `?page=1&pageSize=50` returns newest 50 sorted CreatedAt DESC; GET `?page=2&pageSize=50` returns remaining 10. Tiebreak stable on identical timestamps.

### Docs

- **`docs/architecture/roles/order-message.md`** — existing role file. Note the new repository + queries split per ADR 0023; the 5-min debounce semantics; the per-audience compile-time IDOR shield; the 2 denormalized unread counters on Order.
- **`docs/architecture/roles/order.md`** — note the new domain-method surface (unread counters + notification debounce predicates).
- **`docs/tickets/INDEX.md`** — PM flips T-0079 to `**done**` post-merge.

### NSwag regen

The 6 new endpoints + 2 DTO additions (`CustomerOrderListItemDto.UnreadMessageCount` + the new `OrderMessageDto` / `OrderMessageAuthorRole` types) are contract changes → **NSwag regen REQUIRED in the same PR** for BOTH customer + maker hosts. Per pre-commit hook (T-0013), `frontend/src/lib/api-client/` cannot be edited manually — `npm run generate:api` produces the diff.

## Alternatives Considered

- **Option A — Three-party with admin moderator-post role.** *Rejected per A.1* — pollutes the human channel; complicates UI (admin badge); admin already gets read-only via T-0111 which is the right read-only audit posture.
- **Option B — System-event auto-posts on state transitions.** *Rejected per A.1* — noise; the order header already shows the state machine; the thread should carry only what a human chose to write.
- **Option C — Per-message email (no debounce).** *Rejected per A.2* — storm risk; 8 short messages in 60 seconds = 8 emails; bad UX + outbox cost. The 5-min digest matches the existing outbox retry rhythm.
- **Option D — Separate Azure Function digest job.** *Rejected per A.2 + ADR 0020* — over-engineered for a one-line predicate. The post-write debounce check fits comfortably in the existing UoW commit; no scheduling layer needed.
- **Option E — Compute unread count via subquery at list-projection time.** *Rejected per A.3* — per-row N+1 OR a correlated subquery; both unacceptable at list scale per the bundle's flat-DTO + no-N+1 stance (T-0080 §H precedent).
- **Option F — Separate `order_message_read_state` table.** *Rejected per A.3* — extra JOIN per list row + extra write per mark-as-read. The 2-column denormalization is simpler and matches how every list consumer reads the count (a single field on each row).
- **Option G — Count "messages since last-seen timestamp".** *Rejected per A.3* — timestamp semantics are slipperier than an integer counter. Two posts at the same UTC second + one MarkAsRead in between = ambiguous count under timestamp semantics.
- **Option H — Single generic `order.message.posted` outbox event routed downstream by EmailSendService.** *Rejected per A.4* — loses the recipient-party signal at the outbox-table level + complicates retry semantics + makes debounce harder to reason about (the debounce IS per-recipient-party; encoding it in the event type makes the SQL log self-documenting).
- **Option I — Skip outbox; send email inline from the handler.** *Rejected per A.4 + ADR 0017* — violates the outbox stance. Email send becomes a synchronous handler dependency; any provider hiccup turns into a 5xx; no retry. Outbox is the standard.
- **Option J — Conditional `if (authorRole == X)` branching inside a single shared handler.** *Rejected per ADR 0013 + T-0082 precedent* — runtime authorization branching is the wrong shield. The per-audience compile-time split is the standard. A customer JWT cannot dispatch the maker command because the type isn't registered on the customer host.
- **Option K — Single `IOrderMessageRepository.GetByOrderAsync(orderId, ...)` with a runtime audience parameter.** *Rejected per ADR 0013* — same as J at the repo layer. The WHERE-predicate IS the IDOR shield; baking it into separate methods makes the shield a compile-time guarantee.
- **Option L — Allow attachments inline in messages (file uploads on the thread).** *Rejected for MVP scope* — text-only at MVP. Customers already attach order-level artwork at checkout (Order attachments surface). Coordinating after the fact stays text-only at MVP; attachments inside the thread is a post-MVP feature.
- **Option M — Allow message edit/delete.** *Rejected for MVP scope* — audit trail simplicity. Deferred. If a user posts wrong info, they post a correction message. Soft-delete via `DeactivatedAt` exists at the entity level for admin abuse cleanup but is not surfaced as a feature.
- **Option N — Threading / replies (parent message reference).** *Rejected for MVP scope* — orders are short conversations; threading adds UI complexity for negligible MVP benefit.

## Out of scope

- **Admin moderator-post role.** Admin gets READ-ONLY access via T-0111 admin tooling (out of scope for THIS ticket).
- **System-event auto-posts on state transitions.** Per A.1.
- **Attachment uploads inside messages.** Text-only at MVP per Option L.
- **Threading / replies.** Per Option N.
- **Edit / delete posted messages.** Per Option M.
- **Message history export** (CSV / PDF). Post-MVP if usage warrants.
- **Push / SMS notifications.** Email only at MVP via the existing ADR 0019 pipeline.
- **Read receipts visible to the sender** ("seen at HH:MM"). The thread tracks `ReadByCounterpartyAt` per-message but does NOT expose it to the opposite party in the GET responses at MVP.
- **Typing indicators / WebSocket presence.** Async messaging only at MVP. No realtime layer.
- **Frontend thread UI.** T-0086 + T-0087 own the consumer frontends (customer + maker dashboards). T-0079 only ships the backend.
- **Backfill messages from any prior system.** Greenfield — no historical thread data.

## Acceptance criteria

- **AC-1** Given an order with `State = Paid` (or later) and a customer + maker bound to it, when the customer `POST`s `/api/v1/customer/orders/{orderId}/messages` with body length in `[1, 2000]`, then the response is `200 OK` with `{ messageId, createdAt }`; a new `order_messages` row exists with `AuthorRole = Customer`; `orders.maker_unread_message_count` incremented by 1.
- **AC-2** Given the same setup, when the maker `POST`s `/api/v1/maker/orders/{orderId}/messages` with valid body, then a new row exists with `AuthorRole = Maker`; `orders.customer_unread_message_count` incremented by 1.
- **AC-3** Given the recipient (counterparty) has no pending notification (pointer null), when a message is posted, then exactly one outbox row is inserted with event type `OrderMessagePostedMakerEmail` (or `Customer`-variant for the symmetric direction); `orders.maker_pending_notification_email_at` (or customer pointer) is set to the post timestamp.
- **AC-4** Given the recipient has a pending notification pointer < 5 min old, when a second message is posted, then NO new outbox row is inserted; the pointer remains unchanged. (Debounce holds.)
- **AC-5** Given the recipient has a pending notification pointer > 5 min old, when a new message is posted, then a new outbox row IS inserted and the pointer is refreshed.
- **AC-6** Given the customer `POST`s `/api/v1/customer/orders/{orderId}/messages/mark-read`, when there are 3 unread maker-authored messages on the order, then the response is `200 OK` with `{ markedCount: 3 }`; `orders.customer_unread_message_count == 0`; `orders.customer_pending_notification_email_at == null`; all 3 OrderMessage rows have `ReadByCounterpartyAt` set.
- **AC-7** Given a cross-tenant probe (customer A trying to post to customer B's order, OR maker A trying to post to maker B's order), when the request is dispatched, then the response is `404 OrderNotFound`. No leak about whether the order exists. The IDOR shield is the WHERE predicate baked into the scoped repo.
- **AC-8** Given a request with empty body (`""` or whitespace-only), when posted, then the response is `400` with error code `OrderMessageBodyEmpty`.
- **AC-9** Given a request with body length 2001, when posted, then the response is `400` with error code `OrderMessageBodyTooLong`.
- **AC-10** Given the GET endpoint `/api/v1/customer/orders/{orderId}/messages?page=1&pageSize=50` is called on an order with 60 messages, when fetched, then the response is `200 OK` with `{ messages: { items: [...50 items...], totalCount: 60, page: 1, pageSize: 50 } }`. Items sorted `CreatedAt DESC` (newest first). Each item carries `Id`, `OrderId`, `AuthorRole`, `AuthorName`, `Body`, `CreatedAt`, `IsMine`. `IsMine == true` for messages authored by the requesting party.
- **AC-11** Given T-0080's `GET /api/v1/customer/orders` is called on a customer whose orders have unread maker-authored messages, when fetched, then each list item carries `UnreadMessageCount` populated from `orders.customer_unread_message_count` (NOT null). Symmetric for T-0081 `GET /api/v1/maker/orders` populating from `maker_unread_message_count`.
- **AC-12** Given the EF migration runs, when inspected, then the `order_messages` table exists with PK + FK to `orders.id` + indexed `(order_id, created_at DESC)`; `orders` table has columns `customer_unread_message_count INT NOT NULL DEFAULT 0`, `maker_unread_message_count INT NOT NULL DEFAULT 0`, `customer_pending_notification_email_at TIMESTAMPTZ NULL`, `maker_pending_notification_email_at TIMESTAMPTZ NULL`.
- **AC-13** Build clean. Unit tests: baseline + ~14 new (handlers) + ~8 new (Order domain methods: unread + debounce predicates). Integration tests: baseline + ~6 new (cross-tenant, debounce, unread denormalization, list exposure, mark-as-read idempotency, paging). `node scripts/check-consistency.mjs` exit 0. NSwag regen committed for BOTH customer + maker hosts.
- **AC-14** Given the email outbox dispatcher processes a `OrderMessagePostedCustomerEmail` row, when the recipient email is rendered, then the cs-CZ template "Nová zpráva k objednávce {orderNumber}" is used; the email body links to the customer-host order detail page. Symmetric for `OrderMessagePostedMakerEmail` routed to the maker-host detail page.

## Risk / mitigation

- **Unread-count drift** (counter says 3 but actual unread row count is 5, or vice-versa, due to a missed increment in a partial failure). **Mitigation:** the UoW pipeline commits the OrderMessage insert + the counter increment + the pending-pointer update atomically (single DbContext.SaveChanges). On `MarkAsRead`, the handler bulk-UPDATEs all unread rows + sets the counter to 0 in the same UoW — the counter cannot drift positive after a successful mark-as-read because the reset is unconditional (not a decrement). The domain method `ResetUnread*` clamps at zero defensively; the domain method `IncrementUnread*` clamps at int.MaxValue defensively. The integration test `UnreadCountDenormalizationTests` confirms the invariant.
- **Debounce email storm** (two PostMessage requests racing → both see pointer null → both emit, recipient gets 2 emails). **Mitigation:** the read of `pending_notification_email_at` + the conditional update happen inside the EF UoW; Postgres MVCC + row-level lock on the Order row (via the FK lookup in the same transaction) serializes the two transactions. The second request reads the now-updated pointer and suppresses. The integration test `DebounceSemanticsTests` covers the rapid-fire case. If under heavy contention a duplicate sneaks through (different DB nodes in a clustered topology), it's a one-email duplicate, not a storm — acceptable.
- **PII leak in message bodies** (a customer or maker types an email address / phone in a message body and the recipient sees it). **Mitigation:** the message body IS the channel — it's by design that the two parties can share contact info via the thread. The PII concern is about the maker NEVER getting the customer's account-registered email via the LIST/DETAIL endpoints (covered by T-0081 §A.2); the thread body is the agreed coordination surface.
- **Recipient email enumeration via the post endpoint** (sending POSTs with arbitrary `orderId` values to probe whether the order exists). **Mitigation:** the response is the same `OrderNotFound` for "order doesn't exist" and "order exists but belongs to another tenant". The IDOR shield is the WHERE predicate — the SQL never selects the row, so the handler genuinely cannot distinguish the two cases at the response layer.
- **Outbox email-routing branch missed in EmailSendService** (new event types not handled → silently lost). **Mitigation:** EmailSendService routing branch updated in the same PR; integration test `DebounceSemanticsTests` verifies the outbox row is INSERTED (the dispatch is separately tested via the existing email-outbox integration test that confirms the routing fallback raises an error on unknown event types).

## Test plan reference

See `docs/test-plans/T-0079.md` (to be authored alongside implementation if the inline plan above grows). Inline plan covers ~22 unit + ~6 integration; the separate file is reserved for any post-merge regression fixtures.

## Files touched (expected)

### New
- `backend/src/Makables.Core.Domain/OrderMessages/OrderMessage.cs`
- `backend/src/Makables.Core.Domain/OrderMessages/OrderMessageAuthorRole.cs`
- `backend/src/Makables.Core.Domain/OrderMessages/IOrderMessageRepository.cs`
- `backend/src/Makables.Core.Domain/OrderMessages/IOrderMessageQueries.cs`
- `backend/src/Makables.Core.AppServices/Features/OrderMessages/DTOs/OrderMessageDto.cs`
- `backend/src/Makables.Core.AppServices/Features/OrderMessages/PostCustomerOrderMessage.cs`
- `backend/src/Makables.Core.AppServices/Features/OrderMessages/PostMakerOrderMessage.cs`
- `backend/src/Makables.Core.AppServices/Features/OrderMessages/GetCustomerOrderMessages.cs`
- `backend/src/Makables.Core.AppServices/Features/OrderMessages/GetMakerOrderMessages.cs`
- `backend/src/Makables.Core.AppServices/Features/OrderMessages/MarkCustomerOrderMessagesAsRead.cs`
- `backend/src/Makables.Core.AppServices/Features/OrderMessages/MarkMakerOrderMessagesAsRead.cs`
- `backend/src/Makables.Infra.Database/OrderMessages/OrderMessageRepository.cs`
- `backend/src/Makables.Infra.Database/OrderMessages/OrderMessageQueries.cs`
- `backend/src/Makables.Infra.Database/Configurations/OrderMessageConfiguration.cs`
- `backend/src/Makables.Infra.Database/Migrations/<ts>_AddOrderMessageTableAndOrderUnreadCountsAndPendingPointers.cs`
- `backend/src/Makables.Tests/Domain/Orders/OrderUnreadCountTests.cs`
- `backend/src/Makables.Tests/Domain/Orders/OrderNotificationDebounceTests.cs`
- `backend/src/Makables.Tests/AppServices/Features/OrderMessages/PostCustomerOrderMessageHandlerTests.cs`
- `backend/src/Makables.Tests/AppServices/Features/OrderMessages/PostMakerOrderMessageHandlerTests.cs`
- `backend/src/Makables.Tests/AppServices/Features/OrderMessages/GetCustomerOrderMessagesHandlerTests.cs`
- `backend/src/Makables.Tests/AppServices/Features/OrderMessages/GetMakerOrderMessagesHandlerTests.cs`
- `backend/src/Makables.Tests/AppServices/Features/OrderMessages/MarkCustomerOrderMessagesAsReadHandlerTests.cs`
- `backend/src/Makables.Tests/AppServices/Features/OrderMessages/MarkMakerOrderMessagesAsReadHandlerTests.cs`
- `backend/src/Makables.IntegrationTests/OrderMessages/PostOrderMessageCrossTenantIsolationTests.cs`
- `backend/src/Makables.IntegrationTests/OrderMessages/DebounceSemanticsTests.cs`
- `backend/src/Makables.IntegrationTests/OrderMessages/UnreadCountDenormalizationTests.cs`
- `backend/src/Makables.IntegrationTests/OrderMessages/ListOrdersExposesUnreadCountTests.cs`
- `backend/src/Makables.IntegrationTests/OrderMessages/MarkAsReadIsIdempotentTests.cs`
- `backend/src/Makables.IntegrationTests/OrderMessages/PagedThreadOrderingTests.cs`
- `backend/src/Makables.Infra.Email/Templates/order-message-posted-customer.html`
- `backend/src/Makables.Infra.Email/Templates/order-message-posted-maker.html`

### Modified
- `backend/src/Makables.Core.Domain/Orders/Order.cs` — add 4 columns + 10 domain methods + `NotificationDebounceWindow` constant.
- `backend/src/Makables.Core.Domain/Errors/BusinessErrorMessage.cs` — add `OrderMessageBodyEmpty` + `OrderMessageBodyTooLong`.
- `backend/src/Makables.Core.AppServices/Features/Orders/DTOs/CustomerOrderListItemDto.cs` — add `int? UnreadMessageCount`.
- `backend/src/Makables.Infra.Database/Orders/OrderQueries.cs` — populate `UnreadMessageCount` on both customer + maker list projections.
- `backend/src/Makables.Core.AppServices/Infrastructure/Email/EmailSendService.cs` — add `IsOrderMessagePosted` routing branch.
- `backend/src/Makables.Core.AppServices/Infrastructure/Outbox/OutboxEventTypes.cs` — add 2 constants.
- `backend/src/Makables.Web.Customer/Controllers/OrdersController.cs` (or new `OrderMessagesController.cs`) — 3 new actions.
- `backend/src/Makables.Web.Maker/Controllers/OrdersController.cs` (or new `OrderMessagesController.cs`) — 3 new actions.
- `backend/src/Makables.Config/Extensions/AddMakablesInfrastructure.cs` — register `IOrderMessageRepository` + `IOrderMessageQueries`.
- `backend/src/Makables.Infra.Email/Localization/cs-CZ.resx` (or equivalent i18n source) — add `email.orderMessagePostedCustomer.*` + `email.orderMessagePostedMaker.*` keys.
- `frontend/src/lib/api-client/*` — NSwag-regenerated (BOTH customer + maker hosts); committed in the same PR.
- `docs/architecture/roles/order-message.md` — note repository/queries split + debounce semantics + denormalized counters.
- `docs/architecture/roles/order.md` — note new domain-method surface.

## Commits hint

Suggested commit shape on the implementer's branch:

1. **`test(T-0079): pin pure-logic predicates (red)`** — commit the 8 Order domain tests (unread clamps + debounce predicates) FIRST while the implementations don't exist; verify red.
2. **`feat(T-0079): EF migration + OrderMessage entity + Order unread count columns + pending pointers`** — migration + entity + Order modifications + domain methods. Pure-logic tests now go green.
3. **`feat(T-0079): IOrderMessageRepository + IOrderMessageQueries + 6 features + DI`** — write-side + read-side seams + 6 one-file features + DI registrations + outbox event constants + EmailSendService routing branch + 2 email templates + cs-CZ keys.
4. **`feat(T-0079): customer host controller + handler tests + integration tests`** — Web.Customer endpoints (3 actions) + the 6 customer-side handler unit tests + 3 of the integration tests.
5. **`feat(T-0079): maker host controller + handler tests + remaining integration tests + NSwag regen`** — Web.Maker endpoints (3 actions) + 6 maker-side handler unit tests + remaining 3 integration tests + NSwag regen for both customer + maker hosts + T-0080/T-0081 list-projection updates + frontend client commit.

## Status log

- 2026-06-09 `draft` by PM. Created as the messages-thread ticket closing T-0081's forward-compat `UnreadMessageCount: int?` field. Reference precedents on master or in earlier bundles: T-0060 Order entity + IOrderRepository (write-scoped per ADR 0013), T-0080 GetCustomerOrders (page-based pagination + read-side IOrderQueries seam), T-0081 GetMakerOrders (forward-compat UnreadMessageCount field + maker-scoped IDOR shield), T-0082 per-audience compile-time feature split. Slice scope: new OrderMessage entity + 4 Order columns + 6 one-file features (per-audience-split per ADR 0013) + repository/queries seams + EF migration + 2 outbox event types + 2 email templates + cs-CZ keys + 2 new BusinessErrorMessage codes + ~22 unit tests + ~6 integration tests + NSwag regen on both customer + maker hosts.
- 2026-06-09 `draft → ready` by PM. User answered 4 blocking AskUserQuestion items per `/feature` workflow step 3: **A.1** two-party only with admin read-only via T-0111 (rejected three-party moderator role + system-event auto-posts); **A.2** 5-minute digest debounce per recipient (rejected per-message email storm + 1-min/15-min alternatives + separate Function digest); **A.3** denormalized 2-column unread counters on Order (rejected subquery + separate read-state table + timestamp-based count); **A.4** per-recipient-party outbox event type routing (rejected single generic event + inline email send). 15 PM-absorbed decisions captured in `## Locked design decisions §C` (entity shape, enum, EF migration, repository + queries interfaces, DTO, 6 one-file features, Order domain methods + debounce constant, controllers, outbox event constants, email templates + cs-CZ keys, globally-unique response naming, BusinessErrorMessage codes, T-0080 contract addition, NSwag regen scope on both hosts). No manual_steps. **Ready for dotnet-backend.** Implementer commits the 5-step sequence above; PR includes both backend + frontend client regen.

## Definition of Ready checklist

- [x] Linked user stories present (US-customer-0014 + US-maker-0011).
- [x] Acceptance criteria observable + numbered (AC-1 through AC-14).
- [x] Locked design decisions captured (§A user-locked, §B ADR-locked, §C PM-absorbed).
- [x] Alternatives Considered section with ≥1 rebutted alternative per locked dimension (Options A through N).
- [x] Out of scope explicit.
- [x] Risk / mitigation called out for the 5 leading risks.
- [x] Test plan inline (pure-logic + handler + integration).
- [x] Files touched listed (new + modified).
- [x] Layers / ADRs / dependencies in the frontmatter.
- [x] Security-touching: YES (IDOR shield + PII in message bodies + recipient email enumeration risk).
- [x] Size: M.
- [x] Commits hint with TDD red-first surface called out.
- [x] NSwag regen scope identified (BOTH customer + maker hosts).
- [x] No new Azure Function required (debounce is post-write predicate per ADR 0020).
