---
id: T-0073
title: HandOverOrder command (personal-pickup path) — Accepted → Shipped, no Packeta call
status: ready
size: S
owner: dotnet-backend
created: 2026-06-08
updated: 2026-06-08
depends_on: [T-0071]
blocks: [T-0076]
user_stories: [US-maker-0008]
adrs: [0014, 0017, 0019, 0020]
phase: 4
manual_steps: []
security_touching: false
layers: [domain, appservices, web-maker, frontend-i18n]
---

# T-0073 — HandOverOrder command (personal-pickup path) — Accepted → Shipped, no Packeta call

## Context

T-0073 ships the **personal-pickup symmetric counterpart** to T-0072's Zásilkovna `ShipOrder` command. When a maker meets a customer in person (workshop pickup, market stall, café handover) and physically hands over the finished item, they click "Předáno zákazníkovi" on the maker dashboard. The order transitions from `Accepted → Shipped` and the customer receives the unified shipped-confirmation email. The 7-day auto-deliver window starts ticking (per T-0070 locked decision A.4) so completion isn't gated on customer action.

The slice is the **second of the two Accepted → Shipped writers** in the shipping bundle:
- T-0072 ships the Zásilkovna path: `ShipOrder.Command` → `IShippingCarrierFactory` → `IShippingCarrier.CreateShipmentAsync` → Packeta → `Order.Ship(shippingCarrierRef, autoDeliverWindowDays: 7, trackingUrl)` → unified outbox event `order.shipped.customerEmail` with the real `TrackingUrl`.
- T-0073 ships the personal-pickup path: `HandOverOrder.Command` → **NO factory call**, **NO Packeta call**, **NO label** → `Order.Ship(shippingCarrierRef: null, autoDeliverWindowDays: 7, trackingUrl: null)` → unified outbox event `order.shipped.customerEmail` with `TrackingUrl = null` (conditional template branch hides the row).

This is **the cheap branch**: no integrations, no I/O beyond the DB write + outbox row. The reason it ships as a separate ticket (not folded into T-0072) is the **handler's branching policy**: rather than letting one `ShipOrder.Command` switch on `order.ShippingMethod` and call the carrier conditionally, the codebase models the two methods as distinct commands with distinct endpoints. This mirrors the maker dashboard UX (one button per shipping method) and keeps each handler small + testable. Both handlers share the post-`Order.Ship` outbox-emission code path (single event type, single payload record, single template) per the user-locked Decision A.1 below.

T-0073 has hard dep on **T-0071** because T-0071 ships the `Order.Ship` state-transition method on the Order aggregate (`Accepted → Shipped` plus the `ShippingCarrierRef` / `ShippingCarrierTrackingUrl` / `AutoDeliverAt` set-once writes). T-0073 calls that method with all-nulls-for-carrier args. T-0073 has **no carrier-side concerns** (no Packeta, no label, no error classification mapping) — those are entirely T-0072 + T-0074's territory.

The slice is **NOT security-touching**: no new secrets, no new public endpoints (the new POST is behind the existing maker JWT audience), no new auth surface. Per-handler ownership scoping (`GetByIdForMakerAsync`) is the same pattern T-0067 + T-0072 use.

## Locked design decisions

Captured per `docs/process/deliberation.md`. The user answered 2 blocking AskUserQuestion items before this ticket transitioned to ready. The remaining decisions are PM-absorbed (mechanical extensions of T-0070 + T-0072 locks) or ADR-locked (UoW pipeline, outbox conventions, no-SaveChangesAsync-in-handler).

### A. User-locked at /feature step 3 (non-negotiable)

1. **Unified customer-shipped email with T-0072.** Single `order.shipped.customerEmail` outbox event + `EmailTemplateType.OrderShippedCustomer` template shared with T-0072. T-0073 passes `TrackingUrl = null` in the payload; SendGrid Dynamic Template conditionally renders the tracking-URL row only when present. Czech wording stays generic ("vaše objednávka je na cestě k vám"). Single SendGrid template ID to maintain. **Rejected:** bifurcated (separate event + template + per-method tone); more copy + translations to maintain for negligible UX benefit.

