---
id: T-0072
title: ShipOrder command (Zásilkovna path) + atomic 2-event outbox (customer shipped + generate label)
status: ready
size: M
owner: dotnet-backend
created: 2026-06-08
updated: 2026-06-08
depends_on: [T-0070, T-0071]
blocks: [T-0076]
user_stories: [US-maker-0007]
adrs: [0014, 0017, 0019, 0020]
phase: 4
manual_steps: []
security_touching: false
layers: [domain, appservices, infra-database, web-maker, frontend-i18n]
---

# T-0072 — ShipOrder command (Zásilkovna path) + atomic 2-event outbox (customer shipped + generate label)

## Context

T-0072 is the **first writer** of the shipping seam: the maker presses **"Odeslat"** on an Accepted Zásilkovna order, the backend creates a Packeta shipment via the T-0070 `IShippingCarrier` seam, stamps the carrier ref + pre-computed tracking URL on the Order row, transitions state `Accepted → Shipped`, and atomically enqueues two outbox events under one UoW transaction — `order.shipped.customerEmail` (drives the customer notification with tracking link) and `shipping.generate.label` (queue trigger for the T-0074 GenerateLabel Function that uploads the maker's carrier-label PDF to blob).

This is the Zásilkovna-only ShipOrder slice. The Personal-Pickup variant is T-0073 (separate command name; same email event + template; same 7-day auto-deliver window; no carrier call; no label generate event). Both ship under one PR (the shipping-pipeline bundle: T-0070+T-0071+T-0072+T-0073+T-0074+T-0075), so the email template + the `OrderShippedCustomerEmailPayload` record + the `OrderShippedCustomer` enum value are introduced here and **reused** by T-0073 without re-creation.

The handler mirrors **T-0067 MarkOrderPaid's** atomic 2-event pattern (3 events there; 2 here) — one state transition + N outbox events committed in the same `UnitOfWorkPipelineBehavior` transaction per ADR 0014. Failure of any step rolls the entire transaction back; we never have "Order is Shipped but no customer email queued" or "label generate event enqueued but Order is still Accepted." The carrier I/O happens **before** state mutation: a Packeta 5xx returns a `Transient` `BusinessResult.Failure` that surfaces to the maker's dashboard for re-click, and no outbox events are emitted.

The customer email handler reuses **T-0069's lookup-at-send-time** pattern: when the `order.shipped.customerEmail` event drains, it does NOT attach the label PDF (the label is a maker artifact; customers get a tracking URL instead). The customer payload pre-bakes everything the template needs (OrderNumber, ContactName, ActionUrl, TrackingUrl — TrackingUrl null for T-0073). The `shipping.generate.label` event drains in parallel to the new `generate-label` queue (mirror of T-0069's `generate-invoice` queue split per ADR 0020) and the T-0074 Function uploads the PDF independently.

T-0072 also introduces the `IOutboxQueuePublisher.PublishGenerateLabelAsync` method + `OutboxDispatcher` routing branch for `shipping.generate.label` (mirroring the T-0069 `IsInvoiceGenerate` addition). This keeps the dispatcher's per-event-type routing flat and explicit — no switch fall-throughs, no implicit "default = email."

## Locked design decisions

Captured per `docs/process/deliberation.md`. T-0070 pre-locked the carrier seam (interface shape, error classification, 7-day window, flat label blob path). T-0072 adds 1 user-locked decision (atomic 2-event outbox emission) and 9 PM-absorbed decisions (`Order.Ship` signature extension, payload shapes, transient surface, email-attachment policy, queue split, dispatcher routing, etc.).

### A. User-locked at /feature step 3 (non-negotiable)

1. **Atomic 2-event outbox emission under one UoW.** `ShipOrder.Handler` enqueues BOTH `order.shipped.customerEmail` AND `shipping.generate.label` in the same UoW transaction as the Order.Ship state mutation. Mirrors T-0067 MarkOrderPaid's 3-atomic-events pattern. Customer email's blob-attachment path uses the same lookup-at-send-time + Transient retry pattern from T-0069 (T-0074 handles label-generation independently; email handler queries blob and Transient-fails if not yet ready). **Rejected:** sequential (email-first, label-after-email-delivery — couples label-gen to email success); priority queue (adds priority logic to dispatcher; loses queue independence).

### B. ADR-locked (no relitigation)

- **ADR 0014 (UoW pipeline).** Handler MUST NOT call `SaveChangesAsync()`. `UnitOfWorkPipelineBehavior` commits the Order mutation + 2 outbox rows in a single Postgres transaction. Failure anywhere rolls back everything.
- **ADR 0017 (shipping/Packeta).** `IShippingCarrier.CreateShipmentAsync` accepts full `Order` aggregate; returns `BusinessResult<Shipment>(CarrierRef, TrackingUrl)`. Error classification follows §A.14 (Transient/Permanent/Configuration/Unknown). 7-day auto-deliver window is uniform across shipping methods (T-0070 locked decision A.4). Single platform-wide Packeta account at MVP.
- **ADR 0019 (email pipeline).** Per-event-type switch in `IEmailSendService.SendAsync` per T-0067 Q3. Each new event type adds one branch; no implicit fall-through. Template lookup keyed by `EmailTemplateType` enum.
- **ADR 0020 (background jobs + outbox queue split).** New outbox event types follow `<domain>.<action>.<modality>` convention per T-0067. New queue split per ADR 0020 §queue-per-event-class: `shipping.generate.label` routes to its own `generate-label` queue (NOT the existing `send-email` queue) — mirrors T-0069's `generate-invoice` split. Bare outbox id as queue message body per T-0029 pattern (payload stays in Postgres).
- **One-file feature shape.** `Features/Orders/ShipOrder.cs` contains nested `Command`, `Validator`, `Handler`, `Response`. No separate files per type.
- **`BusinessResult<T>` for expected failures.** Carrier 5xx, ownership mismatch, wrong shipping method, invalid state → `BusinessResult.Failure`. Exceptions reserved for truly unexpected (e.g., DB connection dropped).
- **TDD-with-commit-order hard rule** (T-0067+ enforced) for pure logic: domain entity changes (`Order.Ship` signature extension, set-once on `ShippingCarrierTrackingUrl`) ship test-first.
- **Per-event-type switch in `IEmailSendService`** per T-0067 Q3. New `OrderShippedCustomerEmail` branch added; existing cases untouched.

