---
id: T-0074
title: GenerateLabelFunction (queue-triggered) — fetches Packeta label, stores in blob
status: ready
size: M
owner: dotnet-backend
created: 2026-06-08
updated: 2026-06-08
depends_on: [T-0042, T-0070, T-0072]
blocks: [T-0075]
user_stories: [US-maker-0009]
adrs: [0014, 0017, 0020]
phase: 4
manual_steps: []
security_touching: false
layers: [appservices, infra-functions, infra-database]
---

# T-0074 — GenerateLabelFunction (queue-triggered) — fetches Packeta label, stores in blob

## Context

Closes the proactive half of the maker shipping-label loop. T-0072 ships the `Order.Ship(...)` state transition for Zásilkovna orders, which atomically (a) calls `IShippingCarrier.CreateShipmentAsync`, (b) writes `Order.ShippingCarrierRef` + `ShippingCarrierTrackingUrl`, and (c) enqueues a `shipping.generateLabel.async` outbox event. T-0074 wires the queue-triggered Function that dispatches a new `FetchAndStoreShippingLabel.Command` via MediatR, which calls `IShippingCarrier.GetLabelPdfAsync(carrierRef)` and stores the resulting PDF stream at the T-0070-locked blob path `invoices/{cc}/orders/{orderId}/label.pdf`.

The slice is the **non-mutating, side-effect-only half** of label provisioning. NO Order state changes (the order is already in `Shipped` after T-0072). NO outbox events emitted (this is a leaf consumer). NO customer-facing surface (T-0075 ships the maker download endpoint; T-0086 surfaces the URL in maker UI). The blob's existence at the deterministic path IS the only state — T-0075 reads it (with Packeta fallback on blob-miss). This deliberate decoupling means a sustained Packeta outage stalls the outbox row at 6 attempts; the order stays in `Shipped`, the customer keeps the tracking URL, and only the maker's "Download label" button degrades to the live-Packeta fallback path T-0075 will ship.

The flow end-to-end after T-0074 lands:

1. Maker submits `POST /api/v1/maker/orders/{id}/ship` → T-0072's `ShipOrder.Handler` atomically transitions `Order.Paid → Shipped`, persists `ShippingCarrierRef` + `ShippingCarrierTrackingUrl`, and enqueues outbox event `shipping.generateLabel.async` with payload `{ OrderId }`.
2. `ProcessOutboxFunction` (T-0029) loads due events and `OutboxDispatcher.DispatchDueAsync` classifies by event type; the new `IsShippingGenerateLabel(eventType)` branch routes to `IOutboxQueuePublisher.PublishGenerateLabelAsync(outboxEventId, ct)` (publisher impl added in T-0072 per its scope; T-0074 only consumes).
3. **NEW (T-0074):** `shipping.generateLabel.async` event lands on `OutboxQueues:GenerateLabelQueueName` (default `"generate-label"`) queue → `GenerateLabelFunction` runs → loads outbox row → deserializes `ShippingGenerateLabelOutboxPayload` → dispatches `FetchAndStoreShippingLabel.Command(payload.OrderId)` via MediatR.
4. **NEW (T-0074):** `FetchAndStoreShippingLabel.Handler` (a) loads `Order` via `GetByIdUnscopedAsync` (Function context has no user identity); (b) verifies `Order.ShippingCarrierRef` is non-null (defence: T-0072 should have set it; if not, return Permanent); (c) HEAD-checks blob `invoices/{Order.CountryCode}/orders/{OrderId}/label.pdf` for idempotency; (d) on blob-exists: returns success without calling Packeta; (e) on blob-miss: resolves `IShippingCarrier` via factory by `Order.CountryCode`, calls `GetLabelPdfAsync(ShippingCarrierRef)`, uploads the Stream to the same blob path with `Content-Type: application/pdf`, returns success.
5. T-0075 (downstream) ships `GET /api/v1/maker/orders/{id}/label` which streams the blob to the maker (with live Packeta fallback if HEAD-check finds blob missing).

The central design choices are **HEAD-check idempotency + silent customer surface + no Order state mutation** (all PM-absorbed; mirror T-0069 exactly). The Function is a ~15-line thin MediatR-dispatch wrapper following the T-0069 `GenerateInvoiceFunction` precedent verbatim.

This ticket is part of the shipping-pipeline bundle (T-0070 through T-0075); all six tickets ship in a single PR per bundle convention. T-0074 has hard dependencies on T-0070 (interface + blob path + error codes), T-0072 (outbox event type registration + publisher method + queue config), and T-0042 (blob storage abstraction).

## Locked design decisions

Captured per `docs/process/deliberation.md`. T-0074 had **zero user-input dimensions** at `/feature` step 3 — all design choices flowed from precedents already locked at T-0069 (GenerateInvoiceFunction shape, thin MediatR-dispatch wrapper, idempotency via deterministic side-effect lookup) and T-0070 (label blob path, carrier interface, error codes). Decisions documented below are PM-absorbed.

### A. User-locked at /feature step 3 (non-negotiable)

No user-input dimensions surfaced for T-0074 — all design choices flowed from precedents already locked at T-0069 (GenerateInvoiceFunction) and T-0070 (locked blob path + locked carrier interface).

