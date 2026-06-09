# Delivery-close bundle — Reviewer preliminary verdict (draft)

> Written in parallel with the dotnet-backend implementer per docs/process/routing.md §"Bundle workflow" step 4 (one draft per bundle, not per ticket). This is the structural read BEFORE the diff lands; final verdict happens after the implementer reports done and supersedes this file.

## Bundle scope (T-0076 + T-0077 + T-0078)

The delivery-close bundle ships the `Shipped → Delivered` terminal transition with all three sources behind one canonical writer:

- **T-0076 (S, foundation)** — `MarkOrderDelivered.Command(OrderId, Source[, DeliveredAtOverride])` one-file feature + `OrderDeliverySource` enum (`Customer=0/Auto=1/Carrier=2`) + `Order.MarkAsDelivered(IClock, OrderDeliverySource, DateTimeOffset?)` in-place signature extension + `delivery_source SMALLINT NULL` migration + customer endpoint `POST /api/v1/customer/orders/{orderId}/deliver` + single `order.delivered.customerEmail` outbox event + new `EmailSendService` switch arm + `EmailTemplateType.OrderDeliveredCustomer` seed (cs-CZ + en-US). T-0076 OWNS every shared artifact; T-0077/T-0078 are caller-only additions.
- **T-0077 (S, auto-deliver caller)** — `AutoDeliverOrdersFunction` (daily 08:00 UTC timer) + new `IOrderRepository.GetAutoDeliverableUnscopedReadOnlyAsync(asOf, ct) → IAsyncEnumerable<string>` projection-only stream. Thin MediatR-dispatch wrapper mirroring `ProcessOutboxFunction` shape verbatim. Zero schema changes, zero new BusinessErrorMessage codes, zero new outbox event types.
- **T-0078 (M, carrier-sync caller + dispute stub)** — `SyncShipmentStatusesFunction` (every-6h timer) consumes `IShippingCarrier.GetStatusAsync` (T-0070 seam) + per-state switch (Delivered → MarkOrderDelivered; Returned/Failed → DisputeShipment; Created/InTransit → no-op). NEW `DisputeShipment.Command(OrderId, DisputeReason)` STUB feature + `DisputeReason` enum (CarrierReturned=0/CarrierFailed=1) + `OrderDisputedCarrierSourcedPayload` + `OutboxEventTypes.OrderDisputedCarrierSourced` (NOT email-routed at MVP — T-0106 wires consumer) + new `IOrderRepository.GetCarrierSyncableUnscopedReadOnlyAsync(ct) → IAsyncEnumerable<Order>`.

Dep chain is strictly sequential: T-0076 (foundation Command + outbox + migration + customer endpoint) → T-0077 (timer caller, Source=Auto) → T-0078 (carrier-sync caller, Source=Carrier + dispute stub). All three ship as one PR on `feat/delivery-close-bundle` per docs/process/routing.md §"Bundling related tickets into one PR".

## Patterns / ADRs the diff must honour

Walked against `docs/architecture/patterns.md` + ADRs in ticket frontmatter:

