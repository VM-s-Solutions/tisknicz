# Shipping pipeline bundle — Reviewer preliminary verdict (draft)

> Written in parallel with the dotnet-backend implementer per the parallel-reviewer rule (docs/process/routing.md §"Bundle workflow" step 4). This is the FIRST bundle-scope review artifact — one draft per bundle, not per ticket. Final verdict happens after the implementer reports done; this is the reviewer's structural read of the bundle BEFORE code lands.

## Bundle scope (T-0070 + T-0071 + T-0072 + T-0073 + T-0074 + T-0075)

Ships the complete Phase 4 shipping pipeline end-to-end:

- **T-0070** (M) — `IShippingCarrier` seam + `PacketaShippingCarrier` adapter + `IShippingCarrierFactory` + public `GET /api/v1/public/shipping/widget-config` endpoint + `Order.ShippingCarrierTrackingUrl` column + CZ `default_shipping_carrier = 'packeta'` seed + 4 new `BusinessErrorMessage` codes (ShippingCarrier*). Mirror of T-0065 IPaymentProvider seam.
- **T-0071** (S) — `AcceptOrder` command + `OrderAcceptedCustomer` email template + `order.accepted.customerEmail` outbox event. First maker-initiated state transition (Paid → Accepted).
- **T-0072** (M) — `ShipOrder` command (Zásilkovna path) + atomic 2-event outbox emission (`order.shipped.customerEmail` + `shipping.generate.label`) + `Order.Ship` 4th-param extension for tracking URL + new `generate-label` queue split + `OutboxDispatcher.RouteTarget.GenerateLabel` branch + `ShippingMethodNotEligible` error code + `OrderShippedCustomer` template (shared with T-0073).
- **T-0073** (S) — `HandOverOrder` command (personal-pickup path) — Accepted → Shipped without Packeta call. Reuses T-0072's outbox event + payload + template with `TrackingUrl = null`.
- **T-0074** (M) — `GenerateLabelFunction` (queue-triggered) + `FetchAndStoreShippingLabel.Command` + `IBlobStorageClient.ExistsAsync` interface addition. Mirror of T-0069 GenerateInvoiceFunction.
- **T-0075** (S) — `GET /api/v1/maker/files/orders/{orderId}/label` on new `FilesController` (Web.Maker) — blob cache hit → Packeta fallback with fire-and-forget cache-fill. **Controller-only feature (no MediatR handler)** per ADR 0014 §"Handler-free read paths".

Dep chain is strictly sequential and tight: T-0070 → T-0071 → T-0072 → T-0073 → T-0074 → T-0075. All six ship as one PR on `feat/shipping-pipeline-bundle` per docs/process/routing.md §"Bundling related tickets into one PR".

## Patterns / ADRs the diff must honour

Walked against `docs/architecture/patterns.md` + ADRs referenced in ticket frontmatter:

