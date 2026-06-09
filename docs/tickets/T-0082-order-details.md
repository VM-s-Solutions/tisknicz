---
id: T-0082
title: GetCustomerOrderDetails + GetMakerOrderDetails queries (audience-specific DTOs)
status: ready
size: M
owner: dotnet-backend
created: 2026-06-09
updated: 2026-06-09
depends_on: [T-0060, T-0080, T-0081]
blocks: [T-0086, T-0087]
user_stories: [US-customer-0012, US-maker-0010]
adrs: [0013, 0014, 0023]
phase: 4
manual_steps: []
security_touching: false
layers: [domain, appservices, infra-database, web-customer, web-maker]
---

# T-0082 — GetCustomerOrderDetails + GetMakerOrderDetails queries (audience-specific DTOs)

## Context

T-0082 is the **third ticket in the order-queries bundle** (T-0080 customer list + T-0081 maker list + T-0082 details). All three ship under one PR with sequential implementation: T-0080 introduced the customer paged list query + IOrderQueries seam; T-0081 added the maker paged list query + scoped repository extensions; T-0082 closes the bundle with the **two audience-specific detail queries** that power the customer and maker order-detail pages (T-0086 + T-0087 frontends). The bundle convention — established in the shipping bundle (T-0070-T-0075) and reinforced in the delivery-close bundle (T-0076-T-0078) — is one PR per bundle with locked, globally-unique Response naming to sidestep NSwag TS class collisions across hosts.

The customer and maker order-detail pages have **materially different information needs**. Customers see lifecycle timestamps, a price breakdown, the maker's display name, attachments, and an invoice PDF link — but never the maker's contact details or payout amounts. Makers see the same lifecycle skeleton plus their payout amount, the customer's contact name and phone (for handover coordination on personal-pickup or shipping label generation), Packeta carrier refs, and the Zasilkovna pickup point id when present — but **never the customer's email** (per the T-0081 GDPR data-minimization lock; maker-customer communication routes through the T-0079 messages thread). Cramming both audiences into a single shared DTO with runtime audience checks is exactly the kind of leak surface that gets a maker-internal payout amount accidentally serialized to a customer response under a sloppy refactor; T-0082 forbids the shape entirely.

The bundle locked **two separate Queries** (`GetCustomerOrderDetails.Query(orderId)` + `GetMakerOrderDetails.Query(orderId)`) each with its own DTO, its own handler, and its own IDOR scoping primitive (`GetByIdForCustomerAsync` vs `GetByIdForMakerAsync` per ADR 0013 scoped repository convention). The compile-time type split is the IDOR shield: a customer DTO simply does not have a `MakerPayoutAmountMinor` field, so leaking it requires editing the DTO definition — visible in code review. Mirrors the T-0049a precedent that scoped maker queries from public catalog queries by interface separation rather than by a runtime audience flag.

Detail responses are **inline-rich** — both DTOs carry an `Attachments` collection (T-0064's `OrderAttachment` is already bounded at 10 per order; small payload) and an `InvoicePdfUrl: string | null` (built by the backend from a configured `InvoiceDownloadUrlBase` + invoice number, pointing at the downstream T-0086/T-0087 customer/admin download endpoint when wired; null until T-0068b's `Invoice.PdfBlobPath` is populated). The inline pattern saves a roundtrip per detail-page render (the FE always wants attachments + invoice on first paint) at the cost of a bounded payload. Separate `/orders/{id}/attachments` and `/orders/{id}/invoice` sub-resources were considered and rejected for marginal RESTfulness gain on bounded child collections.

The bundle's globally-unique Response naming convention applies: `GetCustomerOrderDetailsResponse` + `GetMakerOrderDetailsResponse`. No new `BusinessErrorMessage` codes (reuses `OrderNotFound` from T-0060). No EF migrations (read-only projection). NSwag regen runs against **both** customer + maker hosts in the same PR.

## Locked design decisions

Captured per `docs/process/deliberation.md`. The user locked 3 dimensions at `/feature` step 3 (two-Queries vs single shared, inline Attachments vs separate endpoint, inline InvoicePdfUrl vs client-constructed). 8 PM-absorbed decisions follow from T-0049a/T-0080/T-0081 precedents and the bundle's standing locks.

### A. User-locked at /feature step 3 (non-negotiable)

1. **Two separate Queries: `GetCustomerOrderDetails` + `GetMakerOrderDetails`.** Each handler has explicit IDOR scoping (`GetByIdForCustomerAsync` vs `GetByIdForMakerAsync`), separate DTO shapes (customer never sees maker-internal fields like payout amount; maker sees customer contact name + phone for handover but NOT email per T-0081 GDPR lock). Cleaner DI; compile-time IDOR shield. Mirrors T-0049a precedent. **Rejected:** single shared `GetOrderDetails.Query(orderId, audience)` with runtime branch — easy to leak maker-internal fields by accident; runtime audience check instead of compile-time type.

2. **Inline Attachments list in detail response.** Detail DTO includes `Attachments: [{ id, filename, contentType, sizeBytes, downloadUrl }, ...]`. T-0086/T-0087 frontend dashboards ALWAYS render attachments on detail page render — inline saves a second request per render. Attachments are bounded (T-0064 caps at 10/order) so payload stays small. **Rejected:** separate `/orders/{id}/attachments` query (adds roundtrip per detail render for marginal RESTfulness gain on a bounded child collection).

3. **Inline InvoicePdfUrl in detail response.** Detail DTO carries `InvoicePdfUrl: string | null` once T-0068b's `Invoice.PdfBlobPath` is populated (else null). Backend owns URL construction; URL points at the customer/maker download endpoint (downstream ticket; placeholder route TBD) — NOT a direct blob URL (which would leak storage details + bypass auth). **Rejected:** client constructs from InvoiceNumber + route (tighter FE/BE coupling; URL change breaks both); inline raw blob path (exposes storage structure + SAS-token-rotation fragility).

### B. ADR-locked (no relitigation)

