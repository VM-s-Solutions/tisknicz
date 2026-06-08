---
id: T-0075
title: Label download endpoint (maker) — cache → Packeta fallback
status: ready
size: S
owner: dotnet-backend
created: 2026-06-08
updated: 2026-06-08
depends_on: [T-0074]
blocks: []
user_stories: [US-maker-0009]
adrs: [0013, 0014, 0017]
phase: 4
manual_steps: []
security_touching: true
layers: [appservices, web-maker, frontend-i18n]
---

# T-0075 — Label download endpoint (maker) — cache → Packeta fallback

## Context

T-0075 is the maker-facing read path for shipping labels in the Phase 4 shipping-pipeline bundle (T-0070 → T-0075). T-0072 wrote `Order.ShippingCarrierRef` + tracking URL on Paid → Accepted; T-0074 ran the queue-triggered `GenerateLabelFunction` that calls Packeta once and persists the PDF to `invoices/{cc}/orders/{orderId}/label.pdf`. T-0075 is the HTTP surface the maker dashboard hits when the maker clicks "Stáhnout štítek" — a single `GET /api/v1/maker/files/orders/{orderId}/label` on a new `FilesController` in `Web.Maker`.

The slice is intentionally narrow: **no state transitions**, **no outbox emissions**, **no domain method calls**. It is a streaming read against the blob container with a single fallback path to Packeta when the cache hasn't been populated yet. The 99% steady-state case is a blob hit; the cold-start case (T-0074's queue is still in flight, or the blob was purged by a future GDPR sweep) goes through a live Packeta fetch with a fire-and-forget cache-fill so the second request is fast.

The slice is **security-touching** because it introduces a new authenticated maker endpoint that streams a PDF derived from order ownership. The endpoint resolves the maker from the session and scopes the order via the existing `IOrderRepository.GetByIdForMakerAsync` — an unassigned maker sees `404`, never `403`, so the endpoint does not leak the existence of orders belonging to other makers. The endpoint sets `Cache-Control: public, max-age=31536000, immutable` ONLY on `200 OK` responses; error responses (`404`, `503`) are uncached.

The slice **does not** add new `BusinessErrorMessage` codes (reuses `OrderNotFound` and `ShippingCarrierUnavailable` from T-0070), does **not** add new i18n keys (the maker dashboard surfaces existing carrier-unavailable + order-not-found copy), and does **not** modify the Order aggregate or its repository surface. The only new domain-layer touch is the verified-present `IBlobStorageClient.ExistsAsync(...)` method (already shipped in T-0042 per the existing `IBlobStorageClient` interface — verified during grooming; no schema migration here).

The slice depends on T-0074 only for the writer-side guarantee that the blob path convention is honoured (`invoices/{cc}/orders/{orderId}/label.pdf`). It depends on T-0070 transitively (carrier interface + Packeta error classification + flat label blob path lock). It blocks nothing — this is a leaf in the shipping-pipeline bundle.

## Locked design decisions

Captured per `docs/process/deliberation.md`. The user answered 3 blocking AskUserQuestion items at `/feature` step 3 before this ticket transitioned to ready. ADR 0017 + ADR 0014 + ADR 0013 pre-locked the rest.

### A. User-locked at /feature step 3 (non-negotiable)

1. **Blob-miss fallback = fire-and-forget Task.Run + stream live response.** Maker's first download triggers cache fill via background task; subsequent requests hit blob cache (99%+ hit rate after first). Eventual consistency. Simplest code; no blocking. ~5-10KB labels; Packeta latency 1-2s acceptable. **Rejected:** synchronous wait (1-2s extra latency on first request); re-enqueue to generate-label queue + 202 (forces polling UX).

2. **Packeta 5xx during fallback = 503 ServiceUnavailable + Retry-After: 60.** Correct HTTP semantics; maker dashboard shows "Carrier temporarily unavailable". Honest error UX. **Rejected:** 404 (lies about state); synchronous retry within request (15s+ worst case); 202 + polling (overkill).