- **patterns.md A.4 (one-file feature shape).** `AcceptOrder.cs` (T-0071), `ShipOrder.cs` (T-0072), `HandOverOrder.cs` (T-0073), `FetchAndStoreShippingLabel.cs` (T-0074) each contain nested `Command` / `Response` / `Validator` / `Handler` per `MarkOrderPaid.cs` precedent at `backend/src/Makables.Core.AppServices/Features/Orders/MarkOrderPaid.cs`. T-0075 is exempt per ADR 0014 §"Handler-free read paths" — controller IS the use case.
- **patterns.md A.7 (FluentValidation per handler).** Every new Command has a sibling Validator class. AcceptOrder/ShipOrder/HandOverOrder validate `OrderId` (non-empty + length); FetchAndStoreShippingLabel validates `OrderId != Guid.Empty`.
- **patterns.md A.13 (per-event-type EmailSendService switch).** T-0071 + T-0072 each add ONE new switch arm (`OrderAcceptedCustomerEmail`, `OrderShippedCustomerEmail`). T-0073 ADDS NO new arm (reuses T-0072's). The dispatch table at `IEmailSendService.cs:62-77` must grow by exactly 2 cases.
- **patterns.md A.14 (provider adapter error classification).** `PacketaShippingCarrier` must classify per ADR 0017 §"Error classification": 5xx → Transient(`ShippingCarrierUnavailable`); 4xx with body-keyword `addressId` → Permanent(`ShippingCarrierAddressIdNotFound`); 4xx with body-keyword `weight` → Permanent(`ShippingCarrierInvalidWeight`); 401/403 → Configuration(`ShippingCarrierConfigurationError`). Mirrors `ComgatePaymentProvider` (T-0065).
- **patterns.md A.15 (provider seam + keyed services).** `services.AddKeyedScoped<IShippingCarrier, PacketaShippingCarrier>("packeta")` + `IShippingCarrierFactory.ResolveAsync(countryCode)` looks up `CountryConfiguration.DefaultShippingCarrier` then `serviceProvider.GetKeyedService<IShippingCarrier>(carrierCode)`. Mirrors `PaymentProviderFactory` from T-0065.
- **ADR 0013 (scoped repositories).** Maker actions (AcceptOrder/ShipOrder/HandOverOrder/FilesController.GetShippingLabel) use `GetByIdForMakerAsync(orderId, makerId)`. Background-context handler (`FetchAndStoreShippingLabel`) uses `GetByIdUnscopedAsync(orderId)` because Function context has no user identity. Both methods already exist on `IOrderRepository` (verified at `backend/src/Makables.Core.Domain/Orders/IOrderRepository.cs:87` and `:103`).
- **ADR 0014 (UoW pipeline).** No handler in this bundle calls `SaveChangesAsync()`. UoW pipeline commits state mutation + outbox rows atomically. T-0072's 2-event enqueue (`order.shipped.customerEmail` + `shipping.generate.label`) and T-0071's 1-event enqueue (`order.accepted.customerEmail`) and T-0073's 1-event enqueue (`order.shipped.customerEmail`) ALL ride on the same single UoW commit per ticket scope.
- **ADR 0016 (Comgate webhook pattern + IPaymentProvider seam — the precedent for shipping carrier).** Verified that `PacketaShippingCarrier` ships with the same error-matrix shape as `ComgatePaymentProvider` per T-0070 scope §"Infrastructure clients".
- **ADR 0017 (shipping/Packeta).** ADR-locked items (interface shape, API-key model, blob container = `invoices`, error classification, single platform-wide account) match T-0070 scope verbatim.
- **ADR 0019 (email pipeline).** Per-event-type switch convention preserved (T-0067 Q3). Each new event type adds one branch; existing branches untouched.
- **ADR 0020 (outbox + background jobs / queue-per-event-class).** New `shipping.generate.label` event routes to its OWN `generate-label` queue (NOT `send-email`, NOT `generate-invoice`). Bare outbox id as queue message body per T-0029 pattern. Mirror of T-0069's queue split.
- **CLAUDE.md §Money.** N/A — no monetary columns added in this bundle.
- **CLAUDE.md §Security.** T-0070 + T-0075 are security-touching: new public endpoint with rate limiting + new authenticated maker file-download endpoint. Both require `secops` engagement per docs/process/routing.md row "Security-touching change".

## Pre-flight risks (rank HIGH first)

### HIGH

1. **HIGH: Atomic 2-event outbox in `ShipOrder.Handler` (T-0072).** The handler MUST enqueue BOTH `order.shipped.customerEmail` AND `shipping.generate.label` within ONE UoW transaction (per T-0072 §A.1 + AC-2). If the handler accidentally calls a second outbox.Save or splits the enqueues across two UoW scopes, the outbox events lose atomicity with the state mutation. **Verify at PR-open:** `ShipOrder.Handler` body contains exactly two `outbox.Enqueue(...)` calls between `order.Ship(...)` and `return BusinessResult.Success(...)`, with no `unitOfWork.SaveChangesAsync` calls in between. Race-safety on the customer email: T-0069's lookup-at-send-time pattern applies — the customer-email handler does NOT lookup the label blob (label is maker artifact, not in email). However, the T-0072 ticket lines 200-206 (`SendOrderShippedCustomerEmailAsync` description) muddles this — implementer should NOT add a blob lookup in the customer-email handler.

2. **HIGH: `Order.Ship` in-place signature extension breaks all existing callers (T-0072).** Current signature at `backend/src/Makables.Core.Domain/Orders/Order.cs:559`: `Ship(IClock clock, string? shippingCarrierRef, int autoDeliverWindowDays)`. T-0072 §C.1 extends to 4 params with optional `string? trackingUrl = null`. **Verify at PR-open:**
   - All existing call sites compile unchanged (T-0060 `OrderTests`, any integration test seeding an order in Shipped, T-0073 handler — which passes null explicitly).
   - Set-once guard on `ShippingCarrierTrackingUrl` mirrors the existing `ShippingCarrierRef` guard pattern at `Order.cs:576-578` (return `Error.Conflict("trackingUrl", BusinessErrorMessage.OrderInvalidTransition)` on second set).
   - Length validation: trim + verify `<= 500` chars (column cap from T-0070 migration).
   - XML doc updated to reference T-0072 as the writer + remove any T-0070-era TODO pointer on `ShippingCarrierTrackingUrl`.

3. **HIGH: `OutboxDispatcher` classifier exhaustiveness + `RouteTarget` enum extension (T-0072).** Current `OutboxDispatcher.cs:178-184` enum has 3 values: `Unknown / SendEmail / GenerateInvoice`. T-0072 adds `GenerateLabel` as the 4th. **Verify at PR-open:**
   - `ClassifyRoute` at `OutboxDispatcher.cs:171-176` gets a new `if (OutboxEventTypes.IsGenerateLabel(eventType)) return RouteTarget.GenerateLabel;` branch.
   - `PublishToTargetAsync` switch at `OutboxDispatcher.cs:154-161` gets a new `RouteTarget.GenerateLabel => queuePublisher.PublishGenerateLabelAsync(outboxEventId, cancellationToken)` arm.
   - **The current switch uses a discard `_ =>` arm that throws `InvalidOperationException`** (OutboxDispatcher.cs:159-160). Implementer must replace the discard with an explicit `RouteTarget.GenerateLabel` arm or the dispatcher will throw at runtime for every label event. Reviewer will hard-block if discard remains.
   - `IsGenerateLabel` classifier added to `OutboxEventTypes.cs` (currently at `OutboxEventTypes.cs:77-78` we have `IsInvoiceGenerate`; mirror that single-event-classifier shape exactly).
   - Disjointness: verify `OrderShippedCustomerEmail` is added to `IsEmailSend` (line 61-66) NOT to `IsGenerateLabel`. Only `ShippingGenerateLabel` should match `IsGenerateLabel`.

4. **HIGH: EmailSendService ctor swell (T-0067 + T-0068b + T-0069 precedent).** Current ctor at `IEmailSendService.cs:40-47` already takes 7 deps (`templates`, `translations`, `provider`, `invoices`, `blobStorage`, `urls`, `logger`). T-0069 reviewer's earlier note (in `docs/review/runs/T-0069-draft.md`) already flagged the boundary and forced removal of `IOrderRepository`. **For this bundle:** T-0071's `SendOrderAcceptedCustomerEmailAsync` + T-0072's `SendOrderShippedCustomerEmailAsync` MUST NOT add new ctor deps. Both new branches reuse `templates`, `translations`, `provider`, `urls`, `logger` — neither needs a blob or invoice lookup (per locked decision T-0072 C.last: no PDF attachment on shipped email). **Verify at PR-open:** the ctor signature on `EmailSendService` is unchanged after the bundle; if any new dep was added, request changes citing ADR 0015 collaborator cap (~5) and the T-0069 precedent.

5. **HIGH: `FilesController.GetShippingLabel` fire-and-forget Task.Run lifetime (T-0075).** Per T-0075 §C "Background task lifetime" + AC-6 + Technical notes §"Why CancellationToken.None inside the Task.Run". The background blob-upload MUST use `CancellationToken.None`, NOT the request's `ct`. If the implementer passes `ct`, the upload cancels when the response stream closes → cache stays cold forever. **Verify at PR-open:** the `Task.Run(...)` body uses `CancellationToken.None` in the `UploadAsync` call AND the `uploadBuffer` is a SEPARATE `MemoryStream` instance from the response `buffer` (T-0075 Technical notes §"Why the MemoryStream is duplicated" — sharing one stream races on `Position`).

### MEDIUM

6. **MEDIUM: `PacketaShippingCarrier` error classification matrix (T-0070).** The 10-test matrix in T-0070 §Tests pins this. Verify each `Test*ScenarioToError*` actually maps to the right Error.Type at AC-6/7/8 and that the body-keyword sniff handles both `addressId not found` (Permanent) and `weight` (Permanent). Reviewer will read the test file row-by-row at PR-open. The Comgate precedent at `backend/src/Makables.Infra.Clients/Comgate/ComgatePaymentProvider.cs` is the shape model.

7. **MEDIUM: Widget-config endpoint Cache-Control header (T-0070 AC-12) + rate-limit policy (T-0070 AC-13).** `Cache-Control: public, max-age=3600` must be set ONLY on `200 OK` responses (T-0070 §Web.Public host bullet 3) — error responses are uncached. The 100/min partitioned per-IP policy must wire correctly via `AddMakablesRateLimiting.cs`. **Verify at PR-open:** the `Response.Headers.Append` call is positioned AFTER the success branch, not at controller-action top. Mirror T-0031 Mapbox rate-limit pattern.

8. **MEDIUM: Label download Cache-Control header set ONLY on 200 OK (T-0075 AC-7/8/9).** `public, max-age=31536000, immutable` must NOT appear on 503/404/401 responses. T-0075 §Web.Maker host bullet 6 + §Scope last line ("The controller MUST set Cache-Control ONLY on 200 OK paths") is explicit. **Verify at PR-open:** the header set happens inside the success branches (cache-hit AND Packeta-fallback-success), never in the error-return paths.

9. **MEDIUM: T-0075 controller-only feature shape vs Mediator dispatch (no handler).** T-0075 §Scope explicitly forbids a MediatR handler per ADR 0014 §"Handler-free read paths" + locked decision B. **Verify at PR-open:** no new file under `Core.AppServices/Features/Files/` or `Features/Shipping/Labels/`. The controller IS the use case. If implementer adds a handler, request changes citing T-0075 §"Why no MediatR handler" Technical note.

10. **MEDIUM: Controller location ambiguity for T-0071 + T-0072 + T-0073.** Existing `Web.Maker/Controllers/OrdersController.cs` uses `[Route("api/v{version:apiVersion}/orders")]` (NOT `maker/orders`) per T-0071 §C controller-precedent. T-0072 and T-0073 spec calls use `/api/v1/maker/orders/{orderId}/ship` and `.../handover`. **Verify at PR-open:** all three controller actions land in the SAME existing `OrdersController` (not three separate controllers); routes resolve correctly per the existing `[Route]` prefix; the "maker/" semantic is enforced by JWT audience (not URL segment).

11. **MEDIUM: `ShippingMethodNotEligible` error code single-registration (T-0072 + T-0073).** T-0072 ships the code (per T-0072 §Domain layer §BusinessErrorMessage); T-0073 reuses it (per T-0073 §C bullet 7). **Verify at PR-open:** exactly ONE `public const string ShippingMethodNotEligible = "shipping.methodNotEligible";` line is added to `BusinessErrorMessage.cs`. No duplicate registration in T-0073's diff.

12. **MEDIUM: `IBlobStorageClient.ExistsAsync` interface contract (T-0074 + T-0075).** T-0074 §Modified (domain) says "add `ExistsAsync`" if not already present. T-0075 §Domain layer says "ExistsAsync is already present (verified during grooming at Core.Domain/Storage/IBlobStorageClient.cs); the implementer MUST NOT add a duplicate signature." These two statements are inconsistent — T-0074 lands first and adds it; T-0075 then consumes. **Verify at PR-open:** exactly ONE `Task<bool> ExistsAsync(BlobContainer container, string path, CancellationToken ct)` (or its `BusinessResult<bool>` wrapper variant per T-0075 §Web.Maker host bullet 6 use of `existsResult.IsSuccess && existsResult.Value`) signature lives on the interface. Implementation in `Infra.Database/Storage/AzureBlobStorageClient.cs` mirrors the convention. **Reviewer must flag** if `ExistsAsync` returns raw `bool` (T-0074 spec) but the controller checks `IsSuccess && Value` (T-0075 spec) — these contracts mismatch. Implementer must pick one shape and align both consumers.

13. **MEDIUM: `IBlobStorageClient.UploadAsync` return type contract for T-0075 fire-and-forget (T-0075 §Web.Maker host bullet 7).** Controller code checks `uploadResult.IsSuccess` and `uploadResult.Error.Code` — implies `UploadAsync` returns `BusinessResult`. T-0074 §AppServices step 7 says the handler `try/catch`es `RequestFailedException` on the upload — implies `UploadAsync` throws (not returns BusinessResult). **Verify at PR-open:** `UploadAsync` has ONE coherent return contract. If it returns `BusinessResult`, T-0074's handler must check the result; if it throws, T-0075's fire-and-forget must use try/catch around the await.

14. **MEDIUM: Personal-pickup outbox emits ONLY 1 event (T-0073 AC-6 — the T-0072-vs-T-0073 distinguishing constraint).** Unit + integration tests must assert NO `shipping.generate.label` row exists for personal-pickup orders. T-0074's `GenerateLabelFunction` would no-op / fail if it received a personal-pickup OrderId (carrier ref is null), so emitting the label event would clutter outbox dead-letter queues unnecessarily.

15. **MEDIUM: `ShippingGenerateLabel` event-type string canonicalization across T-0072 and T-0074.** T-0072 §Domain layer line 77 says `ShippingGenerateLabel = "shipping.generate.label"`. T-0074 §Context line 30 says `OutboxEventTypes.ShippingGenerateLabel = "shipping.generateLabel.async"` and references `IsShippingGenerateLabel(eventType)`. **These two spellings disagree.** Implementer must pick ONE canonical name and propagate. Reviewer's reading: T-0072 is the producer (registers the constant) so T-0072's `"shipping.generate.label"` wins. T-0074 wording is loose — the classifier name `IsGenerateLabel` (per T-0072 spec) is canonical, not `IsShippingGenerateLabel`. Verify at PR-open against the actual constant + classifier name.

16. **MEDIUM: `ShippingGenerateLabelOutboxPayload` vs `GenerateLabelOutboxPayload` record naming (T-0072 vs T-0074).** T-0072 §Domain layer line 92-95 uses `GenerateLabelOutboxPayload(string OrderId)`. T-0074 §Infrastructure layer line 124 + §Modified (domain) line 261 uses `ShippingGenerateLabelOutboxPayload(Guid OrderId)`. **Two mismatches:** (a) record name; (b) `string` vs `Guid` OrderId. Across the codebase `OrderId` is `string` (e.g., the existing `MarkOrderPaid.Command(string OrderId)` precedent). Implementer MUST pick `string OrderId` for consistency with `Order.Id`. Reviewer's reading: T-0072 is producer (defines payload) so T-0072's `GenerateLabelOutboxPayload(string OrderId)` wins.

17. **MEDIUM: T-0072 `MakerSessionId` vs T-0071 `IUserSessionProvider` pattern divergence.** T-0072 §AppServices line 107 uses `IMakerSessionContext sessionContext` and `sessionContext.RequireMakerId()`. T-0071 §AppServices uses `IUserSessionProvider session` + `session.GetUserId()` + `IMakerRepository.GetByUserIdAsync`. **The two tickets use different session-resolution patterns.** Verify at PR-open which abstraction actually exists in the codebase (Grep for `IMakerSessionContext` — if it doesn't exist, implementer must converge on T-0071's `IUserSessionProvider` + `IMakerRepository.GetByUserIdAsync` pattern, which is the established maker-handler shape per `MarkOrderPaid` precedent).

18. **MEDIUM: T-0071 `OrderId` type — `string` vs `Guid`.** T-0071 uses `Command(string OrderId)` consistently with existing `MarkOrderPaid.Command(string OrderId)` precedent. T-0072 also uses `string OrderId`. T-0073 (§AppServices line 84) and T-0074 (§AppServices §Command) use `Guid OrderId`. **The bundle mixes string and Guid OrderIds across commands.** Reviewer will hard-block if T-0073/T-0074 ship with `Guid` while T-0071/T-0072 ship with `string` (inconsistent NSwag client surface; cross-feature signature drift). Implementer MUST converge — `string OrderId` is the codebase convention.

### LOW

19. **LOW: NSwag regen scope.** 5 new public endpoints land across the bundle (widget-config + accept + ship + handover + label-download). Single `npm run generate:api` at the end of the bundle covers all five. Reviewer Gate 6 verifies the generated client has all five methods + DTOs.

20. **LOW: Function DI registration for `GenerateLabelFunction` (T-0074).** Functions auto-discovered via `Microsoft.Azure.Functions.Worker` reflection. `IBlobStorageClient`, `IShippingCarrierFactory`, `IOrderRepository` all already wired from T-0042 + T-0070. Verify no missing `AddMakablesXxx()` call in `Functions/Program.cs`.

21. **LOW: Migration ordering — T-0070's `AddOrderShippingCarrierTrackingUrl` migration must run BEFORE T-0070's `SetCzDefaultShippingCarrier` (or be folded into one migration).** T-0070 §Database layer notes "combined or separate — implementer judges". Reviewer will accept either; just verify they're applied in the right order on a fresh DB.

## Test coverage expectations (Gate 5)

Per `docs/process/must-cover-tests.md`:

- **Pure logic (TDD-first commit required per T-0067+ rule):**
  - **§5 Validators:** `AcceptOrder.Validator`, `ShipOrder.Validator`, `HandOverOrder.Validator`, `FetchAndStoreShippingLabel.Validator`, `PacketaOptionsValidator` — positive + 1 negative per `RuleFor` clause. ~4-6 tests each.
  - **§4 Order state machine:** `Order.Ship` 4th-param extension (T-0072 OrderShipTrackingUrlTests, 3 tests). MUST be test-first per T-0067 grandfather rule — `Order.cs` was touched in T-0072+. Set-once on `ShippingCarrierTrackingUrl`.
  - **§11 Set-once invariants:** `ShippingCarrierTrackingUrl` joins the set-once table — add a row to must-cover-tests.md §11 table during this bundle (per the rule "Add a row here when a new set-once property lands"). Test sits in `Core.Domain.Tests/Entities/Orders/`.
  - **§9 BusinessErrorMessage codes negative-path:** 4 new codes from T-0070 (ShippingCarrier*) + 1 from T-0072 (`ShippingMethodNotEligible`). Each must have ≥1 negative-path test surfacing the code.
  - **OutboxEventTypes classifiers:** `IsEmailSend("order.accepted.customerEmail")` + `IsEmailSend("order.shipped.customerEmail")` + `IsGenerateLabel("shipping.generate.label")` + `IsGenerateLabel(otherEvent) == false` + `IsInvoiceGenerate("shipping.generate.label") == false` (disjointness). Pure logic; test-first.

- **Handler tests (carve-out — "test alongside" per tdd-policy.md):**
  - `AcceptOrderHandlerTests` ~7 tests (T-0071 §Tests)
  - `ShipOrderHandlerTests` ~10 tests (T-0072 §Tests)
  - `HandOverOrderHandlerTests` ~6 tests (T-0073 §Tests)
  - `FetchAndStoreShippingLabelHandlerTests` ~7 tests (T-0074 §Tests)
  - `FilesControllerLabelDownloadTests` ~10-12 tests (T-0075 §Tests; controller-as-handler since no Mediator).

- **Adapter tests (§10):**
  - `PacketaShippingCarrierTests` ~10 tests including: WidgetConfig shape; CreateShipmentAsync success body + TrackingUrl format; 5xx → Transient; 4xx address-id-not-found → Permanent; 4xx weight → Permanent; 401 → Configuration; timeout → Transient; GetStatusAsync state map; GetLabelPdfAsync success + classified failures.
  - `ShippingCarrierFactoryTests` ~6 tests including: valid resolve, null DefaultShippingCarrier → Configuration, missing keyed registration → Configuration, IMemoryCache cache hit + 5min TTL invalidation, CancellationToken respect.

- **Function tests (mirror T-0069 GenerateInvoiceFunctionTests):**
  - `GenerateLabelFunctionTests` ~6 tests (T-0074 §Tests): happy path, outbox-not-found throws, malformed payload throws, Transient propagates as exception, Permanent re-throws, CT propagation.

- **Dispatcher tests:**
  - `OutboxDispatcherTests` extension ~2 tests (T-0072): label-event routes to `PublishGenerateLabelAsync`; mixed batch routes 3 events to 3 publishers correctly.

- **EmailSendService tests:**
  - 3 new tests for `OrderAcceptedCustomerEmail` branch (T-0071 §Tests).
  - 3 new tests for `OrderShippedCustomerEmail` branch (T-0072 §Tests) covering: template loaded; substitutions with tracking_url non-empty; substitutions with tracking_url null/empty.
  - 1 new test for null-TrackingUrl variant (T-0073 §EmailSendServiceTests).

- **Integration tests (Postgres test container):**
  - `WidgetConfigEndpointTests` 3 tests (T-0070).
  - `AcceptOrderIntegrationTests` 2 tests (T-0071).
  - `ShipOrderIntegrationTests` 2 tests (T-0072).
  - `HandOverOrderIntegrationTests` 1 test (T-0073).
  - `ShippingLabelRoutingIntegrationTests` 1 test (T-0074 — OutboxDispatcher → PublishGenerateLabelAsync routing).
  - `LabelDownloadIntegrationTests` 1 test (T-0075 — blob cache hit end-to-end).

**Bundle total target:** ~85-95 new unit tests + ~10 new integration tests. Final tally is the reviewer's count at PR-open.

## Mechanical-check expectations (Gate 9)

Per `docs/process/quality-gates.md` Gate 9 (`scripts/check-consistency.mjs`):

- **T1 (one-file feature shape):** 4 new features (`AcceptOrder`, `ShipOrder`, `HandOverOrder`, `FetchAndStoreShippingLabel`) each declare `public static class Xxx` containing nested `Command`/`Response`/`Validator`/`Handler`. Per T-0068b precedent (`IssueInvoice.cs` triggered a T1 false-positive), each new feature may shift baseline. Expected baseline shift: **101 → ~105** (4 new false-positive T1 violations, one per feature). Document this in PR description.
- **T3 (no SaveChangesAsync in handlers):** No new violations. All 4 new handlers ride the UoW pipeline.
- **T4 (no `dynamic` / no `any`):** No new violations. T-0070 ticket mandates `IReadOnlyDictionary<string, string>` for widget options (loose dict but typed — no `dynamic`).
- **T5 (BusinessErrorMessage codes referenced via constants):** 5 new codes (`ShippingCarrier*` × 4 + `ShippingMethodNotEligible`). All references via constant, not inline strings. Reviewer greps for inline `"shipping."` literals in the diff.
- **T6 (money columns):** N/A. No monetary columns added.
- **T7 (no `useEffect` in frontend):** N/A. No frontend code beyond i18n + NSwag-regen.

**Expected consistency baseline:** 101 → 105 (4 new one-file features each false-positive T1). PR description must call this out.

## Bundle DoR compliance check

Per docs/process/ticket-lifecycle.md (bundle DoR is implicit in routing.md §"Bundle workflow" step 1):

- All 6 tickets individually satisfy DoR (`status: ready` in frontmatter) ✓
- Bundle scope named in branch name (`feat/shipping-pipeline-bundle`) ✓ (per ticket Status logs)
- Bundle order documented in each ticket's Context block ✓ (T-0070 → T-0075)
- No external blockers between tickets in the bundle ✓ (`depends_on` is internal across the chain)
- Single parallel-reviewer artifact at `docs/review/runs/shipping-pipeline-bundle-draft.md` ✓ (this file)
- L-split rule not triggered (all 6 tickets are S or M) ✓

## Open items the implementer should confirm in the PR description

These are NOT blockers — they're ambiguities the reviewer wants nailed down at PR-open:

1. **`Order.Ship` signature extension implementation:** was the 4th-param extension clean, all existing call sites updated, set-once guard on `ShippingCarrierTrackingUrl` mirroring lines 576-578 of `Order.cs`?
2. **`HandOverOrder` command naming:** the ticket locks `HandOverOrder` (not `ShipOrderPersonalPickup`); controller route is `/handover` (not `/ship/personal-pickup`).
3. **T-0075 fire-and-forget upload uses `CancellationToken.None`** (NOT request `ct`) per AC-11 + Technical note.
4. **`ShippingMethodNotEligible` BusinessErrorMessage code:** ships in T-0072's diff (not T-0073). T-0073 reuses. Single registration line.
5. **Packeta REST error-body keyword detection set:** which substrings does `PacketaShippingCarrier` use to distinguish `ShippingCarrierAddressIdNotFound` from `ShippingCarrierInvalidWeight` (mirrors T-0069 SendGrid keyword pattern)?
6. **EmailSendService ctor unchanged after bundle:** no new dep added. T-0067 + T-0068b + T-0069 + this bundle all preserve the 7-dep ctor.
7. **Outbox event constant + classifier naming converged** between T-0072 and T-0074 (canonical: `ShippingGenerateLabel = "shipping.generate.label"`; `IsGenerateLabel(eventType)`; `GenerateLabelOutboxPayload(string OrderId)`).
8. **Session-resolution pattern converged** between T-0071 and T-0072 (canonical: `IUserSessionProvider` + `IMakerRepository.GetByUserIdAsync` per `MarkOrderPaid` precedent — if `IMakerSessionContext` doesn't exist in the repo, T-0072 falls back to T-0071's pattern).
9. **OrderId type converged across commands** — `string` per codebase convention; if T-0073/T-0074 land with `Guid`, request changes.
10. **`IBlobStorageClient.ExistsAsync` return type** — coherent across T-0074 (raw `bool` per spec) and T-0075 (`BusinessResult<bool>` per spec). Implementer picks one shape and aligns both consumers.
11. **NSwag regen** committed in same PR; covers widget-config + accept + ship + handover + label-download (5 new endpoints + DTOs).
12. **Role docs updated:** `docs/architecture/roles/shipping-carrier.md` promoted from stub to full per `payment-provider.md` template (T-0070 AC-17); `docs/architecture/roles/order.md` lifecycle table notes Paid→Accepted (T-0071), Accepted→Shipped via 2 writers (T-0072 + T-0073).
13. **`docs/process/must-cover-tests.md` §11 table** gains a row for `ShippingCarrierTrackingUrl` (set-once on Order, set by T-0072 ShipOrder.Handler).

## Preliminary verdict

**STRUCTURALLY_SOUND_PENDING_DIFF, with 8 inter-ticket contract ambiguities requiring implementer convergence at code-write time.**

The bundle is well-groomed and pattern-conformant on its locked-decisions surface — ADR 0014/0016/0017/0019/0020 are honored; T-0067 + T-0069 precedents are followed verbatim; one-file feature shape preserved; UoW pipeline discipline preserved; queue-per-event-class principle preserved. The HIGH-risk items (atomic 2-event outbox, `Order.Ship` signature, OutboxDispatcher classifier, EmailSendService ctor, Task.Run lifetime) are well-pinned by the ticket scope + AC + Technical notes, so the implementer has a clear contract to hit.

The MEDIUM-risk items 15-18 (`ShippingGenerateLabel` constant string + payload record name + Guid-vs-string OrderId + session-resolution pattern divergence between T-0072 and T-0074, and between T-0071/T-0072 and T-0073/T-0074) are **spec-level inconsistencies** between tickets that the implementer MUST resolve before the diff lands. Reviewer recommends the canonical choices documented above (T-0072 producer naming wins for the outbox constant + payload; T-0071 session pattern wins; `string OrderId` everywhere). These don't block grooming; they just need implementer judgment + a one-line note in the PR description.

Final review at PR-open will walk the checklist Sections A-J row by row, verify Gate 5 (TDD-first on validators + Order.Ship), Gate 6 (NSwag regen), Gate 8 (optimizer ping on T-0075's Packeta-fallback hot path + T-0072's atomic 2-event handler), Gate 9 (consistency-check baseline shift 101→105 documented), and SecOps sign-off on T-0070 + T-0075 per the security-touching frontmatter flag.