- **ADR 0013 (per-audience JWT enforcement + scoped repositories).** Customer endpoint runs under the `Web.Customer` host audience; maker endpoint runs under the `Web.Maker` host audience. A customer JWT cannot be replayed against the maker host (and vice versa). Repository read-side splits into `ForCustomer` / `ForMaker` only — no `Unscoped` for detail (each audience's detail query routes through its own scoped lookup). `IOrderQueries.GetCustomerOrderDetailsAsync(orderId, customerUserId, ct)` and `GetMakerOrderDetailsAsync(orderId, makerId, ct)` are the read-side primitives; both return `null` when the row is missing OR not owned by the requester (same shape = no IDOR oracle).
- **ADR 0014 (UoW pipeline).** Read-only queries do not commit. `ValidationPipelineBehavior` still runs (Validator pins OrderId non-empty + format). No `UnitOfWorkPipelineBehavior` work because no commands.
- **ADR 0023 (read-side projection performance).** Both handlers MUST use `.AsNoTracking()` + `.IgnoreAutoIncludes()` on the EF projection-only query. Materializes a single anonymous-or-DTO-shape via `Select(...)`. Attachments collection projects in the same query (no N+1) since `OrderAttachment` is bounded at 10/order per T-0064 — a single `LEFT JOIN` materializes the rows in one round-trip.
- **One-file feature shape.** `Features/Orders/GetCustomerOrderDetails.cs` contains nested `Query`, `Validator`, `Handler`, `GetCustomerOrderDetailsResponse`. Same for `GetMakerOrderDetails.cs`. No separate files per type.
- **`BusinessResult<T>` for expected failures.** Ownership mismatch / not-found → `Error.NotFound("order", BusinessErrorMessage.OrderNotFound)` (existing code reused). Exceptions reserved for truly unexpected (e.g., DB connection dropped).
- **Globally-unique Response naming** (bundle-wide lock). `GetCustomerOrderDetailsResponse` + `GetMakerOrderDetailsResponse`. The bundle's other tickets (T-0080, T-0081) ship `GetCustomerOrdersResponse` + `GetMakerOrdersResponse`. No bare `Response` records anywhere.

### C. PM-absorbed (no user input needed)

- **Customer detail DTO** (`CustomerOrderDetailDto` at `Core.AppServices/Features/Orders/DTOs/CustomerOrderDetailDto.cs`): `OrderId`, `OrderNumber`, `State` + ALL lifecycle timestamps (`PaidAt`, `AcceptedAt`, `ShippedAt`, `DeliveredAt`, `CancelledAt` — each nullable per state-machine reality), `TotalAmountMinor` + price breakdown (`ProductPriceMinor`, `ShippingPriceMinor`, `VatAmountMinor`, `VatRateBasisPoints`), `Currency`, `ContactName` + `ContactPhone` (snapshot from Order entity — the customer's own contact, useful for confirming what was entered), `MakerName` (denormalized from Maker entity; NO maker phone/email), `ProductTitle` (nullable — null for custom orders that don't reference a product), `ShippingMethod` (enum), `ShippingCarrierTrackingUrl` (nullable; populated only when ShippingMethod = Zasilkovna and a packet has been created), `Attachments: IReadOnlyList<OrderAttachmentSummaryDto>`, `InvoicePdfUrl` (nullable), `CreatedAt`, `UpdatedAt`.
- **Maker detail DTO** (`MakerOrderDetailDto` at `Core.AppServices/Features/Orders/DTOs/MakerOrderDetailDto.cs`): same lifecycle timestamps + price breakdown, plus: `MakerPayoutAmountMinor` (instead of platform-fee field — the maker only ever sees their own cut), `CustomerContactName` + `CustomerContactPhone` (NOT email per T-0081 lock; maker-customer messaging routes through T-0079 thread), `ShippingCarrierRef` (Packeta numeric id, string-wrapped) + `ShippingCarrierTrackingUrl` (nullable), `ZasilkovnaPickupPointId` (nullable; when present and ShippingMethod = Zasilkovna), `Attachments: IReadOnlyList<OrderAttachmentSummaryDto>`, `InvoicePdfUrl` (nullable), `ShippingMethod` (enum), `OrderId`, `OrderNumber`, `State`, `Currency`, `CreatedAt`, `UpdatedAt`. Distinct shape — no shared base class.
- **Shared `OrderAttachmentSummaryDto`** (at `Core.AppServices/Features/Orders/DTOs/OrderAttachmentSummaryDto.cs`): `Id`, `Filename`, `ContentType`, `SizeBytes` (long), `DownloadUrl` (string). The same shape is safe to share across audiences because both audiences see the same attachment metadata on orders they own; the per-audience routing happens at the `DownloadUrl` level (customer URLs vs maker URLs — see PM note on AttachmentDownloadUrlBase below).
- **Action-buttons in maker detail:** NOT in response. Frontend (T-0087) owns the state-machine knowledge — which transitions are valid from which states. Backend response is pure data; FE inspects `State` and conditionally renders Accept/Ship/Handover buttons. Same shape for customer (T-0086 conditionally renders Mark-as-Delivered based on State).
- **`GetCustomerOrderDetails.Handler` flow:** primary-constructor DI `(ICustomerSessionContext sessionContext, IOrderQueries orderQueries)`. Steps: (1) `var customerUserId = sessionContext.RequireCustomerUserId();` (raises auth failure if missing — handled by middleware); (2) `var dto = await orderQueries.GetCustomerOrderDetailsAsync(command.OrderId, customerUserId, ct);`; (3) `if (dto is null) return BusinessResult.Failure<GetCustomerOrderDetailsResponse>(Error.NotFound("order", BusinessErrorMessage.OrderNotFound));`; (4) `return BusinessResult.Success(new GetCustomerOrderDetailsResponse(dto));` (or return DTO directly — implementer picks the simpler shape, but the wire type name is the locked `GetCustomerOrderDetailsResponse`).
- **`GetMakerOrderDetails.Handler` flow:** primary-constructor DI `(IMakerSessionContext sessionContext, IMakerRepository makerRepository, IOrderQueries orderQueries)`. Steps: (1) resolve `makerId` from session via `IMakerRepository.GetByUserIdAsync(sessionContext.RequireUserId(), ct)` — null → `Error.NotFound("maker", BusinessErrorMessage.MakerNotFound)` (existing code; aligns with T-0049a precedent); (2) `var dto = await orderQueries.GetMakerOrderDetailsAsync(command.OrderId, maker.Id, ct);`; (3) null → `Error.NotFound("order", BusinessErrorMessage.OrderNotFound)`; (4) return `GetMakerOrderDetailsResponse(dto)`.
- **`InvoicePdfUrl` resolution:** if `Order.InvoicePdfBlobPath` is set (or wherever the join lives — implementer checks T-0068b: today the `Invoice` entity owns `PdfBlobPath`; the EF projection LEFT JOINs invoices on `order_id` and projects `invoice.pdf_blob_path` + `invoice.invoice_number`), build URL via injected `IInvoiceDownloadUrlBuilder.BuildForCustomer(invoiceNumber)` / `.BuildForMaker(invoiceNumber)` (or a shared `Build(invoiceNumber, audience)` if simpler). The URL prefix comes from `IOptions<DownloadUrlsOptions>.Value.InvoiceDownloadUrlBase`. Placeholder route accepted at T-0082 — final download endpoint lands in T-0086/T-0087/admin ticket. If `InvoicePdfBlobPath` is null (invoice not yet generated), `InvoicePdfUrl = null`.
- **`AttachmentDownloadUrl` construction:** built from `IOptions<DownloadUrlsOptions>.Value.AttachmentDownloadUrlBase` + attachment ID + audience prefix (`/customer/...` for customer responses, `/maker/...` for maker responses). The existing T-0064 download endpoint is already wired (with its own IDOR + JWT audience checks); URL points at it. Construction happens **in the EF projection** as a string concat (no domain service) or in a post-projection `Select` pass — implementer picks whichever keeps the projection clean.
- **Globally-unique Response naming:** `GetCustomerOrderDetailsResponse(CustomerOrderDetailDto Detail)` + `GetMakerOrderDetailsResponse(MakerOrderDetailDto Detail)`. Sealed records. The wrapper is trivial but the wire-type name is the locked anti-collision shape — NSwag generates `GetCustomerOrderDetailsResponse` + `GetMakerOrderDetailsResponse` TS classes, no `Response` collision.
- **`IOrderQueries` extension:** add `Task<CustomerOrderDetailDto?> GetCustomerOrderDetailsAsync(string orderId, string customerUserId, CancellationToken ct);` + `Task<MakerOrderDetailDto?> GetMakerOrderDetailsAsync(string orderId, string makerId, CancellationToken ct);` to the existing interface introduced in T-0080. EF impl in `OrderQueries.cs` adds two methods; both use `.AsNoTracking().IgnoreAutoIncludes()` + `Select(...)` projection materializing the DTO + attachments in a single round-trip.
- **NSwag regen:** **both** customer + maker hosts (each gets its own typed endpoint). T-0080 + T-0081 already regen their respective hosts in the same PR; T-0082 stacks the detail endpoints on top.
- **No new error codes** (reuses existing `OrderNotFound` from T-0060 + `MakerNotFound` from T-0049a).
- **No migrations** (read-only).

## Scope

### Domain layer

- **No domain entity changes.** Read-only ticket; no state transitions. `Order`, `OrderAttachment`, `Invoice`, `Maker` entities are unchanged.
- **`Core.Domain/Orders/IOrderQueries.cs`** — extend the interface introduced by T-0080:
  ```csharp
  Task<CustomerOrderDetailDto?> GetCustomerOrderDetailsAsync(
      string orderId,
      string customerUserId,
      CancellationToken ct);

  Task<MakerOrderDetailDto?> GetMakerOrderDetailsAsync(
      string orderId,
      string makerId,
      CancellationToken ct);
  ```
  Returns null when the row is missing OR not owned by the requester (same shape = no IDOR oracle). XML doc references T-0082 as the writer and notes the scoping contract.

### AppServices layer

- **`Core.AppServices/Features/Orders/DTOs/OrderAttachmentSummaryDto.cs`** — NEW sealed record:
  ```csharp
  public sealed record OrderAttachmentSummaryDto(
      string Id,
      string Filename,
      string ContentType,
      long SizeBytes,
      string DownloadUrl);
  ```
  Shared by both audience DTOs. `DownloadUrl` is pre-baked with the audience prefix at projection time.
- **`Core.AppServices/Features/Orders/DTOs/CustomerOrderDetailDto.cs`** — NEW sealed record. Fields per §C above. `Attachments` is `IReadOnlyList<OrderAttachmentSummaryDto>`. `InvoicePdfUrl`, `ShippingCarrierTrackingUrl`, `ProductTitle` are nullable strings. Lifecycle timestamps are `DateTimeOffset?`.
- **`Core.AppServices/Features/Orders/DTOs/MakerOrderDetailDto.cs`** — NEW sealed record. Fields per §C above. **Critically does NOT include CustomerContactEmail** (per T-0081 GDPR lock). `MakerPayoutAmountMinor` is `long`. `ZasilkovnaPickupPointId` is nullable string. No shared base class with `CustomerOrderDetailDto` — distinct types by design (the IDOR-shield via compile-time type split per A.1).
- **`Core.AppServices/Features/Orders/GetCustomerOrderDetails.cs`** — NEW one-file feature.
  - `Query(string OrderId)` record (implements `IRequest<BusinessResult<GetCustomerOrderDetailsResponse>>`).
  - `GetCustomerOrderDetailsResponse(CustomerOrderDetailDto Detail)` sealed record — **globally-unique name** to avoid the NSwag TS class collision.
  - `Validator : AbstractValidator<Query>` — `OrderId` non-empty + valid id format (mirror T-0080's validator shape; implementer matches the existing OrderId rule).
  - `Handler(ICustomerSessionContext sessionContext, IOrderQueries orderQueries)` primary-constructor DI. Steps per §C:
    1. `var customerUserId = sessionContext.RequireCustomerUserId();`
    2. `var dto = await orderQueries.GetCustomerOrderDetailsAsync(query.OrderId, customerUserId, ct);`
    3. `if (dto is null) return BusinessResult.Failure<GetCustomerOrderDetailsResponse>(Error.NotFound("order", BusinessErrorMessage.OrderNotFound));`
    4. `return BusinessResult.Success(new GetCustomerOrderDetailsResponse(dto));`
- **`Core.AppServices/Features/Orders/GetMakerOrderDetails.cs`** — NEW one-file feature.
  - `Query(string OrderId)` record.
  - `GetMakerOrderDetailsResponse(MakerOrderDetailDto Detail)` sealed record.
  - `Validator` — `OrderId` non-empty + format (same as customer side).
  - `Handler(IMakerSessionContext sessionContext, IMakerRepository makerRepository, IOrderQueries orderQueries)` primary-constructor DI. Steps per §C:
    1. Resolve maker via `makerRepository.GetByUserIdAsync(sessionContext.RequireUserId(), ct)` → null → `Error.NotFound("maker", BusinessErrorMessage.MakerNotFound)`.
    2. `var dto = await orderQueries.GetMakerOrderDetailsAsync(query.OrderId, maker.Id, ct);` → null → `Error.NotFound("order", BusinessErrorMessage.OrderNotFound)`.
    3. Return `GetMakerOrderDetailsResponse(dto)`.

### Infrastructure / Database layer

- **`Infra.Database/Orders/OrderQueries.cs`** — extend the EF impl introduced by T-0080.
  - **`GetCustomerOrderDetailsAsync`** — LINQ:
    ```
    DbContext.Orders
        .AsNoTracking()
        .IgnoreAutoIncludes()
        .Where(o => o.Id == orderId && o.CustomerUserId == customerUserId)
        .Select(o => new CustomerOrderDetailDto(
            o.Id,
            o.OrderNumber,
            o.State,
            o.PaidAt,
            o.AcceptedAt,
            o.ShippedAt,
            o.DeliveredAt,
            o.CancelledAt,
            o.TotalAmountMinor,
            o.ProductPriceMinor,
            o.ShippingPriceMinor,
            o.VatAmountMinor,
            o.VatRateBasisPoints,
            o.Currency,
            o.ContactName,
            o.ContactPhone,
            o.Maker.DisplayName,
            o.Product != null ? o.Product.Title : null,
            o.ShippingMethod,
            o.ShippingCarrierTrackingUrl,
            o.Attachments
                .OrderBy(a => a.CreatedOn)
                .Select(a => new OrderAttachmentSummaryDto(
                    a.Id,
                    a.Filename,
                    a.ContentType,
                    a.SizeBytes,
                    attachmentUrlBase + "/customer/orders/" + o.Id + "/attachments/" + a.Id))
                .ToList(),
            o.Invoice != null && o.Invoice.PdfBlobPath != null
                ? invoiceUrlBase + "/customer/invoices/" + o.Invoice.InvoiceNumber
                : null,
            o.CreatedOn,
            o.UpdatedOn))
        .FirstOrDefaultAsync(ct);
    ```
    Constructor injection brings `IOptions<DownloadUrlsOptions>` into `OrderQueries` (the `attachmentUrlBase` + `invoiceUrlBase` locals are read from options at method entry). The EF translator handles string-concat to SQL; if any expression fails translation, fall back to post-materialization `Select` on the deserialized rows (rare; the catalog precedent in T-0046 confirms basic string-concat translates fine on PostgreSQL).
  - **`GetMakerOrderDetailsAsync`** — same shape, scoped by `o.MakerId == makerId`, projects `MakerOrderDetailDto`. Attachments URL uses `/maker/orders/...` prefix; invoice URL uses `/maker/invoices/...`. **Does NOT project `o.ContactEmail`** (per T-0081 GDPR lock).
  - Both methods use `.AsNoTracking()` and `.IgnoreAutoIncludes()` per ADR 0023.
- **`Infra.Database/Orders/OrderQueries.cs` DI** — `IOrderQueries` registration is already wired by T-0080; no new DI changes here other than the constructor adding `IOptions<DownloadUrlsOptions>` if not already present.
- **`Infra.Database/Configuration/DownloadUrlsOptions.cs`** (NEW if not introduced upstream) — bound to `DownloadUrls` configuration section with two strings: `AttachmentDownloadUrlBase`, `InvoiceDownloadUrlBase`. Registered in `AddMakablesInfrastructure.cs` via `services.Configure<DownloadUrlsOptions>(configuration.GetSection("DownloadUrls"));`. Defaults sourced from each host's `appsettings.{Environment}.json`. Implementer verifies whether T-0080/T-0081 already introduced this options class; if so, reuse.

### Web.Customer host

- **`Web.Customer/Controllers/OrdersController.cs`** — extend the existing controller (introduced in T-0080 for the customer list endpoint).
  - Add `[HttpGet("{orderId}")]` action `GetByIdAsync(string orderId, CancellationToken ct)`.
  - Route resolves to `GET /api/v1/customer/orders/{orderId}`.
  - `[Authorize]` inherited from controller-level attribute. JWT audience enforced per host per ADR 0013.
  - `[ProducesResponseType(typeof(GetCustomerOrderDetailsResponse), StatusCodes.Status200OK)]` + `[ProducesResponseType(StatusCodes.Status404NotFound)]`.
  - One-liner: `var result = await mediator.Send(new GetCustomerOrderDetails.Query(orderId), ct); return HandleResult(result);`.

### Web.Maker host

- **`Web.Maker/Controllers/OrdersController.cs`** — extend (introduced in T-0081).
  - Add `[HttpGet("{orderId}")]` action `GetByIdAsync(string orderId, CancellationToken ct)`.
  - Route resolves to `GET /api/v1/maker/orders/{orderId}`.
  - `[Authorize]` inherited.
  - `[ProducesResponseType(typeof(GetMakerOrderDetailsResponse), StatusCodes.Status200OK)]` + `[ProducesResponseType(StatusCodes.Status404NotFound)]`.
  - One-liner: `var result = await mediator.Send(new GetMakerOrderDetails.Query(orderId), ct); return HandleResult(result);`.

### Tests

#### GetCustomerOrderDetailsHandlerTests (NEW, ~6 tests)

`backend/src/Makables.Tests/AppServices/Features/Orders/GetCustomerOrderDetailsHandlerTests.cs` — NSubstitute mocks (`ICustomerSessionContext`, `IOrderQueries`).

1. **Happy_path_returns_dto_with_all_lifecycle_timestamps_preserved** — seed a `CustomerOrderDetailDto` with PaidAt, AcceptedAt, ShippedAt, DeliveredAt populated (CancelledAt null). Handler returns Success; Response.Detail mirrors all 5 timestamp fields verbatim.
2. **Customer_userId_mismatch_returns_NotFound** — `IOrderQueries.GetCustomerOrderDetailsAsync` returns null (simulating ownership mismatch — same shape as nonexistent order). Handler returns `Failure(NotFound, "order.notFound")`. No oracle for cross-customer probes.
3. **Order_not_found_returns_NotFound** — same null path. Distinguishes from AC-2 only in test naming (both flow through the same code path; we assert the same shape). Documents that "doesn't exist" and "not yours" are indistinguishable.
4. **Attachments_field_correctness_preserves_order_and_count** — seed DTO with 3 attachments in createdOn order. Assert Response.Detail.Attachments has 3 items with matching Id, Filename, ContentType, SizeBytes, DownloadUrl. Order is preserved (FIFO by createdOn).
5. **InvoicePdfUrl_nullable_when_invoice_not_yet_generated** — seed DTO with `InvoicePdfUrl = null`. Assert Response.Detail.InvoicePdfUrl is null. Then re-seed with a concrete URL; assert it's passed through verbatim.
6. **Session_userId_passed_to_query** — assert `IOrderQueries.GetCustomerOrderDetailsAsync` is called with the exact `customerUserId` returned by `sessionContext.RequireCustomerUserId()`. Confirms the IDOR shield wiring (the handler does not pass an arbitrary id from the request).

#### GetMakerOrderDetailsHandlerTests (NEW, ~6 tests)

`backend/src/Makables.Tests/AppServices/Features/Orders/GetMakerOrderDetailsHandlerTests.cs` — NSubstitute mocks (`IMakerSessionContext`, `IMakerRepository`, `IOrderQueries`).

1. **Happy_path_returns_dto_with_all_lifecycle_timestamps_preserved** — mirror customer test 1. Assert MakerPayoutAmountMinor is set (and NOT a PlatformFeeMinor field — type-checked at compile time anyway, but the test pins it).
2. **Maker_not_found_for_user_returns_MakerNotFound** — `IMakerRepository.GetByUserIdAsync` returns null. Handler returns `Failure(NotFound, "maker.notFound")`. `IOrderQueries.GetMakerOrderDetailsAsync` NOT called.
3. **Order_ownership_mismatch_returns_OrderNotFound** — maker resolves successfully but `IOrderQueries.GetMakerOrderDetailsAsync` returns null. Handler returns `Failure(NotFound, "order.notFound")`.
4. **Attachments_field_correctness** — mirror customer test 4. Assert maker attachment URLs carry the `/maker/...` prefix (the test fixture seeds the DTO with the maker-prefixed URLs; the assertion just verifies passthrough).
5. **CustomerContactEmail_field_does_not_exist_on_DTO** — compile-time guard via `typeof(MakerOrderDetailDto).GetProperties().Should().NotContain(p => p.Name == "CustomerContactEmail")`. Pins the GDPR lock. Runtime equivalent: any future PR that adds `CustomerContactEmail` to `MakerOrderDetailDto` breaks this test.
6. **Session_userId_resolves_maker_then_makerId_passed_to_query** — assert `IMakerRepository.GetByUserIdAsync` called with session userId; THEN `IOrderQueries.GetMakerOrderDetailsAsync` called with the resolved `maker.Id`. Confirms the two-step IDOR shield.

#### EF projection integration tests (NEW, ~2 tests)

`backend/src/Makables.IntegrationTests/Orders/OrderDetailsQueriesTests.cs` — Testcontainers PostgreSQL.

1. **GetCustomerOrderDetailsAsync_returns_correctly_projected_dto** — seed an Order with a Maker, Product, 2 OrderAttachments, an Invoice with PdfBlobPath set. Call `OrderQueries.GetCustomerOrderDetailsAsync(orderId, customerUserId, ct)`. Assert: returned DTO has all 5 lifecycle timestamp fields correctly populated from the row; ProductTitle == product.Title; MakerName == maker.DisplayName; Attachments has 2 items ordered by createdOn; each DownloadUrl matches `{attachmentUrlBase}/customer/orders/{orderId}/attachments/{attachmentId}`; InvoicePdfUrl matches `{invoiceUrlBase}/customer/invoices/{invoice.InvoiceNumber}`.
2. **GetMakerOrderDetailsAsync_returns_correctly_projected_dto_without_customer_email** — seed an Order owned by a maker; call `GetMakerOrderDetailsAsync(orderId, maker.Id, ct)`. Assert: returned DTO has CustomerContactName + CustomerContactPhone populated; **NO CustomerContactEmail field** (compile-time guard, but the test also serializes the DTO to JSON and asserts the string `"customerContactEmail"` does NOT appear). MakerPayoutAmountMinor is set. ZasilkovnaPickupPointId is null (not seeded). ShippingCarrierTrackingUrl is null.

#### Cross-tenant isolation tests (NEW, ~2 tests)

`backend/src/Makables.IntegrationTests/Orders/OrderDetailsIsolationTests.cs` — Testcontainers PostgreSQL.

1. **Customer_cannot_read_another_customers_order_detail** — seed Order A owned by Customer X and Order B owned by Customer Y. Call `GetCustomerOrderDetailsAsync(orderB.Id, customerX.UserId, ct)`. Assert: returns null (same shape as nonexistent). Then call with `customerY.UserId` — returns the DTO.
2. **Maker_cannot_read_another_makers_order_detail** — seed Order A owned by Maker M and Order B owned by Maker N. Call `GetMakerOrderDetailsAsync(orderB.Id, makerM.Id, ct)`. Assert: returns null. Then call with `makerN.Id` — returns the DTO. Pins the maker-side IDOR shield independently from the customer-side one.

### Docs

- **`docs/architecture/roles/order.md`** — append to the read-side query catalog: "Audience-scoped detail queries via `IOrderQueries.GetCustomerOrderDetailsAsync(orderId, customerUserId, ct)` + `.GetMakerOrderDetailsAsync(orderId, makerId, ct)`. Returns null on miss (no IDOR oracle). Customer DTO never carries maker-internal fields; maker DTO never carries customer email (T-0081 GDPR lock)." Reference T-0082 in the Read-side table row.
- **`docs/tickets/INDEX.md`** — flip T-0082 row to `**done**` after PR merge (PM does this).

### NSwag regen

Both new endpoints are contract changes → **NSwag regen REQUIRED in the same PR** for **both** customer and maker host clients. Per pre-commit hook (T-0013), `frontend/src/lib/api-client/` cannot be edited manually — `npm run generate:api` produces the diff. The new `GetCustomerOrderDetailsResponse` + `CustomerOrderDetailDto` + `OrderAttachmentSummaryDto` types appear in the customer client; `GetMakerOrderDetailsResponse` + `MakerOrderDetailDto` + `OrderAttachmentSummaryDto` appear in the maker client.

## Alternatives Considered

- **Option A — Single shared `GetOrderDetails.Query(orderId, audience)` with runtime branch.** *Rejected per A.1* — runtime audience checks are easy to miss in code review. A future contributor adding a `MakerPayoutAmountMinor` field to the shared DTO and gating its population on `audience == Maker` could trivially forget to gate; the field would leak to customer responses. Compile-time type split is the IDOR shield.
- **Option B — Shared base `OrderDetailDtoBase` with audience-specific subclasses.** *Rejected per A.1 (extension)* — inheritance complicates EF projection (`Select(...)` cannot easily materialize discriminated subtypes in a single round-trip), and NSwag client generation produces awkward TS unions. Two flat DTOs are simpler and clearer.
- **Option C — Separate `/orders/{id}/attachments` endpoint instead of inline.** *Rejected per A.2* — T-0086/T-0087 always render attachments on first paint; the extra roundtrip is wasted. Attachments are bounded at 10/order (T-0064) so the inline payload stays small.
- **Option D — Lazy attachments via FE-issued second request on demand.** *Rejected per A.2 (variant)* — same wasted roundtrip on the common path (attachments are visible by default on the detail page, not behind a "show attachments" toggle).
- **Option E — Client constructs InvoicePdfUrl from InvoiceNumber + a known route.** *Rejected per A.3* — tighter FE/BE coupling on the URL shape; route changes break both ends in lockstep, and backend changes (e.g., adding a SAS token query param) require an FE deploy. Backend ownership of the URL is the correct boundary.
- **Option F — Inline raw blob path (`Order.InvoicePdfBlobPath`) in the response.** *Rejected per A.3 (variant)* — exposes storage structure; SAS-token-rotation fragility (every token rotation invalidates the URL); bypasses the audited download endpoint's audience checks. Strictly worse on every axis.
- **Option G — Three Queries: `GetOrderDetails.AsCustomer`, `.AsMaker`, `.AsAdmin`.** *Rejected* — admin detail endpoint is downstream (out of scope per ## Out of scope). When admin lands, it ships as its own one-file feature `GetAdminOrderDetails.cs` with its own DTO superset. Three Queries today encodes a non-existent shape.
- **Option H — Skip the explicit `OrderAttachmentSummaryDto`; project attachments as anonymous tuples.** *Rejected* — NSwag generation requires named DTOs to produce stable TS class names; anonymous tuples generate `OrderDetail_AttachmentsItem` with poor ergonomics on the FE side. The explicit DTO is the contract.
- **Option I — Skip integration tests; rely on handler-only unit tests.** *Rejected per ADR 0023* — read-side EF projections fail in subtle ways (column nullability, string-concat translation, navigation property includes) that handler-only tests can't catch. Two Testcontainers tests pin the projection shape against a real PostgreSQL.
- **Option J — Add a `BusinessErrorMessage.OrderDetailLookupFailed` for the not-found path.** *Rejected per §C* — `OrderNotFound` already exists from T-0060 and has a Czech i18n translation. Adding a parallel code adds maintenance cost for zero behavioral gain.
- **Option K — Expose `CustomerContactEmail` on the maker DTO for "convenience".** *Rejected per T-0081 GDPR lock* — maker-customer communication routes through the T-0079 messages thread; direct email exposure violates data-minimization. The handover phone is enough for personal-pickup coordination.

## Out of scope

- **Frontend customer order-detail page** — T-0086 owns the page wiring, the FE-side attachment rendering, and the InvoicePdfUrl click handler.
- **Frontend maker order-detail page** — T-0087 owns the page wiring, action-button rendering (state-machine-aware), and the maker-side InvoicePdfUrl click handler.
- **Admin order-detail endpoint** — separate ticket later (admin DTO is a superset of both customer + maker, includes payout breakdown + audit trail + DeliverySource from T-0076). Not at MVP.
- **Customer + maker order LIST endpoints** — T-0080 and T-0081 (the bundle's first two tickets; T-0082 stacks on their shared `IOrderQueries` seam).
- **Pagination, filtering, search on detail** — detail is single-resource; no list semantics.
- **Inline maker contact details on customer DTO** — explicitly out per A.1 + T-0081 GDPR lock.
- **Inline customer email on maker DTO** — explicitly out per T-0081 GDPR lock.
- **Invoice download endpoint** — separate (T-0086/T-0087/admin own the actual download routes; T-0082 just constructs URLs pointing at them).
- **Attachment download endpoint** — already exists (T-0064); T-0082 just constructs URLs pointing at it.
- **SAS token generation for blob URLs** — N/A; URLs point at backend download endpoints which handle SAS internally.
- **DTO field versioning / backward-compat strategy for future field additions** — NSwag regen captures the contract on every PR; FE updates ship in the same PR. No separate versioning surface.
- **New `BusinessErrorMessage` codes** — reuses `OrderNotFound` + `MakerNotFound`.
- **EF migrations** — none (read-only ticket).

## Acceptance criteria

- **AC-1** Given an order owned by the requesting customer, when `GET /api/v1/customer/orders/{orderId}` is called with a valid customer JWT, then it returns `200 OK` with body matching `GetCustomerOrderDetailsResponse { Detail: CustomerOrderDetailDto }` carrying all 5 lifecycle timestamps (PaidAt, AcceptedAt, ShippedAt, DeliveredAt, CancelledAt — nullable per state-machine state), full price breakdown (TotalAmountMinor, ProductPriceMinor, ShippingPriceMinor, VatAmountMinor, VatRateBasisPoints), Currency, ContactName, ContactPhone, MakerName, ProductTitle (nullable), ShippingMethod, ShippingCarrierTrackingUrl (nullable), Attachments, InvoicePdfUrl (nullable), CreatedAt, UpdatedAt.
- **AC-2** Given an order NOT owned by the requesting customer (or not present), when the customer endpoint is called, then it returns `404` with error code `order.notFound`. No oracle distinguishes "doesn't exist" from "not yours" — same response shape.
- **AC-3** Given an order owned by the requesting maker, when `GET /api/v1/maker/orders/{orderId}` is called with a valid maker JWT, then it returns `200 OK` with body matching `GetMakerOrderDetailsResponse { Detail: MakerOrderDetailDto }` carrying all 5 lifecycle timestamps, full price breakdown including `MakerPayoutAmountMinor`, Currency, `CustomerContactName`, `CustomerContactPhone` (NOT `CustomerContactEmail`), MakerName context (own), ShippingMethod, `ShippingCarrierRef` (when Zasilkovna), `ShippingCarrierTrackingUrl` (nullable), `ZasilkovnaPickupPointId` (nullable), Attachments, InvoicePdfUrl, CreatedAt, UpdatedAt.
- **AC-4** Given the `MakerOrderDetailDto` type definition, when inspected via reflection, then it has NO property named `CustomerContactEmail`, `CustomerEmail`, or any case-insensitive variant. The compile-time IDOR/GDPR shield is pinned by a passing test. Adding such a field in a future PR fails the test.
- **AC-5** Given an order with attachments, when either detail endpoint is called, then `Attachments` is ordered by `CreatedOn` ascending AND each `DownloadUrl` matches `{configured-base}/{audience-prefix}/orders/{orderId}/attachments/{attachmentId}` exactly. Customer responses use `/customer/...`; maker responses use `/maker/...`.
- **AC-6** Given an order whose invoice has been generated (`Invoice.PdfBlobPath` non-null), when either detail endpoint is called, then `InvoicePdfUrl` equals `{configured-invoice-base}/{audience-prefix}/invoices/{invoice.InvoiceNumber}`. Given an order without an invoice yet, when either endpoint is called, then `InvoicePdfUrl` is `null`.
- **AC-7** Given a user without a maker row, when the maker endpoint is called, then it returns `404` with error code `maker.notFound`. The order lookup is NOT performed (asserted in handler unit test 2).
- **AC-8** Given anonymous (no JWT) or wrong-host JWT (customer JWT against maker host or vice versa), when either endpoint is called, then it returns `401` with error code `auth.required`. ADR 0013 audience enforcement.
- **AC-9** Given a customer querying an order, when `IOrderQueries.GetCustomerOrderDetailsAsync` runs, then the EF SQL has `.AsNoTracking()` + `.IgnoreAutoIncludes()` applied AND the attachments collection projects in a single round-trip (LEFT JOIN), verified by `EFCore.Diagnostics` no-N+1 assertion in the integration test.
- **AC-10** Given a customer X and an order owned by customer Y (and vice versa for makers M and N), when each customer/maker calls the detail endpoint for the other's order, then the response is `404 order.notFound` for ALL four cross-tenant combinations. Pinned by the 2 cross-tenant isolation integration tests.
- **AC-11** Build clean. Unit tests: baseline (after T-0081 in the same PR sequence) + ~12 new (6 GetCustomerOrderDetailsHandlerTests + 6 GetMakerOrderDetailsHandlerTests). Integration tests: baseline + 4 new (2 EF projection + 2 cross-tenant isolation). `node scripts/check-consistency.mjs` exit 0 (no new T1–T7 violations vs the bundle's running baseline).
- **AC-12** NSwag regen committed in the same PR for BOTH customer + maker hosts. `frontend/src/lib/api-client/` types the new `/customer/orders/{id}` endpoint with `GetCustomerOrderDetailsResponse { detail: CustomerOrderDetailDto }` AND the `/maker/orders/{id}` endpoint with `GetMakerOrderDetailsResponse { detail: MakerOrderDetailDto }`. `OrderAttachmentSummaryDto` appears in both client bundles (shared TS shape). No manual edits to the api-client folder (pre-commit hook enforces).

## Technical notes

### Why two separate Queries (not one with a runtime audience flag)

The IDOR/GDPR shield is best enforced at compile time, not at runtime. A shared `GetOrderDetails.Query(orderId, audience)` puts the audience-gating logic inside the handler (or worse, inside the EF projection's `Select` lambda), where a sloppy refactor — "let me just add a maker payout field to the shared DTO and gate it on audience" — can leak a field by missing the gate. With two Queries and two distinct DTOs, leaking a field requires editing the DTO definition itself: `MakerPayoutAmountMinor` simply does not exist on `CustomerOrderDetailDto`. Adding it requires a code change that's visible in review. The same logic applies to GDPR: `CustomerContactEmail` does not exist on `MakerOrderDetailDto`. AC-4 pins this with a reflection test so future PRs cannot accidentally regress the shield.

### Why inline Attachments (not a separate endpoint)

T-0086 and T-0087 always render attachments on the detail page's first paint — attachments are a primary surface of the page (artifact files, mockups, custom-order specs). A separate `/orders/{id}/attachments` endpoint forces the FE to issue two requests on every detail-page render, which doubles the request count for zero ergonomic gain. Attachments are bounded at 10 per order (T-0064's domain rule), so the inline payload stays small (~10 × ~150 bytes each = ~1.5 KB worst case). RESTfulness arguments for sub-resource separation apply when child collections are unbounded; not here.

### Why inline InvoicePdfUrl (not client-constructed)

The customer/maker download endpoints will land later (T-0086/T-0087/admin ticket); their final URLs are subject to backend choice (route shape, query params, SAS-token strategy). Inline construction in the projection means a future URL change (say, adding `?download=1` for forced-attachment vs inline rendering) requires only a backend change — no FE deploy. Client-constructed URLs would couple the FE to the URL shape, and any change would break both ends in lockstep. The backend is the system of record per CLAUDE.md "the backend is the system of record"; URL construction belongs there.

### Why the projection runs in a single EF query (no separate attachment fetch)

ADR 0023 mandates `.AsNoTracking()` + `.IgnoreAutoIncludes()` on read-side projections, with the expectation that the projection LINQ materializes the full DTO shape — including bounded collections — in one SQL round-trip via a `Select(...)` that includes a nested `Select` on the collection navigation. PostgreSQL handles this with a LEFT JOIN + GROUP BY array_agg (EF Core 10's correlated subquery translation). The OrderAttachment collection is bounded at 10 per order, so the JOIN cost is trivial. A separate fetch would double the round-trip count for no benefit. AC-9's no-N+1 assertion (via `EFCore.Diagnostics`) pins this.

### Why the response wrappers exist (vs returning DTOs directly)

The bundle's globally-unique Response naming lock requires every feature's top-level return type to carry the feature prefix (`GetCustomerOrderDetailsResponse`, not bare `Response` or `OrderDetailDto`). NSwag generates one TS class per wire type; without the prefix, multiple features producing `Response` records collide. The wrapper is one line (`sealed record GetCustomerOrderDetailsResponse(CustomerOrderDetailDto Detail)`) and trivial to maintain. The DTO carries the real shape; the wrapper just carries the name. Implementer alternative: skip the wrapper, name the top-level return type `CustomerOrderDetailDto` directly, and let that be the response. Both shapes satisfy the bundle lock as long as the wire-type name is unique. The wrapper is the more conventional choice in the existing codebase (matches T-0049a `GetMyProductsResponse`); the implementer picks whichever fits the existing convention.

### Why `OrderAttachmentSummaryDto` is shared between audiences

Attachment metadata (Id, Filename, ContentType, SizeBytes) carries no audience-specific data. The only per-audience variation is the `DownloadUrl` prefix, which is baked in at projection time. Sharing the DTO keeps the NSwag-generated TS type definition single and avoids `OrderAttachmentSummaryDto_Customer` + `OrderAttachmentSummaryDto_Maker` proliferation. The GDPR shield (no email) lives at the parent DTO level (`MakerOrderDetailDto`), not at the attachment level.

### Why no new error codes

`BusinessErrorMessage.OrderNotFound` (introduced in T-0060) already exists with Czech translation `order.notFound`. `BusinessErrorMessage.MakerNotFound` (T-0049a) already exists with `maker.notFound`. Both have parallel FE i18n keys per CLAUDE.md cross-stack rule. Adding parallel codes for "you don't own this order" would (a) create an IDOR oracle (distinguishable response = enumeration vector); (b) add maintenance cost (FE i18n keys, error-code documentation) for zero behavioral gain. The existing codes are correct.

## Files touched (expected)

### New
- `backend/src/Makables.Core.AppServices/Features/Orders/DTOs/OrderAttachmentSummaryDto.cs`
- `backend/src/Makables.Core.AppServices/Features/Orders/DTOs/CustomerOrderDetailDto.cs`
- `backend/src/Makables.Core.AppServices/Features/Orders/DTOs/MakerOrderDetailDto.cs`
- `backend/src/Makables.Core.AppServices/Features/Orders/GetCustomerOrderDetails.cs`
- `backend/src/Makables.Core.AppServices/Features/Orders/GetMakerOrderDetails.cs`
- `backend/src/Makables.Tests/AppServices/Features/Orders/GetCustomerOrderDetailsHandlerTests.cs`
- `backend/src/Makables.Tests/AppServices/Features/Orders/GetMakerOrderDetailsHandlerTests.cs`
- `backend/src/Makables.IntegrationTests/Orders/OrderDetailsQueriesTests.cs`
- `backend/src/Makables.IntegrationTests/Orders/OrderDetailsIsolationTests.cs`
- `backend/src/Makables.Infra.Database/Configuration/DownloadUrlsOptions.cs` (if not already introduced by T-0080/T-0081 — verify at impl time)

### Modified
- `backend/src/Makables.Core.Domain/Orders/IOrderQueries.cs` — extend with `GetCustomerOrderDetailsAsync` + `GetMakerOrderDetailsAsync` method signatures; XML doc references T-0082.
- `backend/src/Makables.Infra.Database/Orders/OrderQueries.cs` — implement both new methods; constructor gains `IOptions<DownloadUrlsOptions>` if not already present.
- `backend/src/Makables.Infra.Database/Configuration/AddMakablesInfrastructure.cs` — register `DownloadUrlsOptions` binding (if not already present).
- `backend/src/Makables.Web.Customer/Controllers/OrdersController.cs` — new `GET {orderId}` action.
- `backend/src/Makables.Web.Maker/Controllers/OrdersController.cs` — new `GET {orderId}` action.
- `frontend/src/lib/api-client/*` — NSwag-regenerated (both customer + maker hosts); committed in the same PR.
- `docs/architecture/roles/order.md` — note the audience-scoped detail queries + IDOR/GDPR shields.

## Test plan reference

Inline above (see Scope > Tests). No separate `docs/test-plans/T-0082.md`.

## Status log

- 2026-06-09 `draft` by PM. Created as part of the order-queries bundle (T-0080 customer list + T-0081 maker list + T-0082 details). Reference precedents merged or in the same bundle PR: T-0043 GetPagedMakers (paged read query closest precedent — page-based pagination, AsNoTracking, IgnoreAutoIncludes), T-0049a GetMyProducts (maker paged + detail read precedent — IDOR shield via session resolution + IDOR-safe null response), T-0046 catalog listing (customer-facing paged read precedent), T-0080 + T-0081 (the bundle's first two tickets establishing `IOrderQueries` + `IOrderRepository.ForCustomer/ForMaker` scoped seams). Slice scope: 2 one-file features + 3 new DTOs + 2 `IOrderQueries` extensions + 2 controller actions + ~16 new tests + NSwag regen for both hosts. No domain entity changes, no migrations, no new error codes.
- 2026-06-09 `draft → ready` by PM. User answered 3 blocking AskUserQuestion items per `/feature` workflow step 3: **A.1** two separate Queries with compile-time IDOR-shielded DTOs (rejected single shared with runtime audience branch); **A.2** inline Attachments in detail response (rejected separate `/orders/{id}/attachments` sub-resource); **A.3** inline InvoicePdfUrl built by the backend (rejected client-constructed URL + rejected raw blob path). 8 PM-absorbed decisions captured in `## Locked design decisions §C` (DTO field lists for customer + maker, action-buttons FE-owned, handler flow with IDOR shielding, InvoicePdfUrl + AttachmentDownloadUrl resolution from `DownloadUrlsOptions`, globally-unique Response naming, `IOrderQueries` extension shape, NSwag regen scope across both hosts, no new error codes / no migrations). 6 ADR-locked items extracted in §B (ADR 0013 per-audience JWT + scoped repositories, ADR 0014 UoW pipeline read-side N/A, ADR 0023 AsNoTracking + IgnoreAutoIncludes projection performance, one-file feature shape, BusinessResult<T> failure shape, bundle-wide globally-unique Response naming). No manual_steps. **Ready for dotnet-backend.** The implementer processes T-0080 → T-0081 → T-0082 sequentially in the same branch; all three ship in one PR.
