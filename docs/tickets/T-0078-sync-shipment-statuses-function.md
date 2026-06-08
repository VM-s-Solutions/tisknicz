---
id: T-0078
title: SyncShipmentStatuses Function (timer every 6h) + DisputeShipment stub
status: ready
size: M
owner: dotnet-backend
created: 2026-06-08
updated: 2026-06-08
depends_on: [T-0070, T-0076]
blocks: []
user_stories: [US-customer-0013]
adrs: [0014, 0017, 0020]
phase: 4
manual_steps: []
security_touching: false
layers: [domain, appservices, infra-clients, infra-database, infra-functions]
---

# T-0078 — SyncShipmentStatuses Function (timer every 6h) + DisputeShipment stub

## Context

T-0078 closes the third (and last) delivery-source path for Zásilkovna orders: the **carrier-sourced** transition. The customer-confirm path (T-0076) and the 7-day auto-deliver path (T-0077) handle the cases where the customer self-confirms or the auto-deliver window expires; T-0078 wires the **carrier-truth** path where Packeta itself reports the package as Delivered (the customer picked it up at the Zásilkovna pickup point), Returned (customer never picked up; sent back to maker), or Failed (delivery exception — damaged, lost, refused). The Function polls Packeta every 6 hours for all `Shipped + ZasilkovnaPickupPoint + ShippingCarrierRef IS NOT NULL` orders, calls `IShippingCarrier.GetStatusAsync(carrierRef)` (the T-0070 seam already shipped), and dispatches one of three Commands per the carrier's reported state: `MarkOrderDelivered.Command(OrderId, Source: Carrier, DeliveredAt: …)` (the T-0076 single command, third caller) for Delivered; `DisputeShipment.Command(OrderId, DisputeReason.CarrierReturned)` for Returned; `DisputeShipment.Command(OrderId, DisputeReason.CarrierFailed)` for Failed.

This is part of the **delivery-close bundle** (T-0076 + T-0077 + T-0078, shipped in one PR). T-0076 ships the single canonical `MarkOrderDelivered.Command(OrderId, Source)` command, the new `OrderDeliverySource` enum (Customer=0, Auto=1, Carrier=2), the new Order column `delivery_source SMALLINT NULL`, the in-place `Order.MarkAsDelivered` signature extension `(IClock clock, OrderDeliverySource source, DateTimeOffset? deliveredAtOverride = null)` with clock-fallback semantics, and the single `order.delivered.customerEmail` outbox event (silent-success on already-Delivered re-call). T-0077 wires the timer-driven auto-deliver Function (Source=Auto). T-0078 wires the timer-driven carrier-sync Function (Source=Carrier) AND the `DisputeShipment.Command` stub feature that handles Returned/Failed states. The three tickets implement sequentially in the same branch; reviewers see one cohesive delivery-close surface.

T-0078 is **the only delivery-close ticket with non-Delivered branches.** Customer-confirm (T-0076) and auto-deliver (T-0077) only know how to mark Delivered. Carrier-sync (T-0078) handles all three terminal states the carrier can report — Delivered → close happy path; Returned/Failed → dispatch dispute. The DisputeShipment Command is a STUB at MVP: the handler does NOTHING except (a) log Warning with full context, (b) emit `order.disputed.carrierSourced` outbox event for future admin processing. No Order state mutation, no customer email. T-0106 (downstream, post-MVP) will wire the real Disputed state transition + customer notification + admin email; T-0078's responsibility is DETECTING the dispute-worthy states from carrier truth and getting the outbox event durably enqueued. Adding the stub here (vs. deferring entirely to T-0106) means Returned/Failed orders surface immediately in the audit log + outbox table the moment T-0078 lands, giving ops visibility into the volume + character of disputes before T-0106's domain design is even drafted.

The Function shape mirrors T-0077 `AutoDeliverOrdersFunction` verbatim: thin MediatR-dispatch wrapper, fail-continue per Order (one bad order does not stall the sweep), structured end-of-run log line ("synced N, delivered M, disputed K, failed L"), unlimited batch size at MVP per ADR 0023 perf budget (MVP <50 Z-orders/day per country; 4×6h sweeps of <200 orders is negligible Packeta API load). The `IShippingCarrier.GetStatusAsync` seam is the T-0070 contract — already ships with Polly resilience + ADR 0016 §A.14 error classification — so T-0078 just consumes it. The new `IOrderRepository.GetCarrierSyncableUnscopedReadOnlyAsync` returns `IAsyncEnumerable<Order>` with the full Order projection (handler needs ShippingMethod + ShippingCarrierRef + CountryCode + State), predicate-filtered to `State == Shipped AND ShippingMethod == ZasilkovnaPickupPoint AND ShippingCarrierRef IS NOT NULL` (PersonalPickup orders skipped — no carrier ref to query). The already-Delivered race is handled by T-0076's silent-success contract: if the customer confirmed between sweeps, `MarkOrderDelivered.Command` returns Success no-op.

## Locked design decisions

Captured per `docs/process/deliberation.md`. T-0078 has 2 user-locked dimensions at `/feature` step 3 (the DisputeShipment stub scope; the DeliveredAt fallback strategy when Packeta omits the timestamp), 4 ADR-locked items (UoW pipeline, one-file feature, thin Function wrapper, carrier interface contract), and 12 PM-absorbed decisions (timer schedule, batch size, Function shape, repository predicate, per-Order branch logic, failure isolation, DisputeReason enum shape, outbox payload + event type, logging, race handling, no NSwag).

### A. User-locked at /feature step 3 (non-negotiable)