2. **Self-attested handover; no proof captured.** Maker clicks "Předáno zákazníkovi" on dashboard → `HandOverOrder.Command` fires → state transition only. No photo / signature / recipient metadata. UX-friendly for small-practitioner makers. Customer dispute path: admin reviews order history + message thread (T-0079 messages, T-0118 admin monitoring). **Rejected:** mandatory photo (UX regression, OrderAttachment write per ship action, camera permission); optional photo (middle ground — adds test surface for marginal benefit).

### B. ADR-locked (per ADR 0014, ADR 0017, ADR 0019, ADR 0020 — no relitigation)

- **One-file feature shape** (ADR 0014). The feature ships as a single file `Core.AppServices/Features/Orders/HandOverOrder.cs` containing nested `Command` / `Validator` / `Response` / `Handler`. Mirror of T-0067 `MarkOrderPaid.cs` shape.
- **UoW pipeline commits** (ADR 0014). `UnitOfWorkPipelineBehavior` commits the state transition + outbox row in a single Postgres transaction. **The handler never calls `SaveChangesAsync()`**. If anything in the pipeline fails, both the order mutation and the outbox row roll back; the maker sees a failure and the customer never gets a misleading "shipped" email.
- **Scoped repositories** (ADR 0013). `IOrderRepository` is scoped + injected; `GetByIdForMakerAsync(orderId, makerId, ct)` enforces ownership at the repository layer (already shipped per T-0067 precedent).
- **Outbox event naming convention** (ADR 0020 / T-0067). `<domain>.<action>.<modality>`. The shared event type is `order.shipped.customerEmail` (defined by T-0072; T-0073 reuses).
- **Per-event-type switch in `IEmailSendService`** (ADR 0019 / T-0067 Q3). T-0073 does NOT add a new switch arm — it relies on T-0072's arm. T-0073's only `EmailSendService` test is a *conditional-rendering pin* asserting the `TrackingUrl == null` branch produces a SendGrid `Personalization` without the `TrackingUrl` substitution key set (or with empty string, depending on SendGrid template idioms — implementer judges the cleanest approach that matches the existing customer-email tests in T-0067).
- **Error classification** (ADR 0017 §A.14). N/A for T-0073 — no external call. The only handler failures are domain-side (`ShippingMethodNotEligible`, `OrderInvalidTransition`, `OrderNotFound`), all Permanent.
- **TDD-with-commit-order hard rule** (T-0067+ policy). The Domain layer's `Order.Ship` is pure logic (it ships in T-0071, already test-first). The Handler is orchestration over mocked seams — unit tests still ship in the same commit, but they're not the "test-first commit" gate. Integration test pins the wiring end-to-end.

### C. PM-absorbed (no user input needed)