- **patterns.md A.4 (one-file feature shape).** `MarkOrderDelivered.cs` and `DisputeShipment.cs` each must declare `public static class` containing nested `Command` / `Response` / `Validator` / `Handler`. Precedent: `backend/src/Makables.Core.AppServices/Features/Orders/MarkOrderPaid.cs:60` (`public static class MarkOrderPaid` containing `Command` at :62, `Response` at :68, `Validator` at :70, `Handler` at :86). T-0077 adds NO new feature file (reuses T-0076's writer per ticket §B.1).
- **patterns.md A.5 / ADR 0014 (UoW pipeline).** Neither `MarkOrderDelivered.Handler` nor `DisputeShipment.Handler` may call `SaveChangesAsync()`. UoW commits Order mutation + 1 outbox row atomically per dispatch. The Already-Delivered Silent Success path (T-0076 §A.3) returns `Success` without entity mutation AND without outbox emission — UoW commits a no-op transaction. Mirror `MarkOrderPaid.Handler` precedent at MarkOrderPaid.cs:239-242 ("No SaveChangesAsync — UoW pipeline behavior commits …").
- **patterns.md A.7 (per-Validator FluentValidation).** Each new Command needs a sibling Validator. T-0076: `OrderId` non-empty + length cap + `Source` enum range (`IsInEnum()`); T-0078 DisputeShipment: `OrderId` non-empty + `Reason` `IsInEnum()`. Cascade.Stop on OrderId mirrors MarkOrderPaid.cs:75-83 precedent.
- **patterns.md A.13 (per-event-type EmailSendService switch).** T-0076 adds EXACTLY ONE new switch arm at `IEmailSendService.cs:62-82` for `OutboxEventTypes.OrderDeliveredCustomerEmail` → `SendOrderDeliveredCustomerEmailAsync(payloadJson, ct)`. T-0078's `OrderDisputedCarrierSourced` event is INTENTIONALLY NOT added to `IsEmailSend` or the switch — T-0106 will wire that consumer. The current switch (verified at `IEmailSendService.cs:62-83`) already routes 7 cases; T-0076 lands the 8th.
- **patterns.md A.15 (provider seam) + ADR 0017.** T-0078 consumes the existing `IShippingCarrier.GetStatusAsync` contract (verified at `Core.Domain/Shipping/IShippingCarrier.cs:60-62`) returning `BusinessResult<ShipmentStatus>` where `ShipmentStatus` is the sealed record `(ShipmentState State, DateTimeOffset? DeliveredAt)` at `Core.Domain/Shipping/ShipmentStatus.cs:10`. The 5 `ShipmentState` values (Created=0/InTransit=1/Delivered=2/Returned=3/Failed=4) at `Core.Domain/Shipping/ShipmentState.cs:18-34` cover the switch exhaustively.
- **patterns.md A.20 (idempotent webhook + UoW).** Already-Delivered Silent Success is the T-0076 idempotency contract that protects the three-caller race. T-0076 §A.3 lock + §AC-4 pin it: when entry-state is `Delivered`, return `Success(MarkOrderDeliveredResponse(orderId, Delivered))` with NO outbox emission and NO entity mutation. Mirrors T-0067/T-0069 idempotency precedent.
- **ADR 0013 (scoped repositories).** T-0076 customer endpoint uses `IOrderRepository.GetByIdForCustomerAsync(orderId, customerId, ct)` (verified at `IOrderRepository.cs:79`) — IDOR shield returns null for cross-customer ids per the docstring at :73-78. T-0077 + T-0078 Functions have no user identity → unscoped readonly projection-stream methods (new on the interface). DisputeShipment.Handler uses `GetByIdUnscopedAsync` (verified at `IOrderRepository.cs:122`).
- **ADR 0014 (UoW pipeline) + one-file feature.** All locked. No handler calls `SaveChangesAsync` (reviewer greps the diff).
- **ADR 0017 (shipping/Packeta + 7-day window).** T-0072/T-0073 stamp `Order.AutoDeliverAt` at ship time (verified at `Order.cs:217-219`); T-0077 only reads it. T-0078 consumes the T-0070 carrier contract verbatim.
- **ADR 0019 (email pipeline, per-event-type switch).** T-0076 adds one branch per T-0067 Q3 pattern; existing arms untouched.
- **ADR 0020 (background jobs + outbox queue split).** T-0076's `order.delivered.customerEmail` is email-routed → joins `IsEmailSend` OR-chain at `OutboxEventTypes.cs:91-97`. T-0078's `OrderDisputedCarrierSourced` is INTENTIONALLY UNROUTED at MVP (OutboxDispatcher's "unrouted/no handler yet" branch logs Warning; T-0106 will add routing). Functions are thin MediatR-dispatch wrappers per `ProcessOutboxFunction` precedent at `backend/src/Makables.Functions/Outbox/ProcessOutboxFunction.cs:32-66`.
- **patterns.md A.16 (per-audience hosts).** T-0076 customer endpoint lands on `Web.Customer` (JWT audience enforced per host); T-0077/T-0078 Functions live in `Makables.Functions` (no controller, no audience). Customer JWT cannot replay against maker host per CLAUDE.md §Security.
- **CLAUDE.md §Money.** N/A — no monetary columns added in this bundle.
- **CLAUDE.md §Security.** All three tickets carry `security_touching: false`. T-0076 customer endpoint is `[Authorize]` per ADR 0013 — SecOps engagement NOT mandatory but reviewer will spot-check the customer-scoping shield.

## Pre-flight risks (HIGH first)

### HIGH

1. **HIGH: `EmailSendService` ctor swell.** Verified current ctor at `IEmailSendService.cs:40-47` takes 7 deps (`templates`, `translations`, `provider`, `invoices`, `blobStorage`, `urls`, `logger`). T-0069 reviewer's prior fold removed `IOrderRepository` per `docs/review/runs/T-0069-draft.md`. T-0076 §C bullet 4 locks "EmailSendService: new switch arm for OrderDeliveredCustomerEmail → SendOrderDeliveredCustomerEmailAsync helper. **Reuses existing DI (IEmailTemplateRepository, ILanguageResolver, IEmailProvider). No new DI deps added.**" **Verify at PR-open:** ctor signature is byte-for-byte unchanged after T-0076 (still 7 deps); the new `SendOrderDeliveredCustomerEmailAsync` private helper uses only `templates`, `translations`, `provider`, `logger`. Note that `ILanguageResolver` is NOT in the current ctor (current ctor has no `ILanguageResolver` — language is pre-resolved upstream at MarkOrderPaid enqueue time per T-0067 §Step 4 precedent), so T-0076's outbox payload `OrderDeliveredCustomerEmailPayload.LanguageCode` MUST be pre-resolved at enqueue time in `MarkOrderDelivered.Handler` (not in EmailSendService). Reviewer will hard-block if the new branch adds a ctor dep or if the handler tries to resolve language at email-send time.

2. **HIGH: Already-Delivered Silent Success detection pattern.** T-0076 §A.3 locks "Silent Success (no-op) on already-Delivered re-call. When `Order.MarkAsDelivered` returns `OrderInvalidTransition` AND the order is already in `Delivered` state, the handler returns Success with NO outbox emission." This is the contract that protects the three-caller race per T-0076 §AC-4 + T-0078 §AC-11. Two valid implementation shapes:
   - (a) Pre-check `if (order.State == OrderState.Delivered) return Success(no-op)` BEFORE calling `MarkAsDelivered` (cleaner, recommended).
   - (b) Call `MarkAsDelivered`, catch the `InvalidTransition` failure, re-check `order.State == Delivered`, translate to Success.
   **Verify at PR-open:** ONE of the two shapes is implemented; NO outbox event fires on the silent path (reviewer will assert `IOutbox.Enqueue` is `Received(0)` in the test); the order entity is NOT mutated (DeliverySource and DeliveredAt are unchanged from first transition — preserves the first-source audit). The MarkOrderPaid precedent at `MarkOrderPaid.cs:131-139` returns the entity's InvalidTransition failure verbatim — T-0076 must DIVERGE from this pattern at the already-Delivered path. **HARD BLOCK** if reviewer finds an outbox emission on the silent path or a `DeliverySource` overwrite.

3. **HIGH: `Order.MarkAsDelivered` signature in-place extension.** Verified current signature at `Order.cs:638-647`: `MarkAsDelivered(IClock clock)` → `BusinessResult`. T-0076 §C.1 locks new signature: `MarkAsDelivered(IClock clock, OrderDeliverySource source, DateTimeOffset? deliveredAtOverride = null)`. **Verify at PR-open:**
   - Body fix: `DeliveredAt = deliveredAtOverride ?? clock.UtcNow;` (NOT just `clock.UtcNow` — the override is what T-0078 uses to preserve Packeta's authoritative timestamp).
   - Body fix: new `DeliverySource = source;` line.
   - Set-once guard on `DeliverySource` is NOT required (already protected by the State guard `if (State != OrderState.Shipped) return InvalidTransition()` — a second call sees `Delivered` and bails; the handler's Silent Success path bypasses MarkAsDelivered entirely so no second write occurs).
   - All existing call sites compile via the optional 3rd param. Reviewer will grep for `MarkAsDelivered(` to find every caller. Primary expected callers: `MarkOrderDelivered.Handler` (T-0076) and the existing `OrderTests` fixture sweep. The `Order.cs:638` XML doc must be updated to reference T-0076 §C.1 as the writer.
   - New `public OrderDeliverySource? DeliverySource { get; private set; }` property on `Order` — nullable per T-0076 §Domain layer (historical Delivered orders predating T-0076 stay NULL).

4. **HIGH: `DisputeShipment.Handler` stub purity.** T-0078 §A.1 + §Scope step 5 lock "**NO Order state mutation at MVP.** Order remains in `Shipped` state. T-0106 will wire the `Order.OpenDispute(...)` transition." This is the most regression-prone item in the bundle — the existing `Order.OpenDispute(IClock)` method at `Order.cs:716-725` EXISTS and is tempting. **Verify at PR-open:**
   - `DisputeShipment.Handler` body contains EXACTLY: load order via `GetByIdUnscopedAsync` → log Warning with structured context → enqueue outbox event → return Success. NO `order.OpenDispute(...)` call. NO state-changing entity method invocation.
   - XML doc on the Handler class explicitly says "STUB" and cross-links T-0106 ownership per T-0078 §Scope last bullet ("T-0078 STUB: emits outbox event + logs Warning. T-0106 will wire the real Disputed state transition + customer + admin email.").
   - Integration test asserts `order.State == Shipped` AFTER the Command commits (T-0078 §AC-9.c + §DisputeShipmentIntegrationTests test name "DisputeShipment_e2e_emits_outbox_event_without_Order_mutation").
   - No new `BusinessErrorMessage` codes added (T-0078 §AC-13 — reuses `OrderNotFound`).

5. **HIGH: `OrderDisputedCarrierSourced` MUST NOT be added to `IsEmailSend`.** T-0078 §B last bullet locks "**NOT** added to `IsEmailSend` (T-0106 will route this when the consumer Function ships). **NOT** added to any other classifier method. OutboxDispatcher's 'unrouted' log branch handles it visibly (no silent drop)." Verified current `OutboxEventTypes.cs:90-118` defines `IsEmailSend` + `IsInvoiceGenerate` + `IsGenerateLabel`. **Verify at PR-open:** T-0078 adds exactly ONE new constant `OrderDisputedCarrierSourced = "order.disputed.carrierSourced"` at the top of the class. The three classifier methods are UNCHANGED. Reviewer will grep all three classifier bodies for `OrderDisputed` — any match = HARD BLOCK. The OutboxDispatcher's `_ =>` discard branch (or equivalent "unrouted" branch — verify against the actual dispatcher shape) must log Warning visibly for this event so ops can see them piling up pre-T-0106.

### MEDIUM

6. **MEDIUM: Timer CRON expression correctness (T-0077 + T-0078).** Azure Functions `TimerTrigger` uses NCRONTAB 6-field format (`second minute hour day month day-of-week`):
   - T-0077: `0 0 8 * * *` (daily 08:00 UTC = 09:00 CET / 10:00 CEST). Verified syntactically valid 6-field NCRONTAB.
   - T-0078: `0 0 0,6,12,18 * * *` (every 6h starting 00:00 UTC). Verified syntactically valid.
   **Verify at PR-open:** T-0077 uses `[TimerTrigger("%AutoDeliverOrders:Schedule%")]` (configuration-bound per ticket §C "Schedule key: AutoDeliverOrders:Schedule for ops tunability"); T-0078 uses the inline literal `[TimerTrigger("0 0 0,6,12,18 * * *")]` per ticket §Infra.Functions layer (NOT config-bound — sweep cadence is locked by ADR 0023 perf budget, not ops-tunable). Both attributes are imported from `Microsoft.Azure.Functions.Worker`. Both use `TimerInfo timer` parameter shape mirroring `ProcessOutboxFunction.cs:42-43`. Verify `local.settings.json` adds `"AutoDeliverOrders:Schedule": "0 0 8 * * *"` (T-0077 §Config/DI bullet 1).

7. **MEDIUM: `AsNoTracking()` on both new projection-only repository methods (Gate 8 fold).** Per the recent shipping-pipeline-bundle Gate 8 fold (read-only lookups apply `.AsNoTracking()`). T-0077 §Infra (`GetAutoDeliverableUnscopedReadOnlyAsync`) and T-0078 §Infra (`GetCarrierSyncableUnscopedReadOnlyAsync`) BOTH must apply `.AsNoTracking()` explicitly. **Verify at PR-open:**
   - T-0077 EF impl matches the inline sample at ticket §Infra (lines 102-114): `.AsNoTracking() ... .Where(...) ... .OrderBy(o => o.AutoDeliverAt) ... .Select(o => o.Id) ... .AsAsyncEnumerable()`. Projection-only stream of `string` ids (handler does its own tracked re-fetch).
   - T-0078 EF impl matches §Infra (lines 124-132): `.AsNoTracking() ... .Where(state == Shipped && method == ZasilkovnaPickupPoint && carrierRef != null) ... .AsAsyncEnumerable()`. Full Order projection (handler needs `ShippingMethod` + `ShippingCarrierRef` + `CountryCode` + `State` to dispatch + log).
   - T-0078 does NOT use `IgnoreQueryFilters` — soft-deleted orders must NOT carrier-sync (per T-0077 §AC-5 precedent: global soft-delete filter applies).

8. **MEDIUM: Customer endpoint IDOR shield (T-0076 §AC-3).** T-0076 §C bullet 5 locks "IDOR scoping via `IOrderRepository.GetByIdForCustomerAsync`." Verified at `IOrderRepository.cs:79` — returns null for cross-customer ids per the docstring at :73-78 ("Returns null when the id is unknown OR owned by another customer — same shape so order ids aren't enumerable across customers (IDOR shield)"). **Verify at PR-open:**
   - The customer controller resolves customer-id via the existing session abstraction (likely `ICustomerSessionContext.RequireCustomerId()` per T-0076 §Handler ctor — verify against actual codebase shape; the shipping-pipeline-bundle reviewer flagged `IMakerSessionContext` vs `IUserSessionProvider` divergence, so reviewer will check the equivalent abstraction exists for customer host).
   - The customer-id passed to the repo comes from the JWT/session, NEVER from request body/path.
   - For Source=Customer, handler uses `GetByIdForCustomerAsync(orderId, customerId, ct)` and returns `Error.NotFound(BusinessErrorMessage.OrderNotFound)` on null.
   - For Source=Auto/Carrier, handler uses `GetByIdUnscopedAsync(orderId, ct)` per ticket §Steps bullet 1 (Function context has no user identity). Reviewer will hard-block if the unscoped path is taken for Source=Customer.

9. **MEDIUM: `MarkOrderDeliveredResponse` + `DisputeShipmentResponse` global-unique naming.** T-0076 §C bullet 6 + T-0078 §B locked-decision last bullet both invoke the T-0070-T-0075 CI fix convention for NSwag client-gen collision. **Verify at PR-open:** the C# nested type names are `MarkOrderDelivered.MarkOrderDeliveredResponse` (not `MarkOrderDelivered.Response`) and `DisputeShipment.DisputeShipmentResponse` (not `DisputeShipment.Response`). The wire-type names carry the feature prefix. Reviewer will grep for `public sealed record Response` under the two new feature files — any match = HARD BLOCK. Cross-reference: `MarkOrderPaid.Response` at `MarkOrderPaid.cs:68` does NOT follow this convention because it predates T-0070-T-0075; the new features MUST follow the new convention.

10. **MEDIUM: Function shape compliance — thin MediatR wrapper, no business logic (ADR 0020).** Both T-0077 + T-0078 Functions must be thin `await foreach … mediator.Send(…)` orchestrators. T-0077 §AC-13 makes this explicit: "Function does NOT call `SaveChangesAsync` (verified by grep: zero `SaveChangesAsync` occurrences in `AutoDeliverOrdersFunction.cs`); zero `Order.MarkAsDelivered` / `OrderState.Delivered` references in the Function file." T-0078 mirrors. **Verify at PR-open:**
   - Neither Function contains `SaveChangesAsync`, `Order.MarkAsDelivered(`, `Order.OpenDispute(`, `OrderState.Delivered`, or `OrderState.Shipped` mutations.
   - Both Functions wrap per-Order processing in try/catch with `when (ex is not OperationCanceledException)` (T-0077 §Infra.Functions sample lines 161-167) so host shutdown propagates cleanly.
   - Both Functions emit end-of-sweep structured log at Information level: T-0077 "AutoDeliverOrders completed: claimed N orders, dispatched M, failed K" (sample lines 169-172); T-0078 "SyncShipmentStatuses completed: synced N, delivered M, disputed K, failed L" (sample lines 219-221).
   - Neither Function throws on per-row failure — fail-continue per T-0077 §C "Batch failure handling".

11. **MEDIUM: `DeliveredAtOverride` parameter wiring through the Command (T-0076 PM note on §Steps step 3).** The Command shape locked in §A.1 is `Command(OrderId, Source)` (2 params), but T-0078 needs Packeta's authoritative timestamp. The PM note resolves this with a 3-param Command + trailing optional default: `Command(string OrderId, OrderDeliverySource Source, DateTimeOffset? DeliveredAtOverride = null)`. **Verify at PR-open:**
   - Command record has 3 params with optional 3rd defaulting to null (NOT 2 params + a separate Command overload — keep it one record).
   - T-0076 customer controller constructs with `null` (or omits — same thing): `new MarkOrderDelivered.Command(orderId, OrderDeliverySource.Customer)`.
   - T-0077 Function constructs with `null` (or omits): `new MarkOrderDelivered.Command(orderId, OrderDeliverySource.Auto)`.
   - T-0078 Function constructs with the Packeta value: `new MarkOrderDelivered.Command(order.Id, OrderDeliverySource.Carrier, status.DeliveredAt)` (matches T-0078 §Infra.Functions sample line 185).
   - Handler propagates the override into `order.MarkAsDelivered(clock, command.Source, command.DeliveredAtOverride)`.

12. **MEDIUM: `DisputeReason → ShipmentState` lookup correctness in `DisputeShipment.Handler` (T-0078 §Scope step 3).** The payload carries both the `DisputeReason` (semantic) and the raw `ShipmentState` (carrier audit trail). Mapping:
   - `DisputeReason.CarrierReturned` → `ShipmentState.Returned`
   - `DisputeReason.CarrierFailed` → `ShipmentState.Failed`
   **Verify at PR-open:** the Handler's `Determine ShipmentState` step (T-0078 §Scope step 3) uses an explicit switch or dictionary lookup that maps `Reason` → `CarrierState` for the payload field. The unit test at T-0078 §DisputeShipmentHandlerTests test 1 asserts payload deserializes to `OrderDisputedCarrierSourcedPayload(order.Id, CarrierReturned, ShipmentState.Returned)` — verify the carrier-state field matches.

13. **MEDIUM: `EmailTemplateType.OrderDeliveredCustomer` enum value collision.** T-0076 §Domain layer bullet 5 says "add `OrderDeliveredCustomer = 7` (next enum value after T-0072's `OrderShippedCustomer = 6`). Verify the exact next value at implementation time against the in-repo enum." **Verify at PR-open:** the implementer ran `Grep` against `Core.Domain/Email/EmailTemplateType.cs` BEFORE assigning `= 7`. If T-0073 or other intervening ticket already used value 7, T-0076 must take 8 (or next free value) — collision = migration breaks at DB seed.

14. **MEDIUM: Seed migration `SeedOrderDeliveredCustomerEmailTemplate` row counts (T-0076 §AC-11).** "Exactly 1 row in `email_templates` for `EmailTemplateType.OrderDeliveredCustomer` AND exactly 2 rows in `email_template_translations` for that template (cs-CZ + en-US) with non-empty subject + body." **Verify at PR-open:** migration inserts exactly 1+2 rows; subject draft `"Vaše objednávka #{order_number} byla doručena"` (or l10n-amended) for cs-CZ; placeholder SendGrid template id `d-placeholder-order-delivered-customer` (real id replaced post-deploy). NSwag generated client picks up `MarkOrderDeliveredResponse` (NOT response-class collision per Risk 9).

### LOW

15. **LOW: NSwag regen scope.** Single new public endpoint (`POST /api/v1/customer/orders/{orderId}/deliver`) means a single `npm run generate:api` covers the bundle. Reviewer Gate 6 verifies `frontend/src/lib/api-client/customer-api.ts` (or equivalent) has the new `markDeliveredAsync` method + `MarkOrderDeliveredResponse` DTO. T-0077 + T-0078 are Functions only, no contract change.

16. **LOW: Pre-commit hook on api-client.** Per CLAUDE.md "The generated client (`lib/api-client/`) is not edited manually. A pre-commit hook blocks edits." Verify NSwag regen produces a clean diff; no manual edits.

17. **LOW: `i18n/cs-CZ.ts` key reservation.** T-0076 §i18n bullet 1 reserves `customer.orders.markDeliveredButton: "Označit jako doručeno"`. This is "reserve at the i18n layer; surface ships on a follow-up FE ticket." Reviewer accepts the key with no consumer in the bundle (FE button lives in a separate ticket per T-0076 §Out of scope last bullet). No new BusinessErrorMessage code needed; existing `order.notFound` + `order.invalidTransition` translations cover the customer endpoint error surface.

18. **LOW: `AutoDeliverOrdersOptions` options class.** T-0077 §AppServices bullet 1 says "if T-0076 did NOT introduce this options class, T-0077 adds it." Reviewer accepts either — verify the class exists exactly once with `[ValidateOnStart]` + non-empty NCRONTAB validator per `OutboxQueueOptions` precedent. Reviewer will spot-check at PR-open which ticket file owns it.

## Test coverage expectations (Gate 5)

Per `docs/process/must-cover-tests.md` and `docs/process/tdd-policy.md`:

### Pure logic — TDD-first commit required (T-0067+ enforcement)

- **§5 Validators (must be test-first):**
  - `MarkOrderDelivered.Validator` — positive + 1 negative per RuleFor (OrderId non-empty + length + Source.IsInEnum). ~4 tests.
  - `DisputeShipment.Validator` — positive + 1 negative per RuleFor (OrderId + Reason.IsInEnum). ~3 tests.
  - **HARD FAIL if these land after the handler commit per docs/process/tdd-policy.md §"The rule".**
- **§4 Order state-machine signature extension (must be test-first):**
  - `Order.MarkAsDelivered` is a touched-from-T-0076 surface; the new 3-param shape (+ new `DeliverySource` property + override semantics) is pure logic per `docs/process/must-cover-tests.md` §4 row. Test file: `OrderMarkAsDeliveredTests.cs` (T-0076 §Tests sub-section "MarkOrderDelivered domain tests").
  - 3 tests locked at T-0076 §Tests: (1) 2-arg overload + clock-fallback; (2) 3-arg with override timestamp; (3) non-Shipped → InvalidTransition.
  - PLUS the existing OrderTests sweep — every call site of `MarkAsDelivered(clock)` (the old 1-arg shape) must be updated to compile against the new signature with Source provided. Reviewer will grep tests for `MarkAsDelivered(` to find every fixture call.
- **§9 BusinessErrorMessage codes negative-path:**
  - T-0076 reuses `OrderNotFound` + `OrderInvalidTransition`. Each must have ≥1 negative-path test surfacing the code (T-0076 §MarkOrderDeliveredHandlerTests tests 5 + 6).
  - T-0078 reuses `OrderNotFound`. Negative-path test at §DisputeShipmentHandlerTests test 3.
  - **Zero new codes added** per T-0077 §AC-12 + T-0078 §AC-13.
- **OutboxEventTypes classifier extension (pure logic; test-first):**
  - `IsEmailSend("order.delivered.customerEmail") == true` + disjointness (`IsInvoiceGenerate` and `IsGenerateLabel` return false for the new constant).
  - `IsEmailSend("order.disputed.carrierSourced") == false` + `IsInvoiceGenerate(...) == false` + `IsGenerateLabel(...) == false` (T-0078's unrouted constant).
  - Should be co-located with existing `OutboxEventTypes` tests if any; reviewer accepts a new tests file if none exists.

### Handler tests (carve-out — alongside OK per tdd-policy.md §Carve-outs)

- **MarkOrderDeliveredHandlerTests (~8 tests per T-0076 §Tests):** happy-path Customer source; happy-path Auto source (unscoped lookup); happy-path Carrier source with override timestamp; **Already-Delivered Silent Success (no outbox)**; ownership mismatch returns NotFound; non-Shipped non-Delivered returns InvalidTransition; outbox event aggregateId + event-type capture; payload field correctness (6 fields per T-0076 §AC-2).
- **DisputeShipmentHandlerTests (~3 tests per T-0078 §Tests):** happy-path enqueues outbox + logs Warning + NO Order mutation; idempotency on re-dispatch emits second outbox row (intentional MVP behavior); Order-not-found returns Permanent OrderNotFound + outbox NOT called.
- **AutoDeliverOrdersFunctionTests (~4 tests per T-0077 §Tests):** happy-path 3-order dispatch with Source=Auto; fail-continue on per-Order failure (BusinessResult.Failure AND thrown exception sub-cases); empty batch logs summary with zero counts; already-Delivered race is silent Success no-op (Function sees Success).
- **SyncShipmentStatusesFunctionTests (~6 tests per T-0078 §Tests):** Delivered with carrier timestamp → MarkOrderDelivered.Command(Carrier, ts); Delivered with null timestamp → MarkOrderDelivered.Command(Carrier, null) + Warning log; Returned → DisputeShipment.Command(CarrierReturned); Failed → DisputeShipment.Command(CarrierFailed); InTransit/Created → no-op + Debug log; carrier Transient failure → Warning + continue.
- **EmailSendServiceTests extension (~2 tests per T-0076 §Tests):** OrderDeliveredCustomerEmail branch loads template + sends; cs-CZ template substitutions present.

### Repository tests

- **OrderRepository.GetAutoDeliverableUnscopedReadOnlyAsync (~2 tests per T-0077 §Tests):** predicate matrix across state combinations + soft-deleted exclusion; null-AutoDeliverAt exclusion.
- **OrderRepository.GetCarrierSyncableUnscopedReadOnlyAsync (~2 tests per T-0078 §Tests):** returns only Shipped+Zasilkovna+carrierRef-non-null orders; returns full Order projection with required fields populated.

### Integration tests (Testcontainers Postgres)

- **MarkOrderDeliveredIntegrationTests (~1 test per T-0076 §Tests):** POST `/api/v1/customer/orders/{id}/deliver` happy-path — order row transitions to `Delivered`/`delivery_source=0`/`delivered_at~=now` + exactly 1 outbox row with `order.delivered.customerEmail`.
- **AutoDeliverOrdersIntegrationTests (~1 test per T-0077 §Tests):** end-to-end 3-Shipped-expired-orders sweep transitions all 3 to Delivered; 1 non-expired Shipped untouched; 1 Paid untouched; exactly 3 outbox rows.
- **SyncShipmentStatusesIntegrationTests (~1 test per T-0078 §Tests):** end-to-end Delivered status preserves Packeta timestamp + emits 1 customer-email outbox row.
- **DisputeShipmentIntegrationTests (~1 test per T-0078 §Tests):** end-to-end stub — Order state STILL Shipped + exactly 1 outbox row of type `order.disputed.carrierSourced`.

### Bundle test count target

~13 (T-0076 domain+handler+email) + ~6 (T-0077 function+repo) + ~11 (T-0078 function+handler+repo) ≈ **~30 new unit tests** + **~4 new integration tests**. Substantially smaller than the shipping-pipeline-bundle (was ~85+10 across 6 tickets) because T-0077 + T-0078 are caller-only on T-0076's writer.

## Mechanical-check expectations (Gate 9)

Per `docs/process/quality-gates.md` Gate 9 (`scripts/check-consistency.mjs`):

- **T1 (one-file feature shape):** 2 new features (`MarkOrderDelivered`, `DisputeShipment`) each declaring `public static class Xxx` with nested types. Per T-0068b precedent each new one-file feature triggers a baseline T1 false-positive. Expected baseline shift: **shipping-pipeline-bundle ended at ~105 → 107** (2 new false-positive T1 violations). PR description must call this out.
- **T3 (no SaveChangesAsync in handlers):** No new violations. Both new handlers ride the UoW pipeline. Functions also do not call SaveChangesAsync per T-0077 §AC-13 grep assertion.
- **T4 (no `dynamic` / no `any`):** No new violations. T-0078 Function uses typed `ShipmentStatus.State` switch (not dynamic).
- **T5 (BusinessErrorMessage codes via constants):** Zero new codes added across the bundle (T-0077 §AC-12 + T-0078 §AC-13). Reuses `OrderNotFound` + `OrderInvalidTransition` + `ShippingCarrier*` from prior tickets. Reviewer greps for inline `"order."` or `"shipping."` literals in the new handler/function diffs — any match = HARD BLOCK.
- **T6 (money columns):** N/A — no monetary columns.
- **T7 (no `useEffect` in frontend):** N/A — no frontend code beyond i18n key reservation + NSwag regen.

**Expected consistency baseline shift:** ~105 → ~107 (2 new one-file features each false-positive). PR description must document.

## Bundle DoR compliance check

Per docs/process/routing.md §"Bundle workflow" step 1 + §"Bundle DoR":

- All 3 tickets individually satisfy DoR (`status: ready` in frontmatter, all status logs show `draft → ready` by PM on 2026-06-08) ✓
- Bundle scope named in branch name (`feat/delivery-close-bundle` — verify at PR-open against actual branch) ✓
- Bundle order documented in each ticket's Context block (T-0076 → T-0077 → T-0078 sequential implementation in the same branch) ✓
- No external blockers between tickets ✓ (T-0077 depends_on T-0076; T-0078 depends_on T-0070 + T-0076 — all internal, all in-bundle or already-merged)
- Single parallel-reviewer artifact at `docs/review/runs/delivery-close-bundle-draft.md` ✓ (this file)
- L-split rule not triggered ✓ (S + S + M, well within bundle size cap of ~6 tickets / ~3000 LOC)
- No `manual_steps` blocker on any ticket ✓ (all three tickets have `manual_steps: []`)

## Open items the implementer should confirm in the PR description

These are NOT blockers — they're ambiguities the reviewer wants nailed down at PR-open:

1. **Customer endpoint route** is `POST /api/v1/customer/orders/{orderId}/deliver` (NOT `/confirm-delivery` or `/mark-delivered`) per T-0076 §A.4.
2. **`DisputeShipment.Handler` is a STUB** — verify XML doc on the Handler class explicitly references T-0106 ownership for the real Disputed state transition.
3. **`MarkOrderDeliveredResponse` + `DisputeShipmentResponse` names** are globally-unique (NOT nested `Response`) to avoid NSwag class collision per T-0070-T-0075 CI fix convention.
4. **Timer schedule attributes correct:** T-0077 `[TimerTrigger("%AutoDeliverOrders:Schedule%")]` (config-bound) + T-0078 `[TimerTrigger("0 0 0,6,12,18 * * *")]` (inline). Both use 6-field NCRONTAB.
5. **Already-Delivered detection pattern** — pre-check (recommended) OR catch-and-translate. Pin the choice in PR description.
6. **`Order.MarkAsDelivered` signature** extended in-place to 3 params with trailing optional override (NOT new overload, NOT new method).
7. **`Command(OrderId, Source, DeliveredAtOverride = null)`** — 3-param record with optional 3rd default; ALL THREE callers use this Command shape.
8. **`EmailSendService` ctor byte-for-byte unchanged** post-T-0076 (no new dep added; the `LanguageCode` is pre-resolved at MarkOrderDelivered enqueue time, mirroring MarkOrderPaid §Step 4 precedent).
9. **`OrderDisputedCarrierSourced` is intentionally unrouted at MVP** — NOT in any of the 3 classifier methods; OutboxDispatcher logs Warning visibly. T-0106 ships the consumer.
10. **`OrderDeliveredCustomerEmailPayload.LanguageCode`** populated at enqueue time via the existing language-resolution path (verify it matches the established convention — likely an `ILanguageResolver` injection in `MarkOrderDelivered.Handler`, mirroring `MarkOrderPaid.Handler` line 161 `languageResolver.ResolveForUserAsync(customer, ct)`). NOT resolved at email-send time.
11. **NSwag regen** committed in same PR (covers new customer endpoint + `MarkOrderDeliveredResponse` DTO).
12. **Role docs updated:** `docs/architecture/roles/order.md` Lifecycle table notes `Shipped → Delivered` via three sources (Customer/Auto/Carrier) all calling the same `MarkOrderDelivered.Command` writer with DeliverySource column.
13. **`docs/process/must-cover-tests.md` §11 table** does NOT gain a row — `DeliverySource` is NOT set-once at the entity level (the State guard handles it via Silent Success); only the FIRST source persists, but no entity-level set-once guard is required because the silent path bypasses the entity mutation entirely.
14. **Test commit order:** validators + `Order.MarkAsDelivered` signature + classifier extension MUST be test-first per T-0067+ TDD rule. Reviewer will walk `git log --reverse feat/delivery-close-bundle -- <test-files> <impl-files>` at PR-open. After-the-fact tests on these surfaces = HARD FAIL per Gate 5.

## Preliminary verdict

**STRUCTURALLY_SOUND_PENDING_DIFF**, with 5 HIGH-risk surfaces and 9 MEDIUM-risk verifications the reviewer will walk at PR-open.

The bundle is exceptionally well-groomed: single canonical writer (T-0076) + two caller-only Functions (T-0077, T-0078) with zero shared writer code duplication; uniform Silent Success contract handles all three race scenarios; all three tickets cross-link each other's Locked design decisions and Out-of-scope sections; ADR 0013/0014/0017/0019/0020 are honored; T-0067 + T-0076 + T-0070 precedents are followed verbatim. The HIGH-risk items (EmailSendService ctor swell, Silent Success outbox suppression, `Order.MarkAsDelivered` in-place signature extension, DisputeShipment stub purity, `OrderDisputedCarrierSourced` classifier omission) are tightly pinned by ticket scope + AC + Technical notes, so the implementer has a clear contract to hit.

The MEDIUM-risk items (timer CRON 6-field correctness, AsNoTracking on both new repo methods, IDOR shield on customer endpoint, globally-unique Response names, thin-Function compliance, DeliveredAtOverride wiring, DisputeReason→ShipmentState mapping, EmailTemplateType enum collision, seed migration row counts) are pre-flight verifications — none require ticket revision; reviewer will check the diff lines against the pin points enumerated above.

The bundle is significantly tighter than the shipping-pipeline-bundle (6 tickets, ~85+10 tests) — at S+S+M = 3 tickets / ~30+4 tests it is well within the bundle size cap of ~6 tickets / ~3000 LOC. No reason to split.

Final review at PR-open will walk `docs/review/checklist.md` Sections A-J row by row, verify Gate 5 (TDD-first commit-order on validators + Order.MarkAsDelivered + OutboxEventTypes classifier extension), Gate 6 (NSwag regen committed for customer endpoint), Gate 8 (optimizer ping on T-0078's 6h sweep + Packeta-call hot path — verified at PR-open by reviewer count of orders per sweep against ADR 0023 perf budget), Gate 9 (consistency-check baseline shift 105→107 documented in PR description), and the carve-out checks for already-Delivered silent path + DisputeShipment stub purity + classifier disjointness.