### C. PM-absorbed (no user input needed)

- **`Order.Ship` signature extension:** in-place extension with a 4th optional `string? trackingUrl = null` parameter (sets `ShippingCarrierTrackingUrl` if non-null; preserves backwards compatibility for callers that don't carry one). The personal-pickup path (T-0073) passes `null`. **Rejected:** new overload (signature swell).
- **GenerateLabel outbox payload:** new sealed record `GenerateLabelOutboxPayload(string OrderId)`. T-0074 handler looks up `Order.ShippingCarrierRef` from DB. **Rejected:** pre-bake carrier ref into payload (premature; payload mutates if Ship() restamps the ref, which it won't under current state-graph but cleaner to fetch fresh).
- **Transient carrier errors in ShipOrder.Handler:** fail-fast with BusinessResult.Failure(Transient(ShippingCarrierUnavailable)). Maker dashboard re-tries on next click. ShipOrder is a synchronous Mediator command — no outbox-style automatic retry. **Rejected:** park-and-retry (adds command-level retry policy; out of scope).
- **Email timing:** parallel via atomic outbox. Email handler may transient-fail (lookup-at-send-time) until T-0074 finishes blob upload. Retry policy resolves naturally.
- **ShipOrder.Handler scope:** Zásilkovna-only at T-0072. T-0073 ships the PersonalPickup variant under a separate command name (`HandOverOrder` or `ShipOrderPersonalPickup` — see T-0073 ticket).
- **Email template + event:** UNIFIED with T-0073 — both ship under `order.shipped.customerEmail` event + `EmailTemplateType.OrderShippedCustomer` template. Template conditionally renders the `tracking_url` line when non-empty.
- **Outbox event for label:** new `shipping.generate.label` constant; routes to new `generate-label` queue (mirror T-0069 `generate-invoice` queue split per ADR 0020).
- **New OutboxQueueOptions.GenerateLabelQueueName** (default `"generate-label"`) + IOutboxQueuePublisher.PublishGenerateLabelAsync method + impl, all mirroring T-0069 patterns.
- **OutboxDispatcher routing:** new `IsGenerateLabel(string eventType)` classifier; dispatcher branch routes shipping.generate.label to the new queue. Existing dispatcher branches (IsEmailSend, IsInvoiceGenerate) untouched.
- **Email-attachment flow:** SendOrderShippedCustomerEmailAsync helper fetches Order via IOrderRepository.GetByIdUnscopedAsync (mirrors T-0069 fix that removed IOrderRepository — wait, T-0069 used payload.OrderNumber). REUSE payload.OrderNumber from T-0067 precedent — the OrderShippedCustomerEmailPayload pre-bakes OrderNumber + TrackingUrl. No Invoice lookup at this point (this is shipping, not invoice).
- **Label attachment:** customer shipped email does NOT carry the PDF as an attachment (label is for maker carrier). Customer-facing tracking_url is enough.

## Scope

### Domain layer

- **`Core.Domain/Orders/Order.cs`** — extend `Ship(IClock, string? shippingCarrierRef, int autoDeliverWindowDays)` with a 4th optional parameter `string? trackingUrl = null`:
  - If non-null: validate `trackingUrl.Length <= 500` (matches column cap); set `ShippingCarrierTrackingUrl = trackingUrl.Trim()` once. Set-once guard: if `ShippingCarrierTrackingUrl is not null` → `BusinessResult.Failure(Error.Conflict("trackingUrl", BusinessErrorMessage.OrderInvalidTransition))` (mirrors the existing `ShippingCarrierRef` set-once guard at `Order.cs:576-578`).
  - If null: do NOT touch `ShippingCarrierTrackingUrl` (T-0073 personal-pickup path passes null).
  - XML doc updated to describe the new parameter + reference T-0072 as the writer (replacing the T-0070-era "TODO" pointer if present).
  - Backwards compatibility: existing call sites that omit the 4th argument compile + behave identically.
- **`Core.Domain/Outbox/OutboxEventTypes.cs`** — add 2 new constants:
  - `OrderShippedCustomerEmail = "order.shipped.customerEmail"`
  - `ShippingGenerateLabel = "shipping.generate.label"`
  - Extend `IsEmailSend(string eventType)` to return true for `OrderShippedCustomerEmail` (joined into the existing OR-chain).
  - Add **new** classifier method `IsGenerateLabel(string eventType)` returning true only for `ShippingGenerateLabel`. Mirrors T-0069's `IsInvoiceGenerate` shape (single-event classifier, kept explicit for future expansion).
- **`Core.Domain/Outbox/OrderShippedCustomerEmailPayload.cs`** — NEW sealed record:
  ```csharp
  public sealed record OrderShippedCustomerEmailPayload(
      string OrderId,
      string OrderNumber,
      string Email,
      string ContactName,
      string LanguageCode,
      string ActionUrl,
      string? TrackingUrl);
  ```
  PascalCase JSON property names (matches `OrderPaidCustomerEmailPayload` convention from T-0067). `TrackingUrl` is nullable — T-0072 always sets it; T-0073 personal-pickup always passes null. Template conditionally renders the tracking line.
- **`Core.Domain/Outbox/GenerateLabelOutboxPayload.cs`** — NEW sealed record:
  ```csharp
  public sealed record GenerateLabelOutboxPayload(string OrderId);
  ```
  T-0074 handler looks up `Order.ShippingCarrierRef` + country code via `IOrderRepository.GetByIdUnscopedAsync(OrderId, ct)`.
- **`Core.Domain/Email/EmailTemplateType.cs`** — add `OrderShippedCustomer = 6` (next enum value after T-0067's `OrderPaidCustomer = 4` + `OrderPlacedMaker = 5`).
- **`Core.Domain/Common/BusinessErrorMessage.cs`** — add 1 new code:
  - `ShippingMethodNotEligible = "shipping.methodNotEligible"` (Validation — covers "this is a PersonalPickup order, use the other endpoint" UX). Covers the assert in ShipOrder.Handler step 3.

### AppServices layer

- **`Core.AppServices/Features/Orders/ShipOrder.cs`** — NEW one-file feature.
  - `Command(string OrderId)` record.
  - `Response(string OrderId, string CarrierRef, string TrackingUrl)` record. `MakerSessionId` not surfaced (audit fields cover it).
  - `Validator : AbstractValidator<Command>` — `OrderId` non-empty + valid id format.
  - `Handler(IClock clock, IMakerSessionContext sessionContext, IOrderRepository orderRepository, IShippingCarrierFactory shippingCarrierFactory, IOutbox outbox, IPublicAppUrls publicAppUrls)` primary-constructor DI.
  - Steps (NO `SaveChangesAsync()` — UoW pipeline commits):
    1. **Resolve maker session** — `sessionContext.RequireMakerId()` or equivalent existing helper. Failure surfaces as Authorization error.
    2. **Load Order** via `orderRepository.GetByIdForMakerAsync(command.OrderId, makerId, ct)` (ownership-scoped — returns null if not owned by the maker). Null → `BusinessResult.Failure(Error.NotFound(BusinessErrorMessage.OrderNotFound))`.
    3. **Assert ShippingMethod == ZasilkovnaPickupPoint.** Else → `BusinessResult.Failure(Error.Validation("shippingMethod", BusinessErrorMessage.ShippingMethodNotEligible))`. Personal-pickup orders route to the T-0073 command (`HandOverOrder` / `ShipOrderPersonalPickup` — separate endpoint).
    4. **Resolve carrier** via `shippingCarrierFactory.ResolveAsync(order.CountryCode, ct)`. Propagate failure (`ShippingCarrierConfigurationError` surfaces as-is).
    5. **Create shipment** via `carrier.CreateShipmentAsync(order, ct)`. Propagate failure (`ShippingCarrierUnavailable` / `ShippingCarrierInvalidWeight` / `ShippingCarrierAddressIdNotFound` / `ShippingCarrierConfigurationError` surface as-is per ADR 0016 §A.14).
    6. **Order state transition** — `var shipResult = order.Ship(clock, shipment.CarrierRef, autoDeliverWindowDays: 7, trackingUrl: shipment.TrackingUrl);` — propagate failure (InvalidTransition → `BusinessResult.Failure(Error.Conflict("state", BusinessErrorMessage.OrderInvalidTransition))`).
    7. **Build customer payload + enqueue** — `var customerPayload = new OrderShippedCustomerEmailPayload(order.Id, order.OrderNumber, order.ContactEmail, order.ContactName, order.LanguageCode, $"{publicAppUrls.WebBaseUrl}/objednavka/{order.Id}", shipment.TrackingUrl); outbox.Enqueue(order.Id, OutboxEventTypes.OrderShippedCustomerEmail, JsonSerializer.Serialize(customerPayload));`.
    8. **Build label payload + enqueue** — `var labelPayload = new GenerateLabelOutboxPayload(order.Id); outbox.Enqueue(order.Id, OutboxEventTypes.ShippingGenerateLabel, JsonSerializer.Serialize(labelPayload));`.
    9. **Return** `BusinessResult.Success(new Response(order.Id, shipment.CarrierRef, shipment.TrackingUrl))`. UoW pipeline commits the Order row + 2 outbox rows atomically per ADR 0014.
- **`Core.AppServices/Features/Email/IEmailSendService.cs` + `EmailSendService.cs`** — extend the per-event-type switch (T-0067 Q3 pattern):
  - Add new `case OutboxEventTypes.OrderShippedCustomerEmail`:
    ```csharp
    case OutboxEventTypes.OrderShippedCustomerEmail
        => await SendOrderShippedCustomerEmailAsync(payloadJson, ct);
    ```
  - New helper `SendOrderShippedCustomerEmailAsync(string payloadJson, CancellationToken ct)`:
    - Deserialize `OrderShippedCustomerEmailPayload`.
    - Lookup template via `IEmailTemplateRepository.GetByTypeAndLanguageAsync(EmailTemplateType.OrderShippedCustomer, payload.LanguageCode, ct)` (existing convention).
    - Build SendGrid dynamic-template substitutions: `order_number`, `contact_name`, `action_url`, `tracking_url` (the template renders the `tracking_url` line conditionally when non-empty — handled in template content, not in code).
    - Send via existing SendGrid pipeline. **No PDF attachment** — the label is for the maker; customer gets the tracking URL.
- **`Core.AppServices/Common/OutboxQueuesOptions.cs`** — add `GenerateLabelQueueName` string property (default `"generate-label"`). Extend validator regex (existing queue-name pattern — alphanumeric + hyphens, lowercase).
- **`Core.Domain/Outbox/IOutboxQueuePublisher.cs`** — add `Task PublishGenerateLabelAsync(string outboxEventId, CancellationToken ct);`.
- **`Core.AppServices/Features/Outbox/OutboxDispatcher.cs`** — extend the routing:
  - New `RouteTarget.GenerateLabel` enum value (alongside existing `EmailSend`, `InvoiceGenerate`).
  - Extend `ClassifyRoute(string eventType)` — return `GenerateLabel` when `OutboxEventTypes.IsGenerateLabel(eventType)`. Existing branches (IsEmailSend, IsInvoiceGenerate) untouched.
  - Extend `PublishToTargetAsync` switch — `RouteTarget.GenerateLabel` → `await queuePublisher.PublishGenerateLabelAsync(outboxEventId, ct)`.
  - Unrecognized event types still log `Critical` (existing behaviour preserved).

### Infrastructure layer

- **`Infra.Functions/Outbox/StorageQueueOutboxPublisher.cs`** (or wherever the impl lives — same file as `PublishSendEmailAsync` + `PublishGenerateInvoiceAsync`) — implement `PublishGenerateLabelAsync` using the new `OutboxQueues.GenerateLabelQueueName` config. Body = bare outbox id (T-0029 pattern).
- **EF seed migration `SeedOrderShippedCustomerEmailTemplate`** — adds:
  - 1 row to `email_templates` for `EmailTemplateType.OrderShippedCustomer` with `d-placeholder-order-shipped-customer` SendGrid template id (replaced post-deploy when the real SendGrid template is built).
  - 2 rows to `email_template_translations` (cs-CZ + en-US) with subject + body referencing the placeholder.

### Web.Maker host

- **`Web.Maker/Controllers/OrdersController.cs`** (or `MakerOrdersController.cs` — match existing naming):
  - Add `[HttpPost("{orderId}/ship")]` action `ShipAsync(string orderId, CancellationToken ct)`.
  - Route resolves to `POST /api/v1/maker/orders/{orderId}/ship`.
  - `[Authorize]` (maker scheme) — JWT audience enforced per host per CLAUDE.md security rules.
  - One-liner: `var result = await mediator.Send(new ShipOrder.Command(orderId), ct); return HandleResult(result);`.

### i18n

- **`frontend/src/lib/i18n/cs-CZ.ts`** — add 1 new Czech key for the new BusinessErrorMessage code:
  - `'shipping.methodNotEligible': 'Tato objednávka není zásilkovnová — použijte tlačítko Předat osobně.'`

### NSwag regen

The new `POST /api/v1/maker/orders/{orderId}/ship` endpoint is a contract change → **NSwag regen REQUIRED in the same PR**. Per pre-commit hook (T-0013), `frontend/src/lib/api-client/` cannot be edited manually — `npm run generate:api` produces the diff. The new `ShipOrder.Response` type (`OrderId`, `CarrierRef`, `TrackingUrl`) appears in the generated client.

### Tests

#### ShipOrderHandlerTests (NEW, ~10 tests)

`backend/src/Makables.Tests/AppServices/Features/Orders/ShipOrderHandlerTests.cs` — NSubstitute mocks (IOrderRepository, IShippingCarrierFactory, IShippingCarrier, IOutbox, IClock, IMakerSessionContext, IPublicAppUrls).

1. **Happy_path_Zasilkovna_transitions_to_Shipped_stamps_tracking_url_enqueues_2_outbox_events** — full happy path. Carrier returns `Shipment("PKT-9876543210", "https://tracking.packeta.com/ZPKT-9876543210")`. Assert: `order.State == Shipped`, `order.ShippingCarrierRef == "PKT-9876543210"`, `order.ShippingCarrierTrackingUrl == "https://tracking.packeta.com/ZPKT-9876543210"`, `order.AutoDeliverAt == clock.UtcNow.AddDays(7)`, IOutbox.Enqueue called exactly 2x with `(order.Id, OrderShippedCustomerEmail, …)` AND `(order.Id, ShippingGenerateLabel, …)`. Response carries OrderId + CarrierRef + TrackingUrl.
2. **Order_not_owned_by_maker_returns_NotFound** — `GetByIdForMakerAsync` returns null. Assert: NotFound result, OUtbox not called, carrier factory not called.
3. **Wrong_shipping_method_PersonalPickup_returns_ShippingMethodNotEligible** — Order's `ShippingMethod == PersonalPickup`. Assert: `Validation(ShippingMethodNotEligible)`, carrier factory NOT called, outbox NOT called.
4. **Invalid_state_non_Accepted_returns_OrderInvalidTransition** — Order is in `Paid` (or `Shipped`, or `Delivered`). Carrier IS called (handler reaches step 5 happy path), but `order.Ship` returns InvalidTransition. Assert: failure surfaces, outbox NOT called. (Important: this asserts the handler does NOT enqueue outbox events when Ship fails.)
5. **Carrier_5xx_returns_Transient_ShippingCarrierUnavailable** — `IShippingCarrier.CreateShipmentAsync` returns `BusinessResult.Failure(Error.Transient(ShippingCarrierUnavailable))`. Assert: Transient surface, order NOT mutated (state still Accepted), outbox NOT called.
6. **Carrier_AddressIdNotFound_returns_Permanent** — `CreateShipmentAsync` returns `Permanent(ShippingCarrierAddressIdNotFound)`. Assert: Permanent surface, no mutation, no outbox.
7. **Atomic_2_event_outbox_enqueue_order_and_aggregate_id** — assert exact order of `IOutbox.Enqueue` calls: customer email FIRST, label SECOND. Both share `aggregateId = order.Id`. Verified via NSubstitute `Received.InOrder`.
8. **OrderShippedCustomerEmailPayload_field_correctness** — capture the JSON payload via NSubstitute `Arg.Do<string>`; deserialize; assert: `OrderId == order.Id`, `OrderNumber == order.OrderNumber`, `Email == order.ContactEmail`, `ContactName == order.ContactName`, `LanguageCode == order.LanguageCode`, `ActionUrl == $"{publicAppUrls.WebBaseUrl}/objednavka/{order.Id}"`, `TrackingUrl == shipment.TrackingUrl`.
9. **GenerateLabelOutboxPayload_field_correctness** — capture the second payload; deserialize; assert `OrderId == order.Id`.
10. **Tracking_url_stamped_on_order_via_Ship_signature** — after handler completes, `order.ShippingCarrierTrackingUrl` is the value the carrier returned. Asserts the new 4th parameter on `Order.Ship` flows through.

#### Order.Ship signature tests (NEW, ~3 tests)

`backend/src/Makables.Tests/Domain/Orders/OrderShipTrackingUrlTests.cs` — pure domain tests. **TDD-first commit** per T-0067+ rule.

1. **Ship_with_trackingUrl_sets_ShippingCarrierTrackingUrl** — order in Accepted, Ship(clock, "PKT-1", 7, "https://tracking.example/Z1") → success, `ShippingCarrierTrackingUrl == "https://tracking.example/Z1"`.
2. **Ship_with_null_trackingUrl_leaves_ShippingCarrierTrackingUrl_null** — order in Accepted, Ship(clock, null, 7, null) → success, `ShippingCarrierTrackingUrl is null`.
3. **Ship_setOnce_guard_on_ShippingCarrierTrackingUrl_rejects_overwrite** — manually set `ShippingCarrierTrackingUrl` (reflection or test seam if exists), then call Ship with a different non-null trackingUrl → `BusinessResult.Failure(Conflict, OrderInvalidTransition)`. Field-only check: any prior non-null value is sticky (mirrors existing ShippingCarrierRef set-once at Order.cs:576-578).

#### ShipOrderIntegrationTests (NEW, ~2 tests)

`backend/src/Makables.IntegrationTests/Orders/ShipOrderIntegrationTests.cs` — Testcontainers postgres + faked `IShippingCarrier` + faked `IOutbox` (or real outbox table assertion).

1. **POST_ship_happy_path_transitions_order_and_writes_2_outbox_rows** — seed an Accepted Zásilkovna order, POST `/api/v1/maker/orders/{id}/ship`, assert 200 + Response body + DB state: order row has `state == Shipped`, `shipping_carrier_ref == fake-ref`, `shipping_carrier_tracking_url == fake-url`, `auto_deliver_at` ~7 days out, AND `outbox_events` has exactly 2 rows with `aggregate_id == order.Id` and event types `order.shipped.customerEmail` + `shipping.generate.label`.
2. **POST_ship_wrong_shipping_method_returns_400_with_method_not_eligible** — seed an Accepted PersonalPickup order, POST same endpoint, assert 400 with error code `shipping.methodNotEligible`. DB unchanged.

#### OutboxDispatcherTests extension (~2 new tests)

`backend/src/Makables.Tests/AppServices/Features/Outbox/OutboxDispatcherTests.cs` — extend with:

1. **shipping_generate_label_event_routed_to_PublishGenerateLabelAsync** — enqueue an outbox event with `event_type = "shipping.generate.label"`, run dispatcher, assert `IOutboxQueuePublisher.PublishGenerateLabelAsync(outboxEventId, ct)` called once AND `PublishSendEmailAsync` + `PublishGenerateInvoiceAsync` NOT called.
2. **mixed_batch_email_invoice_label_routes_to_3_publishers** — enqueue one `order.shipped.customerEmail` + one `invoice.generate` + one `shipping.generate.label`, run dispatcher, assert each publisher called exactly once with the matching outbox id.

#### EmailSendServiceTests extension (~3 new tests)

`backend/src/Makables.Tests/AppServices/Features/Email/EmailSendServiceTests.cs` — extend with:

1. **OrderShippedCustomerEmail_branch_loads_template_and_sends** — pass `OrderShippedCustomerEmailPayload` JSON + `event_type = order.shipped.customerEmail`. Assert template lookup keyed by `EmailTemplateType.OrderShippedCustomer` + payload.LanguageCode, and SendGrid called with substitutions matching payload fields.
2. **OrderShippedCustomerEmail_with_TrackingUrl_passes_tracking_url_substitution** — payload has non-null TrackingUrl, assert SendGrid substitutions include `tracking_url == payload.TrackingUrl`.
3. **OrderShippedCustomerEmail_with_null_TrackingUrl_passes_empty_tracking_url_substitution** — payload has null TrackingUrl (T-0073 personal-pickup case), assert SendGrid substitutions include `tracking_url == ""` (or omits the key — implementer judges; template conditionally renders).

### Docs

- **`docs/architecture/roles/order.md`** — note the new state transition: "Accepted → Shipped via `ShipOrder.Command` (Zásilkovna) emits 2 outbox events (`order.shipped.customerEmail` + `shipping.generate.label`) atomically per ADR 0014." Reference T-0072 in the Lifecycle table row.
- **`docs/tickets/INDEX.md`** — flip T-0072 row to `**done**` after PR merge (PM does this).

## Alternatives Considered

- **Option A — Sequential outbox (email-first, label-after-email-delivery).** *Rejected per A.1* — couples label-generation to email-send success. If SendGrid is down but Packeta is up, the maker can't get their label. Independent queues + atomic enqueue at producer-side is the ADR 0020 pattern.
- **Option B — Priority queue (label first, email after).** *Rejected per A.1* — adds priority logic to the dispatcher; loses queue independence; we'd be re-implementing what the queue-per-event-class split already gives us.
- **Option C — New `Order.ShipWithTracking(...)` overload.** *Rejected per C.1 (Order.Ship signature)* — signature swell. Optional 4th parameter is the minimal change and keeps all existing call sites compiling unchanged.
- **Option D — Pre-bake carrier ref into GenerateLabelOutboxPayload.** *Rejected per C.2* — premature; payload would mutate if Ship() ever restamps the ref (it won't under current state graph, but the cleaner pattern is "fetch fresh from DB at handler time" — mirrors T-0069's `IssueInvoice.Command(OrderId)` shape).
- **Option E — Park-and-retry on carrier Transient failures in ShipOrder.Handler.** *Rejected per C.3* — adds command-level retry policy; out of scope. ShipOrder is synchronous; the maker dashboard re-tries on the next click. Outbox-style automatic retry is for events that have already been enqueued, not for the synchronous command itself.
- **Option F — Attach the label PDF to the customer shipped email.** *Rejected per C.last* — the label is a maker artifact (the carrier picks it up at hand-off). Customers get a tracking URL instead. No need to ship a multi-MB attachment to every customer.
- **Option G — Reuse the existing `send-email` queue for `shipping.generate.label`.** *Rejected per C.7 + ADR 0020* — queue-per-event-class split per ADR 0020 lets us scale the GenerateLabel Function independently (e.g., higher concurrency limit because Packeta label-PDF download is slower than SendGrid send).
- **Option H — Separate email events for Zásilkovna vs PersonalPickup.** *Rejected per C.6* — unified event + template + payload + enum value. Template conditionally renders the tracking_url line. Halves the email-template surface area + halves the EmailSendService switch surface.

## Out of scope

- **PersonalPickup ShipOrder variant** — T-0073 owns the separate command (no carrier call, no label event, null tracking URL, same email event + template).
- **GenerateLabel Function (queue trigger + blob upload)** — T-0074 owns the `IShippingCarrier.GetLabelPdfAsync` call + blob upload to `invoices/{cc}/orders/{orderId}/label.pdf`.
- **Label download endpoint** for makers — T-0075 (returns a SAS URL or proxies the stream).
- **Shipment status sync timer** — T-0078 (polls Packeta status; transitions Shipped → Delivered).
- **Customer-confirm command** — T-0076 (manual customer delivery confirmation before AutoDeliverAt fires).
- **Auto-deliver job** — T-0077 (timer that transitions Shipped → Delivered after AutoDeliverAt).
- **Frontend "Odeslat" button + maker order-detail page wiring** — separate frontend ticket (FE-side).
- **Per-maker Packeta accounts** — Phase 5+.
- **Product.Weight / Order.Weight field** — at MVP, weight is platform-default per T-0070 risk register.
- **Re-ship / cancel-shipment / void-label flows** — out of MVP.
- **SendGrid template content build** — the seed migration uses `d-placeholder-order-shipped-customer`. The real SendGrid template is built post-deploy by the user; placeholder rows are sufficient for CI green.

## Acceptance criteria

- **AC-1** Given an Accepted Zásilkovna order owned by the requesting maker, when `POST /api/v1/maker/orders/{orderId}/ship` is called, then it returns `200 OK` with body `{ OrderId, CarrierRef, TrackingUrl }`, AND the order row has `state = Shipped`, `shipping_carrier_ref = <carrier-returned-ref>`, `shipping_carrier_tracking_url = <carrier-returned-url>`, `shipped_at = clock.UtcNow`, `auto_deliver_at = clock.UtcNow + 7 days`.
- **AC-2** Given the same happy path, when the handler returns, then exactly **2 rows** exist in `outbox_events` with `aggregate_id = order.Id`: event types `order.shipped.customerEmail` (first) and `shipping.generate.label` (second). The customer payload deserializes to `OrderShippedCustomerEmailPayload` with all fields populated (OrderId, OrderNumber, Email, ContactName, LanguageCode, ActionUrl, TrackingUrl). The label payload deserializes to `GenerateLabelOutboxPayload(OrderId)`.
- **AC-3** Given a Zásilkovna order NOT owned by the requesting maker, when the endpoint is called, then it returns `404` with error code `order.notFound`. No outbox rows, no carrier call.
- **AC-4** Given an Accepted PersonalPickup order, when the endpoint is called, then it returns `400` with error code `shipping.methodNotEligible`. No outbox rows, no carrier call. (The maker must use the T-0073 personal-pickup endpoint instead.)
- **AC-5** Given an order in a state other than Accepted (e.g., Paid, Shipped, Delivered, Cancelled), when the endpoint is called, then it returns `409` with error code `order.invalidTransition`. The carrier IS called (handler reaches step 5 happy path before state mutation fails in step 6) — **but no outbox rows are written** (UoW rolls back the whole transaction on Ship failure).
- **AC-6** Given Packeta returns HTTP 503 to `CreateShipmentAsync`, when the endpoint is called, then it returns `503` (or whatever `Error.Transient` maps to per `MakablesApiController`) with error code `shipping.carrierUnavailable`. The order row is unchanged (state still Accepted, no carrier ref, no tracking URL). No outbox rows.
- **AC-7** Given the `Order.Ship` signature, when callers omit the 4th parameter (e.g., T-0073 PersonalPickup tests), then it compiles and behaves identically to T-0070 (no `ShippingCarrierTrackingUrl` mutation).
- **AC-8** Given `Order.Ship` is called twice on the same entity with different non-null `trackingUrl` values (defensive — state graph blocks this normally), the second call returns `BusinessResult.Failure(Conflict, OrderInvalidTransition)` — set-once guard fires. Field-only check: any prior non-null `ShippingCarrierTrackingUrl` is sticky.
- **AC-9** Given the `OutboxEventTypes` static class, when read, then `OrderShippedCustomerEmail = "order.shipped.customerEmail"` and `ShippingGenerateLabel = "shipping.generate.label"` exist. `IsEmailSend("order.shipped.customerEmail")` returns true. `IsGenerateLabel("shipping.generate.label")` returns true; `IsGenerateLabel` returns false for every other event type (including `order.shipped.customerEmail` + `invoice.generate` + auth events).
- **AC-10** Given a `shipping.generate.label` outbox event in the queue, when `OutboxDispatcher.DispatchDueAsync` runs, then `IOutboxQueuePublisher.PublishGenerateLabelAsync(outboxEventId, ct)` is called once AND `PublishSendEmailAsync` + `PublishGenerateInvoiceAsync` are NOT called. The bare outbox id is the queue message body.
- **AC-11** Given an `order.shipped.customerEmail` outbox event being drained by `IEmailSendService.SendAsync`, when the switch routes, then the new `SendOrderShippedCustomerEmailAsync` helper runs. SendGrid is called with template id matching `EmailTemplateType.OrderShippedCustomer` for the payload's `LanguageCode`, and dynamic-template substitutions include `order_number`, `contact_name`, `action_url`, `tracking_url` (verbatim from payload). No PDF attachment.
- **AC-12** Given `frontend/src/lib/i18n/cs-CZ.ts`, when loaded, then key `shipping.methodNotEligible` exists with the Czech translation. Build clean. Unit tests: baseline (after T-0070 + T-0071 merge) + ~18 new (10 ShipOrderHandlerTests + 3 OrderShipTrackingUrlTests + 2 OutboxDispatcherTests + 3 EmailSendServiceTests). Integration tests: baseline + 2 new (ShipOrderIntegrationTests).
- **AC-13** Consistency script exit 0 (no new T1–T7 violations vs the bundle's running baseline). NSwag regen committed in the same PR; `frontend/src/lib/api-client/` types the new `/maker/orders/{id}/ship` endpoint with `ShipOrderResponse { orderId, carrierRef, trackingUrl }`. No manual edits to the api-client folder (pre-commit hook enforces).

## Technical notes

### Why the customer email + label generate are atomic peers (not parent + child)

Both events belong to the same business fact: "the order has shipped." Either both happen or neither happens. The UoW pipeline commits the Order state mutation + 2 outbox rows in one Postgres transaction per ADR 0014. If the customer ever sees a "your order has shipped" email, the maker will (eventually) be able to download the label — both events are durable in the outbox. Conversely, if the carrier call fails, neither event is enqueued and the order stays Accepted. This is the same pattern as T-0067's 3-event MarkOrderPaid emission.

### Why the email handler does NOT block on label generation

The label PDF is for the maker's carrier hand-off. The customer email contains the tracking URL (pre-baked into the payload — single source of truth on the Order row). If we attached the label PDF to the customer email, we'd couple email-send latency to Packeta's label-PDF download latency + we'd be sending the wrong artifact to the wrong audience. The lookup-at-send-time pattern from T-0069 (where the order-paid email DOES attach the invoice PDF) does not apply here — there's nothing for the customer email to wait for.

### Why a new queue (generate-label) instead of reusing send-email

ADR 0020's queue-per-event-class principle: each event class gets its own queue + Function so concurrency limits + retry policies tune independently. The send-email queue handles SendGrid sends (~100ms per call). The generate-label queue will handle Packeta label-PDF downloads (~500ms-2s per call) + blob uploads. Mixing them would force shared concurrency. T-0069 established the precedent with `generate-invoice`; T-0072 extends it.

### Why the carrier ref is fetched from DB in the GenerateLabel handler (not pre-baked into the payload)

The payload `GenerateLabelOutboxPayload(OrderId)` is intentionally minimal. T-0074's handler queries the Order via `IOrderRepository.GetByIdUnscopedAsync(OrderId, ct)` and reads `ShippingCarrierRef` + `CountryCode` fresh. Rationale: (a) if the state graph ever lets Ship() restamp the ref (it won't under current rules), the payload stays in sync; (b) it mirrors T-0069's `IssueInvoice.Command(OrderId)` shape — the queue trigger Function is a thin dispatcher that hands the OrderId to MediatR.

### Why `Order.Ship` gets the 4th parameter (not a new method)

The T-0070 `Order.Ship(IClock, string?, int)` signature already handles the carrier-ref + auto-deliver-window concerns. Adding a 4th optional `string? trackingUrl = null` parameter is the minimal change: (a) all existing callers (test fixtures, T-0071 hypothetically) keep compiling unchanged; (b) the set-once guard pattern is shared with `ShippingCarrierRef` (Order.cs:576-578); (c) the personal-pickup path (T-0073) calls the same method with null and gets identical semantics. A new `Order.RecordShipmentReady(...)` would split the state mutation across two methods that always must be called together.

## Files touched (expected)

### New
- `backend/src/Makables.Core.AppServices/Features/Orders/ShipOrder.cs`
- `backend/src/Makables.Core.Domain/Outbox/OrderShippedCustomerEmailPayload.cs`
- `backend/src/Makables.Core.Domain/Outbox/GenerateLabelOutboxPayload.cs`
- `backend/src/Makables.Infra.Database/Migrations/<timestamp>_SeedOrderShippedCustomerEmailTemplate.cs` (+ Designer)
- `backend/src/Makables.Tests/AppServices/Features/Orders/ShipOrderHandlerTests.cs`
- `backend/src/Makables.Tests/Domain/Orders/OrderShipTrackingUrlTests.cs`
- `backend/src/Makables.IntegrationTests/Orders/ShipOrderIntegrationTests.cs`

### Modified
- `backend/src/Makables.Core.Domain/Orders/Order.cs` — extend `Ship(...)` with 4th optional `string? trackingUrl = null` parameter; set-once guard + length validation on `ShippingCarrierTrackingUrl`; XML doc updated.
- `backend/src/Makables.Core.Domain/Outbox/OutboxEventTypes.cs` — add `OrderShippedCustomerEmail` + `ShippingGenerateLabel` constants; extend `IsEmailSend`; add new `IsGenerateLabel` classifier method.
- `backend/src/Makables.Core.Domain/Email/EmailTemplateType.cs` — add `OrderShippedCustomer = 6`.
- `backend/src/Makables.Core.Domain/Common/BusinessErrorMessage.cs` — add `ShippingMethodNotEligible`.
- `backend/src/Makables.Core.Domain/Outbox/IOutboxQueuePublisher.cs` — add `PublishGenerateLabelAsync`.
- `backend/src/Makables.Core.AppServices/Common/OutboxQueuesOptions.cs` + validator — add `GenerateLabelQueueName` (default `"generate-label"`).
- `backend/src/Makables.Core.AppServices/Features/Outbox/OutboxDispatcher.cs` — new `RouteTarget.GenerateLabel`; classifier branch; PublishToTargetAsync branch.
- `backend/src/Makables.Core.AppServices/Features/Email/IEmailSendService.cs` + `EmailSendService.cs` — new `OrderShippedCustomerEmail` case; `SendOrderShippedCustomerEmailAsync` helper.
- `backend/src/Makables.Infra.Functions/Outbox/StorageQueueOutboxPublisher.cs` — implement `PublishGenerateLabelAsync`.
- `backend/src/Makables.Web.Maker/Controllers/OrdersController.cs` (or matching file) — new `POST {orderId}/ship` action.
- `backend/src/Makables.Tests/AppServices/Features/Outbox/OutboxDispatcherTests.cs` — 2 new tests.
- `backend/src/Makables.Tests/AppServices/Features/Email/EmailSendServiceTests.cs` — 3 new tests.
- `frontend/src/lib/i18n/cs-CZ.ts` — 1 new key (`shipping.methodNotEligible`).
- `frontend/src/lib/api-client/*` — NSwag-regenerated; committed in the same PR.
- `docs/architecture/roles/order.md` — note the Accepted → Shipped transition + 2-event atomic outbox.

## Test plan reference

Inline above (see Scope > Tests). No separate `docs/test-plans/T-0072.md`.

## Status log

- 2026-06-08 `draft` by PM. Created as part of the shipping-pipeline bundle (T-0070 + T-0071 + T-0072 + T-0073 + T-0074 + T-0075). T-0070 + T-0071 already groomed. Slice scope: ShipOrder.Command (Zásilkovna path) + atomic 2-event outbox emission + new `shipping.generate.label` event type + queue split + dispatcher routing extension + EmailSendService branch + unified `OrderShippedCustomer` template/event (reused by T-0073).
- 2026-06-08 `draft → ready` by PM. User answered 1 blocking AskUserQuestion item per `/feature` workflow step 3 (**A.1**: atomic 2-event outbox emission under one UoW — both `order.shipped.customerEmail` + `shipping.generate.label` enqueued in the same UoW transaction as `Order.Ship`; mirrors T-0067 MarkOrderPaid's 3-event atomic pattern; rejected sequential + priority queue). 9 PM-absorbed decisions captured in `## Locked design decisions §C` (Order.Ship 4th-param extension; GenerateLabelOutboxPayload shape; carrier Transient surface; parallel email timing; Zásilkovna-only scope; unified shipped event + template; `generate-label` queue split; OutboxDispatcher routing; no PDF attachment on customer email). 5 ADR-locked items extracted in §B (ADR 0014 UoW pipeline; ADR 0017 carrier seam + 7-day window; ADR 0019 per-event-type EmailSendService switch; ADR 0020 outbox queue-per-event-class; one-file feature shape). No manual_steps. **Ready for dotnet-backend.** The implementer processes T-0070 → T-0071 → T-0072 → T-0073 → T-0074 → T-0075 sequentially in the same branch; all six ship in one PR.