1. **Dispatch `DisputeShipment.Command` stub for Returned/Failed carrier statuses.** New one-file feature `Features/Orders/DisputeShipment.cs` with `Command(OrderId, DisputeReason)` + Handler that does NOTHING at MVP except: (a) log Warning with full context (OrderId, Reason, source = "carrier-sourced"); (b) emit `order.disputed.carrierSourced` outbox event with payload for future admin processing. T-0106 (OpenDispute future ticket) wires the real domain logic. T-0078 keeps full responsibility for DETECTING dispute-worthy statuses; T-0106 wires what HAPPENS next. **Rejected:** defer entirely to T-0106 (Returned orders silent until T-0106 ships — loses ops visibility into dispute volume); transition to Disputed state directly (pre-empts T-0106 domain design — locks in state-graph choices before they've been deliberated); raw outbox event for admin review with no Command wrapper (lower automation; still needs admin manual action and bypasses the MediatR pipeline that gives us logging + validation + UoW discipline).

2. **DeliveredAt fallback to `clock.UtcNow` + log Warning when Packeta omits the timestamp.** Tolerant strategy: Packeta's Delivered STATUS is authoritative; missing timestamp is a data-quality issue at the carrier. Function passes `deliveredAtOverride = packetaResponse.DeliveredAt` (may be null) into `MarkOrderDelivered.Command(OrderId, Source: Carrier, DeliveredAt: …)`; T-0076 handler falls back to `clock.UtcNow` when override is null. Log Warning with order context for ops visibility. **Rejected:** skip transition + retry next 6h sweep (worse customer UX — 6h+ delay on an order Packeta already says is Delivered); always use clock.UtcNow regardless of Packeta-provided timestamp (loses authoritative carrier timestamp when present, which matters for invoicing + dispute windows); mark delivered now but defer email until next sweep (over-engineered; introduces email-vs-state desync risk for zero gain).

### B. ADR-locked (no relitigation)

- **ADR 0014 (UoW pipeline).** `DisputeShipment.Handler` MUST NOT call `SaveChangesAsync()`. `UnitOfWorkPipelineBehavior` commits the outbox row in a single Postgres transaction. The Function dispatches one MediatR Command per Order; each Command commits independently (fail-continue semantics — one Order's dispatch failure does not roll back peers).
- **ADR 0017 (shipping/Packeta).** `IShippingCarrier.GetStatusAsync(string carrierRef, CancellationToken ct) → Task<BusinessResult<ShipmentStatus>>` is the T-0070 contract; T-0078 only consumes it. `ShipmentStatus.State` enum values (Created / InTransit / Delivered / Returned / Failed) are T-0070-locked. Error classification follows ADR 0016 §A.14 (Transient → log Warning + skip Order, retry next 6h; Configuration → log Critical, surface via ApplicationInsights).
- **ADR 0020 (background jobs).** Timer-trigger Function = thin MediatR-dispatch wrapper. Mirrors T-0077 `AutoDeliverOrdersFunction` precedent verbatim. Per-Order failure isolation (one bad Order does not stall the sweep). End-of-run structured log line. Outbox event for downstream admin processing follows ADR 0020's "Function does NOT do business logic; queue persists work for downstream consumers" pattern.
- **One-file feature shape.** `Features/Orders/DisputeShipment.cs` contains nested `Command`, `Validator`, `Handler`, `Response`. No separate files per type.
- **`BusinessResult<T>` for expected failures.** Carrier Transient surfaces as `BusinessResult.Failure(Error.Transient(…))` from `GetStatusAsync`; Function catches per-Order and continues. Exceptions reserved for truly unexpected (e.g., DB connection dropped mid-sweep).
- **`Response` records must be GLOBALLY UNIQUE** (T-0070-T-0075 CI fix convention). The DisputeShipment Response is named `DisputeShipmentResponse` (not nested `Response`) to avoid type-name collision with other features compiled into the same assembly.
- **Per-event-type switch in outbox dispatcher** (ADR 0019 / T-0067 Q3 pattern). T-0078 adds `OutboxEventTypes.OrderDisputedCarrierSourced` constant. This event is NOT email-routed (no `IsEmailSend` branch); T-0106 will add the consumer Function. The OutboxDispatcher will simply log it as "unrouted" until T-0106 wires the consumer — which is the intended behaviour (no silent drop; visible in logs).

### C. PM-absorbed (no user input needed)

- **Timer schedule:** `0 0 0,6,12,18 * * *` (every 6h starting 00:00 UTC). Per INDEX line. Locked for launch. At MVP <50 Z-orders/day per country, 4×6h sweeps of <200 orders = negligible Packeta API load.
- **Batch size cap:** unlimited at MVP. ADR 0023 perf budget. If Packeta 429s observed, add cap + back-off (post-MVP follow-up ticket).
- **Function shape:** thin MediatR-dispatch wrapper mirroring `AutoDeliverOrdersFunction`. Reads `IOrderRepository.GetCarrierSyncableUnscopedReadOnlyAsync(CancellationToken ct)` → `IAsyncEnumerable<Order>` (full Order projection here — handler needs `ShippingMethod` + `ShippingCarrierRef` + `CountryCode` + `State`).
- **Repository method predicate:** `State == Shipped AND ShippingMethod == ZasilkovnaPickupPoint AND ShippingCarrierRef IS NOT NULL`. PersonalPickup orders skipped (no carrier ref to query). Unscoped (Function context has no user identity). ReadOnly (`.AsNoTracking()` per CLAUDE.md perf rule for read-only iteration).
- **Per-Order processing pipeline:**
  1. `IShippingCarrierFactory.ResolveAsync(countryCode, ct)` → carrier (propagate Configuration failure → log Critical + skip).
  2. `carrier.GetStatusAsync(carrierRef, ct)` returns `BusinessResult<ShipmentStatus>`.
  3. Switch on `ShipmentStatus.State`:
     - `Delivered` → dispatch `MarkOrderDelivered.Command(OrderId, Source: Carrier, DeliveredAtOverride: packetaResponse.DeliveredAt)`.
     - `Returned` → dispatch `DisputeShipment.Command(OrderId, DisputeReason.CarrierReturned)`.
     - `Failed` → dispatch `DisputeShipment.Command(OrderId, DisputeReason.CarrierFailed)`.
     - `Created` / `InTransit` → no-op (log Debug; package still in flight).
     - unknown enum value (defensive — future Packeta state additions) → log Warning + no-op (do NOT dispatch; let next ticket extend).
- **Failure isolation:** fail-continue per Order. Polly resilience on Packeta calls (already wired at T-0070). Carrier Transient failure → log Warning, skip Order (retry next 6h sweep). Carrier Configuration failure → log Critical, surface via ApplicationInsights. MediatR-dispatch failure (e.g., MarkOrderDelivered returns Failure for some unexpected reason) → log Warning + counter increment + continue with next Order. Per-Order try/catch wraps the whole pipeline; outer Function never throws unless DB connection drops mid-sweep.
- **DisputeReason enum:** new `Core.Domain/Orders/DisputeReason.cs` with `CarrierReturned = 0, CarrierFailed = 1` (T-0106 future ticket will add customer-initiated + maker-initiated reasons).
- **`OrderDisputedCarrierSourcedPayload` (outbox event payload):** sealed record `OrderDisputedCarrierSourcedPayload(string OrderId, DisputeReason Reason, ShipmentState CarrierState)`. The `CarrierState` field preserves the raw Packeta state for audit (so T-0106 admin processing can distinguish Returned vs. Failed even after `DisputeReason` semantics evolve).
- **Outbox event type constant:** `OutboxEventTypes.OrderDisputedCarrierSourced = "order.disputed.carrierSourced"`. NOT email-routed at MVP (no `IsEmailSend` branch — T-0106 will add the consumer Function + classifier branch). The OutboxDispatcher will log it as "unrouted" / "no handler yet" until T-0106 ships.
- **Logging:** end-of-Function structured log `"SyncShipmentStatuses completed: synced N, delivered M, disputed K, failed L"` where N = total Orders processed, M = Delivered transitions dispatched, K = Disputes dispatched (Returned + Failed combined), L = per-Order failures (Transient + skipped). Per-failure Warning log carries OrderId + CarrierRef + Error.Code for ops triage.
- **Already-Delivered race:** silent-Success contract from T-0076 handles this. If the customer confirmed between sweeps (Customer source already mutated the Order to Delivered), `MarkOrderDelivered.Command(OrderId, Carrier, …)` returns Success no-op (does NOT re-emit the outbox email event, does NOT overwrite `DeliverySource = Customer`).
- **No NSwag regen:** Function only, no public contract change.

## Scope

### Domain layer

- **`Core.Domain/Orders/DisputeReason.cs`** — NEW enum:
  ```csharp
  public enum DisputeReason
  {
      CarrierReturned = 0,
      CarrierFailed = 1,
  }
  ```
  T-0106 will extend with customer-initiated + maker-initiated values; T-0078 ships only the two carrier-sourced reasons it needs.
- **`Core.Domain/Outbox/OrderDisputedCarrierSourcedPayload.cs`** — NEW sealed record:
  ```csharp
  public sealed record OrderDisputedCarrierSourcedPayload(
      string OrderId,
      DisputeReason Reason,
      ShipmentState CarrierState);
  ```
  PascalCase JSON property names (project convention from T-0067). `CarrierState` preserves the raw Packeta state for audit + future admin processing.
- **`Core.Domain/Outbox/OutboxEventTypes.cs`** — add 1 new constant:
  - `OrderDisputedCarrierSourced = "order.disputed.carrierSourced"`
  - **NOT** added to `IsEmailSend` (T-0106 will route this when the consumer Function ships).
  - **NOT** added to any other classifier method. OutboxDispatcher's "unrouted" log branch handles it visibly (no silent drop).
- **`Core.Domain/Orders/Order.cs`** — no signature change at T-0078 (T-0076 extends `Order.MarkAsDelivered`; T-0078 only consumes the new signature via `MarkOrderDelivered.Command`).

### AppServices layer

- **`Core.AppServices/Features/Orders/DisputeShipment.cs`** — NEW one-file feature (STUB handler).
  - `Command(string OrderId, DisputeReason Reason) : IRequest<BusinessResult<DisputeShipmentResponse>>` record.
  - `DisputeShipmentResponse(string OrderId, DisputeReason Reason)` record. **Name is globally unique** (per T-0070-T-0075 CI convention) — avoid the unnamed nested `Response` collision.
  - `Validator : AbstractValidator<Command>` — `OrderId` non-empty + valid id format; `Reason` must be a defined enum value.
  - `Handler(IOrderRepository orderRepository, IOutbox outbox, ILogger<Handler> logger)` primary-constructor DI.
  - Steps (NO `SaveChangesAsync()` — UoW pipeline commits):
    1. **Load Order via `orderRepository.GetByIdUnscopedAsync(command.OrderId, ct)`** (Function context has no user identity → unscoped lookup is correct). Null → `BusinessResult.Failure<DisputeShipmentResponse>(Error.Permanent(BusinessErrorMessage.OrderNotFound))`.
    2. **Log Warning** with structured context: `logger.LogWarning("DisputeShipment STUB: order {OrderId} flagged for dispute (reason: {Reason}, source: carrier-sourced, carrierRef: {CarrierRef}). T-0106 will wire real domain logic.", order.Id, command.Reason, order.ShippingCarrierRef);`.
    3. **Determine `ShipmentState`** for payload audit. Lookup table: `DisputeReason.CarrierReturned → ShipmentState.Returned`; `DisputeReason.CarrierFailed → ShipmentState.Failed`. (T-0106 may evolve this when customer-initiated reasons land.)
    4. **Build outbox payload + enqueue** — `var payload = new OrderDisputedCarrierSourcedPayload(order.Id, command.Reason, carrierState); outbox.Enqueue(order.Id, OutboxEventTypes.OrderDisputedCarrierSourced, JsonSerializer.Serialize(payload));`.
    5. **NO Order state mutation at MVP.** Order remains in `Shipped` state. T-0106 will wire the `Order.OpenDispute(...)` transition.
    6. **Return** `BusinessResult.Success(new DisputeShipmentResponse(order.Id, command.Reason))`. UoW pipeline commits the outbox row atomically per ADR 0014.
  - **XML doc on the Handler class:** `"T-0078 STUB: emits outbox event + logs Warning. T-0106 will wire the real Disputed state transition + customer + admin email."`

- **`Core.AppServices/Features/Outbox/OutboxDispatcher.cs`** — no new RouteTarget at T-0078. `OrderDisputedCarrierSourced` event type goes through the existing "unrouted / no handler yet" branch (logs `Warning` with the event type for visible ops surface; does NOT silent-drop). T-0106 will add the routing.

### Infrastructure / Database layer

- **`Core.Domain/Orders/IOrderRepository.cs`** — add 1 new method signature:
  - `IAsyncEnumerable<Order> GetCarrierSyncableUnscopedReadOnlyAsync(CancellationToken ct);`
- **`Infra.Database/Repositories/OrderRepository.cs`** — implement:
  ```csharp
  public IAsyncEnumerable<Order> GetCarrierSyncableUnscopedReadOnlyAsync(CancellationToken ct)
      => dbContext.Orders
          .AsNoTracking()
          .Where(o => o.State == OrderState.Shipped
                   && o.ShippingMethod == ShippingMethod.ZasilkovnaPickupPoint
                   && o.ShippingCarrierRef != null)
          .AsAsyncEnumerable();
  ```
  Streaming iteration (per T-0077 precedent for unbounded sweeps). `.AsNoTracking()` per CLAUDE.md perf rule for read-only iteration. The predicate filter is on indexed columns (`state` + `shipping_method` are indexed from T-0070 + T-0072 migrations); no new index needed at MVP volumes.

### Infrastructure / Functions layer

- **`Infra.Functions/Delivery/SyncShipmentStatusesFunction.cs`** — NEW timer-trigger Function (~50 lines). Mirrors `AutoDeliverOrdersFunction` shape:
  ```csharp
  public sealed class SyncShipmentStatusesFunction(
      IOrderRepository orderRepository,
      IShippingCarrierFactory carrierFactory,
      ISender mediator,
      ILogger<SyncShipmentStatusesFunction> logger)
  {
      [Function(nameof(SyncShipmentStatusesFunction))]
      public async Task RunAsync(
          [TimerTrigger("0 0 0,6,12,18 * * *")] TimerInfo timer,
          CancellationToken cancellationToken)
      {
          int synced = 0, delivered = 0, disputed = 0, failed = 0;
          await foreach (var order in orderRepository.GetCarrierSyncableUnscopedReadOnlyAsync(cancellationToken))
          {
              synced++;
              try
              {
                  var carrierResult = await carrierFactory.ResolveAsync(order.CountryCode, cancellationToken);
                  if (!carrierResult.IsSuccess)
                  {
                      logger.LogCritical(
                          "SyncShipmentStatuses: carrier resolve failed for order {OrderId} (cc={CountryCode}): {Code}",
                          order.Id, order.CountryCode, carrierResult.Error!.Code);
                      failed++;
                      continue;
                  }
                  var statusResult = await carrierResult.Value!.GetStatusAsync(order.ShippingCarrierRef!, cancellationToken);
                  if (!statusResult.IsSuccess)
                  {
                      logger.LogWarning(
                          "SyncShipmentStatuses: GetStatusAsync failed for order {OrderId} (carrierRef={CarrierRef}): {Code} — retrying next sweep",
                          order.Id, order.ShippingCarrierRef, statusResult.Error!.Code);
                      failed++;
                      continue;
                  }
                  var status = statusResult.Value!;
                  switch (status.State)
                  {
                      case ShipmentState.Delivered:
                          if (status.DeliveredAt is null)
                          {
                              logger.LogWarning(
                                  "SyncShipmentStatuses: Packeta reported Delivered without timestamp for order {OrderId} — falling back to clock.UtcNow",
                                  order.Id);
                          }
                          var deliverResult = await mediator.Send(
                              new MarkOrderDelivered.Command(order.Id, OrderDeliverySource.Carrier, status.DeliveredAt),
                              cancellationToken);
                          if (deliverResult.IsSuccess) delivered++;
                          else { failed++; logger.LogWarning("SyncShipmentStatuses: MarkOrderDelivered failed for order {OrderId}: {Code}", order.Id, deliverResult.Error!.Code); }
                          break;
                      case ShipmentState.Returned:
                          var returnResult = await mediator.Send(
                              new DisputeShipment.Command(order.Id, DisputeReason.CarrierReturned),
                              cancellationToken);
                          if (returnResult.IsSuccess) disputed++;
                          else { failed++; logger.LogWarning("SyncShipmentStatuses: DisputeShipment(Returned) failed for order {OrderId}: {Code}", order.Id, returnResult.Error!.Code); }
                          break;
                      case ShipmentState.Failed:
                          var failResult = await mediator.Send(
                              new DisputeShipment.Command(order.Id, DisputeReason.CarrierFailed),
                              cancellationToken);
                          if (failResult.IsSuccess) disputed++;
                          else { failed++; logger.LogWarning("SyncShipmentStatuses: DisputeShipment(Failed) failed for order {OrderId}: {Code}", order.Id, failResult.Error!.Code); }
                          break;
                      case ShipmentState.Created:
                      case ShipmentState.InTransit:
                          logger.LogDebug("SyncShipmentStatuses: order {OrderId} still in transit (state={State})", order.Id, status.State);
                          break;
                      default:
                          logger.LogWarning("SyncShipmentStatuses: unknown ShipmentState for order {OrderId}: {State}", order.Id, status.State);
                          break;
                  }
              }
              catch (Exception ex)
              {
                  failed++;
                  logger.LogError(ex, "SyncShipmentStatuses: unexpected error processing order {OrderId}", order.Id);
              }
          }
          logger.LogInformation(
              "SyncShipmentStatuses completed: synced {Synced}, delivered {Delivered}, disputed {Disputed}, failed {Failed}",
              synced, delivered, disputed, failed);
      }
  }
  ```
- **`Infra.Functions/Program.cs`** — no change required; `Microsoft.Azure.Functions.Worker` discovers Functions via reflection. DI for `IOrderRepository` + `ISender` + `IShippingCarrierFactory` is already wired from T-0070 / T-0029.

### Web host

**No controller.** T-0078 is a Function + Command-stub-only ticket. The customer-facing `POST /api/v1/customer/orders/{orderId}/deliver` endpoint is owned by T-0076.

### i18n

**No new i18n keys.** All error codes reuse `ShippingCarrier*` from T-0070 + `OrderNotFound` from T-0063. The Function path is admin/log-facing only — customer never sees these errors (silent customer-facing surface per T-0074 precedent for proactive background jobs).

### NSwag regen

**Not required.** T-0078 introduces no public contract changes. Function only.

### Tests

#### SyncShipmentStatusesFunctionTests (NEW, ~6 tests)

`backend/src/Makables.Tests/Functions/Delivery/SyncShipmentStatusesFunctionTests.cs` — NSubstitute mocks (`IOrderRepository`, `IShippingCarrierFactory`, `IShippingCarrier`, `ISender`).

1. **Delivered_state_dispatches_MarkOrderDelivered_with_Carrier_source_and_carrier_timestamp** — seed 1 Order via mocked async-enumerable; carrier returns `ShipmentStatus(State: Delivered, DeliveredAt: someTimestamp)`. Assert `ISender.Send` called with `MarkOrderDelivered.Command(order.Id, OrderDeliverySource.Carrier, someTimestamp)` exactly once; counters: delivered=1, disputed=0, failed=0.
2. **Delivered_state_with_null_timestamp_dispatches_with_null_override_and_logs_Warning** — carrier returns `ShipmentStatus(State: Delivered, DeliveredAt: null)`. Assert `ISender.Send` called with `MarkOrderDelivered.Command(order.Id, OrderDeliverySource.Carrier, null)` (T-0076 handler falls back to clock); assert `logger.LogWarning` called with message containing "without timestamp"; counter delivered=1.
3. **Returned_state_dispatches_DisputeShipment_with_CarrierReturned_reason** — carrier returns `ShipmentStatus(State: Returned)`. Assert `ISender.Send` called with `DisputeShipment.Command(order.Id, DisputeReason.CarrierReturned)`; counter disputed=1; assert `MarkOrderDelivered` NOT called.
4. **Failed_state_dispatches_DisputeShipment_with_CarrierFailed_reason** — carrier returns `ShipmentStatus(State: Failed)`. Assert `ISender.Send` called with `DisputeShipment.Command(order.Id, DisputeReason.CarrierFailed)`; counter disputed=1.
5. **InTransit_or_Created_state_is_noop_and_logged_Debug** — carrier returns `ShipmentStatus(State: InTransit)`. Assert NO `ISender.Send` call; counters: delivered=0, disputed=0, failed=0; assert `logger.LogDebug` called with message containing "still in transit".
6. **Carrier_Transient_failure_logs_Warning_and_continues_to_next_order** — seed 2 Orders. First order's `GetStatusAsync` returns `Transient(ShippingCarrierUnavailable)`. Second order's returns Delivered. Assert: failed=1 (first), delivered=1 (second); `logger.LogWarning` called with "retrying next sweep"; sweep does NOT throw; the outer Function completes normally.

#### DisputeShipmentHandlerTests (NEW, ~3 tests)

`backend/src/Makables.Tests/AppServices/Features/Orders/DisputeShipmentHandlerTests.cs` — NSubstitute mocks (`IOrderRepository`, `IOutbox`, `ILogger<Handler>`).

1. **Happy_path_enqueues_outbox_event_and_logs_Warning** — order exists with `ShippingCarrierRef = "PKT-9"`. Call `DisputeShipment.Command(order.Id, DisputeReason.CarrierReturned)`. Assert: `IOutbox.Enqueue` called once with `(order.Id, OutboxEventTypes.OrderDisputedCarrierSourced, <payloadJson>)`; payload deserializes to `OrderDisputedCarrierSourcedPayload(order.Id, CarrierReturned, ShipmentState.Returned)`; `logger.LogWarning` called with structured context including "carrier-sourced" + `order.ShippingCarrierRef`; result Success; **NO Order state mutation** (Order is still in `Shipped`).
2. **Idempotency_on_re_dispatch_emits_second_outbox_row** — call the Command twice (simulating two separate 6h sweeps both seeing Returned). Assert: `IOutbox.Enqueue` called twice (one per Command — the stub does NOT dedupe; T-0106 will handle dedupe in the consumer). Both invocations return Success. (Note: this is intentional MVP behaviour — visible duplicate outbox rows give ops a signal that the dispute keeps re-firing, which T-0106 will resolve via state-graph transition.)
3. **Order_not_found_returns_Permanent_OrderNotFound** — `GetByIdUnscopedAsync` returns null. Assert: result is `Permanent(OrderNotFound)`; `IOutbox.Enqueue` NOT called; `logger.LogWarning` NOT called.

#### SyncShipmentStatusesIntegrationTests (NEW, ~1 e2e)

`backend/src/Makables.IntegrationTests/Delivery/SyncShipmentStatusesIntegrationTests.cs` — Testcontainers postgres + faked `IShippingCarrier`.

1. **Function_e2e_Delivered_status_transitions_Order_and_writes_outbox_email_row** — seed 1 `Shipped + ZasilkovnaPickupPoint + ShippingCarrierRef = "PKT-1"` order; configure fake carrier to return `ShipmentStatus(Delivered, deliveredAt: clock.UtcNow.AddHours(-2))`. Run Function. Assert: DB Order row state == `Delivered`, `delivery_source == Carrier (2)`, `delivered_at == clock.UtcNow.AddHours(-2)` (carrier timestamp preserved); `outbox_events` has exactly 1 new row with `event_type = "order.delivered.customerEmail"` and `aggregate_id = order.Id`; counters in log: synced=1, delivered=1.

#### DisputeShipmentIntegrationTests (NEW, ~1 e2e)

`backend/src/Makables.IntegrationTests/Orders/DisputeShipmentIntegrationTests.cs` — Testcontainers postgres.

1. **DisputeShipment_e2e_emits_outbox_event_without_Order_mutation** — seed 1 `Shipped` order; dispatch `DisputeShipment.Command(order.Id, DisputeReason.CarrierFailed)` via MediatR. Assert: DB Order row state STILL == `Shipped` (no mutation per stub semantics); `outbox_events` has exactly 1 row with `event_type = "order.disputed.carrierSourced"` and `aggregate_id = order.Id`; payload deserializes to `OrderDisputedCarrierSourcedPayload(order.Id, CarrierFailed, ShipmentState.Failed)`.

#### OrderRepository.GetCarrierSyncableUnscopedReadOnlyAsync tests (NEW, ~2 tests)

`backend/src/Makables.IntegrationTests/Repositories/OrderRepositoryCarrierSyncableTests.cs` — Testcontainers postgres.

1. **Returns_only_Shipped_Zasilkovna_orders_with_carrier_ref** — seed 5 orders: (a) Shipped + Zasilkovna + carrierRef → INCLUDED; (b) Shipped + PersonalPickup + null carrierRef → EXCLUDED; (c) Shipped + Zasilkovna + null carrierRef → EXCLUDED; (d) Paid + Zasilkovna + carrierRef → EXCLUDED; (e) Delivered + Zasilkovna + carrierRef → EXCLUDED. Iterate result; assert exactly 1 Order returned (case a) with matching Id.
2. **Returns_full_Order_projection_with_required_fields** — seed 1 matching Order; iterate; assert returned Order has non-null `ShippingMethod`, `ShippingCarrierRef`, `CountryCode`, and correct `State`. (Defensive check that `.AsNoTracking()` does NOT strip required projections.)

### Docs

- **`docs/architecture/roles/order.md`** — extend the state-transition table row "Shipped → Delivered" to include the third source: "Shipped → Delivered via `MarkOrderDelivered.Command` (Source: Customer | Auto | **Carrier**) — Carrier source dispatched by `SyncShipmentStatusesFunction` (T-0078) on Packeta-reported Delivered state."
- **`docs/architecture/roles/shipping-carrier.md`** — extend Lifecycle section to mention T-0078's 6h timer-driven carrier-status sync + the three carrier-state → Command branches.
- **`docs/tickets/INDEX.md`** — PM flips T-0078 row to `**done**` after PR merge.

## Alternatives Considered

- **Option A — Defer DisputeShipment entirely to T-0106 (T-0078 only handles Delivered, silently skips Returned/Failed).** *Rejected per A.1* — loses ops visibility into dispute volume + character before T-0106's domain design is even drafted. The stub costs ~30 lines + 1 outbox event constant + 1 enum + 1 payload record; the operational visibility from day one is worth the surface area.
- **Option B — Transition the Order to a new `Disputed` state directly in T-0078.** *Rejected per A.1* — pre-empts T-0106's state-graph design decisions (does Disputed branch from Shipped or Delivered? Can Disputed transition back to Delivered if the package eventually arrives? Are there sub-states like AwaitingMakerResponse?). Locking these in before the domain has been deliberated would force T-0106 to either accept T-0078's choices or migrate state data.
- **Option C — Emit a raw outbox event from the Function directly (no Command wrapper).** *Rejected per A.1* — bypasses the MediatR pipeline that gives us automatic logging + validation + UoW discipline. The Command wrapper is cheap and aligns with ADR 0014's "every use case is one MediatR feature" rule.
- **Option D — Skip the MarkOrderDelivered dispatch when Packeta omits the DeliveredAt timestamp; retry next 6h sweep.** *Rejected per A.2* — worse customer UX. The customer sees their order stuck in Shipped for 6+ hours despite Packeta saying it's Delivered. The carrier's STATE is authoritative; the missing timestamp is a data-quality issue we can route around with a clock fallback + log signal.
- **Option E — Always use `clock.UtcNow` for the DeliveredAt, ignoring Packeta's timestamp when present.** *Rejected per A.2* — loses authoritative carrier timestamp when Packeta provides it, which matters for invoicing windows, dispute timing, and customer-visible "delivered at" surface. Preferring Packeta's truth when available + falling back to clock when absent is the symmetric correct choice.
- **Option F — Mark delivered now but defer the customer email until next sweep when timestamp is missing.** *Rejected per A.2* — over-engineered. Introduces state-vs-email desync risk (Order is Delivered but email is "queued for later") for zero customer-visible gain. The customer doesn't care about a 5-minute timestamp drift; they care about getting a "your order is delivered" email promptly.
- **Option G — Reuse the existing auto-deliver queue / Function (T-0077) and just add a Packeta-poll branch.** *Rejected per PM-absorbed §C (Function shape)* — couples the two delivery paths under one timer. Different schedules (auto-deliver at 7-day-window expiry; carrier-sync every 6h regardless of order age); different failure modes (auto-deliver is local DB-only; carrier-sync depends on Packeta); different perf characteristics. Separate Functions = independent observability + retry tuning.
- **Option H — Batch all Packeta status calls into a single multi-tracking API call.** *Rejected per PM-absorbed §C (batch size)* — Packeta's API supports per-shipment status queries; multi-tracking endpoints (if any) add response-parsing complexity for marginal latency gain at MVP volumes (<200 orders per sweep). Revisit when volume exceeds 1k orders per sweep.
- **Option I — Cap batch size + back-off between Packeta calls.** *Rejected per PM-absorbed §C (batch size)* — premature. ADR 0023 perf budget; MVP volumes are far below Packeta's rate limits. Add cap + back-off when 429s are observed (post-MVP follow-up ticket).
- **Option J — Add `OrderDisputedCarrierSourced` to `IsEmailSend` and route to send-email queue immediately.** *Rejected per PM-absorbed §C (no email at MVP)* — there is no email template + no template-translation row for "your shipment is disputed" yet. T-0106 owns the customer + admin email design. Emitting to the email queue prematurely would create dead-letter rows for missing templates.
- **Option K — Dedupe DisputeShipment.Handler when an outbox row for the same Order + Reason already exists.** *Rejected per PM-absorbed §C (idempotency)* — visible duplicate outbox rows give ops a signal that the dispute keeps re-firing across sweeps. T-0106 will resolve via state-graph transition (once an Order is Disputed, subsequent SyncShipmentStatusesFunction sweeps will short-circuit at the State check). Dedupe in the stub adds complexity for marginal benefit during the narrow MVP-to-T-0106 window.

## Out of scope

- **`MarkOrderDelivered.Command` + `OrderDeliverySource` enum + `Order.MarkAsDelivered` extension + `delivery_source` column + `order.delivered.customerEmail` outbox event + customer endpoint** — T-0076 (delivery-close bundle peer; T-0078 only consumes the new Command shape).
- **`AutoDeliverOrdersFunction` (7-day auto-deliver timer)** — T-0077 (delivery-close bundle peer; T-0078 mirrors its shape but does not share code).
- **Real `Disputed` state transition + `Order.OpenDispute(...)` method + `Order.State.Disputed` enum value + customer "your shipment is disputed" email + admin "dispute opened" email + admin dispute UI + dedupe logic in DisputeShipment.Handler** — T-0106 (post-MVP). T-0078 ships only the detection + outbox-emit STUB.
- **Customer-initiated + maker-initiated `DisputeReason` enum values** — T-0106.
- **`OutboxEventTypes.IsEmailSend` extension for `OrderDisputedCarrierSourced` + per-event-type switch branch in `IEmailSendService`** — T-0106.
- **Email template + translation rows for dispute notification** — T-0106.
- **`PersonalPickup` orders carrier sync** — never; PersonalPickup orders have no carrier ref to query. They rely on T-0076 (customer confirm) + T-0077 (7-day auto-deliver) for delivery close. The repository predicate explicitly filters them out.
- **Frontend customer-facing "your order was disputed by the carrier" surface** — T-0106 (paired with the email).
- **Maker-facing carrier-status dashboard** (live view of every Shipped order's Packeta state) — out of MVP. Maker sees the eventual Delivered/Disputed state once T-0078's sweep dispatches.
- **Batch-size cap + back-off on Packeta 429s** — post-MVP follow-up. Add when observed.
- **Packeta webhook (push-based status updates instead of poll)** — Packeta does not currently offer a webhook for Czech accounts. If it lands, a future ticket replaces or supplements the timer-driven sweep.
- **NSwag regen** — no public contract changes.

## Acceptance criteria

- **AC-1** Given the timer trigger fires at one of `0 0 0,6,12,18 * * *` UTC, when `SyncShipmentStatusesFunction` runs, then it iterates `IOrderRepository.GetCarrierSyncableUnscopedReadOnlyAsync(ct)` and the result includes only orders matching `State == Shipped AND ShippingMethod == ZasilkovnaPickupPoint AND ShippingCarrierRef != null`. Verified by the repository integration test that seeds 5 orders across all 5 predicate-matrix cells and asserts exactly 1 is returned.
- **AC-2** Given a Shipped + Zasilkovna order with `ShippingCarrierRef = "PKT-9"` and Packeta returns `ShipmentStatus(State: Delivered, DeliveredAt: timestamp T)`, when the Function processes the order, then it dispatches `MarkOrderDelivered.Command(OrderId, OrderDeliverySource.Carrier, T)` via `ISender.Send` exactly once. End-of-sweep counter `delivered` increments by 1.
- **AC-3** Given the same Shipped order but Packeta returns `ShipmentStatus(State: Delivered, DeliveredAt: null)`, when the Function processes it, then it dispatches `MarkOrderDelivered.Command(OrderId, OrderDeliverySource.Carrier, null)` AND logs `Warning` with message containing "without timestamp" AND order context (OrderId). T-0076's handler then falls back to `clock.UtcNow` for the DeliveredAt column.
- **AC-4** Given Packeta returns `ShipmentStatus(State: Returned)` for a Shipped order, when the Function processes it, then it dispatches `DisputeShipment.Command(OrderId, DisputeReason.CarrierReturned)` exactly once AND does NOT dispatch `MarkOrderDelivered`. End-of-sweep counter `disputed` increments by 1.
- **AC-5** Given Packeta returns `ShipmentStatus(State: Failed)` for a Shipped order, when the Function processes it, then it dispatches `DisputeShipment.Command(OrderId, DisputeReason.CarrierFailed)` exactly once. Counter `disputed` increments by 1.
- **AC-6** Given Packeta returns `ShipmentStatus(State: Created)` or `ShipmentStatus(State: InTransit)`, when the Function processes the order, then NO MediatR Command is dispatched AND `logger.LogDebug` is called with "still in transit". Counters: `delivered`, `disputed`, `failed` all unchanged for this Order.
- **AC-7** Given Packeta's `GetStatusAsync` returns `BusinessResult.Failure(Error.Transient(ShippingCarrierUnavailable))` for an Order, when the Function processes it, then it logs `Warning` with message containing "retrying next sweep" + OrderId + CarrierRef + error code AND increments `failed` counter AND continues to the next Order (no exception thrown by the Function). Verified by seeding 2 Orders where the first carrier-fails Transiently and the second succeeds: first → failed=1, second → delivered=1 (or disputed=1 depending on state); Function completes normally.
- **AC-8** Given `IShippingCarrierFactory.ResolveAsync` returns a Configuration failure for an Order's CountryCode, when the Function processes it, then it logs `Critical` with OrderId + CountryCode + error code AND increments `failed` counter AND continues to the next Order. ApplicationInsights surface inherited from the existing `ILogger<T>` wiring.
- **AC-9** Given `DisputeShipment.Command(OrderId, DisputeReason.CarrierReturned)` is dispatched against an existing Order, when the Handler runs, then (a) it logs `Warning` with structured context including OrderId, Reason, source = "carrier-sourced", and `order.ShippingCarrierRef`; (b) it enqueues exactly 1 outbox row with `event_type = "order.disputed.carrierSourced"` and `aggregate_id = order.Id` and payload deserializing to `OrderDisputedCarrierSourcedPayload(order.Id, CarrierReturned, ShipmentState.Returned)`; (c) Order state remains `Shipped` (no mutation); (d) result is `BusinessResult.Success(new DisputeShipmentResponse(order.Id, CarrierReturned))`.
- **AC-10** Given `DisputeShipment.Command` is dispatched against a non-existent OrderId, when the Handler runs, then it returns `BusinessResult.Failure(Error.Permanent(OrderNotFound))` AND `IOutbox.Enqueue` is NOT called AND `logger.LogWarning` is NOT called.
- **AC-11** Given an Order is already in `Delivered` state (race: the customer confirmed between sweeps and the Customer source already mutated the Order), when Packeta-sync dispatches `MarkOrderDelivered.Command(OrderId, Carrier, …)`, then T-0076's silent-success contract handles it: the Command returns Success no-op, does NOT re-emit `order.delivered.customerEmail`, does NOT overwrite `DeliverySource = Customer`. T-0078's counter `delivered` still increments (the dispatch itself was successful even though the handler was a no-op). Verified by the integration test that pre-seeds the Order in Delivered state then runs the Function.
- **AC-12** Given the Function completes a full sweep, when the final log line is emitted, then it matches the structured message template `"SyncShipmentStatuses completed: synced {Synced}, delivered {Delivered}, disputed {Disputed}, failed {Failed}"` with the four counters as integer parameters. Visible in ApplicationInsights for ops monitoring.
- **AC-13** Build clean. Unit tests: baseline (after T-0076 + T-0077 merge in the bundle) + ~11 new (~6 SyncShipmentStatusesFunctionTests + ~3 DisputeShipmentHandlerTests + ~2 OrderRepositoryCarrierSyncableTests). Integration tests: baseline + 2 new (SyncShipmentStatusesIntegrationTests + DisputeShipmentIntegrationTests). `node scripts/check-consistency.mjs` exit 0 (no new T1–T7 violations vs. the bundle's running baseline). Zero new `BusinessErrorMessage` codes (reuses `ShippingCarrier*` from T-0070 + `OrderNotFound` from T-0063). Zero new i18n keys. Zero new NSwag-exposed types.

## Technical notes

### Why two separate sweep Functions (auto-deliver vs. carrier-sync) instead of one merged sweep

The two paths have orthogonal triggers + perf profiles + failure modes. Auto-deliver (T-0077) fires when `AutoDeliverAt <= clock.UtcNow` — a local-DB-only sweep with constant-time per-Order cost (no external I/O). Carrier-sync (T-0078) fires every 6h regardless of order age — a Packeta-bound sweep with ~500ms-2s per-Order cost (network + adapter). Merging would force shared observability (one log line for two different concerns), shared retry tuning, and shared timer (you can't tune auto-deliver to fire daily and carrier-sync to fire 4×daily). The thin-Function pattern (ADR 0020) is cheap; separation is the symmetric correct choice.

### Why the DisputeShipment Handler does NOT mutate Order state at MVP

T-0106's state-graph design is undeliberated. Adding `Order.State.Disputed` or a `DisputedAt` column now would lock in choices T-0106 needs to make freely (does Disputed branch from Shipped? Does Delivered transition to Disputed if the package is later found damaged? Can Disputed transition back to Delivered?). The outbox event durably captures the detection event; T-0106's handler can read the outbox history when wiring the real transition. The stub costs nothing operationally — the Order stays in `Shipped`, the customer sees no change, ops sees the outbox row + the Warning log for triage.

### Why the DisputeReason enum is sized to exactly two values at T-0078

The Function only emits two Reasons: `CarrierReturned` (Packeta Returned state) and `CarrierFailed` (Packeta Failed state). Shipping all 4-5 reasons T-0106 will eventually need (CustomerInitiated, MakerInitiated, AdminInitiated, …) at T-0078 would pollute the domain with values that have no caller. The enum extends cleanly when T-0106 needs more values; the JSON payload format (`Reason: integer`) means new values do not break existing deserialization.

### Why the Function dispatches MediatR Commands (not just calls IOrderRepository directly)

The Function is a thin orchestrator. Putting state-transition logic (Order.MarkAsDelivered call) or outbox-enqueue logic (DisputeShipment effect) directly in the Function would bypass the MediatR pipeline (validation behavior, UoW behavior, logging behavior). The Command + Handler pattern means: (a) the per-Order operation is atomic (UoW commits Order mutation + outbox row in one transaction; failure rolls back); (b) it can be unit-tested in isolation without spinning up the Function host; (c) future callers (manual admin retry, alternate carrier integrations) can dispatch the same Command without reimplementing the side-effect.

### Why `IAsyncEnumerable<Order>` streaming instead of `List<Order>` batch

MVP volumes are small but the streaming pattern (mirroring T-0077) means there is no memory-bound failure mode if volume grows. Each Order is processed + released; the next Order is yielded from the DB cursor on demand. No buffering of carrier-fetched data; no risk of OOM on large sweeps. EF Core's `AsAsyncEnumerable()` over an `.AsNoTracking()` query is well-tested at scale.

### Why the silent-Success contract from T-0076 covers the customer-then-carrier race

The customer-confirm path (T-0076 endpoint) and the carrier-sync path (T-0078 Function) can both race to mark the same Order Delivered. T-0076's `MarkOrderDelivered.Handler` checks Order state on entry: if already Delivered, it returns `BusinessResult.Success` immediately without re-emitting the email outbox event and without overwriting `DeliverySource`. This means whoever dispatches the Command first "wins" the audit trail (DeliverySource records the first source), and subsequent dispatches are no-ops. T-0078 does not need any custom race handling — the contract handles it transparently.

## Files touched (expected)

### New

- `backend/src/Makables.Core.Domain/Orders/DisputeReason.cs`
- `backend/src/Makables.Core.Domain/Outbox/OrderDisputedCarrierSourcedPayload.cs`
- `backend/src/Makables.Core.AppServices/Features/Orders/DisputeShipment.cs`
- `backend/src/Makables.Infra.Functions/Delivery/SyncShipmentStatusesFunction.cs`
- `backend/src/Makables.Tests/Functions/Delivery/SyncShipmentStatusesFunctionTests.cs`
- `backend/src/Makables.Tests/AppServices/Features/Orders/DisputeShipmentHandlerTests.cs`
- `backend/src/Makables.IntegrationTests/Delivery/SyncShipmentStatusesIntegrationTests.cs`
- `backend/src/Makables.IntegrationTests/Orders/DisputeShipmentIntegrationTests.cs`
- `backend/src/Makables.IntegrationTests/Repositories/OrderRepositoryCarrierSyncableTests.cs`

### Modified

- `backend/src/Makables.Core.Domain/Outbox/OutboxEventTypes.cs` — add `OrderDisputedCarrierSourced = "order.disputed.carrierSourced"` constant. **NOT** added to `IsEmailSend` or any other classifier (T-0106 will route it).
- `backend/src/Makables.Core.Domain/Orders/IOrderRepository.cs` — add `GetCarrierSyncableUnscopedReadOnlyAsync(CancellationToken ct)` signature.
- `backend/src/Makables.Infra.Database/Repositories/OrderRepository.cs` — implement `GetCarrierSyncableUnscopedReadOnlyAsync` with `.AsNoTracking()` + predicate + `.AsAsyncEnumerable()`.
- `docs/architecture/roles/order.md` — extend the Shipped → Delivered transition row to include the Carrier source + T-0078 Function reference.
- `docs/architecture/roles/shipping-carrier.md` — extend Lifecycle section with the 6h timer-driven status sync + the three carrier-state → Command branches.

## Test plan reference

Inline above (see Scope > Tests). No separate `docs/test-plans/T-0078.md`.

## Status log

- 2026-06-08 `draft` by PM. Created from bundle plan (delivery-close T-0076 + T-0077 + T-0078). Reference precedents: T-0067 MarkOrderPaid (state-transition + outbox), T-0029 ProcessOutboxFunction (timer-trigger), T-0069 GenerateInvoiceFunction (queue-trigger), T-0070 IShippingCarrier seam (GetStatusAsync). Slice scope: timer-trigger Function (6h sweep of Shipped + Zasilkovna + carrierRef orders) + DisputeShipment.Command STUB (Warning log + outbox emit, NO Order state mutation at MVP) + new DisputeReason enum (CarrierReturned, CarrierFailed) + new OrderDisputedCarrierSourcedPayload + new OutboxEventTypes.OrderDisputedCarrierSourced constant + new IOrderRepository.GetCarrierSyncableUnscopedReadOnlyAsync.
- 2026-06-08 `draft → ready` by PM. User answered 2 blocking AskUserQuestion items per `/feature` workflow step 3: (**A.1**) dispatch DisputeShipment.Command STUB for Returned/Failed carrier statuses — Handler logs Warning + emits `order.disputed.carrierSourced` outbox event for future admin processing; NO Order state mutation at MVP; T-0106 wires the real Disputed state transition + customer + admin email; rejected defer-entirely-to-T-0106 (loses ops visibility) + transition-to-Disputed-directly (pre-empts T-0106 domain design) + raw-outbox-event-no-Command (bypasses MediatR pipeline); (**A.2**) DeliveredAt fallback to `clock.UtcNow` + log Warning when Packeta omits the timestamp — tolerant strategy: Packeta's Delivered STATUS is authoritative; missing timestamp is a carrier data-quality issue; Function passes `deliveredAtOverride = packetaResponse.DeliveredAt` (may be null) into `MarkOrderDelivered.Command`; T-0076 handler falls back to clock when override is null; rejected skip+retry (6h+ customer-visible delay) + always-use-clock (loses authoritative timestamp) + delivered-now-email-deferred (over-engineered). 12 PM-absorbed decisions captured in §C (timer schedule 0 0 0,6,12,18 * * *; unlimited batch at MVP; thin MediatR-dispatch Function mirroring AutoDeliverOrdersFunction; repository predicate State=Shipped + Method=ZasilkovnaPickupPoint + carrierRef NOT NULL; per-Order switch on ShipmentState; fail-continue isolation with Polly resilience inherited from T-0070; DisputeReason enum sized to 2 values; OrderDisputedCarrierSourcedPayload shape carrying raw ShipmentState for audit; outbox event NOT email-routed at MVP; end-of-Function structured log line "synced N delivered M disputed K failed L"; already-Delivered race handled by T-0076 silent-Success contract; no NSwag regen). 6 ADR-locked items in §B (ADR 0014 UoW pipeline + one-file feature; ADR 0017 IShippingCarrier contract; ADR 0020 thin Function wrapper + fail-continue + outbox-event-for-downstream; BusinessResult<T> for expected failures; globally-unique Response record name `DisputeShipmentResponse` per T-0070-T-0075 CI convention; per-event-type switch in outbox dispatcher). No `manual_steps` (timer schedule needs no manual step; queue + outbox table already provisioned). **Ready for dotnet-backend.** Implementer processes T-0076 → T-0077 → T-0078 sequentially in the same branch; all three ship in one PR.