### B. ADR-locked (per ADR 0014, 0017, 0020 — no relitigation)

- **One-file feature shape (per ADR 0014).** `FetchAndStoreShippingLabel.cs` ships as a single file under `Core.AppServices/Features/Shipping/` containing nested `Command`, `Response`, `Validator`, `Handler`. No separate per-class files.
- **UoW pipeline behavior commits (per ADR 0014).** `FetchAndStoreShippingLabel.Handler` **NEVER** calls `SaveChangesAsync`. The `UnitOfWorkPipelineBehavior` commits on success exit. Although T-0074 introduces no Order state mutation in the happy path (the blob is the only side-effect), the handler still flows through the pipeline so future Order metadata additions (`LabelGeneratedAt`, etc., if a downstream ticket adds them) inherit the discipline automatically.
- **ValidationPipelineBehavior runs first (per ADR 0014).** `Validator` validates `OrderId != Guid.Empty` before the handler runs. Empty Guid → `BusinessResult.Failure(Error.Permanent(ValidationFailed))` per existing pipeline contract; queue retry policy fast-stalls.
- **Queue-triggered Function = thin MediatR-dispatch wrapper (per ADR 0020).** Mirrors T-0069 `GenerateInvoiceFunction` and T-0029 `SendEmailFunction` precedents. ~15-line shape: `[QueueTrigger]` → load outbox row → deserialize payload → `mediator.Send(Command)` → throw on failure to trigger queue retry; log on success. No business logic in the Function.
- **Function throws on Command failure (per ADR 0020).** Azure Functions queue trigger signals retry via thrown exceptions. The Function MUST re-throw on `!result.IsSuccess` so the queue retry policy fires for Transient errors and dead-letters on Permanent. Returning normally would silently swallow failures.
- **IShippingCarrier interface contract (per ADR 0017 + T-0070 locked decision B).** `GetLabelPdfAsync(string carrierRef, CancellationToken ct) → Task<BusinessResult<Stream>>`. Caller (this handler) disposes the returned Stream after upload. Error classification is per ADR 0016 §A.14: 5xx/timeout → Transient(ShippingCarrierUnavailable); 4xx address-id → Permanent(ShippingCarrierAddressIdNotFound); 401/403 → Configuration(ShippingCarrierConfigurationError).
- **Blob path = `invoices/{cc}/orders/{orderId}/label.pdf` (per T-0070 locked decision A.7).** Flat, reuses `BlobContainer.Invoices`. CountryCode segment is `Order.CountryCode` (lowercase per existing convention — verify against T-0068b precedent during implementation; if T-0068b uses uppercase, match it exactly).
- **Error code reuse (per ADR 0017 + T-0070 §B).** All Packeta-surface error codes already exist on `BusinessErrorMessage` from T-0070 (`ShippingCarrierUnavailable`, `ShippingCarrierInvalidWeight`, `ShippingCarrierAddressIdNotFound`, `ShippingCarrierConfigurationError`). T-0074 introduces ZERO new codes.
- **Outbox event type reuse (per T-0067 naming convention + T-0072 registration).** `OutboxEventTypes.ShippingGenerateLabel = "shipping.generateLabel.async"` is registered by T-0072 in the same PR. T-0074 only consumes the constant.

### C. PM-absorbed (no user input needed)

- **Idempotency:** handler pre-check (HEAD-check on blob existence before Packeta fetch). Mirrors T-0069 IssueInvoice.Handler's deterministic idempotency pattern (step 3 lookup). Saves Packeta API quota on retries. Cheap blob HEAD (~10ms) << Packeta GET (~1-2s).
- **Customer-facing error surfacing:** silent. Mirrors T-0069's invoice generation — label is a proactive background job. If Packeta has a sustained outage, the outbox retry policy stalls after 6 attempts; ops intervenes. Customer never sees a "your label is broken" surface.
- **Order state mutation:** none. Label generation is an async side-effect of T-0072's Order.Ship transition. The blob's existence at deterministic path `invoices/{cc}/orders/{orderId}/label.pdf` is the only state. T-0075 reads this blob.
- **Function shape:** thin MediatR-dispatch wrapper (~15 lines), mirroring T-0069 GenerateInvoiceFunction exactly.
- **Command:** new `FetchAndStoreShippingLabel.Command(OrderId)` one-file feature. Handler steps: (1) GetByIdUnscopedAsync; (2) verify ShippingCarrierRef non-null; (3) HEAD-check blob at deterministic path; (4) if exists, return success (idempotent); (5) IShippingCarrierFactory.ResolveAsync(countryCode); (6) carrier.GetLabelPdfAsync(carrierRef) -> Stream; (7) IBlobStorageClient.UploadAsync(BlobContainer.Invoices, "{cc}/orders/{orderId}/label.pdf", stream, "application/pdf"); (8) return Response.
- **Idempotency safety on partial failure:** if step 6 succeeds + step 7 fails, retry's HEAD-check at step 3 finds no blob → re-fetches. If step 7 succeeds + handler crashes before returning success, the same retry's HEAD-check finds the blob → returns success without re-fetching. Net: at-most-once Packeta fetch under normal cases; at-least-once delivery semantic preserved.
- **Queue config:** `OutboxQueues:GenerateLabelQueueName` default `"generate-label"` already shipped by T-0072. T-0074 just consumes from that queue.
- **No Order state mutation = no OutOfSync risk:** if blob upload fails permanently, Order is still in Shipped state, customer still sees tracking URL, only maker label download (T-0075) is affected (T-0075 falls back to Packeta on blob-miss).

