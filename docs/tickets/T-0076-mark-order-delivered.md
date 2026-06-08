---
id: T-0076
title: MarkOrderDelivered command + customer-notification outbox + DeliverySource tracking
status: ready
size: S
owner: dotnet-backend
created: 2026-06-08
updated: 2026-06-08
depends_on: [T-0072, T-0073]
blocks: [T-0077, T-0078, T-0102]
user_stories: [US-customer-0013]
adrs: [0013, 0014, 0019, 0020]
phase: 4
manual_steps: []
security_touching: false
layers: [domain, appservices, infra-database, web-customer, frontend-i18n]
---

# T-0076 — MarkOrderDelivered command + customer-notification outbox + DeliverySource tracking

## Context

T-0076 is the **single writer** of the `Shipped → Delivered` state transition. The same `MarkOrderDelivered.Command(OrderId, Source)` handler is dispatched by three different callers in the delivery-close bundle: (a) the customer pressing "Označit jako doručeno" on their order-detail page (this ticket — `POST /api/v1/customer/orders/{orderId}/deliver`); (b) the T-0077 auto-deliver timer Function that fires when `AutoDeliverAt` has elapsed without an explicit confirmation; (c) the T-0078 carrier-sync timer that polls Packeta `GetStatusAsync` and transitions when the packet status indicates delivered. The handler is **source-aware**: a new `OrderDeliverySource` enum (`Customer = 0`, `Auto = 1`, `Carrier = 2`) is stamped on the `Order` row at transition time so dispute trails (Phase 5 T-0106/T-0118) and analytics can query "how did this order close?" without joining the outbox or audit logs.

This is the **first ticket in the delivery-close bundle** (T-0076 + T-0077 + T-0078) and all three ship under one PR with sequential implementation: T-0076 introduces the command + domain extensions + customer endpoint + outbox event; T-0077 wires the auto-deliver timer Function; T-0078 wires the Packeta status-sync timer. The bundle convention is that T-0076 owns every shared artifact (enum, domain signature extension, EF migration, outbox event constant, email template, EmailSendService branch) and T-0077/T-0078 are pure caller-side additions that dispatch the same `MarkOrderDelivered.Command(OrderId, Source)` with different `Source` values.

The handler mirrors **T-0067 MarkOrderPaid's** atomic outbox-emit-under-UoW pattern (3 events there; 1 here — see decision A.2 below for the maker-email rejection). One state transition + one outbox event committed in the same `UnitOfWorkPipelineBehavior` transaction per ADR 0014. The customer email handler reuses **T-0067's payload-only-at-send-time** pattern: when the `order.delivered.customerEmail` event drains, it deserializes `OrderDeliveredCustomerEmailPayload` (pre-baked at enqueue time with OrderId, OrderNumber, Email, ContactName, LanguageCode, ActionUrl) and renders the SendGrid dynamic template via existing `EmailSendService` plumbing. No PDF attachment; no Packeta call; no Order re-lookup.

T-0076 also introduces the **Silent Success (no-op) on already-Delivered re-call** idempotency pattern: when `Order.MarkAsDelivered` returns `OrderInvalidTransition` AND the order is already in `Delivered` state, the handler returns `Success` with NO outbox emission. This protects the bundle's three-caller race scenarios: (a) customer hits "Označit jako doručeno" then T-0078 carrier sync fires seconds later with Packeta's status update — second call no-ops cleanly; (b) T-0077 auto-deliver fires at minute T then T-0078 carrier sync arrives at minute T+1 — same; (c) customer + carrier race where both succeed within the same second. No duplicate emails; no spurious 409 responses. Mirrors T-0067/T-0069 idempotency precedent.

The customer endpoint route follows the T-0072/T-0073 short-verb convention (`/ship`, `/handover`): `POST /api/v1/customer/orders/{orderId}/deliver`. JWT audience is enforced per host per ADR 0013 (customer tokens cannot be replayed against maker/admin hosts). IDOR scoping is via `IOrderRepository.GetByIdForCustomerAsync` — a customer cannot mark another customer's order delivered.

## Locked design decisions

Captured per `docs/process/deliberation.md`. The user locked 4 dimensions at `/feature` step 3 (DeliverySource column vs payload-only vs audit-log inference; single customer email vs customer+maker; Silent Success vs Conflict on re-call; short-verb route). 7 PM-absorbed decisions follow from T-0067/T-0072 precedents.

### A. User-locked at /feature step 3 (non-negotiable)

1. **Capture DeliverySource as an Order column.** New `OrderDeliverySource` enum at `Core.Domain/Orders/OrderDeliverySource.cs` (Customer = 0, Auto = 1, Carrier = 2). New EF migration adds `delivery_source SMALLINT NULL` to `orders`. All 3 caller paths (T-0076 customer endpoint, T-0077 auto-deliver timer, T-0078 carrier sync) dispatch the same `MarkOrderDelivered.Command(OrderId, Source)`. Queryable per-order for dispute trails (Phase 5 T-0106/T-0118) and analytics. **Rejected:** outbox-payload-only (joins required for order-level queries); audit-log-inference (hardest to query; relies on stable event_type strings).

2. **Single `order.delivered.customerEmail` outbox event** — no maker email. Customer gets the "your order arrived" confirmation. Maker doesn't need a delivery notification at MVP: T-0102's weekly payout cron is the contract (no maker action required at delivery). **Rejected:** customer + maker (higher SendGrid cost for marginal UX); conditional by ShippingMethod (speculative branch for marginal gain).