- **Command name:** `HandOverOrder` (semantic clarity for the personal-pickup path) — explicitly NOT `ShipOrderPersonalPickup` (avoids "Ship" verb which doesn't apply to in-person handover).
- **Endpoint:** `POST /api/v1/maker/orders/{orderId}/handover`.
- **Order.Ship signature:** call with `shippingCarrierRef: null`, `autoDeliverWindowDays: 7`, `trackingUrl: null` per T-0070 locked decision 4. The 7-day window is identical to Zásilkovna.
- **Outbox event:** only `order.shipped.customerEmail` (single). NO `shipping.generate.label` event for personal-pickup (locked per T-0070 — personal-pickup has no label, no Packeta call, no carrier ref).
- **OrderShippedCustomerEmailPayload:** same record as T-0072 — TrackingUrl field is null for the T-0073 caller. Template renders conditional block.
- **ShippingMethod assertion:** Handler asserts `order.ShippingMethod == PersonalPickup` else BusinessResult.Failure(Validation, ShippingMethodNotEligible) — symmetric to T-0072's assertion.
- **Error reuse:** `ShippingMethodNotEligible` BusinessErrorMessage code shipped by T-0072 covers this assertion too; no new code needed in T-0073.
- **Carrier-side error handling:** N/A (no Packeta call).
- **NSwag regen:** ships in the same bundle as T-0072's regen (single regeneration covers both new endpoints).

## Scope

### Domain layer

**No changes.** `Order.Ship(IClock clock, string? shippingCarrierRef, int autoDeliverWindowDays, string? trackingUrl)` ships in T-0071. T-0073 only calls it with `(clock, null, 7, null)`.

T-0073 also relies on:
- `BusinessErrorMessage.ShippingMethodNotEligible` — shipped by T-0072.
- `OutboxEventTypes.OrderShippedCustomerEmail = "order.shipped.customerEmail"` — shipped by T-0072.
- `OrderShippedCustomerEmailPayload` record — shipped by T-0072, with a nullable `TrackingUrl` field.
- `EmailTemplateType.OrderShippedCustomer` — shipped by T-0072.

If the implementer discovers any of these are NOT yet on the branch when T-0073 lands (because the bundle processes tickets sequentially and T-0072 may have skipped one of the four reuse points), the implementer treats it as a missing dep and adds the smallest possible code to satisfy T-0073's contract while flagging the deviation in the PR description.

### AppServices layer

- **`Core.AppServices/Features/Orders/HandOverOrder.cs`** — new one-file feature with nested types:
  - `public sealed record Command(Guid OrderId) : IRequest<BusinessResult<Response>>`.
  - `public sealed record Response(Guid OrderId, string OrderNumber, DateTimeOffset ShippedAt, DateTimeOffset AutoDeliverAt)` — mirror of T-0072's Response shape minus `TrackingUrl`. Implementer chooses Response shape that fits the maker dashboard refresh contract.
  - `public sealed class Validator : AbstractValidator<Command>` — `RuleFor(c => c.OrderId).NotEmpty()`.
  - `public sealed class Handler(IUserSessionProvider session, IOrderRepository orders, IClock clock, IOutbox outbox, IPublicAppUrls publicAppUrls, ILanguageResolver languageResolver) : IRequestHandler<Command, BusinessResult<Response>>`.

  Handler steps (in order):
  1. **Resolve maker session.** `var makerId = session.RequireMakerId();` — same pattern as T-0072 + existing maker handlers. Failure path: pipeline behaviour surfaces 401.
  2. **Load order with ownership scope.** `var order = await orders.GetByIdForMakerAsync(command.OrderId, makerId, ct);` — returns null if not found OR not owned by this maker (no leak between makers). Null → `BusinessResult.Failure(Error.Permanent(BusinessErrorMessage.OrderNotFound))`.
  3. **Assert eligible shipping method.** `if (order.ShippingMethod != ShippingMethod.PersonalPickup) return BusinessResult.Failure(Error.Validation(BusinessErrorMessage.ShippingMethodNotEligible));` — symmetric to T-0072's `if (order.ShippingMethod != ShippingMethod.Zasilkovna)` assertion. Prevents a maker calling the wrong endpoint on the wrong order.
  4. **Call `Order.Ship`.** `var shipResult = order.Ship(clock, shippingCarrierRef: null, autoDeliverWindowDays: 7, trackingUrl: null);` — `Order.Ship` returns `BusinessResult` so invalid state transitions (`Accepted` is the only legal source state) surface as `OrderInvalidTransition`. If failure → return the failure verbatim (handler propagation).
  5. **Build customer-shipped payload.** `var actionUrl = $"{publicAppUrls.WebBaseUrl}/objednavka/{order.Id}";` (matches T-0067 pre-bake pattern). `var languageCode = await languageResolver.ResolveForCustomerAsync(order.CustomerId, ct);` (or whatever name the existing pattern uses — see T-0067 for precedent). Construct:
     ```csharp
     var payload = new OrderShippedCustomerEmailPayload(
         OrderId: order.Id.ToString(),
         OrderNumber: order.OrderNumber,
         Email: order.CustomerEmail,
         ContactName: order.ContactName,
         LanguageCode: languageCode,
         ActionUrl: actionUrl,
         TrackingUrl: null);   // <-- the T-0073 distinguishing field
     ```
  6. **Enqueue outbox event.** `await outbox.EnqueueAsync(aggregateId: order.Id, eventType: OutboxEventTypes.OrderShippedCustomerEmail, payloadJson: JsonSerializer.Serialize(payload), ct);` — single event (vs T-0072's 2-event enqueue: customer email + `shipping.generate.label`). NO label event for personal-pickup per the T-0070 + T-0073 locked decisions.
  7. **Return Response.** `return BusinessResult.Success(new Response(order.Id, order.OrderNumber, order.ShippedAt!.Value, order.AutoDeliverAt!.Value));`. The UoW pipeline commits the order mutation + the outbox row atomically.

### Infrastructure layer

**No changes.** No new client adapters. No new repositories. No new migrations.

### Database layer

**No changes.** `Order.Ship` writes are covered by columns shipped in T-0070 (`shipping_carrier_tracking_url`) + T-0071 (`auto_deliver_at`, `shipped_at`, `shipping_carrier_ref` columns if they don't already exist from T-0060). T-0073 contributes zero schema diff.

### Web.Maker host

- **`Web.Maker/Controllers/OrdersController.cs`** — add a new action method (the controller already exists per T-0067 precedent). If T-0072 hasn't added a controller-level `[Route("api/v{version:apiVersion}/maker/orders")]` segment, the new action's route is fully qualified.
  ```csharp
  [HttpPost("{orderId:guid}/handover")]
  [Authorize] // maker audience policy already enforced at controller level
  public async Task<IActionResult> HandOver(
      [FromRoute] Guid orderId,
      CancellationToken ct)
  {
      var result = await Mediator.Send(new HandOverOrder.Command(orderId), ct);
      return HandleResult(result);
  }
  ```
  One-liner controller per the ADR-locked discipline. `HandleResult` is the existing `MakablesApiController` base mapping (BusinessResult → HTTP status + body).

- **JWT audience enforcement** — already in place at the Web.Maker host level (per `Program.cs` + `AddMakablesAuthentication`). A customer JWT replayed against this endpoint hits the 401 ceiling before reaching the handler. No T-0073-specific security work.

### Config / DI

**No changes.** Handler picks up existing scoped registrations (`IOrderRepository`, `IOutbox`, `IClock`, `IPublicAppUrls`, `ILanguageResolver`, `IUserSessionProvider`). MediatR auto-discovers the `Handler` + `Validator` per existing assembly scan.

### i18n

- **`frontend/src/lib/i18n/cs-CZ.ts`** — optional 1 new key for the maker-dashboard success state (frontend-i18n layer; the actual frontend wiring is out of scope, but the key is reserved here to match T-0072's pattern):
  - `'orders.handover.success': 'Předání zákazníkovi potvrzeno. Objednávka byla označena jako odeslaná.'`

  No new error-code keys: `ShippingMethodNotEligible`, `OrderNotFound`, `OrderInvalidTransition` all already have Czech translations (shipped by T-0072 + T-0067 respectively).

### NSwag regen

The new `POST /api/v1/maker/orders/{orderId}/handover` endpoint is a public contract change → **NSwag regen REQUIRED in the same PR**. Per PM-absorbed decision C, the bundle ships T-0072's regen + T-0073's regen as a single `npm run generate:api` run (one diff for both new endpoints).

### Manual deployment steps

**None.** No new secrets, no new config keys, no new infrastructure resources, no new Function deployments. T-0073 ships behind the existing maker authentication + the existing infrastructure.

### Tests

#### Unit — `Makables.Tests/AppServices/Features/Orders/HandOverOrderHandlerTests.cs` (NEW, ~6 tests)

1. **`HandOver_happy_path_with_PersonalPickup_transitions_Accepted_to_Shipped`** — Arrange order in `Accepted` state with `ShippingMethod = PersonalPickup` owned by maker `M1`. `IUserSessionProvider` returns `M1`. `IOrderRepository.GetByIdForMakerAsync` returns the order. Act: send `Command(orderId)`. Assert: result is success; `order.State == Shipped`; `order.ShippingCarrierRef == null`; `order.ShippingCarrierTrackingUrl == null`; `order.AutoDeliverAt == clock.UtcNow + 7d`.
2. **`HandOver_with_order_owned_by_different_maker_returns_OrderNotFound`** — `GetByIdForMakerAsync(orderId, M2, ct)` returns null. Assert: `BusinessResult.Failure(Error.Permanent(OrderNotFound))`. No outbox enqueue.
3. **`HandOver_with_Zasilkovna_shipping_method_returns_ShippingMethodNotEligible`** — Order has `ShippingMethod = Zasilkovna`. Assert: `BusinessResult.Failure(Error.Validation(ShippingMethodNotEligible))`. No `Order.Ship` call. No outbox enqueue. Pins the symmetric assertion vs T-0072.
4. **`HandOver_with_order_in_Paid_state_returns_OrderInvalidTransition`** — Order is `Paid` (not yet Accepted). `Order.Ship` rejects. Assert: failure propagates verbatim; no outbox enqueue.
5. **`HandOver_enqueues_exactly_one_outbox_event_NOT_two`** — Happy path. Assert via NSubstitute: `outbox.EnqueueAsync(order.Id, "order.shipped.customerEmail", Arg.Any<string>(), ct)` called exactly once; NO call with event type `"shipping.generate.label"`. Pins the T-0073 vs T-0072 distinguishing constraint.
6. **`HandOver_payload_TrackingUrl_is_null`** — Happy path. Capture the payload JSON passed to `outbox.EnqueueAsync` (NSubstitute `Arg.Do`). Deserialize as `OrderShippedCustomerEmailPayload`. Assert: `payload.TrackingUrl is null`; `payload.ActionUrl == $"{webBaseUrl}/objednavka/{orderId}"`; `payload.OrderNumber == order.OrderNumber`; `payload.Email == order.CustomerEmail`. Pins the conditional-template contract.

#### Unit — `Makables.Tests/AppServices/Features/Email/EmailSendServiceTests.cs` (EXTEND, ~1 test)

1. **`SendAsync_with_OrderShippedCustomerEmail_and_null_TrackingUrl_omits_tracking_substitution`** — Arrange payload JSON with `"TrackingUrl": null`. Mock `IEmailProvider.SendAsync`. Capture the SendGrid `Personalization` substitutions dict. Assert: either the `TrackingUrl` key is absent OR its value is empty string (whichever pattern T-0072's positive test established). The positive `TrackingUrl is not null` case is pinned by T-0072's own test; T-0073's test pins the negative variant.

#### Integration — `Makables.IntegrationTests/Orders/HandOverOrderIntegrationTests.cs` (NEW, ~1 test)

1. **`POST_handover_with_PersonalPickup_order_transitions_to_Shipped_and_enqueues_customer_email`** — End-to-end against `postgres:16-alpine` container. Arrange: seed a `PersonalPickup` order owned by maker M1 in `Accepted` state. Authenticate as M1 (existing test helper produces maker JWT). Act: `POST /api/v1/maker/orders/{orderId}/handover` with empty body. Assert:
   - 200 OK with response body matching the `Response` shape.
   - DB: `orders.state == 'Shipped'`; `orders.shipped_at` set; `orders.auto_deliver_at` set to `shipped_at + 7d`; `orders.shipping_carrier_ref IS NULL`; `orders.shipping_carrier_tracking_url IS NULL`.
   - DB: exactly **1 row** in `outbox_events` with `aggregate_id == orderId` and `event_type == 'order.shipped.customerEmail'`. NO row with `event_type == 'shipping.generate.label'`.
   - Payload JSON deserializes to `OrderShippedCustomerEmailPayload` with `TrackingUrl == null`.

### Docs

- **`docs/architecture/roles/order.md`** — append under "Lifecycle > Shipped" that the `Accepted → Shipped` transition has two writers: T-0072's `ShipOrder` (Zásilkovna; carrier call + tracking URL) and T-0073's `HandOverOrder` (PersonalPickup; no carrier; tracking URL null).
- **`docs/architecture/roles/shipping-carrier.md`** — append a note that personal-pickup orders bypass the carrier entirely; the role doc's contract applies only to Zásilkovna orders.
- **`docs/tickets/INDEX.md`** — flip T-0073 row to `**done**` after PR merge (PM does this).

## Alternatives Considered

- **Option A — Separate customer-shipped email event + template per shipping method.** *Rejected per A.1* — bifurcated `order.shipped.zasilkovna.customerEmail` + `order.shipped.personalPickup.customerEmail` events with separate SendGrid templates means more copy + translations to maintain for negligible UX benefit. Generic Czech wording ("vaše objednávka je na cestě k vám") covers both cases; the conditional tracking-URL row is the only template-level branch and SendGrid Dynamic Templates handle it natively.
- **Option B — Mandatory photo / signature capture on handover.** *Rejected per A.2* — UX regression for small-practitioner makers (camera permission prompts, photo upload, OrderAttachment write per ship action). Dispute path is admin-mediated via order history + T-0079 messages + T-0118 admin monitoring, not maker-side proof capture.
- **Option C — Optional photo on handover (middle ground).** *Rejected per A.2* — adds test surface (handler branches on payload, frontend conditional uploader, OrderAttachment optional FK) for marginal benefit (very few makers would use it; dispute path doesn't depend on it).
- **Option D — Fold personal-pickup into T-0072's `ShipOrder.Command` with internal branching on `order.ShippingMethod`.** *Rejected per C* — one handler with conditional carrier call (`if Zasilkovna: factory.ResolveAsync + carrier.CreateShipmentAsync; else: skip`) muddles the test matrix and the controller surface. Two commands + two endpoints + two handlers mirror the maker dashboard UX (one button per method) and keep each handler small + testable. Shared post-state-transition path (outbox + payload + template) preserves DRY where it matters.
- **Option E — Reuse name `ShipOrder` for both commands (verb collision).** *Rejected per C* — "Ship" doesn't apply to in-person handover. Semantic clarity (`HandOverOrder` vs `ShipOrder`) helps maker support conversations and audit-log readability.
- **Option F — Different auto-deliver window for personal-pickup (1 day or no auto-deliver).** *Rejected per T-0070 A.4* — locked at T-0070 grooming; T-0073 only honors it. 1-day = risky UX (maker delay before clicking → auto-deliver fires before customer gets item); no auto-deliver = locks completion behind manual customer action many won't take.
- **Option G — Emit a `shipping.generate.label` event for personal-pickup too (uniformity).** *Rejected per T-0070 + T-0073 C* — there's no label to generate for in-person handover (no carrier ref, no Packeta call). Emitting a no-op event clutters outbox + admin monitoring + retry alerts.

## Out of scope

- **Zásilkovna `ShipOrder.Command`** — T-0072 (the carrier-calling sibling).
- **`OrderShippedCustomerEmailPayload` record + `OrderShippedCustomerEmail` outbox event type + `EmailTemplateType.OrderShippedCustomer` enum + SendGrid template seed** — all T-0072. T-0073 reuses verbatim with `TrackingUrl = null`.
- **`ShippingMethodNotEligible` BusinessErrorMessage code + Czech i18n key** — T-0072 ships them; T-0073 reuses.
- **Label generation Function + label download endpoint** — T-0074 + T-0075. Personal-pickup has no label.
- **`Order.Ship(IClock, string?, int, string?)` state-transition method** — T-0071.
- **Maker dashboard frontend wiring** (the "Předáno zákazníkovi" button + API call + success toast) — separate frontend ticket; T-0073 only reserves the i18n key.
- **Customer dispute UI / admin investigation surface** — T-0079 (customer-maker messages), T-0118 (admin monitoring).
- **Auto-deliver Function** (the timer that flips `Shipped → Delivered` at `AutoDeliverAt`) — T-0076 (this ticket blocks T-0076; the writer needs T-0073 to land first so AutoDeliverAt is reliably set on personal-pickup orders).
- **In-app maker notifications** — post-MVP; T-0073 emits only the customer email.
- **Photo / signature / recipient metadata capture** — explicitly rejected per A.2; not in any current ticket.
- **NSwag regen as a separate PR** — bundled with T-0072's regen per C.

## Acceptance criteria

- **AC-1** `Core.AppServices/Features/Orders/HandOverOrder.cs` exists with nested `Command(Guid OrderId)`, `Validator`, `Response`, `Handler` per the one-file feature shape. Handler is constructor-injected (primary-ctor) with `IUserSessionProvider`, `IOrderRepository`, `IClock`, `IOutbox`, `IPublicAppUrls`, `ILanguageResolver`. **Handler does NOT call `SaveChangesAsync()`.**
- **AC-2** Given an order in `Accepted` state with `ShippingMethod = PersonalPickup` owned by maker `M1`, when `M1` sends `Command(orderId)`, then `order.State == Shipped`, `order.ShippedAt == clock.UtcNow`, `order.AutoDeliverAt == clock.UtcNow + 7d`, `order.ShippingCarrierRef IS NULL`, `order.ShippingCarrierTrackingUrl IS NULL`. State-transition pin.
- **AC-3** Given an order owned by maker `M1`, when `M2` sends `Command(orderId)`, then the result is `BusinessResult.Failure(Error.Permanent(BusinessErrorMessage.OrderNotFound))`. **No outbox event is enqueued.** Ownership-scoping pin (no cross-maker leak).
- **AC-4** Given an order with `ShippingMethod = Zasilkovna`, when the owning maker sends `Command(orderId)`, then the result is `BusinessResult.Failure(Error.Validation(BusinessErrorMessage.ShippingMethodNotEligible))`. `Order.Ship` is NOT called. **No outbox event is enqueued.** Symmetric to T-0072's PersonalPickup-not-eligible assertion.
- **AC-5** Given an order in `Paid` state (not yet `Accepted`), when the owning maker sends `Command(orderId)`, then `Order.Ship` returns `BusinessResult.Failure(Error.Validation(BusinessErrorMessage.OrderInvalidTransition))` and the handler propagates the failure verbatim. **No outbox event is enqueued.**
- **AC-6** On the happy path, exactly **1 outbox row** is enqueued with `aggregate_id == order.Id`, `event_type == "order.shipped.customerEmail"`. **NO row** is enqueued with `event_type == "shipping.generate.label"`. Verified by NSubstitute (unit) AND by DB row count (integration). The T-0072-vs-T-0073 distinguishing constraint.
- **AC-7** The outbox payload deserializes to `OrderShippedCustomerEmailPayload` with `TrackingUrl is null`, `ActionUrl == $"{publicAppUrls.WebBaseUrl}/objednavka/{order.Id}"`, `OrderNumber == order.OrderNumber`, `Email == order.CustomerEmail`, `ContactName == order.ContactName`, `LanguageCode` resolved via the existing `ILanguageResolver`. Payload-shape pin.
- **AC-8** `Web.Maker/Controllers/OrdersController.cs` exposes `POST /api/v1/maker/orders/{orderId}/handover` returning `Mediator.Send(new HandOverOrder.Command(orderId))` via `HandleResult`. The action is `[Authorize]` (inherited from controller or explicit) — a request without a maker JWT returns 401; a customer JWT returns 401 (audience enforcement at host level).
- **AC-9** `EmailSendService.SendAsync` correctly handles the `TrackingUrl == null` payload variant: the SendGrid `Personalization` substitutions dict either omits the `TrackingUrl` key or sets it to empty string (whichever pattern T-0072's positive test established). The conditional tracking-URL row in the template renders nothing for personal-pickup orders. New unit test in `EmailSendServiceTests` pins this behaviour.
- **AC-10** `frontend/src/lib/api-client/` regenerated via NSwag and committed in the same PR (bundled with T-0072's regen). The new `POST /api/v1/maker/orders/{orderId}/handover` endpoint is typed in the maker client.
- **AC-11** Architectural compliance: no `Console.*`; no `SaveChangesAsync()` in handler (UoW pipeline commits Order mutation + outbox row atomically per ADR 0014); no `dynamic`; no inline error strings (`ShippingMethodNotEligible` + `OrderNotFound` + `OrderInvalidTransition` all read from `BusinessErrorMessage`); no new business logic in the controller (one-liner).
- **AC-12** Test count: at least **6 new unit tests** in `HandOverOrderHandlerTests.cs` + **1 extended unit test** in `EmailSendServiceTests.cs` + **1 new integration test** in `HandOverOrderIntegrationTests.cs`. Build clean. Consistency script exit 0 (no new T1–T7 violations vs baseline).

## Technical notes

### Why two commands instead of one branching command

The codebase models user-initiated state transitions as discrete commands. The maker dashboard renders one button per shipping method (the customer-checkout chose the method at checkout time; the maker doesn't switch methods mid-order). Two commands = two endpoints = two handlers = two test surfaces. The shared post-`Order.Ship` code path (outbox payload + event type + template) is small enough that duplication is cheaper than abstraction — and the conditional-rendering on `TrackingUrl == null` lives entirely in SendGrid's template engine, not in our C#.

### Why one outbox event (not two) for personal-pickup

T-0072's Zásilkovna handler enqueues two events: `order.shipped.customerEmail` AND `shipping.generate.label`. The second one drives T-0074's label-generation Function. Personal-pickup has no label to generate — there's no Packeta carrier ref, no Packeta endpoint to call, no PDF to fetch. Emitting a no-op `shipping.generate.label` event would: (1) clutter the outbox table; (2) cause T-0074's Function to either fail or no-op, both wasteful; (3) muddy admin monitoring + retry-alerting dashboards. The two-vs-one event count is the clearest test pin for "the personal-pickup path skipped the carrier."

### Why the TrackingUrl-null variant lives in the email-service test (not the handler test)

The handler's job is to enqueue the payload with `TrackingUrl = null`. AC-7 + AC-6 cover that at the handler level. The email-service's job is to translate that null into the right SendGrid substitution behaviour — that's a separate concern. Splitting the test ownership matches the existing T-0067 + T-0072 patterns: handler tests pin "what's in the outbox row"; email-service tests pin "how that row is sent." If a future change moves the conditional rendering from SendGrid to our C# (unlikely but possible if we ever drop SendGrid), only the email-service test needs to change.

### Why no new error code

`ShippingMethodNotEligible` is shipped by T-0072 to cover its assertion `if (order.ShippingMethod != ShippingMethod.Zasilkovna)`. The same code semantically covers T-0073's symmetric assertion `if (order.ShippingMethod != ShippingMethod.PersonalPickup)`. From the caller's perspective the error means "you called the wrong shipping endpoint for this order's shipping method"; the specific direction is implicit in which endpoint they hit. One code, one Czech translation, two assertion sites.

### Why the integration test count is "1" instead of T-0070's "3"

T-0070's integration tests covered three orthogonal endpoint behaviours (200 + headers, rate limit 429, configuration-error 4xx). T-0073 has no headers concern (no Cache-Control), no rate limit (authenticated maker endpoint), no configuration failure mode (no factory call). The single end-to-end test covers the one happy path plus the DB + outbox assertions. The error-path unit tests (AC-3 + AC-4 + AC-5) are sufficient for the four failure surfaces; an end-to-end test for each would not catch additional regressions.

## Files touched (expected)

### New
- `backend/src/Makables.Core.AppServices/Features/Orders/HandOverOrder.cs`
- `backend/src/Makables.Tests/AppServices/Features/Orders/HandOverOrderHandlerTests.cs`
- `backend/src/Makables.IntegrationTests/Orders/HandOverOrderIntegrationTests.cs`

### Modified
- `backend/src/Makables.Web.Maker/Controllers/OrdersController.cs` — add `HandOver` action.
- `backend/src/Makables.Tests/AppServices/Features/Email/EmailSendServiceTests.cs` — add 1 test pinning `TrackingUrl = null` rendering.
- `frontend/src/lib/i18n/cs-CZ.ts` — add 1 success key (`orders.handover.success`).
- `frontend/src/lib/api-client/*` — NSwag-regenerated (bundled with T-0072's regen); committed in the same PR.
- `docs/architecture/roles/order.md` — note the two `Accepted → Shipped` writers.
- `docs/architecture/roles/shipping-carrier.md` — note that personal-pickup bypasses the role entirely.

## Test plan reference

Inline above (see Scope > Tests). No separate `docs/test-plans/T-0073.md`.

## Status log

- 2026-06-08 `draft` by PM. Created during shipping-bundle grooming after T-0070 + T-0071 + T-0072 specs locked.
- 2026-06-08 `draft → ready` by PM. User answered 2 blocking AskUserQuestion items per `/feature` workflow step 3: (1) unified vs bifurcated customer-shipped email — user chose **unified** (single event + template + payload shared with T-0072; conditional template branch on `TrackingUrl == null`); (2) handover proof capture — user chose **self-attested, no proof** (UX-friendly for small makers; dispute path is admin-mediated via order history + T-0079 messages + T-0118 monitoring). 9 PM-absorbed mechanical decisions captured (command name, endpoint, `Order.Ship` arg shape, single outbox event, payload reuse, assertion symmetry, error reuse, no carrier-side error handling, bundled NSwag regen). 5 ADR-locked items noted (one-file feature shape, UoW pipeline, scoped repos, outbox naming, per-event-type switch). No manual deployment steps; not security-touching. **Ready for dotnet-backend.**