## Scope

### AppServices layer

- **`Core.AppServices/Features/Shipping/FetchAndStoreShippingLabel.cs`** — new one-file feature containing nested types:
  - **`Command`** — `public sealed record Command(Guid OrderId) : IRequest<BusinessResult<Response>>;`
  - **`Response`** — `public sealed record Response(string BlobPath);` — returned for logging only (Function logs the path on success).
  - **`Validator`** — `internal sealed class Validator : AbstractValidator<Command>` with `RuleFor(c => c.OrderId).NotEqual(Guid.Empty);`.
  - **`Handler`** — `internal sealed class Handler(IOrderRepository orders, IShippingCarrierFactory carrierFactory, IBlobStorageClient blobStorage, ILogger<Handler> logger) : IRequestHandler<Command, BusinessResult<Response>>`. Primary-constructor DI per project convention. Handler body executes these steps in order:
    1. **Load order unscoped:** `var order = await orders.GetByIdUnscopedAsync(request.OrderId, ct);` (Function context has no user identity → unscoped lookup is correct; matches T-0067/T-0068b precedent). If `order is null` → return `BusinessResult.Failure<Response>(Error.Permanent(BusinessErrorMessage.OrderNotFound));` (reuse existing error code from T-0063).
    2. **Verify carrier ref present:** if `string.IsNullOrWhiteSpace(order.ShippingCarrierRef)` → return `BusinessResult.Failure<Response>(Error.Permanent(BusinessErrorMessage.ShippingCarrierConfigurationError));` (defence — T-0072 should have set it before enqueueing; this branch is a guardrail not an expected path).
    3. **Compute blob path:** `var blobPath = $"{order.CountryCode}/orders/{order.Id}/label.pdf";` (lowercase country code if `Order.CountryCode` is uppercase — match T-0068b convention; document the exact casing in the technical notes below). Container is `BlobContainer.Invoices`.
    4. **HEAD-check idempotency:** `var exists = await blobStorage.ExistsAsync(BlobContainer.Invoices, blobPath, ct);` — if `IBlobStorageClient.ExistsAsync` does not yet exist on the interface from T-0042, add the single method here as part of the T-0074 scope (signature: `Task<bool> ExistsAsync(BlobContainer container, string path, CancellationToken ct)`). If it returns `true`: log `"FetchAndStoreShippingLabel: blob already exists at {BlobPath} (idempotent skip)"` and return `BusinessResult.Success(new Response(blobPath));`.
    5. **Resolve carrier:** `var carrierResult = await carrierFactory.ResolveAsync(order.CountryCode, ct);` — if `!carrierResult.IsSuccess` return `BusinessResult.Failure<Response>(carrierResult.Error!);` (propagate Configuration error verbatim).
    6. **Fetch PDF stream:** `var pdfResult = await carrierResult.Value!.GetLabelPdfAsync(order.ShippingCarrierRef!, ct);` — if `!pdfResult.IsSuccess` return `BusinessResult.Failure<Response>(pdfResult.Error!);` (propagate Transient/Permanent/Configuration verbatim from the carrier adapter).
    7. **Upload blob:** `await using var stream = pdfResult.Value!;` then `await blobStorage.UploadAsync(BlobContainer.Invoices, blobPath, stream, contentType: "application/pdf", ct);`. Wrap in try/catch for blob-layer exceptions:
       - Catch `RequestFailedException` (Azure.Storage.Blobs) or equivalent with status 5xx → `Error.Transient(BusinessErrorMessage.ShippingCarrierUnavailable)` (reuse — blob-side transient surfaces under the same outbox-retry semantics).
       - Catch any other unexpected exception → `Error.Permanent(BusinessErrorMessage.ShippingCarrierConfigurationError)` (e.g., container missing, auth misconfig). Log `Critical` with structured context.
    8. **Return success:** `logger.LogInformation("FetchAndStoreShippingLabel: stored label for order {OrderId} at {BlobPath}", order.Id, blobPath);` then `return BusinessResult.Success(new Response(blobPath));`.

### Infrastructure / Functions layer