3. **Cache-Control = `public, max-age=31536000` (1 year, immutable).** Labels are deterministic per ADR 0017. Browsers + CDN aggressively cache; re-fetch only if URL changes (it won't — order-id-keyed path). Massive backend cost reduction. **Rejected:** 1h (T-0070 widget cache TTL — labels are far more cacheable); private 24h (defeats CDN benefit); no-cache (matches T-0064 OrderAttachment but those carry customer-uploaded sensitive files; labels are deterministic system artifacts).

### B. ADR-locked (no relitigation)

- **One-file feature shape** per `docs/architecture/patterns.md`. T-0075 ships a controller-only feature (no MediatR handler — the slice is a single read with a passthrough adapter call; introducing a handler adds ceremony without separation benefit per ADR 0014 §"Handler-free read paths"). The controller method itself is the use case.
- **No `SaveChangesAsync` anywhere in this slice** per ADR 0014 (UoW pipeline). No state mutation = no UoW commit point.
- **Scoped repositories** per ADR 0013. `IOrderRepository.GetByIdForMakerAsync` is the only repository touch. No new repository methods.
- **Carrier interface + factory shape** per ADR 0017 + T-0070 lock — `IShippingCarrierFactory.ResolveAsync(countryCode, ct)` returns `BusinessResult<IShippingCarrier>`; `carrier.GetLabelPdfAsync(carrierRef, ct)` returns `BusinessResult<Stream>` with the ADR 0016 §A.14 error classification (`Transient` / `Permanent` / `Configuration` / `Unknown`).
- **Flat label blob path** `invoices/{cc}/orders/{orderId}/label.pdf` per T-0070 §A.7. The country prefix uses the lower-cased ISO-3166-1 alpha-2 code per ADR 0011 path convention (`cz/...`).
- **Blob container = `BlobContainer.Invoices`** per T-0070 §B (one container = one set of access controls = simpler) and ADR 0017.
- **Error classification translation:** `Transient` → 503 + `Retry-After: 60`; `Permanent` → 404 (the carrier has lost the label permanently, treat as not-found from the maker's POV); `Configuration` → 503 (operationally-fixable; maker should retry later while the team rotates the key); `Unknown` → 503 (conservative).
- **`BusinessErrorMessage` reuse:** `OrderNotFound` for 404; `ShippingCarrierUnavailable` for 503. No new codes.
- **Pipeline behaviors** per ADR 0014 — none apply: no MediatR request = no `ValidationPipelineBehavior` = no `UnitOfWorkPipelineBehavior`. The controller is its own request boundary.

### C. PM-absorbed (no user input needed)

- **Endpoint:** `GET /api/v1/maker/files/orders/{orderId}/label` per INDEX line. New `FilesController` on Web.Maker.
- **Auth:** `[Authorize]` + maker role. Order ownership-scoped via existing IOrderRepository.GetByIdForMakerAsync.
- **Maker scoping:** resolve maker from session via IUserSessionProvider; lookup Order via repository's ForMaker-scoped method.
- **Streaming:** direct stream (no buffer-to-memory). Labels are 5-10KB; FileStreamResult or PushStreamContent.
- **No new BusinessErrorMessage codes** — reuse OrderNotFound (404) + ShippingCarrierUnavailable (mapped to 503).
- **PdfBlobPath conventions:** flat `invoices/{cc}/orders/{orderId}/label.pdf` per T-0070 lock.
- **Cache-control specifics:** `public, max-age=31536000, immutable` on 200 OK responses. NO cache on 503 / 404 / 401 responses.
- **Background task lifetime:** fire-and-forget via `_ = Task.Run(...)`. NOT awaited. Logs success/failure but doesn't surface to maker. `ILogger<FilesController>` logs at Information for cache-fills + Warning on background blob upload failures (Packeta delivered the label inline but blob save broke — next request will re-fallback).
- **Stream lifetime:** Packeta response Stream is consumed twice in the fallback path — once for the maker response, once for the background upload. Must use `MemoryStream` buffer (label fits comfortably in memory; 30 KB max realistic). Stream the buffer to the maker; pass the same buffer to the upload task.

## Scope

### Domain layer

- **No new files.** `IBlobStorageClient.ExistsAsync(string container, string path, CancellationToken)` is already present (verified during grooming at `Core.Domain/Storage/IBlobStorageClient.cs`); the implementer MUST NOT add a duplicate signature.
- **No domain method on Order.** This slice is a pure read; no state transition.

### AppServices layer

- **No new MediatR handler.** Per ADR 0014 §"Handler-free read paths" + locked decision B, controller-only slice. T-0075 ships only the controller; no `Features/Files/<UseCase>.cs`.

### Infrastructure layer

- **No new client.** The implementer reuses `IShippingCarrierFactory` + `IShippingCarrier` from T-0070 verbatim. No changes to `PacketaShippingCarrier`.
- **No new blob client method.** `IBlobStorageClient.ExistsAsync` and `IBlobStorageClient.DownloadAsync` already exist (T-0011 / T-0042 surface).

### Database layer

- **No schema changes.** No migration. No repository changes.

### Web.Maker host

- **`backend/src/Makables.Web.Maker/Controllers/FilesController.cs`** — new sealed controller. Mirrors `Web.Maker/Controllers/OrdersController.cs` shape:
  - `[ApiController]`, `[ApiVersion("1.0")]`, `[Route("api/v{version:apiVersion}/maker/files")]`, `[Authorize]`. Inherits `MakablesApiController`.
  - Primary-ctor DI: `IOrderRepository orders`, `IMakerRepository makers`, `IBlobStorageClient blobs`, `IShippingCarrierFactory carrierFactory`, `IUserSessionProvider session`, `ILogger<FilesController> logger`.
  - **`[HttpGet("orders/{orderId}/label")] GetShippingLabel(string orderId, CancellationToken ct)`** with `[ProducesResponseType(StatusCodes.Status200OK)]`, `[ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]`, `[ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]`, `[ProducesResponseType(typeof(Error), StatusCodes.Status503ServiceUnavailable)]`.
  - **Flow** (exact sequence the implementer follows):
    1. `var userId = session.GetUserId();` — if null/empty → `Unauthorized(Error.Unauthorized())`.
    2. `var maker = await makers.GetByUserIdAsync(userId, ct);` — if null → `NotFound(Error.NotFound("orderId", BusinessErrorMessage.OrderNotFound))`.
    3. `var order = await orders.GetByIdForMakerAsync(orderId, maker.Id, ct);` — if null → `NotFound(Error.NotFound("orderId", BusinessErrorMessage.OrderNotFound))`.
    4. If `string.IsNullOrEmpty(order.ShippingCarrierRef)` → `NotFound(Error.NotFound("orderId", BusinessErrorMessage.OrderNotFound))` (personal-pickup order, or shipment not yet recorded — same 404 surface).
    5. Compute `var cc = order.CountryCode.ToLowerInvariant();` and `var path = $"{cc}/orders/{orderId}/label.pdf";` (per ADR 0011 path convention; container is `BlobContainer.Invoices`).
    6. `var existsResult = await blobs.ExistsAsync(BlobContainer.Invoices, path, ct);` — if `existsResult.IsSuccess && existsResult.Value` (cache hit):
       - `var dl = await blobs.DownloadAsync(BlobContainer.Invoices, path, ct);` — if failure → fall through to fallback (treat blob-row-but-no-blob as a cache miss; rare race).
       - Set `Response.Headers.CacheControl = "public, max-age=31536000, immutable";`.
       - Return `File(dl.Value.Content, "application/pdf")` (no enableRangeProcessing — labels are too small to benefit; eliminates an attack surface).
    7. **Fallback path** (cache miss):
       - `var carrierResult = await carrierFactory.ResolveAsync(order.CountryCode, ct);` — if failure → map per `HandleResult` (Configuration → 503; etc.).
       - `var labelResult = await carrierResult.Value.GetLabelPdfAsync(order.ShippingCarrierRef, ct);` — on:
         - `Transient` → log `Information("Packeta transient on label fallback for order {OrderId}", orderId);` → `Response.Headers["Retry-After"] = "60";` → return `StatusCode(503, Error.Transient(BusinessErrorMessage.ShippingCarrierUnavailable))`.
         - `Permanent` → return `NotFound(Error.NotFound("orderId", BusinessErrorMessage.OrderNotFound))` (Packeta has lost the label permanently — surface as not-found to maker).
         - `Configuration` → `Response.Headers["Retry-After"] = "60";` → `StatusCode(503, Error.Transient(BusinessErrorMessage.ShippingCarrierUnavailable))` (operationally-fixable; same 503 surface).
         - `Unknown` → `StatusCode(503, Error.Transient(BusinessErrorMessage.ShippingCarrierUnavailable))` (no Retry-After — unknown classification = unknown recovery window).
       - Buffer the Packeta `Stream` to a `MemoryStream`: `var buffer = new MemoryStream(); await labelResult.Value.CopyToAsync(buffer, ct); buffer.Position = 0;` then `await labelResult.Value.DisposeAsync();`.
       - Capture a **second `MemoryStream`** copy of the bytes for the background upload (`var uploadBuffer = new MemoryStream(buffer.ToArray());`). Do NOT share the same `MemoryStream` instance with the background task — the response writer and the upload would race on `Position`.
       - **Fire-and-forget background cache fill:**
         ```csharp
         _ = Task.Run(async () =>
         {
             try
             {
                 var uploadResult = await blobs.UploadAsync(
                     BlobContainer.Invoices, path, uploadBuffer, "application/pdf", CancellationToken.None);
                 if (!uploadResult.IsSuccess)
                 {
                     logger.LogWarning(
                         "Background label cache-fill failed for order {OrderId}: {ErrorCode}",
                         orderId, uploadResult.Error.Code);
                 }
                 else
                 {
                     logger.LogInformation("Background label cache-fill succeeded for order {OrderId}", orderId);
                 }
             }
             catch (Exception ex)
             {
                 logger.LogWarning(ex, "Background label cache-fill threw for order {OrderId}", orderId);
             }
             finally
             {
                 await uploadBuffer.DisposeAsync();
             }
         });
         ```
         Use `CancellationToken.None` inside `Task.Run` — the request's `ct` is cancelled when the response completes, which would orphan the upload. The implementer MUST NOT pass `ct` into the background task.
       - Set `Response.Headers.CacheControl = "public, max-age=31536000, immutable";`.
       - Return `File(buffer, "application/pdf")` (the controller-returned `MemoryStream` is disposed by ASP.NET after streaming completes).
  - **Helper:** no helpers needed; logic inlines cleanly.
- The controller MUST NOT do any work between resolving the carrier result and writing the response (no DB writes, no audit logs, no MediatR sends).
- The controller MUST set `Cache-Control` ONLY on `200 OK` paths. Verify this by NOT setting the header before the `if (existsResult ...)` block.

### Config / DI

- No DI changes. `IOrderRepository`, `IMakerRepository`, `IBlobStorageClient`, `IShippingCarrierFactory`, `IUserSessionProvider`, `ILogger<>` are all already registered in `AddMakablesXxx()` extensions used by `Web.Maker/Program.cs`.

### i18n

- **No new keys.** `shipping.carrierUnavailable` (T-0070) covers the 503 surface; the maker dashboard's existing order-not-found copy covers 404. The maker dashboard frontend (T-0086 territory; out of scope here) maps these codes to user-facing strings.

### NSwag regen

- The new `GET /api/v1/maker/files/orders/{orderId}/label` endpoint adds one method to the Maker host's OpenAPI document. NSwag regen is REQUIRED in the same PR (pre-commit hook blocks manual edits to `frontend/src/lib/api-client/`). Run `npm run generate:api` after the backend builds clean.

### Tests

- **`backend/src/Makables.Tests/Web/Maker/Controllers/FilesControllerLabelDownloadTests.cs`** (NEW, ~10 unit tests):
  1. `GetShippingLabel_NoUserId_ReturnsUnauthorized` — `IUserSessionProvider.GetUserId()` returns null → 401.
  2. `GetShippingLabel_UserWithoutMakerRow_Returns404` — `IMakerRepository.GetByUserIdAsync` returns null → 404 with `OrderNotFound`.
  3. `GetShippingLabel_OrderNotFoundForMaker_Returns404` — `IOrderRepository.GetByIdForMakerAsync` returns null (ownership scope mismatch) → 404 with `OrderNotFound`. Asserts that `IBlobStorageClient.ExistsAsync` was NOT called (`Received(0)`).
  4. `GetShippingLabel_OrderShippingCarrierRefNull_Returns404` — order found but `ShippingCarrierRef` is null → 404 with `OrderNotFound`.
  5. `GetShippingLabel_BlobCacheHit_StreamsBlobWithImmutableCacheControl` — `ExistsAsync` returns true; `DownloadAsync` returns a stream; asserts `Cache-Control == "public, max-age=31536000, immutable"`, content-type `application/pdf`, stream contents byte-equal to seeded bytes. Asserts `IShippingCarrierFactory.ResolveAsync` was NOT called.
  6. `GetShippingLabel_BlobMissPacketaSuccess_StreamsAndSchedulesCacheFill` — `ExistsAsync` returns false; `carrier.GetLabelPdfAsync` returns success with a seeded `MemoryStream`. Asserts response is 200 with correct bytes + immutable Cache-Control. Asserts that `IBlobStorageClient.UploadAsync` is eventually invoked with `BlobContainer.Invoices`, expected path, and `application/pdf` content type (use a `TaskCompletionSource` or `Received(1).UploadAsync(...)` with a short polling helper; the implementer MAY use `await Task.Delay(50)` to allow the fire-and-forget Task.Run to land — keep the delay <= 200ms to keep tests fast).
  7. `GetShippingLabel_BlobMissPacketaTransient_Returns503WithRetryAfter60` — `carrier.GetLabelPdfAsync` returns `Error.Transient(ShippingCarrierUnavailable)` → asserts status 503, `Retry-After == "60"`, error body code matches `shipping.carrierUnavailable`, and `Cache-Control` header NOT present on the response.
  8. `GetShippingLabel_BlobMissPacketaPermanent_Returns404` — `carrier.GetLabelPdfAsync` returns `Error.Permanent(...)` → 404 with `OrderNotFound`. No `Cache-Control`.
  9. `GetShippingLabel_BlobMissPacketaConfiguration_Returns503WithRetryAfter60` — Configuration error → 503 + Retry-After: 60. No `Cache-Control`.
  10. `GetShippingLabel_CacheControlAbsentOn503` — explicit assertion that `Response.Headers.CacheControl` is unset when the response is 503 (re-checks AC-9 from a different angle: cold cache + Packeta Transient + verifies header absence; can be merged with test #7 if implementer prefers).
  11. `GetShippingLabel_ResponseMimeTypeIsApplicationPdf` — both cache-hit and cache-miss success paths return `Content-Type: application/pdf` exactly.
  12. `GetShippingLabel_StreamLengthMatchesSourceBytes` — for both the blob-hit and Packeta-fallback paths, the response body length equals the seeded buffer length (sanity check that the `MemoryStream` buffer wasn't truncated by a missed `Position = 0`).
  - Test framework: xUnit + NSubstitute, mirroring existing `Makables.Tests/Web/...` patterns. The implementer can stub `Task.Run` background work by injecting a `TaskCompletionSource` callback OR by polling `Received().UploadAsync` for up to 200ms — both are precedented in T-0067 outbox tests.
- **`backend/src/Makables.IntegrationTests/Shipping/LabelDownloadIntegrationTests.cs`** (NEW, 1 test):
  - `GetShippingLabel_BlobCacheHit_EndToEnd` — seeds a Postgres row for an Order (Paid → Accepted with `ShippingCarrierRef` set), seeds a fake blob in `FakeBlobStorageClient` at the expected path with random bytes, hits the endpoint with a valid maker JWT, asserts 200 + bytes-equal + Cache-Control + content-type. Uses the existing integration-test harness (`FakeBlobStorageClient` in `Makables.IntegrationTests/Common/`).
  - The implementer MAY skip an end-to-end Packeta fallback integration test (would require wiring a Wiremock or fake `IShippingCarrier` into the integration host); the unit tests cover the fallback paths exhaustively.

### Docs

- **`docs/architecture/roles/shipping-carrier.md`** — append one paragraph under "Consumers" noting T-0075 as a read-side consumer (label fallback path). Mirrors how `payment-provider.md` lists Comgate consumers.
- **`docs/tickets/INDEX.md`** — PM flips T-0075 row to `**done**` after PR merge.

## Alternatives Considered

- **Option A — Synchronous wait for queue + 202 + polling.** *Rejected per A.1* — forces a polling UX in the maker dashboard, doubles roundtrips on every first-download, and the 1-2s Packeta latency is acceptable for a maker-initiated action (not customer-facing checkout).
- **Option B — Synchronous blocking cache-fill before responding.** *Rejected per A.1* — adds 1-2s of blob-write latency on top of the Packeta fetch on the cold path. Fire-and-forget gives the maker the bytes immediately.
- **Option C — Return 404 on Packeta 5xx.** *Rejected per A.2* — lies about the state; the order has a label, the carrier is just temporarily down. 503 + Retry-After is the honest HTTP contract.
- **Option D — Synchronous retry within request.** *Rejected per A.2* — worst-case 15s+ with exponential backoff on top of the original 1-2s; UX terrible. Polly resilience on the `HttpClient` already gives one-shot retries inside the Packeta call; further retry belongs in the queue path (T-0074), not the maker fallback.
- **Option E — 202 Accepted + poll endpoint.** *Rejected per A.2* — overkill for a slice this small; no other endpoint in the codebase uses this pattern.
- **Option F — 1h cache (matching T-0070 widget cache).** *Rejected per A.3* — labels are far more deterministic than widget configs (which rotate keys); 1h is wasteful when 1-year + immutable is honest.
- **Option G — `private, max-age=86400` (24h, private).** *Rejected per A.3* — defeats CDN benefits. Labels are not customer-private; they are addressable only via an authenticated maker URL, so once delivered to the browser they can safely sit in the CDN's edge for the addressee.
- **Option H — `no-cache` (matching T-0064 OrderAttachment).** *Rejected per A.3* — attachments are customer-uploaded sensitive files (CAD specs, contracts); labels are deterministic system artifacts derived purely from the order id. Cache discipline can differ.
- **Option I — Introduce MediatR handler for the read path.** *Rejected per ADR 0014 §"Handler-free read paths" + locked B* — a single passthrough adapter call with no validation, no business rule, no transaction. Handler adds ceremony without separation benefit. The controller is the use case.
- **Option J — Add new `LabelDownloadFailed` BusinessErrorMessage code.** *Rejected per PM-absorbed* — `OrderNotFound` + `ShippingCarrierUnavailable` already cover the two surfaces (resource-missing vs carrier-down). No new code adds discrimination value at the maker UI.
- **Option K — Re-enqueue T-0074 generate-label queue on cache miss.** *Rejected per A.1* — would require a queue producer in `Web.Maker` (new dependency on `Infra.Common.Outbox`) and forces async polling. Direct Packeta call from the controller is simpler and matches what T-0078 already does for status sync.
- **Option L — Use `BlobContainer.OrderAttachments` instead of `BlobContainer.Invoices`.** *Rejected per T-0070 §B + ADR 0017* — labels are platform-generated artifacts addressed by order id, identical access controls to invoices. One container = simpler.

## Out of scope

- **Cache invalidation on label regeneration** — labels are deterministic per ADR 0017; the path is order-id-keyed and never changes. If a future ticket adds label re-issuance (e.g., maker corrects the address), it owns the invalidation.
- **Per-IP rate limiting** — the endpoint is `[Authorize]`-gated; per-user throttling (if needed) is a Phase 5 cross-cutting concern, not endpoint-specific. The maker dashboard hits this endpoint rarely (one click per order).
- **Frontend maker dashboard integration** — the "Stáhnout štítek" button on the maker order detail page is T-0086 territory. T-0075 ships only the backend endpoint + NSwag-generated client method.
- **Admin host parity** — admins can already download labels via direct blob access in the Azure portal; an `/api/v1/admin/files/...` parity endpoint is a future ticket.
- **End-to-end Packeta fallback integration test** — covered exhaustively by unit tests; integration test only validates the blob-hit happy path (avoids wiring Wiremock for a single edge).
- **Outbox event for label download** — labels are reads; no audit trail required at MVP. If GDPR / compliance later requires it, a separate ticket adds the audit log.
- **Tracking-URL endpoint** — `Order.ShippingCarrierTrackingUrl` is already on the Order DTO; no separate endpoint needed.
- **Customer-facing label download** — customers do not download labels; they receive a notification when the package ships (T-0072 outbox event). Out of scope here and forever.
- **HEAD-method support** — only `GET` is in scope. A future ticket can add `HEAD` for size-probing if browser caching needs it.
- **Range-request support** — `enableRangeProcessing` is intentionally `false`. Labels are 5-10KB; partial-content requests add complexity for zero benefit.

## Acceptance criteria

- **AC-1** Given a maker with no session userId, when they call `GET /api/v1/maker/files/orders/{orderId}/label`, then the response is `401 Unauthorized` with body matching `Error.Unauthorized()`. No `Cache-Control` header on the response.
- **AC-2** Given an authenticated user with no maker row (a customer token replayed against Maker host), when they hit the endpoint, then the response is `404 Not Found` with code `order.notFound`. No `Cache-Control` header.
- **AC-3** Given an authenticated maker AND an `orderId` belonging to another maker, when they hit the endpoint, then `IOrderRepository.GetByIdForMakerAsync` returns null and the response is `404 Not Found` with code `order.notFound`. Asserts (via NSubstitute `Received(0)`) that `IBlobStorageClient.ExistsAsync`, `IShippingCarrierFactory.ResolveAsync`, and `IBlobStorageClient.DownloadAsync` are NOT called.
- **AC-4** Given an order owned by the maker but with `ShippingCarrierRef == null` (personal-pickup OR shipment-not-yet-recorded), when they hit the endpoint, then the response is `404 Not Found` with code `order.notFound`. Asserts `IBlobStorageClient.ExistsAsync` NOT called.
- **AC-5** Given the order's label is present in blob storage at `{cc}/orders/{orderId}/label.pdf` in `BlobContainer.Invoices`, when the maker hits the endpoint, then the response is `200 OK` with `Content-Type: application/pdf`, body byte-equal to the blob contents, and `Cache-Control: public, max-age=31536000, immutable`. Asserts `IShippingCarrierFactory.ResolveAsync` NOT called.
- **AC-6** Given the label is NOT in blob storage AND Packeta returns the label PDF successfully, when the maker hits the endpoint, then the response is `200 OK` with `Content-Type: application/pdf`, body byte-equal to the Packeta stream contents, and `Cache-Control: public, max-age=31536000, immutable`. Within 200ms after the response completes, `IBlobStorageClient.UploadAsync` is invoked with `BlobContainer.Invoices`, path `{cc}/orders/{orderId}/label.pdf`, content type `application/pdf`, and bytes matching the Packeta stream.
- **AC-7** Given the label is NOT in blob storage AND `IShippingCarrier.GetLabelPdfAsync` returns `BusinessResult.Failure(Error.Transient(ShippingCarrierUnavailable))`, when the maker hits the endpoint, then the response is `503 Service Unavailable` with header `Retry-After: 60`, body code `shipping.carrierUnavailable`, and NO `Cache-Control` header.
- **AC-8** Given the label is NOT in blob storage AND `IShippingCarrier.GetLabelPdfAsync` returns `BusinessResult.Failure(Error.Permanent(...))`, when the maker hits the endpoint, then the response is `404 Not Found` with code `order.notFound` and NO `Cache-Control` header.
- **AC-9** Given the label is NOT in blob storage AND `IShippingCarrier.GetLabelPdfAsync` returns `BusinessResult.Failure(Error.Configuration(ShippingCarrierConfigurationError))`, when the maker hits the endpoint, then the response is `503 Service Unavailable` with header `Retry-After: 60` and body code `shipping.carrierUnavailable`. No `Cache-Control` header.
- **AC-10** Given a successful response (200 OK), when the Content-Length header is inspected, then it equals the source buffer's byte length (verifies `MemoryStream.Position = 0` is set before `File(...)` returns).
- **AC-11** Given the background cache-fill task throws an exception (e.g., simulated blob storage outage), when the maker has already received the 200 OK response, then `ILogger<FilesController>.LogWarning` is invoked exactly once with the order id in the log scope. The maker response is unaffected.
- **AC-12** Build clean. Unit tests: baseline + 11 new (FilesControllerLabelDownloadTests). Integration tests: baseline + 1 new (LabelDownloadIntegrationTests). All green.
- **AC-13** Consistency script exit 0 (no new T1–T7 violations vs the baseline at the head of this bundle).
- **AC-14** NSwag regen committed in the same PR; `frontend/src/lib/api-client/` contains the typed `GetShippingLabel` method on the Maker client surface.

## Technical notes

### Why no MediatR handler

The slice is a single read with a passthrough adapter call. ADR 0014 §"Handler-free read paths" + `docs/architecture/patterns.md` allow controller-only slices when (a) there is no validation rule worth running through `ValidationPipelineBehavior` (the only input is a path-bound `orderId`), (b) there is no state mutation worth wrapping in `UnitOfWorkPipelineBehavior`, and (c) the use case fits on one page. T-0075 satisfies all three. Adding a handler would add ~80 lines of nested-class ceremony for zero separation benefit and would force a fake `IMediator` setup in every test. The controller IS the use case.

### Why fire-and-forget Task.Run (not BackgroundService, not IHostedService, not queue)

`Task.Run` is appropriate when (a) the work is short (<5s; here a single blob upload of 30 KB), (b) loss of the work on process restart is acceptable (the next request will re-trigger the fallback), and (c) the work has no downstream dependencies (no outbox emit, no domain event). All three hold. A `BackgroundService` would couple the controller's lifetime to an additional channel/queue; an `IHostedService` would add app-startup coordination overhead. The trade-off is: if the worker process is killed mid-upload, the cache stays cold for one extra request. Acceptable per locked decision A.1.

### Why `CancellationToken.None` inside the Task.Run

The request's `CancellationToken` is signalled when ASP.NET completes the response (or when the client disconnects). If we pass `ct` into the `Task.Run`, the upload would be cancelled the instant the maker's browser closes the connection — orphaning the cache. The background task is by design fire-and-forget; it intentionally outlives the request. Per `docs/architecture/patterns.md` §"Background work in controllers", use `CancellationToken.None` for fire-and-forget work.

### Why the MemoryStream is duplicated (two buffers)

The maker response and the background upload would race on `MemoryStream.Position` if they shared the same instance. ASP.NET's `FileResult` reads forward from `Position` (potentially asynchronously with the Task.Run already iterating). The cleanest fix is to allocate two `MemoryStream` instances backed by the same `byte[]` snapshot. At 30 KB per label this is negligible. The Packeta response `Stream` is disposed AFTER `CopyToAsync` completes (synchronous-await before returning), so the network stream is not leaked.

### Why Configuration-classified errors map to 503 (not 500)

A Configuration error means "API key is wrong" or "sender label rejected" — operationally fixable by the team without code deploy. Surfacing `500 Internal Server Error` would (a) blow alarms unnecessarily and (b) lie about recoverability. `503 + Retry-After` tells the maker "try again in a minute" which is the correct behaviour while ops rotates the key. The team gets paged via the `LogWarning` in `PacketaShippingCarrier` (T-0070) and the alert-on-warning-rate metric.

### Why immutable + 1-year (not just 1-year)

`Cache-Control: public, max-age=31536000, immutable` instructs browsers to NOT revalidate on reload. Without `immutable`, Chrome / Firefox issue a conditional `If-None-Match` request on hard-reload (Ctrl+F5) — defeating the cache for the most common debugging gesture. Per RFC 8246. The label is order-id-keyed; the bytes are deterministic; revalidation is wasted bandwidth.

## Files touched (expected)

### New
- `backend/src/Makables.Web.Maker/Controllers/FilesController.cs`
- `backend/src/Makables.Tests/Web/Maker/Controllers/FilesControllerLabelDownloadTests.cs`
- `backend/src/Makables.IntegrationTests/Shipping/LabelDownloadIntegrationTests.cs`

### Modified
- `frontend/src/lib/api-client/*` — NSwag-regenerated; committed in the same PR.
- `docs/architecture/roles/shipping-carrier.md` — append one paragraph listing T-0075 as a read-side consumer.

### NOT modified (explicit non-changes — guards against scope creep)
- `backend/src/Makables.Core.Domain/Storage/IBlobStorageClient.cs` — `ExistsAsync` already present.
- `backend/src/Makables.Core.Domain/Orders/Order.cs` — no schema or method change.
- `backend/src/Makables.Core.Domain/Common/BusinessErrorMessage.cs` — no new codes.
- `backend/src/Makables.Core.Domain/Orders/IOrderRepository.cs` — no new repository method.
- `backend/src/Makables.Infra.Database/Migrations/` — no migration.
- `backend/src/Makables.Config/Extensions/` — no DI changes.
- `frontend/src/lib/i18n/cs-CZ.ts` — no new keys.

## Test plan reference

Inline above (see Scope > Tests). No separate `docs/test-plans/T-0075.md`.

## Status log

- 2026-06-08 `draft` by PM. Created from INDEX line; T-0074 grooming completed in the same bundle pass.
- 2026-06-08 `draft → ready` by PM. User answered 3 blocking AskUserQuestion items per `/feature` workflow step 3 (fire-and-forget Task.Run fallback; 503 + Retry-After on Packeta 5xx; `public, max-age=31536000, immutable` Cache-Control). Remaining decisions PM-absorbed (no new error codes, no new i18n keys, controller-only feature shape per ADR 0014, flat blob path per T-0070 §A.7, `CancellationToken.None` inside background Task.Run, double-buffered MemoryStream to avoid Position race). ADR 0017 + ADR 0014 + ADR 0013 cover the remaining architectural choices. No new manual deploy steps; the Packeta secret was provisioned in T-0070. **Ready for dotnet-backend.**