3. **Silent Success (no-op) on already-Delivered re-call.** When `Order.MarkAsDelivered` returns `OrderInvalidTransition` AND the order is already in `Delivered` state, the handler returns `Success` with NO outbox emission. Customer + T-0078 carrier sync firing within seconds both succeed; no duplicate emails; no spurious 409 responses on the race path. Mirrors T-0069 / T-0067 idempotency precedent. **Rejected:** Conflict on already-Delivered (worse race UX); source-conditional (surprise to future callers).

4. **Customer endpoint route = `POST /api/v1/customer/orders/{orderId}/deliver`.** Matches the T-0072/T-0073 maker-side short-verb pattern (`/ship`, `/handover`). UX button: "Označit jako doručeno". **Rejected:** `/confirm-delivery` (verbose; less consistent); `/mark-delivered` (verbose; matches C# command name but breaks the short-verb convention).

### B. ADR-locked (no relitigation)

- **ADR 0013 (per-audience JWT enforcement).** Customer endpoint `[Authorize]` runs under the `Web.Customer` host audience; a customer JWT cannot be replayed against the maker or admin hosts. T-0077/T-0078 Functions have no user identity (queue/timer context); they use `IOrderRepository.GetByIdUnscopedAsync`. T-0076's customer endpoint uses `IOrderRepository.GetByIdForCustomerAsync(orderId, customerId, ct)` for ownership scoping (returns null if the order is not owned by the requesting customer).
- **ADR 0014 (UoW pipeline).** Handler MUST NOT call `SaveChangesAsync()`. `UnitOfWorkPipelineBehavior` commits the Order mutation + 1 outbox row in a single Postgres transaction. Failure anywhere rolls back everything. Already-Delivered Silent Success path returns `BusinessResult.Success` with no entity changes and no outbox writes; the pipeline commits a no-op transaction (cheap).
- **ADR 0019 (email pipeline).** Per-event-type switch in `IEmailSendService.SendAsync` per T-0067 Q3. The new `OrderDeliveredCustomerEmail` branch is added; existing cases untouched. Template lookup keyed by `EmailTemplateType.OrderDeliveredCustomer` + payload.LanguageCode (cs-CZ + en-US seeded).
- **ADR 0020 (background jobs + outbox queue split).** `order.delivered.customerEmail` is an email event — routes through the existing `send-email` queue (no new queue, no new publisher method). `IsEmailSend` classifier is extended to include the new event type. No new queue split.
- **One-file feature shape.** `Features/Orders/MarkOrderDelivered.cs` contains nested `Command`, `Validator`, `Handler`, `MarkOrderDeliveredResponse`. No separate files per type.
- **`BusinessResult<T>` for expected failures.** Ownership mismatch → NotFound; invalid state (non-Shipped, non-Delivered) → Conflict (OrderInvalidTransition); already-Delivered → Success (per A.3). Exceptions reserved for truly unexpected (e.g., DB connection dropped).
- **TDD-with-commit-order hard rule** (T-0067+ enforced) for pure logic: domain entity changes (`Order.MarkAsDelivered` signature extension, DeliverySource field) ship test-first.
- **Per-event-type switch in `IEmailSendService`** per T-0067 Q3. New `OrderDeliveredCustomerEmail` branch added; existing cases untouched.

### C. PM-absorbed (no user input needed)

- **`Order.MarkAsDelivered` signature extension:** `(IClock clock, OrderDeliverySource source, DateTimeOffset? deliveredAtOverride = null)`. `DeliveredAt = deliveredAtOverride ?? clock.UtcNow` (T-0078 carrier sync passes Packeta's authoritative timestamp when available). `DeliverySource` set on the entity. Trailing optional param preserves backwards compatibility for any future 2-arg call sites.
- **Outbox event payload:** new sealed record `OrderDeliveredCustomerEmailPayload(string OrderId, string OrderNumber, string Email, string ContactName, string LanguageCode, string ActionUrl)`. Mirror `OrderPaidCustomerEmailPayload` from T-0067 verbatim. ActionUrl pre-baked to `{WebBaseUrl}/objednavka/{orderId}`.
- **Email template:** new `EmailTemplateType.OrderDeliveredCustomer` enum value + EF seed migration with cs-CZ + en-US translations. Czech subject: "Vaše objednávka #{OrderNumber} byla doručena" (or similar; final wording belongs to l10n). Mirror T-0067 OrderPaidCustomer template structure.
- **EmailSendService:** new switch arm for `OrderDeliveredCustomerEmail` → `SendOrderDeliveredCustomerEmailAsync` helper. Reuses existing DI (IEmailTemplateRepository, ILanguageResolver, IEmailProvider). No new DI deps added.
- **Customer controller endpoint:** new action in customer OrdersController (or create if not present). `[Authorize]` + customer-role-bound JWT (per T-0027 host audience enforcement). IDOR scoping via `IOrderRepository.GetByIdForCustomerAsync`. Source = `OrderDeliverySource.Customer` injected by the handler when the controller path fires.
- **Response shape:** `MarkOrderDeliveredResponse(string OrderId, OrderState State)`. Globally-unique name to avoid the NSwag TS class collision from the T-0070-T-0075 CI fix.
- **One-file feature structure:** `MarkOrderDelivered.cs` under `Core.AppServices/Features/Orders/` with nested `Command`, `Validator`, `Handler`, `MarkOrderDeliveredResponse`. Validator pins `OrderId` non-empty + Source enum range.
- **NSwag regen:** customer host only (new endpoint). T-0077/T-0078 are Functions, no contract change.

## Scope

### Domain layer

- **`Core.Domain/Orders/OrderDeliverySource.cs`** — NEW enum:
  ```csharp
  public enum OrderDeliverySource : short
  {
      Customer = 0,
      Auto = 1,
      Carrier = 2,
  }
  ```
  Explicit `: short` so the EF `SMALLINT` column maps without conversion drama. Values are stable; new sources (e.g., Admin manual override) append.
- **`Core.Domain/Orders/Order.cs`** — extend `MarkAsDelivered` in-place:
  - **Old signature** (assume T-0072/T-0073 era): `MarkAsDelivered(IClock clock)` returning `BusinessResult`.
  - **New signature:** `MarkAsDelivered(IClock clock, OrderDeliverySource source, DateTimeOffset? deliveredAtOverride = null)`.
  - Body: validate state is `Shipped` else return `OrderInvalidTransition`. Set `State = Delivered`. Set `DeliveredAt = deliveredAtOverride ?? clock.UtcNow`. Set `DeliverySource = source`. Trailing optional parameter preserves backwards compatibility for any current 2-arg call sites (none expected — sweep test fixtures for compile errors at implementation time).
  - New `DeliverySource` property on `Order` entity: `public OrderDeliverySource? DeliverySource { get; private set; }`. Nullable — historical Delivered orders (pre-T-0076) have no source recorded.
  - XML doc updated to describe the new parameters + reference T-0076 as the writer.
- **`Core.Domain/Outbox/OutboxEventTypes.cs`** — add 1 new constant:
  - `OrderDeliveredCustomerEmail = "order.delivered.customerEmail"`
  - Extend `IsEmailSend(string eventType)` to return true for `OrderDeliveredCustomerEmail` (joined into the existing OR-chain).
- **`Core.Domain/Outbox/OrderDeliveredCustomerEmailPayload.cs`** — NEW sealed record:
  ```csharp
  public sealed record OrderDeliveredCustomerEmailPayload(
      string OrderId,
      string OrderNumber,
      string Email,
      string ContactName,
      string LanguageCode,
      string ActionUrl);
  ```
  PascalCase JSON property names (matches `OrderPaidCustomerEmailPayload` convention from T-0067).
- **`Core.Domain/Email/EmailTemplateType.cs`** — add `OrderDeliveredCustomer = 7` (next enum value after T-0072's `OrderShippedCustomer = 6`). Verify the exact next value at implementation time against the in-repo enum.

### AppServices layer

- **`Core.AppServices/Features/Orders/MarkOrderDelivered.cs`** — NEW one-file feature.
  - `Command(string OrderId, OrderDeliverySource Source)` record.
  - `MarkOrderDeliveredResponse(string OrderId, OrderState State)` record — **globally-unique name** to avoid the NSwag TS class collision from the T-0070-T-0075 CI fix.
  - `Validator : AbstractValidator<Command>` — `OrderId` non-empty + valid id format; `Source` enum range (`IsInEnum()`).
  - `Handler(IClock clock, ICustomerSessionContext sessionContext, IOrderRepository orderRepository, IOutbox outbox, IPublicAppUrls publicAppUrls)` primary-constructor DI. Note: the customer controller path uses `ICustomerSessionContext`; T-0077/T-0078 Functions dispatch the same Command but their Function bodies pre-load the Order via `IOrderRepository.GetByIdUnscopedAsync` and route through a path that does NOT call `RequireCustomerId`. **PM-absorbed implementation detail:** if the handler is shared by all 3 callers, the customer-scoping lookup must be conditional on `Source == OrderDeliverySource.Customer`. Alternative shape: the handler accepts an optional `CustomerId` and uses `GetByIdForCustomerAsync` only when present, else `GetByIdUnscopedAsync`. The implementer picks the cleaner shape at code time; both preserve the same external contract.
  - Steps (NO `SaveChangesAsync()` — UoW pipeline commits):
    1. **Load Order** — for `Source == Customer`: `sessionContext.RequireCustomerId()` then `orderRepository.GetByIdForCustomerAsync(command.OrderId, customerId, ct)`. For `Source == Auto` or `Source == Carrier`: `orderRepository.GetByIdUnscopedAsync(command.OrderId, ct)`. Null → `BusinessResult.Failure<MarkOrderDeliveredResponse>(Error.NotFound(BusinessErrorMessage.OrderNotFound))`.
    2. **Already-Delivered Silent Success guard** — if `order.State == OrderState.Delivered`: log `"MarkOrderDelivered: order {OrderId} already Delivered (idempotent skip, source={Source})"`. Return `BusinessResult.Success(new MarkOrderDeliveredResponse(order.Id, order.State))` WITHOUT mutating + WITHOUT enqueuing the outbox event. (Mirrors T-0067/T-0069 idempotency precedent per A.3.)
    3. **State transition** — `var deliveredAt = command.DeliveredAtOverride;` (only T-0078 sets this; T-0076 customer path and T-0077 auto path pass null at the Command-construction site — see PM note below). `var result = order.MarkAsDelivered(clock, command.Source, deliveredAt);` — propagate failure (InvalidTransition → `BusinessResult.Failure<MarkOrderDeliveredResponse>(Error.Conflict("state", BusinessErrorMessage.OrderInvalidTransition))`).
       - **PM note on DeliveredAtOverride wiring:** the Command shape locked above is `Command(string OrderId, OrderDeliverySource Source)`. T-0078's carrier-sync path needs to pass Packeta's authoritative timestamp. The cleanest extension is a 3-parameter Command: `Command(string OrderId, OrderDeliverySource Source, DateTimeOffset? DeliveredAtOverride = null)`. T-0076 customer path constructs with `null`; T-0077 auto path constructs with `null`; T-0078 carrier path constructs with the Packeta timestamp. Trailing optional preserves the locked decision A wording (which says `MarkOrderDelivered.Command(OrderId, Source)` — the 3rd param is optional and defaults to null, so the 2-arg call site shape locked in A.1 still compiles).
    4. **Build customer payload + enqueue** — `var payload = new OrderDeliveredCustomerEmailPayload(order.Id, order.OrderNumber, order.ContactEmail, order.ContactName, order.LanguageCode, $"{publicAppUrls.WebBaseUrl}/objednavka/{order.Id}"); outbox.Enqueue(order.Id, OutboxEventTypes.OrderDeliveredCustomerEmail, JsonSerializer.Serialize(payload));`.
    5. **Return** `BusinessResult.Success(new MarkOrderDeliveredResponse(order.Id, order.State))`. UoW pipeline commits the Order row + 1 outbox row atomically per ADR 0014.
- **`Core.AppServices/Features/Email/IEmailSendService.cs` + `EmailSendService.cs`** — extend the per-event-type switch (T-0067 Q3 pattern):
  - Add new `case OutboxEventTypes.OrderDeliveredCustomerEmail`:
    ```csharp
    case OutboxEventTypes.OrderDeliveredCustomerEmail
        => await SendOrderDeliveredCustomerEmailAsync(payloadJson, ct);
    ```
  - New helper `SendOrderDeliveredCustomerEmailAsync(string payloadJson, CancellationToken ct)`:
    - Deserialize `OrderDeliveredCustomerEmailPayload`.
    - Lookup template via `IEmailTemplateRepository.GetByTypeAndLanguageAsync(EmailTemplateType.OrderDeliveredCustomer, payload.LanguageCode, ct)` (existing convention).
    - Build SendGrid dynamic-template substitutions: `order_number`, `contact_name`, `action_url`.
    - Send via existing SendGrid pipeline. **No PDF attachment.**

### Infrastructure / Database layer

- **EF migration `AddOrderDeliverySource`** — add `delivery_source SMALLINT NULL` column to `orders`. Nullable so historical Delivered orders (pre-T-0076) don't fail the migration. EF config mapping in `OrderConfiguration` (or wherever the Order EF type config lives) — column name `delivery_source`, type `smallint`, nullable, mapped to `Order.DeliverySource` property with the `OrderDeliverySource?` CLR type via `HasConversion<short?>()` (or equivalent EF enum conversion — the implementer matches existing enum-mapping patterns in the codebase).
- **EF seed migration `SeedOrderDeliveredCustomerEmailTemplate`** — adds:
  - 1 row to `email_templates` for `EmailTemplateType.OrderDeliveredCustomer` with `d-placeholder-order-delivered-customer` SendGrid template id (replaced post-deploy when the real SendGrid template is built).
  - 2 rows to `email_template_translations` (cs-CZ + en-US) with subject + body referencing the placeholder.
  - cs-CZ subject draft: `"Vaše objednávka #{{order_number}} byla doručena"`. en-US subject draft: `"Your order #{{order_number}} has been delivered"`. Final wording belongs to l10n.

### Web.Customer host

- **`Web.Customer/Controllers/OrdersController.cs`** (or create if not present — match existing naming):
  - Add `[HttpPost("{orderId}/deliver")]` action `MarkDeliveredAsync(string orderId, CancellationToken ct)`.
  - Route resolves to `POST /api/v1/customer/orders/{orderId}/deliver`.
  - `[Authorize]` (customer scheme) — JWT audience enforced per host per CLAUDE.md security rules + ADR 0013.
  - One-liner: `var result = await mediator.Send(new MarkOrderDelivered.Command(orderId, OrderDeliverySource.Customer), ct); return HandleResult(result);`.

### i18n

- **`frontend/src/lib/i18n/cs-CZ.ts`** — add 1-2 new Czech keys for the new EmailTemplate row presentation surface (UX button caption + any new error code reuse note). Final key set is small:
  - Optional: `'customer.orders.markDeliveredButton': 'Označit jako doručeno'` (if the surface lives in the i18n bundle vs hard-coded in the new customer order-detail page — confirmed at FE-ticket time, but reserve the key).
  - No new BusinessErrorMessage code is introduced; existing `order.notFound` and `order.invalidTransition` already have Czech translations.

### NSwag regen

The new `POST /api/v1/customer/orders/{orderId}/deliver` endpoint is a contract change → **NSwag regen REQUIRED in the same PR** (customer host client). Per pre-commit hook (T-0013), `frontend/src/lib/api-client/` cannot be edited manually — `npm run generate:api` produces the diff. The new `MarkOrderDeliveredResponse` type (`OrderId`, `State`) appears in the generated client. T-0077 + T-0078 are Functions (no public contract); no maker/admin client regen needed.

### Tests

#### MarkOrderDelivered domain tests (NEW, ~3 tests)

`backend/src/Makables.Tests/Domain/Orders/OrderMarkAsDeliveredTests.cs` — pure domain tests. **TDD-first commit** per T-0067+ rule.

1. **MarkAsDelivered_with_clock_only_overload_compiles_via_optional_params** — call `order.MarkAsDelivered(clock, OrderDeliverySource.Auto)` (2-arg shape, deliveredAtOverride defaulted to null). Assert: state == Delivered, DeliveredAt == clock.UtcNow, DeliverySource == Auto.
2. **MarkAsDelivered_with_deliveredAtOverride_uses_override_timestamp** — call `order.MarkAsDelivered(clock, OrderDeliverySource.Carrier, deliveredAtOverride: new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero))`. Assert: DeliveredAt == the override timestamp, clock.UtcNow IGNORED.
3. **MarkAsDelivered_from_non_Shipped_state_returns_InvalidTransition** — order in Paid (or Accepted, or Cancelled). Call MarkAsDelivered. Assert: failure with OrderInvalidTransition. DeliverySource not set; DeliveredAt not set.

#### MarkOrderDeliveredHandlerTests (NEW, ~8 tests)

`backend/src/Makables.Tests/AppServices/Features/Orders/MarkOrderDeliveredHandlerTests.cs` — NSubstitute mocks (IOrderRepository, IOutbox, IClock, ICustomerSessionContext, IPublicAppUrls).

1. **Happy_path_Customer_source_transitions_to_Delivered_enqueues_1_outbox_event** — Customer source; order in Shipped owned by customer. Assert: state == Delivered, DeliverySource == Customer, DeliveredAt == clock.UtcNow, IOutbox.Enqueue called exactly 1x with (order.Id, OrderDeliveredCustomerEmail, …). Response carries (OrderId, Delivered).
2. **Happy_path_Auto_source_transitions_to_Delivered_enqueues_1_outbox_event** — Auto source; order in Shipped (unscoped lookup). Assert: state == Delivered, DeliverySource == Auto, IOutbox.Enqueue called 1x.
3. **Happy_path_Carrier_source_with_deliveredAtOverride_stamps_override_timestamp** — Carrier source; deliveredAtOverride = `2026-06-01T12:00:00Z`. Assert: DeliveredAt == override timestamp (NOT clock.UtcNow), DeliverySource == Carrier, IOutbox.Enqueue called 1x.
4. **Already_Delivered_silent_success_no_outbox_emission** — order already in Delivered state. Call with Source = Carrier (simulating T-0078 race after customer hit it). Assert: result is Success, MarkOrderDeliveredResponse returned with State == Delivered, IOutbox.Enqueue NOT called (Received(0)), order entity NOT mutated (DeliverySource unchanged from whatever it was).
5. **Customer_source_with_ownership_mismatch_returns_NotFound** — Source = Customer, `GetByIdForCustomerAsync` returns null (order is not owned by the requesting customer). Assert: NotFound result with OrderNotFound, outbox NOT called.
6. **Non_Shipped_non_Delivered_state_returns_OrderInvalidTransition** — order in Accepted (or Paid, or Cancelled). Source = Customer. Assert: Conflict with OrderInvalidTransition, outbox NOT called. (Distinguish from AC-4 above: this is the genuine "wrong state" path; Already-Delivered is the Silent Success path.)
7. **Outbox_event_enqueued_with_correct_aggregate_id_and_event_type** — happy path. Capture IOutbox.Enqueue arguments via NSubstitute. Assert: aggregateId == order.Id, eventType == "order.delivered.customerEmail". Single Received(1) call.
8. **OrderDeliveredCustomerEmailPayload_field_correctness** — capture the JSON payload via `Arg.Do<string>`; deserialize; assert: `OrderId == order.Id`, `OrderNumber == order.OrderNumber`, `Email == order.ContactEmail`, `ContactName == order.ContactName`, `LanguageCode == order.LanguageCode`, `ActionUrl == $"{publicAppUrls.WebBaseUrl}/objednavka/{order.Id}"`. All 6 fields present and correctly populated.

#### MarkOrderDeliveredIntegrationTests (NEW, ~1 test)

`backend/src/Makables.IntegrationTests/Orders/MarkOrderDeliveredIntegrationTests.cs` — Testcontainers postgres + faked `IOutbox` (or real outbox table assertion).

1. **POST_deliver_happy_path_transitions_order_and_writes_1_outbox_row** — seed a Shipped order owned by the requesting customer, POST `/api/v1/customer/orders/{id}/deliver`, assert 200 + MarkOrderDeliveredResponse body + DB state: order row has `state == Delivered`, `delivered_at` ~= now, `delivery_source == 0` (Customer), AND `outbox_events` has exactly 1 row with `aggregate_id == order.Id` and event type `order.delivered.customerEmail`.

#### EmailSendServiceTests extension (~2 new tests)

`backend/src/Makables.Tests/AppServices/Features/Email/EmailSendServiceTests.cs` — extend with:

1. **OrderDeliveredCustomerEmail_branch_loads_template_and_sends** — pass `OrderDeliveredCustomerEmailPayload` JSON + `event_type = order.delivered.customerEmail`. Assert template lookup keyed by `EmailTemplateType.OrderDeliveredCustomer` + payload.LanguageCode, and SendGrid called with substitutions matching payload fields (`order_number`, `contact_name`, `action_url`).
2. **OrderDeliveredCustomerEmail_cs_CZ_template_substitutions_present** — payload.LanguageCode == `"cs-CZ"`; assert IEmailTemplateRepository.GetByTypeAndLanguageAsync called with (OrderDeliveredCustomer, "cs-CZ", ct). Asserts the seed migration's cs-CZ row is wired.

### Docs

- **`docs/architecture/roles/order.md`** — note the new state transition: "Shipped → Delivered via `MarkOrderDelivered.Command(OrderId, Source)` emits 1 outbox event (`order.delivered.customerEmail`) atomically per ADR 0014. Source tracked as `DeliverySource` column (Customer/Auto/Carrier)." Reference T-0076 in the Lifecycle table row.
- **`docs/tickets/INDEX.md`** — flip T-0076 row to `**done**` after PR merge (PM does this).

## Alternatives Considered

- **Option A — DeliverySource in outbox payload only (no Order column).** *Rejected per A.1* — every order-level dispute query ("how did this order close?") would require an `INNER JOIN outbox_events ON aggregate_id = order_id AND event_type = 'order.delivered.customerEmail'` plus JSON extraction. The Order column makes the query a single SELECT against an indexed-ish (or at least narrow) column.
- **Option B — Infer DeliverySource from audit-log event types.** *Rejected per A.1* — relies on stable event_type strings being grep-friendly forever; brittle and the hardest to query. Audit logs are append-only narrative, not a queryable analytical surface.
- **Option C — Two outbox events (customer email + maker email).** *Rejected per A.2* — higher SendGrid cost for marginal UX. Maker has no action to take at delivery (T-0102's weekly payout cron is the contract). Adding an event would also expand the EmailSendService switch + the EmailTemplateType enum without a downstream consumer.
- **Option D — Conditional second outbox event by ShippingMethod (e.g., maker email only for PersonalPickup).** *Rejected per A.2* — speculative branch for marginal gain. If maker-side notifications become a thing, ship them under a separate ticket with a clear use case.
- **Option E — Return Conflict (409) on already-Delivered re-call.** *Rejected per A.3* — worse race UX. Customer + T-0078 carrier sync race is the expected concurrent path under MVP traffic; both should succeed silently. Mirrors T-0067/T-0069 idempotency precedent.
- **Option F — Source-conditional Silent Success (e.g., only Carrier re-calls are silent; Customer + Auto get Conflict).** *Rejected per A.3* — surprises future callers + bakes in an asymmetry that's hard to remember. All three callers see the same contract.
- **Option G — Route `/confirm-delivery` or `/mark-delivered`.** *Rejected per A.4* — verbose vs the T-0072/T-0073 short-verb convention (`/ship`, `/handover`). One route shape across the order-action surface.
- **Option H — Separate `MarkOrderDelivered.Customer`, `.Auto`, `.Carrier` commands (one per caller).** *Rejected per A.1 + C.6* — 3× the feature surface, 3× the test fixtures, 3× the validators, all to encode a tiny variation. Single Command with Source enum is the minimal shape.
- **Option I — Add a new outbox-event-type queue (`order-delivered-email`) instead of reusing `send-email`.** *Rejected per ADR 0020 §C* — the queue-per-event-class principle applies when the consumer Function has a meaningfully different latency or concurrency profile. `order.delivered.customerEmail` is just another SendGrid send (~100ms); reusing `send-email` is correct.
- **Option J — Stamp the actor identity (CustomerId / SystemUserId) instead of a Source enum.** *Rejected per C.1* — actor identity is already in `UpdatedBy` (audit fields per `Auditable` base entity). Source is a categorical that's queryable for analytics ("what fraction of orders close via auto-deliver?"); actor identity is per-row noise.

## Out of scope

- **Auto-deliver timer Function** (`AutoDeliverOrdersFunction` or similar) — T-0077 owns the Azure Functions timer trigger that scans `orders WHERE state = Shipped AND auto_deliver_at <= now()` and dispatches `MarkOrderDelivered.Command(orderId, OrderDeliverySource.Auto)` per row.
- **Packeta status-sync timer Function** — T-0078 owns the timer that polls `IShippingCarrier.GetStatusAsync(carrierRef)` (per T-0070's seam) and dispatches `MarkOrderDelivered.Command(orderId, OrderDeliverySource.Carrier, deliveredAtOverride: packetaTimestamp)` when the carrier status indicates delivered.
- **Maker delivery notification email** — explicitly rejected per A.2.
- **Customer-side "I haven't received my order" dispute flow** — Phase 5 T-0106/T-0118 (this ticket sets up the DeliverySource column those tickets read).
- **Frontend "Označit jako doručeno" button + customer order-detail page wiring** — separate frontend ticket (FE-side). T-0076 only ships the backend endpoint + i18n key reservation.
- **SendGrid template content build** — the seed migration uses `d-placeholder-order-delivered-customer`. The real SendGrid template is built post-deploy by the user; placeholder rows are sufficient for CI green.
- **Admin manual delivery override** — not at MVP. If needed later, add `OrderDeliverySource.AdminManual = 3` and a separate admin endpoint.
- **DeliverySource backfill for pre-T-0076 Delivered orders** — column is nullable. Historical rows stay NULL; analytics queries filter or coalesce as needed.
- **Outbox event for delivery cancellation / un-delivery** — not at MVP. Re-opening a Delivered order is out of scope.

## Acceptance criteria

- **AC-1** Given a Shipped order owned by the requesting customer, when `POST /api/v1/customer/orders/{orderId}/deliver` is called with a valid customer JWT, then it returns `200 OK` with body `{ OrderId, State: "Delivered" }`, AND the order row has `state = Delivered`, `delivered_at = clock.UtcNow`, `delivery_source = 0` (Customer).
- **AC-2** Given the same happy path, when the handler returns, then exactly **1 row** exists in `outbox_events` with `aggregate_id = order.Id` and event type `order.delivered.customerEmail`. The payload deserializes to `OrderDeliveredCustomerEmailPayload` with all 6 fields populated (OrderId, OrderNumber, Email, ContactName, LanguageCode, ActionUrl). ActionUrl equals `{WebBaseUrl}/objednavka/{order.Id}`.
- **AC-3** Given a Shipped order NOT owned by the requesting customer, when the customer endpoint is called, then it returns `404` with error code `order.notFound`. No outbox rows, no state mutation.
- **AC-4** Given an order already in `Delivered` state, when `MarkOrderDelivered.Command(orderId, OrderDeliverySource.Carrier)` is dispatched (simulating T-0078 race after customer hit it), then the handler returns `BusinessResult.Success(MarkOrderDeliveredResponse(orderId, Delivered))` AND `IOutbox.Enqueue` is NOT called (Received(0)) AND the order entity's `DeliverySource` is unchanged from whatever it was on first transition.
- **AC-5** Given an order in a state other than Shipped or Delivered (Accepted, Paid, Cancelled), when the customer endpoint is called, then it returns `409` with error code `order.invalidTransition`. No outbox rows, no state mutation.
- **AC-6** Given `MarkOrderDelivered.Command(orderId, OrderDeliverySource.Carrier, deliveredAtOverride: timestamp)` is dispatched (T-0078 path), when the handler runs against a Shipped order, then `order.DeliveredAt == timestamp` (NOT `clock.UtcNow`) AND `order.DeliverySource == Carrier`.
- **AC-7** Given `MarkOrderDelivered.Command(orderId, OrderDeliverySource.Auto)` is dispatched (T-0077 path), when the handler runs against a Shipped order, then `order.DeliveredAt == clock.UtcNow` (no override) AND `order.DeliverySource == Auto`.
- **AC-8** Given the `OutboxEventTypes` static class, when read, then `OrderDeliveredCustomerEmail = "order.delivered.customerEmail"` exists AND `IsEmailSend("order.delivered.customerEmail")` returns true.
- **AC-9** Given an `order.delivered.customerEmail` outbox event being drained by `IEmailSendService.SendAsync`, when the switch routes, then the new `SendOrderDeliveredCustomerEmailAsync` helper runs. SendGrid is called with template id matching `EmailTemplateType.OrderDeliveredCustomer` for the payload's `LanguageCode`, and dynamic-template substitutions include `order_number`, `contact_name`, `action_url` (verbatim from payload). No PDF attachment.
- **AC-10** Given the `orders` table after the `AddOrderDeliverySource` migration runs, when inspected, then it has a `delivery_source SMALLINT NULL` column. Existing rows have `delivery_source IS NULL`. The migration is reversible (`Down` drops the column).
- **AC-11** Given the `email_templates` + `email_template_translations` tables after the seed migration runs, when queried, then exactly 1 row exists in `email_templates` for `EmailTemplateType.OrderDeliveredCustomer` AND exactly 2 rows in `email_template_translations` for that template (cs-CZ + en-US) with non-empty subject + body.
- **AC-12** Build clean. Unit tests: baseline (after T-0073 in the same PR sequence) + ~13 new (~3 OrderMarkAsDeliveredTests + 8 MarkOrderDeliveredHandlerTests + 2 EmailSendServiceTests). Integration tests: baseline + 1 new (MarkOrderDeliveredIntegrationTests). `node scripts/check-consistency.mjs` exit 0 (no new T1–T7 violations vs the bundle's running baseline). NSwag regen committed in the same PR; `frontend/src/lib/api-client/` types the new `/customer/orders/{id}/deliver` endpoint with `MarkOrderDeliveredResponse { orderId, state }`. No manual edits to the api-client folder (pre-commit hook enforces).

## Technical notes

### Why DeliverySource as an Order column (not payload-only or audit-inferred)

Dispute trails and analytics in Phase 5 (T-0106 customer dispute resolution, T-0118 marketplace KPIs) need to query "how did this order close?" without joining 3 tables. A column on `orders` is a single indexed lookup; a payload-only model requires `INNER JOIN outbox_events ON aggregate_id = order_id AND event_type = ...` plus JSON extraction; an audit-log-inference model relies on stable event_type strings being grep-friendly forever. The column also makes the analytical query trivial: `SELECT delivery_source, COUNT(*) FROM orders WHERE state = 'Delivered' GROUP BY delivery_source`. The nullable column gracefully handles historical (pre-T-0076) Delivered orders.

### Why Silent Success on already-Delivered (not Conflict)

Three callers can race within seconds: customer presses the button while T-0078's carrier-sync timer fetches Packeta's status. Under MVP traffic this race IS the expected concurrent path. Returning 409 on the second call would either (a) surface a spurious error to the customer ("how is this conflict?") or (b) force T-0078 to swallow the Conflict and log noise. Returning Success on the no-op path is the same idempotency stance T-0067 (MarkOrderPaid) and T-0069 (IssueInvoice) already locked. The handler still loads the Order (cheap) and the UoW pipeline still commits a no-op transaction (cheap); no outbox row is written, so no duplicate email is sent.

### Why the Source enum has explicit `: short` backing

The EF column is `SMALLINT NULL` to keep storage tight (Int16 vs Int32 saves 2 bytes per row × N orders). Explicit `: short` on the enum lets EF map the conversion without a default-Int32 round-trip. `OrderDeliverySource.Customer = 0` is the default value; explicit assignment is documentation. Future appended values (e.g., `AdminManual = 3`) get stable wire codes.

### Why a single email event (no maker notification)

Maker has no action to take when an order is marked Delivered. T-0102's weekly payout cron sweeps Delivered orders and calculates payouts independently. A maker email at delivery would be (a) a redundant ping (the maker already sees the order in their dashboard "in transit" tab moving to "delivered"); (b) a SendGrid cost multiplier (every order generates 2 emails instead of 1); (c) a surface area expansion in EmailTemplateType + EmailSendService for zero downstream value. If the maker UX ever needs a "your order arrived" ping, ship it as a separate ticket with a concrete use case.

### Why `Order.MarkAsDelivered` gets in-place signature extension (not a new method)

The current `MarkAsDelivered(IClock clock)` shape already handles the state transition; adding a 2nd required parameter `OrderDeliverySource source` + a 3rd optional `DateTimeOffset? deliveredAtOverride = null` is the minimal change: (a) all 3 callers in the delivery-close bundle use the same method; (b) the optional 3rd param keeps the method shape clean for the 2 of 3 callers that pass null; (c) a new `Order.MarkAsDeliveredFromCarrier(...)` method would split the state mutation across N methods that always must be called together (per-source). The signature extension mirrors the T-0072 `Order.Ship(...)` 4th-param extension pattern.

### Why the `MarkOrderDeliveredResponse` name is globally-unique

The T-0070-T-0075 CI fix exposed an NSwag client-gen collision: multiple `Response` classes from different features generate the same TS class name and the build breaks. T-0076 sidesteps the collision proactively by naming the response record `MarkOrderDeliveredResponse` instead of `Response`. The nested-type convention `MarkOrderDelivered.Response` is preserved in C# (no source-code reader confusion), but the wire-type name carries the feature prefix. Implementer: verify the existing T-0070-T-0075 CI fix pattern at code time and match its exact convention (record naming + nested-class collision rules).

## Files touched (expected)

### New
- `backend/src/Makables.Core.Domain/Orders/OrderDeliverySource.cs`
- `backend/src/Makables.Core.Domain/Outbox/OrderDeliveredCustomerEmailPayload.cs`
- `backend/src/Makables.Core.AppServices/Features/Orders/MarkOrderDelivered.cs`
- `backend/src/Makables.Infra.Database/Migrations/<timestamp>_AddOrderDeliverySource.cs` (+ Designer)
- `backend/src/Makables.Infra.Database/Migrations/<timestamp>_SeedOrderDeliveredCustomerEmailTemplate.cs` (+ Designer)
- `backend/src/Makables.Tests/Domain/Orders/OrderMarkAsDeliveredTests.cs` (or extend existing file if present)
- `backend/src/Makables.Tests/AppServices/Features/Orders/MarkOrderDeliveredHandlerTests.cs`
- `backend/src/Makables.IntegrationTests/Orders/MarkOrderDeliveredIntegrationTests.cs`

### Modified
- `backend/src/Makables.Core.Domain/Orders/Order.cs` — extend `MarkAsDelivered(IClock)` to `MarkAsDelivered(IClock, OrderDeliverySource, DateTimeOffset?)` in-place; add `DeliverySource` private-set property; XML doc updated.
- `backend/src/Makables.Core.Domain/Outbox/OutboxEventTypes.cs` — add `OrderDeliveredCustomerEmail` constant; extend `IsEmailSend`.
- `backend/src/Makables.Core.Domain/Email/EmailTemplateType.cs` — add `OrderDeliveredCustomer = 7` (verify next enum value).
- `backend/src/Makables.Infra.Database/Configurations/OrderConfiguration.cs` (or equivalent) — add `delivery_source` column mapping with enum conversion.
- `backend/src/Makables.Core.AppServices/Features/Email/IEmailSendService.cs` + `EmailSendService.cs` — new `OrderDeliveredCustomerEmail` case; `SendOrderDeliveredCustomerEmailAsync` helper.
- `backend/src/Makables.Web.Customer/Controllers/OrdersController.cs` (or matching file) — new `POST {orderId}/deliver` action.
- `backend/src/Makables.Tests/AppServices/Features/Email/EmailSendServiceTests.cs` — 2 new tests.
- `frontend/src/lib/i18n/cs-CZ.ts` — reserve `customer.orders.markDeliveredButton` (or defer to FE ticket — verify at impl time).
- `frontend/src/lib/api-client/*` — NSwag-regenerated (customer host); committed in the same PR.
- `docs/architecture/roles/order.md` — note the Shipped → Delivered transition + 1-event outbox + DeliverySource column.

## Test plan reference

Inline above (see Scope > Tests). No separate `docs/test-plans/T-0076.md`.

## Status log

- 2026-06-08 `draft` by PM. Created as part of the delivery-close bundle (T-0076 + T-0077 + T-0078). Reference precedents merged or in the same bundle PR: T-0067 MarkOrderPaid (state-transition + outbox pattern), T-0029 ProcessOutboxFunction (timer-trigger precedent for T-0077), T-0069 GenerateInvoiceFunction (queue-trigger precedent), T-0070 IShippingCarrier seam (T-0078 uses GetStatusAsync), T-0072/T-0073 Ship/Handover (short-verb route convention). Slice scope: MarkOrderDelivered one-file feature + OrderDeliverySource enum + Order.MarkAsDelivered signature extension + EF migration + outbox event + EmailSendService branch + customer endpoint. T-0077 + T-0078 sit downstream as caller-only additions to the same handler.
- 2026-06-08 `draft → ready` by PM. User answered 4 blocking AskUserQuestion items per `/feature` workflow step 3: **A.1** DeliverySource captured as Order column (rejected payload-only + audit-log inference); **A.2** single `order.delivered.customerEmail` event — no maker email (rejected customer+maker + conditional by ShippingMethod); **A.3** Silent Success on already-Delivered re-call (rejected Conflict + source-conditional); **A.4** short-verb route `/deliver` (rejected `/confirm-delivery` + `/mark-delivered`). 7 PM-absorbed decisions captured in `## Locked design decisions §C` (Order.MarkAsDelivered signature extension, OrderDeliveredCustomerEmailPayload shape mirroring T-0067, EmailTemplateType + cs-CZ/en-US seed, EmailSendService switch arm, customer endpoint IDOR scoping, globally-unique Response name, one-file feature structure). 7 ADR-locked items extracted in §B (ADR 0013 per-audience JWT enforcement, ADR 0014 UoW pipeline, ADR 0019 per-event-type EmailSendService switch, ADR 0020 reuse existing send-email queue, one-file feature shape, BusinessResult<T>, TDD-first commit order). No manual_steps. **Ready for dotnet-backend.** The implementer processes T-0076 → T-0077 → T-0078 sequentially in the same branch; all three ship in one PR.