- **`Infra.Functions/Outbox/GenerateLabelFunction.cs`** — new queue-triggered Function. Mirrors T-0069 `GenerateInvoiceFunction` shape **verbatim**:
  ```csharp
  public sealed class GenerateLabelFunction(
      IOutboxRepository outbox,
      ISender mediator,
      ILogger<GenerateLabelFunction> logger)
  {
      [Function(nameof(GenerateLabelFunction))]
      public async Task RunAsync(
          [QueueTrigger("%OutboxQueues:GenerateLabelQueueName%")] string outboxEventId,
          CancellationToken cancellationToken)
      {
          var evt = await outbox.GetByIdAsync(outboxEventId, cancellationToken)
              ?? throw new InvalidOperationException($"OutboxEvent {outboxEventId} not found.");
          var payload = JsonSerializer.Deserialize<ShippingGenerateLabelOutboxPayload>(evt.PayloadJson)
              ?? throw new InvalidOperationException($"Malformed ShippingGenerateLabelOutboxPayload for {outboxEventId}.");
          var result = await mediator.Send(
              new FetchAndStoreShippingLabel.Command(payload.OrderId),
              cancellationToken);
          if (!result.IsSuccess)
          {
              logger.LogError(
                  "GenerateLabelFunction: FetchAndStoreShippingLabel failed for outbox {OutboxId}: {Code}",
                  outboxEventId, result.Error!.Code);
              throw new InvalidOperationException($"FetchAndStoreShippingLabel failed: {result.Error.Code}");
          }
          logger.LogInformation(
              "GenerateLabelFunction: outbox {OutboxId} → label stored at {BlobPath}",
              outboxEventId, result.Value!.BlobPath);
      }
  }
  ```
- **`ShippingGenerateLabelOutboxPayload`** — sealed record `(Guid OrderId)`. **T-0072 ships this type** as part of the outbox-emit side; T-0074 only consumes the constant + record. If T-0072's groomed scope does not explicitly add the record, add it as part of T-0074 at `Core.Domain/Outbox/Payloads/ShippingGenerateLabelOutboxPayload.cs` (sealed record per project convention; XML-doc mentions T-0072 as the producer and T-0074 as the consumer).
- **`Infra.Functions/Program.cs`** — no change required; `Microsoft.Azure.Functions.Worker` discovers Functions via reflection. DI for `IOutboxRepository` + `ISender` is already wired from T-0029/T-0069. `IShippingCarrierFactory` + `IBlobStorageClient` are already registered (T-0070 + T-0042 respectively).

### Infrastructure / Database layer

- **`IBlobStorageClient.ExistsAsync(BlobContainer container, string path, CancellationToken ct)`** — add this method to the interface declared in `Core.Domain/Storage/IBlobStorageClient.cs` (or wherever T-0042 placed it; the implementer should locate the existing interface and mirror naming exactly). Impl in `Infra.Database/Storage/AzureBlobStorageClient.cs` (or equivalent T-0042 location) calls `BlobClient.ExistsAsync(ct)` and unwraps `Response<bool>.Value`. Pure additive change; no existing callers break.
- **`BlobContainer.Invoices`** — already exists per T-0070 locked decision. **No new BlobContainer constant.** If T-0042's `BlobContainer` enum does not yet have `Invoices` (it should from T-0068b), add it.

### Database layer

No EF migrations. No schema changes. No new tables, columns, or indexes. T-0072 owns the `ShippingCarrierRef` writer; T-0070 owns the `ShippingCarrierTrackingUrl` column.

### Web host

**No controller.** T-0074 is a Function-only ticket. T-0075 owns the maker label download endpoint. No `Web.*` host file is touched by T-0074.

### Config / DI

- **`Core.AppServices/Common/OutboxQueuesOptions.cs`** — `GenerateLabelQueueName` property is added by T-0072 (per its scope) with default `"generate-label"`. T-0074 verifies presence; if T-0072's diff did not include it, T-0074 adds it (sealed property `public string GenerateLabelQueueName { get; init; } = "generate-label";` + extend `OutboxQueuesOptionsValidator` with the Azure queue-name regex check).
- **No new DI registrations.** `FetchAndStoreShippingLabel.Handler` is auto-discovered by the existing MediatR `AddMediatR(typeof(...).Assembly)` scan. Validator auto-discovered by `AddValidatorsFromAssemblyContaining`. `IShippingCarrierFactory` + `IBlobStorageClient` + `IOrderRepository` are already registered.

### i18n

**No new i18n keys.** All error codes (ShippingCarrier*) already have Czech translations from T-0070. The Function path is admin/log-facing only — customer never sees these errors per locked decision C ("silent customer-facing surface").

### NSwag regen

**Not required.** T-0074 introduces no public contract changes. No new controllers, no new endpoints, no new DTOs exposed via OpenAPI. The Function is internal background plumbing.

### Tests

- **`Makables.Tests/AppServices/Features/Shipping/FetchAndStoreShippingLabelHandlerTests.cs`** (NEW, ~7 tests) with NSubstitute mocks:
  1. **Happy path:** Order with `ShippingCarrierRef = "9876543210"`, `CountryCode = "CZ"`; `ExistsAsync` returns false; carrier resolves; `GetLabelPdfAsync` returns a `MemoryStream(pdfBytes)`; assert `UploadAsync` called once with `BlobContainer.Invoices`, path `"cz/orders/{orderId}/label.pdf"`, content-type `"application/pdf"`; result is Success with `Response.BlobPath == "cz/orders/{orderId}/label.pdf"`.
  2. **Idempotent blob-exists path:** `ExistsAsync` returns true; assert `IShippingCarrierFactory.ResolveAsync` NOT called (`Received(0)`); `UploadAsync` NOT called; result Success.
  3. **ShippingCarrierRef null guard:** Order with `ShippingCarrierRef = null`; assert result is `Permanent(ShippingCarrierConfigurationError)`; assert `IShippingCarrierFactory.ResolveAsync` NOT called.
  4. **Carrier Transient propagates:** `GetLabelPdfAsync` returns `Transient(ShippingCarrierUnavailable)`; assert handler returns `Transient(ShippingCarrierUnavailable)` verbatim; assert `UploadAsync` NOT called.
  5. **Carrier Permanent propagates:** `GetLabelPdfAsync` returns `Permanent(ShippingCarrierAddressIdNotFound)`; assert handler returns `Permanent(ShippingCarrierAddressIdNotFound)` verbatim.
  6. **Blob upload Transient:** `UploadAsync` throws `RequestFailedException` with `Status = 503`; assert handler catches and returns `Transient(ShippingCarrierUnavailable)`.
  7. **Blob upload Permanent:** `UploadAsync` throws generic `InvalidOperationException` (e.g., container missing); assert handler catches and returns `Permanent(ShippingCarrierConfigurationError)`; assert Critical log written.
- **`Makables.Tests/Functions/Outbox/GenerateLabelFunctionTests.cs`** (NEW, ~6 tests; mirrors `GenerateInvoiceFunctionTests` shape verbatim):
  1. **Happy path:** outbox row returned; payload deserializes to `{ OrderId }`; `mediator.Send` returns Success; Function logs the blob path; no exception.
  2. **Outbox not found:** `outbox.GetByIdAsync` returns null; assert `InvalidOperationException` thrown with message containing "not found".
  3. **Malformed payload:** outbox row exists but `PayloadJson` deserializes to null (or throws); assert `InvalidOperationException` thrown with message containing "Malformed".
  4. **Mediator returns Transient → re-throws:** `mediator.Send` returns `Transient(ShippingCarrierUnavailable)`; assert `InvalidOperationException` thrown with message containing `"ShippingCarrierUnavailable"`; assert LogError called with structured fields.
  5. **Mediator returns Permanent → re-throws:** `mediator.Send` returns `Permanent(ShippingCarrierAddressIdNotFound)`; assert `InvalidOperationException` thrown (queue dead-letter path).
  6. **CT propagation:** assert the `CancellationToken` passed to the Function is forwarded to both `outbox.GetByIdAsync` and `mediator.Send`.
- **`Makables.IntegrationTests/Shipping/ShippingLabelRoutingIntegrationTests.cs`** (NEW, 1 test):
  - Enqueue an outbox row with `EventType = OutboxEventTypes.ShippingGenerateLabel` and a serialized `ShippingGenerateLabelOutboxPayload`; run `OutboxDispatcher.DispatchDueAsync` against a fake `IOutboxQueuePublisher` (NSubstitute); assert `PublishGenerateLabelAsync(outboxEventId, ct)` was called `Received(1)` AND `PublishSendEmailAsync` + `PublishGenerateInvoiceAsync` were NOT called.

### Docs

- **`docs/architecture/roles/shipping-carrier.md`** — extend the Lifecycle / Implementation pointer to mention T-0074's queue-triggered label-storage flow alongside T-0070's adapter wiring. Mirror the structure T-0069 used to update `docs/architecture/roles/invoice.md`.
- **`docs/tickets/INDEX.md`** — PM flips T-0074 row to `**done**` after PR merge. No status edit by the implementer.

## Alternatives Considered

- **Option A — Eager label fetch inside T-0072's `ShipOrder.Handler`.** *Rejected per ADR 0020 background-jobs principle* — coupling Packeta label round-trip (~1-2s) to the maker's synchronous Ship request adds latency to the user-facing call and creates a "Packeta down → can't ship" hard failure. Queue-decoupled async fetch preserves the order state machine and degrades gracefully.
- **Option B — No idempotency check (re-fetch every retry).** *Rejected per PM-absorbed §C* — wastes Packeta API quota on duplicate fetches under at-least-once queue semantics. HEAD-check costs ~10ms vs ~1-2s Packeta GET; trivially worth it.
- **Option C — Function-level `ProcessedAt` tracking table.** *Rejected per T-0069 precedent + PM-absorbed §C* — redundant with the blob-existence check (the blob itself IS the idempotency receipt). Extra DB write per invocation; no operational value.
- **Option D — Mutate `Order.LabelGeneratedAt` (or similar metadata) on success.** *Rejected per PM-absorbed §C* — adds Order-state mutation risk if blob upload succeeds + DB commit fails (or vice versa). The deterministic blob path is sufficient state; T-0075 reads it.
- **Option E — Customer-facing "label generation failed" surface.** *Rejected per PM-absorbed §C* — label is a proactive background job; customer has zero context for the error. The order is in `Shipped` state and the customer already has the tracking URL via T-0072. Maker label download (T-0075) is the only consumer; T-0075 owns the fallback path.
- **Option F — Dedicated `ILabelStorageService` instead of MediatR Command.** *Rejected per ADR 0014 CQRS discipline* — every use case is a MediatR Command per the one-file feature shape. A service class would bypass the validation + UoW pipeline behaviors.
- **Option G — Nested blob path `invoices/{cc}/orders/{orderId}/shipping/label.pdf`.** *Rejected per T-0070 locked decision A.7* — flat path mirrors T-0068b's `invoices/{cc}/orders/{id}/{invoiceNumber}.pdf` precedent. One order = one label at MVP; nesting is unjustified.
- **Option H — Reuse the invoice generate queue (merged queue).** *Rejected per T-0069 Q2 precedent* — loses failure isolation; one poison label stalls invoice rendering and vice versa. T-0072 ships the separate `generate-label` queue + publisher method for exactly this reason.
- **Option I — Function emits a follow-up outbox event on success (e.g., `shipping.labelGenerated.async`).** *Rejected per PM-absorbed §C* — no downstream consumer needs the signal at MVP. T-0075 reads the blob directly. Adding the event for speculative future use violates YAGNI and ADR 0020 thin-wrapper discipline.
- **Option J — Transient classification on generic blob upload failures.** *Rejected per ADR 0016 §A.14* — Azure Storage SDK already retries transient network blips internally; a failure surfaced to user code means container missing, auth misconfigured, or beyond-SDK-budget outage. All three are ops-investigation territory; Permanent + log Critical gets the right person paged (mirrors T-0069's blob-download stance).

## Out of scope

- **`Order.Ship(...)` state transition + outbox-event emit** — T-0072 (Zásilkovna ShipOrder). T-0074 only consumes the event.
- **Maker label download endpoint** (`GET /api/v1/maker/orders/{id}/label`) — T-0075. T-0074 only writes the blob.
- **Live-Packeta fallback in the download endpoint** (on blob-miss) — T-0075.
- **PersonalPickup label generation** — T-0073 owns the personal-pickup ShipOrder path; the personal-pickup flow does NOT generate a Packeta label (no carrier integration). The `shipping.generateLabel.async` outbox event is emitted ONLY by T-0072's Zásilkovna path, never by T-0073.
- **Order metadata column for label state** (`LabelGeneratedAt`, `LabelBlobPath`, etc.) — out of scope. Blob existence at the deterministic path IS the state.
- **Customer-facing label/tracking UI** — T-0086 surfaces the tracking URL (already on `Order.ShippingCarrierTrackingUrl` from T-0072). The label PDF is maker-facing only.
- **Re-label scenarios** (Packeta requires void + new packet id) — out of scope at MVP. On the rare re-label case, ops manually deletes the blob; the next outbox retry (if any) regenerates via the HEAD-check path. A future ticket can introduce explicit re-label commands.
- **Shipment status sync** (Packeta `GetStatusAsync` timer poll) — T-0078.
- **Customer email "your package is on the way" outbox** — T-0072's responsibility (the outbox sequence in `ShipOrder.Handler`).
- **NSwag regen** — no public contract changes.

## Acceptance criteria

- **AC-1** Given a `shipping.generateLabel.async` outbox event lands on `OutboxQueues:GenerateLabelQueueName` (default `"generate-label"`), when `GenerateLabelFunction` runs, then it loads the outbox row via `IOutboxRepository.GetByIdAsync(outboxEventId, ct)`, deserializes the `PayloadJson` into `ShippingGenerateLabelOutboxPayload`, dispatches `FetchAndStoreShippingLabel.Command(payload.OrderId)` via `ISender.Send`, logs the resulting blob path on success, and returns without throwing.
- **AC-2** Given the same outbox event is delivered twice (queue redelivery), when `GenerateLabelFunction` runs the second time, then `FetchAndStoreShippingLabel.Handler` HEAD-checks the blob, finds it present, and returns Success WITHOUT calling `IShippingCarrierFactory.ResolveAsync` (verified via mock `Received(0)`) AND WITHOUT calling `GetLabelPdfAsync` AND WITHOUT calling `IBlobStorageClient.UploadAsync`. Net: zero Packeta API calls on the second invocation.
- **AC-3** Given an `Order` with `ShippingCarrierRef = "9876543210"`, `CountryCode = "CZ"`, and no existing blob at the deterministic path, when `FetchAndStoreShippingLabel.Handler` runs, then it calls `carrier.GetLabelPdfAsync("9876543210", ct)`, calls `IBlobStorageClient.UploadAsync(BlobContainer.Invoices, "cz/orders/{Order.Id}/label.pdf", stream, "application/pdf", ct)` exactly once, and returns `BusinessResult.Success(new Response("cz/orders/{Order.Id}/label.pdf"))`.
- **AC-4** Given the order's `ShippingCarrierRef IS NULL` when the Command runs (defence guardrail — T-0072 should always set it), when the handler executes, then it returns `BusinessResult.Failure(Error.Permanent(ShippingCarrierConfigurationError))` AND `IShippingCarrierFactory.ResolveAsync` was NOT called (`Received(0)`).
- **AC-5** Given `IShippingCarrier.GetLabelPdfAsync` returns `BusinessResult.Failure(Error.Transient(ShippingCarrierUnavailable))` (e.g., Packeta 503), when the handler propagates, then the result is `BusinessResult.Failure(Error.Transient(ShippingCarrierUnavailable))` verbatim AND `IBlobStorageClient.UploadAsync` was NOT called. The Function then re-throws `InvalidOperationException` with message containing `"ShippingCarrierUnavailable"`, signalling the queue trigger to retry.
- **AC-6** Given `IShippingCarrier.GetLabelPdfAsync` returns `BusinessResult.Failure(Error.Permanent(ShippingCarrierAddressIdNotFound))`, when the handler propagates, then the result is Permanent verbatim. The Function re-throws and the queue moves the message toward dead-letter per Azure Functions queue-trigger retry policy.
- **AC-7** Given `IBlobStorageClient.UploadAsync` throws `RequestFailedException` with HTTP status 5xx (transient Azure Storage outage beyond SDK retry budget), when the handler catches, then it returns `BusinessResult.Failure(Error.Transient(ShippingCarrierUnavailable))`. Given `UploadAsync` throws any other exception (e.g., `InvalidOperationException` for missing container), then the handler returns `BusinessResult.Failure(Error.Permanent(ShippingCarrierConfigurationError))` AND `logger.LogCritical` is invoked with structured context (`OrderId`, `BlobPath`).
- **AC-8** Given the outbox row does not exist when `GenerateLabelFunction` looks it up, when the Function runs, then it throws `InvalidOperationException` with message `"OutboxEvent {outboxEventId} not found."` so the queue retry policy fires (the row may exist on the next polling sweep due to read-replica lag).
- **AC-9** Given the outbox row exists but `PayloadJson` deserializes to a malformed value (null or wrong shape), when the Function runs, then it throws `InvalidOperationException` with message containing `"Malformed ShippingGenerateLabelOutboxPayload"` for ops dead-letter investigation.
- **AC-10** Given an outbox row with `EventType = OutboxEventTypes.ShippingGenerateLabel`, when `OutboxDispatcher.DispatchDueAsync` runs (per the integration test), then it routes the event to `IOutboxQueuePublisher.PublishGenerateLabelAsync(outboxEventId, ct)` exactly once AND does NOT call `PublishSendEmailAsync` AND does NOT call `PublishGenerateInvoiceAsync`. The payload field on the published outbox row matches the original `ShippingGenerateLabelOutboxPayload(OrderId)` shape verbatim.
- **AC-11** Given the handler runs against an order where `Order.CountryCode = "CZ"` and `Order.Id = {someGuid}`, when the blob path is computed, then it exactly equals `$"{lowercase-cc}/orders/{orderId}/label.pdf"` (matching the T-0070 locked decision A.7 path; the implementer matches T-0068b's exact casing convention for the country segment). Container is `BlobContainer.Invoices`.
- **AC-12** Given the Function context has no user identity (queue trigger runs as background worker), when `FetchAndStoreShippingLabel.Handler` loads the order, then it calls `IOrderRepository.GetByIdUnscopedAsync` (NOT `GetByIdAsync` or the scoped variant per ADR 0013) — verified via mock `Received(1)` on the unscoped method and `Received(0)` on the scoped methods.
- **AC-13** Build clean. Unit tests: baseline (after T-0070 + T-0071 + T-0072 + T-0073 in the same bundle) + ~13 new (~7 handler + ~6 Function). Integration tests: baseline + 1 new. `node scripts/check-consistency.mjs` exit 0 (no new T1–T7 violations vs the 101-tracked baseline carried by the bundle).
- **AC-14** Zero new `BusinessErrorMessage` codes (reuses ShippingCarrier* from T-0070). Zero new i18n keys (errors are admin/log-only). Zero new NSwag-exposed types (no public contract change).
- **AC-15** Handler does NOT call `SaveChangesAsync` (per ADR 0014 UoW pipeline discipline). Verified by grep: zero `SaveChangesAsync` occurrences in `FetchAndStoreShippingLabel.cs`.

## Technical notes

### Why blob HEAD-check is sufficient idempotency (no DB tracking)

The blob at the deterministic path IS the receipt. Azure Storage's `ExistsAsync` is a sub-10ms metadata call; Packeta's `GetLabelPdfAsync` is a 1-2 second round-trip to a third-party REST endpoint with a finite quota. The cost ratio (~100×) makes the HEAD-check trivially worth it. A DB tracking table (`LabelGeneratedAt` column or `processed_labels` row) would add a write per success path + a read per HEAD-check — pure overhead vs. the blob-as-receipt model. The partial-failure analysis in PM-absorbed §C confirms at-most-once Packeta fetch under normal operation and at-least-once delivery semantic.

### Why the handler propagates carrier errors verbatim (no translation)

T-0070's `IShippingCarrier` adapter already classifies Packeta failures per ADR 0016 §A.14 (Transient / Permanent / Configuration). Translating those at the handler boundary would lose information and require a per-error-code branch in the handler — purely additive complexity. The Function re-throws on any failure type; the queue retry policy distinguishes Transient (retry) from Permanent (move toward dead-letter) via the existing `ErrorType` field on `BusinessErrorMessage`. The propagation pattern mirrors T-0069 `IssueInvoice` → Function exactly.

### Why no Order state mutation (and why that's safe)

T-0072 has already done the work: `Order.ShippingCarrierRef` is set, `Order.ShippingCarrierTrackingUrl` is set, the state machine is in `Shipped`, and the customer's tracking URL is visible in their dashboard. The label PDF is a maker-facing artifact; its existence does not affect customer experience or order lifecycle. Mutating Order metadata on label success/failure introduces OutOfSync risk (DB commit + blob upload can desync if one fails) for zero operational gain. T-0075's download endpoint reads the blob directly with a live-Packeta fallback on blob-miss — the system stays self-consistent across every failure mode.

### Why the Function throws instead of returning normally

Azure Functions queue triggers use thrown exceptions as the retry signal. Returning normally on a failed `mediator.Send` would silently swallow the failure: the queue marks the message as processed and removes it; the label is never generated; the maker hits T-0075's fallback path forever. Throwing forces the queue retry policy to fire (Transient → re-deliver per the configured backoff; Permanent → dead-letter after the configured attempts cap). The idempotent HEAD-check at step 3 of the handler ensures retries don't double-charge Packeta.

### Why `BlobContainer.Invoices` (not a new `BlobContainer.Labels`)

T-0070 locked decision A.7 + B explicitly chose `invoices` as the container — one container = one set of access controls = simpler ops. Shipping labels and invoice PDFs have the same access model (maker-readable for their own orders; admin-readable globally; no public read). A separate `labels` container would duplicate the RBAC config without operational benefit. The path convention `invoices/{cc}/orders/{orderId}/label.pdf` keeps labels and invoices co-located by order, simplifying ops queries ("show me all artifacts for order X").

### Why `IBlobStorageClient.ExistsAsync` is added as part of T-0074

T-0042 shipped the blob storage abstraction with `UploadAsync` and `DownloadAsync`; HEAD-checks were not needed at that point. T-0074 is the first consumer that needs cheap existence-checking, so the method ships with this ticket. Pure additive change; no existing callers break. The Azure Blob impl is one line: `(await blobClient.ExistsAsync(ct)).Value`.

## Files touched (expected)

### New

- `backend/src/Makables.Core.AppServices/Features/Shipping/FetchAndStoreShippingLabel.cs`
- `backend/src/Makables.Infra.Functions/Outbox/GenerateLabelFunction.cs`
- `backend/src/Makables.Tests/AppServices/Features/Shipping/FetchAndStoreShippingLabelHandlerTests.cs`
- `backend/src/Makables.Tests/Functions/Outbox/GenerateLabelFunctionTests.cs`
- `backend/src/Makables.IntegrationTests/Shipping/ShippingLabelRoutingIntegrationTests.cs`

### Modified (domain)

- `backend/src/Makables.Core.Domain/Storage/IBlobStorageClient.cs` — add `ExistsAsync(BlobContainer container, string path, CancellationToken ct) → Task<bool>`.
- `backend/src/Makables.Core.Domain/Outbox/Payloads/ShippingGenerateLabelOutboxPayload.cs` — verify T-0072 added it; otherwise add the sealed record here.

### Modified (infra)

- `backend/src/Makables.Infra.Database/Storage/AzureBlobStorageClient.cs` (or equivalent T-0042 location) — implement `ExistsAsync` using `BlobClient.ExistsAsync`.

### Modified (config — only if T-0072 did not already ship it)

- `backend/src/Makables.Core.AppServices/Common/OutboxQueuesOptions.cs` — `GenerateLabelQueueName` property with default `"generate-label"`.
- Corresponding `OutboxQueuesOptionsValidator` — extend non-empty + Azure queue-name regex check.

### Modified (docs)

- `docs/architecture/roles/shipping-carrier.md` — extend Lifecycle + Implementation pointer to mention T-0074's queue-triggered label-storage flow.
- `docs/tickets/INDEX.md` — PM flips T-0074 row to `done` after PR merge.

## Test plan reference

Inline above (see Scope > Tests). No separate `docs/test-plans/T-0074.md`.

## Status log

- 2026-06-08 `draft` by PM. Created from bundle plan (shipping-pipeline T-0070 → T-0075). Reference precedent T-0069 GenerateInvoiceFunction merged; T-0070 + T-0072 in the same bundle PR (T-0070 ships interface + blob path constant + error codes; T-0072 ships outbox event + queue name + publisher method that T-0074 consumes).
- 2026-06-08 `draft → ready` by PM. Zero blocking AskUserQuestion items surfaced at `/feature` step 3 — all design choices flowed from T-0069 (Function shape, MediatR-dispatch wrapper, idempotency-via-deterministic-side-effect-lookup, throw-on-failure semantics) and T-0070 (blob path, carrier interface, error codes, blob container). PM-absorbed decisions captured in `## Locked design decisions §C` (HEAD-check idempotency, silent customer surface, no Order state mutation, partial-failure analysis, queue config reuse). ADR-locked items in §B (one-file feature per ADR 0014, thin Function wrapper per ADR 0020, IShippingCarrier contract per ADR 0017). Zero `manual_steps` (queue auto-created by Azure Storage Queue binding; no new secrets; no migration). **Ready for dotnet-backend.**
