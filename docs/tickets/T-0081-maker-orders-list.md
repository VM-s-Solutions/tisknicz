---
id: T-0081
title: GetMakerOrders paged + filtered list query
status: ready
size: M
owner: dotnet-backend
created: 2026-06-09
updated: 2026-06-09
depends_on: [T-0060, T-0080]
blocks: [T-0087]
user_stories: [US-maker-0005]
adrs: [0013, 0014, 0023]
phase: 4
manual_steps: []
security_touching: false
layers: [domain, appservices, infra-database, web-maker]
---

# T-0081 — GetMakerOrders paged + filtered list query

## Context

T-0081 is the **maker-side counterpart** to T-0080's customer orders list. Where T-0080 ships `GET /api/v1/customer/orders` returning a customer's own orders, T-0081 ships `GET /api/v1/maker/orders` returning every order routed to the requesting maker's workshop. The handler resolves the maker entity from the session-bound user (via existing `IMakerRepository.GetByUserIdAsync`) and dispatches an EF projection query scoped to `Where(o => o.MakerId == makerId)`. The DTO is a flat `MakerOrderListItemDto` with the maker-relevant fields denormalized at projection time so the frontend dashboard list view (T-0087) renders without a follow-up roundtrip per row.

This is the **second ticket in the order-queries bundle** (T-0080 customer list + T-0081 maker list + T-0082 detail-by-id). All three ship under one PR with sequential implementation per the bundle's locked decisions: page-based pagination (`Page=1-based, PageSize`), `State + DateRange` filter set, GET with query params, flat DTO shape, shared `IOrderQueries` read-side interface (created by T-0080 — T-0081 extends it with a `GetMakerOrdersPagedAsync` method), `PagedData<T>` pagination envelope (already on master per T-0043), globally-unique response naming (`GetMakerOrdersResponse`), and ADR 0013 scoped repo conventions (`ForCustomer` / `ForMaker` only). The bundle convention is that T-0080 owns every shared artifact (the `IOrderQueries` interface, the `OrderFilter` + `OrderSort` types, the `PagedData<T>` envelope usage) and T-0081 is a pure extension that adds a separate projection method + a separate DTO type.

The maker list intentionally has a **different DTO shape from the customer list** (US-maker-0005 vs US-customer-0011): the maker view surfaces the maker's payout figure (`MakerPayoutAmountMinor`) instead of the customer-facing total breakdown, includes the customer contact NAME (NOT email) so the maker has a person to address, and reserves a nullable `UnreadMessageCount` field for forward compatibility with T-0079's message-thread feature. The customer email is **never** exposed on this surface per the bundle's GDPR data-minimization decision (A.2): contact between maker and customer is mediated by T-0079's order-scoped message thread, not by direct email exchange. Two separate DTOs (`CustomerOrderListItemDto` from T-0080 and `MakerOrderListItemDto` from T-0081) make the contract self-documenting and prevent accidental field-leak via shared shape.

The filter set is identical to T-0080 (`State` single-value + `DateRange` created-at min/max), the pagination shape is identical (`Page` 1-based + `PageSize` clamped 1-50), and the sort options are identical (default `CreatedAt DESC`, secondary `Id DESC` for stable pagination). T-0080's locked decisions about query-string GET (vs POST-search-body), about the absence of text search at MVP, and about the `PagedData<T>` envelope all carry forward verbatim — the maker list MUST behave identically to the customer list at the pagination/filter plane so the frontend can share a single paginator component (T-0087). The only divergence is the DTO field set + the IDOR boundary (maker-scoped vs customer-scoped).

A deliberate non-feature of T-0081: there is **no backend "needs action" pseudo-state**. US-maker-0005 AC-3 references a "X nových objednávek čeká" badge surfacing the count of attention-required orders. The bundle decision (A.3) keeps the backend single-state-filter-only (`?state=Paid`) and pushes the multi-state composition to T-0087's frontend — either multiple parallel queries or (post-MVP) a comma-separated multi-state filter parameter. Backend stays small + composable. Drift-free.

## Locked design decisions

Captured per `docs/process/deliberation.md`. The user locked 3 dimensions at `/feature` step 3 (mirror T-0080's pagination + filter + GET + flat-DTO consistency; customer email never exposed in maker responses; no backend pseudo-state for "needs action"). PM-absorbed decisions follow from T-0049a precedent + bundle-wide convention.

### A. User-locked at /feature step 3 (non-negotiable)

1. **Page-based pagination + State+DateRange filter set + GET with query params + flat DTO.** Mirror T-0080's locked decisions 1–4 — bundle-wide consistency. Maker list MUST behave identically to customer list at the pagination/filter level so T-0087 can share a single paginator component. **Rejected alternatives**: identical to T-0080 §A.1–4 — cursor pagination (overkill for MVP; page-based is the catalog precedent per T-0043/T-0046); free-text customer-name search (no requirement at MVP; index cost without product demand); POST search bodies (breaks the GET-cacheable read-side principle and the bundle's read-only-via-query-string convention); nested/structured response shape (flat is the frontend's preferred shape — denormalized at projection time = zero follow-up roundtrips).

2. **Customer EMAIL is NEVER exposed in maker responses.** GDPR data minimization. Maker uses T-0079's order-scoped message thread to coordinate with the customer; direct-email exchange is not the channel. The DTO carries `CustomerContactName` only. **Rejected:** include email on detail view (bigger PII surface; XSS on a maker page would leak email batches); conditional include (binary contracts are easier for the frontend than "sometimes" — and the `UnreadMessageCount` reservation is already the forward-compat handle for T-0079).

3. **No backend "needs action" pseudo-state.** Backend ships single-state filter (`?state=Paid`). T-0087 frontend issues multi-state queries OR (post-MVP) a comma-separated multi-state filter when needed. Backend stays small + composable. **Rejected:** backend magic value (`State=NEEDS_ACTION` maps to Paid+Accepted+Shipped) — pseudo-label drifts with product changes; "needs action" semantics will evolve as the maker workflow matures (e.g. should `Shipped` orders awaiting auto-deliver count?), and baking the answer into the wire contract makes every product change a contract change. Deferring is right.

### B. ADR-locked (no relitigation)

- **ADR 0013 (per-audience JWT enforcement + scoped repo split).** Maker endpoint `[Authorize]` runs under the `Web.Maker` host audience; a customer JWT cannot be replayed against the maker host. The read-side interface follows the `ForCustomer` / `ForMaker` split convention — `IOrderQueries.GetMakerOrdersPagedAsync(makerId, …)` is maker-scoped by construction (the `makerId` parameter is non-optional and resolved from session before dispatch). The write-side `IOrderRepository` remains untouched.
- **ADR 0014 (UoW pipeline).** Read query → no `UnitOfWorkPipelineBehavior` writes. `ValidationPipelineBehavior` still runs (clamps `Page >= 1`, `PageSize` ∈ [1, 50], `State` enum range, `DateRange` min ≤ max). Handler is read-only — no entity mutation, no outbox emission, no `SaveChangesAsync` (none would be called anyway since the pipeline is the only writer).
- **ADR 0023 (read-side query interface separation).** The new `GetMakerOrdersPagedAsync` method extends the existing `IOrderQueries` interface created by T-0080. `IOrderRepository` remains write-scoped per the existing CQRS split. The EF impl (`OrderQueries.cs`) extends with a second projection method specific to the maker DTO shape (Customer DTO and Maker DTO have different field sets — the projection is NOT shared).
- **One-file feature shape.** `Features/Orders/GetMakerOrders.cs` contains nested `Query`, `Validator`, `Handler`, `GetMakerOrdersResponse`. No separate files per type.
- **`BusinessResult<T>` for expected failures.** No-maker-row for the requesting user → NotFound (existing `maker.notFound` per T-0049a). Validation failures → Bad Request (existing pipeline error shape). Exceptions reserved for truly unexpected (e.g., DB connection dropped).
- **`.AsNoTracking()` on every read.** `IgnoreAutoIncludes()` on the projection-only query. Bundle-wide consistency per T-0049a + T-0080.

### C. PM-absorbed (no user input needed)

- **Sort options:** same as T-0080. Default `CreatedAt DESC`, secondary `Id DESC` for stable pagination on identical-timestamp rows. ULIDs are lexicographically time-ordered so `Id DESC` is a faithful tiebreaker. No exposed sort-by selector at MVP — the wire contract is fixed-sort.
- **Read-side interface extension:** add `GetMakerOrdersPagedAsync(string makerId, OrderFilter filter, OrderSort sort, int page, int pageSize, CancellationToken ct)` to `IOrderQueries` (interface already created by T-0080). `OrderFilter` + `OrderSort` types are the same ones T-0080 introduced — re-used verbatim. Return type: `PagedData<MakerOrderListItemDto>`.
- **EF projection:** `Select` projection (no entity materialization). `AsNoTracking()`. `IgnoreAutoIncludes()`. JOIN to maker (already implicit via `MakerId` FK; the projection pulls denormalized `MakerName` from the joined `Maker.DisplayName` field). JOIN to user (the customer's user record) for `CustomerContactName`. Customer EMAIL deliberately NOT selected — the EF expression tree must not even reference `user.Email` (so a future SELECT-* refactor can't accidentally leak it). LEFT JOIN to `OrderItem` for the first product's title (nullable — custom orders have no product, in which case `ProductTitle` is null).
- **Repository scope:** filter `Where(o => o.MakerId == makerId)`. Resolve `makerId` from the session-bound user via `IMakerRepository.GetByUserIdAsync(userId, ct)` (existing per T-0049a). If the lookup returns null → `BusinessResult.Failure<GetMakerOrdersResponse>(Error.NotFound(BusinessErrorMessage.MakerNotFound))`. IDOR shield is enforced **twice** — once by the handler refusing to dispatch without a resolved `makerId`, once by the projection's `Where` predicate.
- **`MakerOrderListItemDto`** (NEW DTO, separate from T-0080's `CustomerOrderListItemDto`):
  - `OrderId: string`
  - `OrderNumber: string`
  - `State: OrderState`
  - `TotalAmountMinor: long`
  - `MakerPayoutAmountMinor: long` (the maker's net — NOT the platform fee, which is maker-irrelevant noise)
  - `Currency: string`
  - `CreatedAt: DateTimeOffset`
  - `CustomerContactName: string` (NOT email — see A.2)
  - `ShippingMethod: ShippingMethod` (Zasilkovna / PersonalPickup)
  - `ProductTitle: string?` (first `OrderItem.ProductTitle`; nullable for custom orders with no product line)
  - `UnreadMessageCount: int?` (nullable; reserved for T-0079; populated as `null` until T-0079 ships)
- **Future-compat with T-0079 UnreadMessageCount:** include nullable `UnreadMessageCount` field in DTO today; populate as `null` in T-0081's projection. NSwag emits the field today so T-0079 doesn't trigger a contract change — only the projection logic flips. Frontend (T-0087) renders the badge as `0` when null, `N` when populated, with no contract-version sniff.
- **Globally-unique response naming:** `GetMakerOrdersResponse` (record-typed, wraps `PagedData<MakerOrderListItemDto>`). Avoids the NSwag TS class collision encountered in the shipping-bundle CI fix. Same convention as T-0080's `GetCustomerOrdersResponse`.
- **PageSize clamp:** same as T-0080. `Page >= 1` (default 1 if omitted), `PageSize` ∈ [1, 50] (default 20 if omitted). Validator enforces.
- **Maker authorization:** `[Authorize]` on the controller + maker role enforced by host audience. Resolve `MakerId` via `IMakerRepository.GetByUserIdAsync` (existing per T-0049a).
- **NSwag regen:** maker host only. Customer host is regen'd by T-0080. Admin / Public hosts untouched.
- **No new error codes, migrations, outbox events.** `MakerNotFound` (T-0049a) is reused for the no-maker-row path. Validation errors flow through existing pipeline shape.

## Scope

### Domain layer

- **`Core.Domain/Orders/IOrderQueries.cs`** — extend the interface introduced by T-0080 in the same PR. Add one method:
  ```csharp
  Task<PagedData<MakerOrderListItemDto>> GetMakerOrdersPagedAsync(
      string makerId,
      OrderFilter filter,
      OrderSort sort,
      int page,
      int pageSize,
      CancellationToken ct);
  ```
  `OrderFilter` + `OrderSort` are the same types T-0080 introduced (re-used verbatim; the projection branches on `OrderFilter.State` and `OrderFilter.CreatedAtMin/Max` exactly like the customer variant). `IOrderRepository` remains write-scoped — no edit needed on the repository interface.
- No new domain entity, no new enum, no new value object. `OrderState`, `ShippingMethod`, `OrderFilter`, `OrderSort` all exist (T-0080 or earlier).

### AppServices layer

- **`Core.AppServices/Features/Orders/DTOs/MakerOrderListItemDto.cs`** — NEW DTO. Sealed record with the 11 fields enumerated in §C above:
  ```csharp
  public sealed record MakerOrderListItemDto(
      string OrderId,
      string OrderNumber,
      OrderState State,
      long TotalAmountMinor,
      long MakerPayoutAmountMinor,
      string Currency,
      DateTimeOffset CreatedAt,
      string CustomerContactName,
      ShippingMethod ShippingMethod,
      string? ProductTitle,
      int? UnreadMessageCount);
  ```
  XML doc references T-0081 + notes the deliberate absence of `CustomerEmail` (A.2). Separate from T-0080's `CustomerOrderListItemDto` — different field set, different DTO; no shared base type (no inheritance — flat records).
- **`Core.AppServices/Features/Orders/GetMakerOrders.cs`** — NEW one-file feature.
  - `Query(OrderState? State, DateTimeOffset? CreatedAtMin, DateTimeOffset? CreatedAtMax, int Page = 1, int PageSize = 20) : IRequest<BusinessResult<GetMakerOrdersResponse>>` record.
  - `GetMakerOrdersResponse(PagedData<MakerOrderListItemDto> Orders)` record — globally-unique name to avoid the NSwag TS class collision encountered in the shipping bundle.
  - `Validator : AbstractValidator<Query>` — `Page >= 1`; `PageSize` in `[1, 50]`; `State` `IsInEnum()` when present; `CreatedAtMin <= CreatedAtMax` when both present.
  - `Handler(IUserSessionProvider sessionProvider, IMakerRepository makerRepository, IOrderQueries orderQueries)` primary-constructor DI. Steps:
    1. **Resolve maker** — `var userId = sessionProvider.RequireUserId();` then `var maker = await makerRepository.GetByUserIdAsync(userId, ct);`. Null → `BusinessResult.Failure<GetMakerOrdersResponse>(Error.NotFound(BusinessErrorMessage.MakerNotFound))`.
    2. **Build filter + sort** — `var filter = new OrderFilter(query.State, query.CreatedAtMin, query.CreatedAtMax);` and `var sort = OrderSort.Default;` (the fixed sort — `CreatedAt DESC, Id DESC`).
    3. **Dispatch projection** — `var paged = await orderQueries.GetMakerOrdersPagedAsync(maker.Id, filter, sort, query.Page, query.PageSize, ct);`.
    4. **Return** — `BusinessResult.Success(new GetMakerOrdersResponse(paged))`.
  - No `SaveChangesAsync()` (pipeline doesn't run UoW for queries anyway).

### Infrastructure / Database layer

- **`Infra.Database/Orders/OrderQueries.cs`** — extend with the maker variant projection. NEW method on the existing class (created by T-0080):
  ```csharp
  public async Task<PagedData<MakerOrderListItemDto>> GetMakerOrdersPagedAsync(
      string makerId,
      OrderFilter filter,
      OrderSort sort,
      int page,
      int pageSize,
      CancellationToken ct)
  ```
  Implementation outline:
  - Base `IQueryable<Order>` = `dbContext.Orders.AsNoTracking().IgnoreAutoIncludes().Where(o => o.MakerId == makerId)`.
  - Filter chain: `if (filter.State.HasValue) q = q.Where(o => o.State == filter.State.Value);` + `if (filter.CreatedAtMin.HasValue) q = q.Where(o => o.CreatedAt >= filter.CreatedAtMin.Value);` + `if (filter.CreatedAtMax.HasValue) q = q.Where(o => o.CreatedAt <= filter.CreatedAtMax.Value);`.
  - Order: `q = q.OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id);`.
  - Count first (for `PagedData.TotalCount`): `var totalCount = await q.CountAsync(ct);`.
  - Projection (`Select` into the DTO; JOIN via navigation property to `Maker` for `MakerName` (not in this DTO but used as the JOIN seed if needed for future fields), JOIN via navigation to `Customer.User` for `CustomerContactName`, LEFT JOIN via `OrderItems.OrderBy(i => i.SortOrder).Select(i => i.ProductTitle).FirstOrDefault()` for `ProductTitle`):
    ```csharp
    var items = await q
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .Select(o => new MakerOrderListItemDto(
            o.Id,
            o.OrderNumber,
            o.State,
            o.TotalAmountMinor,
            o.MakerPayoutAmountMinor,
            o.Currency,
            o.CreatedAt,
            o.Customer.User.DisplayName, // CustomerContactName — explicitly NOT o.Customer.User.Email
            o.ShippingMethod,
            o.OrderItems.OrderBy(i => i.SortOrder).Select(i => i.ProductTitle).FirstOrDefault(),
            (int?)null)) // UnreadMessageCount — reserved for T-0079
        .ToListAsync(ct);
    return new PagedData<MakerOrderListItemDto>(items, totalCount, page, pageSize);
    ```
  - **`o.Customer.User.Email` MUST NOT appear anywhere in this method.** A grep-friendly absence; the projection's expression tree carries no email reference. Reviewer enforces.
  - **`AsNoTracking()` + `IgnoreAutoIncludes()`** both present per ADR 0023 + T-0080 convention.

### Web.Maker host

- **`Web.Maker/Controllers/OrdersController.cs`** (create if not present — match existing controller naming):
  - Add `[HttpGet("")]` action:
    ```csharp
    [HttpGet("")]
    [ProducesResponseType<GetMakerOrdersResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] OrderState? state,
        [FromQuery] DateTimeOffset? createdAtMin,
        [FromQuery] DateTimeOffset? createdAtMax,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetMakerOrders.Query(state, createdAtMin, createdAtMax, page, pageSize), ct);
        return HandleResult(result);
    }
    ```
  - Route resolves to `GET /api/v1/maker/orders?state=Paid&createdAtMin=...&createdAtMax=...&page=1&pageSize=20`.
  - `[Authorize]` (maker scheme) — JWT audience enforced per host per ADR 0013.
  - `[ProducesResponseType]` so NSwag generates the typed `GetMakerOrdersResponse` return shape.

### Tests

#### GetMakerOrdersHandlerTests (NEW, ~8 tests)

`backend/src/Makables.Tests/AppServices/Features/Orders/GetMakerOrdersHandlerTests.cs` — NSubstitute mocks (`IUserSessionProvider`, `IMakerRepository`, `IOrderQueries`).

1. **Happy_path_returns_PagedData_with_maker_scoped_orders** — session resolves to a user with a Maker row; `IOrderQueries.GetMakerOrdersPagedAsync` returns a `PagedData` with 3 items. Assert: `GetMakerOrdersResponse.Orders.Items.Count == 3`, `TotalCount == 3`, `Page == 1`, `PageSize == 20`.
2. **No_maker_row_for_user_returns_MakerNotFound** — `IMakerRepository.GetByUserIdAsync` returns null. Assert: `BusinessResult.Failure` with `BusinessErrorMessage.MakerNotFound`. `IOrderQueries.GetMakerOrdersPagedAsync` is **not** called (Received(0)).
3. **Resolved_makerId_is_forwarded_to_OrderQueries** — capture `IOrderQueries.GetMakerOrdersPagedAsync` arguments. Assert first positional arg == `maker.Id` returned by the repository mock. Belt-and-braces against future refactor leaks (the IDOR shield is enforced twice — handler resolution + projection predicate; this test pins the handler layer).
4. **State_filter_is_forwarded_to_OrderFilter** — `Query.State = Paid`. Assert the captured `OrderFilter.State == OrderState.Paid`.
5. **DateRange_filter_is_forwarded_to_OrderFilter** — `Query.CreatedAtMin + CreatedAtMax` set. Assert the captured `OrderFilter.CreatedAtMin` + `CreatedAtMax` match.
6. **Page_and_PageSize_are_forwarded** — `Query.Page = 3, PageSize = 50`. Assert the captured `page == 3` and `pageSize == 50`.
7. **MakerPayoutAmountMinor_is_present_on_dto_and_PlatformFee_is_absent** — projection mock returns a DTO with `MakerPayoutAmountMinor = 12345`. Assert the DTO field is preserved through the handler. Asserts the maker DTO carries the maker's net (not the platform's cut). (Compile-time gate: the DTO record has no `PlatformFeeAmountMinor` field — verified at type-definition time.)
8. **UnreadMessageCount_is_null_until_T_0079** — projection mock returns a DTO with `UnreadMessageCount = null`. Assert the DTO field is preserved as null through the handler. Compile-time gate: the DTO field type is `int?` (verified at type-definition time).

#### GetMakerOrdersIntegrationTests (NEW, ~3 tests)

`backend/src/Makables.IntegrationTests/Orders/GetMakerOrdersIntegrationTests.cs` — Testcontainers postgres + `WebApplicationFactory` for the Maker host. Seeds 2 makers (`makerA`, `makerB`), 2 users (one per maker), 4 orders (2 for each maker), and exercises the wire end-to-end.

1. **GET_orders_happy_path_returns_only_requesting_makers_orders** — log in as makerA's user; GET `/api/v1/maker/orders?page=1&pageSize=20`. Assert 200 + body matches `GetMakerOrdersResponse` shape + every item in `Orders.Items` has `MakerId == makerA.Id` (verified via DB cross-check). 2 items returned. None of makerB's orders surface.
2. **GET_orders_cross_maker_isolation_makerA_cannot_see_makerB_orders** — direct positive test that pages through makerA's results and asserts that NONE of makerB's order ids appear in the response. Even with `pageSize=50` (which would fit all 4 seeded orders if no scoping). The IDOR shield is enforced at the projection layer — this pins it.
3. **GET_orders_UnreadMessageCount_is_null_pin** — log in as makerA's user; GET `/api/v1/maker/orders?page=1&pageSize=20`. Assert every item in `Orders.Items` has `UnreadMessageCount == null`. This is the forward-compat pin: T-0079 will flip this to a populated int when the message-thread feature ships. Until then the field is null. NSwag emits the field today so T-0079 doesn't trigger a contract change — only the projection logic flips.

### Docs

- **`docs/architecture/roles/order.md`** — note the new read query: "Maker-scoped paged list query via `IOrderQueries.GetMakerOrdersPagedAsync(makerId, …)` returns `PagedData<MakerOrderListItemDto>` (flat DTO denormalized at projection time; customer email deliberately not selected). T-0081 ships this; T-0079 will populate `UnreadMessageCount`."
- **`docs/tickets/INDEX.md`** — flip T-0081 row to `**done**` after PR merge (PM does this).

### NSwag regen

The new `GET /api/v1/maker/orders` endpoint is a contract change → **NSwag regen REQUIRED in the same PR** (maker host client). Per pre-commit hook (T-0013), `frontend/src/lib/api-client/` cannot be edited manually — `npm run generate:api` produces the diff. The new `GetMakerOrdersResponse`, `MakerOrderListItemDto`, and the query parameters appear in the generated maker client. Customer / admin / public clients untouched.

## Alternatives Considered

- **Option A — Backend pseudo-state `?state=NEEDS_ACTION`** mapping to (Paid OR Accepted OR Shipped). *Rejected per A.3* — couples wire contract to a UX label that will evolve; every product change to "what counts as needs-action" would be a breaking contract change. Frontend composition (multiple parallel queries or comma-separated states) is the right boundary.
- **Option B — Shared DTO between customer + maker views.** *Rejected per A.2 + C.6* — different field sets (customer needs total breakdown; maker needs net payout), different PII surfaces (customer view shows customer's own email; maker view must NOT). Sharing the DTO would force conditional field-emission or shape-drift via inheritance — both worse than two flat records.
- **Option C — Include `CustomerEmail` on the maker DTO.** *Rejected per A.2* — bigger PII surface; any XSS on a maker page would leak emails in batches. T-0079's message thread is the right channel for maker-customer contact at the application layer.
- **Option D — Conditional `CustomerEmail` field (present when maker has marked the order as Accepted; absent otherwise).** *Rejected per A.2* — binary wire contracts are easier for the frontend than "sometimes present" fields, and the gain (faster maker→customer first-contact?) is hypothetical at MVP.
- **Option E — Free-text customer-name search.** *Rejected per A.1* — no requirement at MVP; index cost without product demand. Add when a real workflow needs it.
- **Option F — POST `/maker/orders/search` with a JSON request body** (Cleansia precedent). *Rejected per A.1 + bundle consistency* — breaks the read-via-query-string convention shared with T-0080 + T-0049a; loses HTTP caching headroom; complicates the frontend paginator component.
- **Option G — Cursor pagination (opaque cursor token).** *Rejected per A.1* — overkill for MVP; the page-based shape is the catalog precedent (T-0043/T-0046) and the bundle's shared paginator depends on it.
- **Option H — Defer the `UnreadMessageCount` field until T-0079 ships.** *Rejected per C.7* — adding the field then would trigger a contract change + NSwag regen + a frontend release. Reserving the field today (nullable, populated as null) makes T-0079 a pure projection-logic edit with zero contract churn.
- **Option I — Include `PlatformFeeAmountMinor` on the maker DTO.** *Rejected per C.6* — maker-irrelevant noise (the maker cares about their net payout, not the platform's cut). The fee figure lives on `Invoice` (the platform→maker fee invoice surfaces it per T-0049a's downstream tickets); the order list is the wrong surface for it.
- **Option J — Single `IOrderQueries.GetOrdersPagedAsync` method with a discriminator parameter** (e.g. `OrderQueryScope.Customer | OrderQueryScope.Maker`). *Rejected per ADR 0023 + C.2* — two separate methods are clearer at the call site, allow the DTOs to remain typed-different, and prevent accidental cross-scope leak via a flipped enum. One projection, one DTO, one method.

## Out of scope

- **Customer-scope list query** — T-0080 ships `GET /api/v1/customer/orders` + `IOrderQueries.GetCustomerOrdersPagedAsync` + `CustomerOrderListItemDto`. T-0081 only extends with the maker variant.
- **Order detail-by-id queries (customer + maker)** — T-0082 ships two separate detail queries (`GetCustomerOrderDetails` + `GetMakerOrderDetails`), each with its own DTO + its own IDOR shield + inline attachments list + inline `InvoicePdfUrl`.
- **Free-text customer-name search** — explicitly rejected per A.1.
- **Backend "needs action" pseudo-state** — explicitly rejected per A.3.
- **Multi-state filter** (e.g. `?state=Paid,Accepted,Shipped`) — post-MVP. Single-state at MVP; T-0087 frontend issues multiple parallel queries when it needs a composite.
- **Sort selector** — fixed sort at MVP (`CreatedAt DESC, Id DESC`). Add when a real workflow needs custom sorting.
- **`UnreadMessageCount` population logic** — T-0079 owns the message-thread feature and will populate the field in this projection. T-0081 reserves the field and emits null.
- **Maker `[ProducesResponseType]` rollout for the rest of the Maker host** — out of bundle scope.
- **Frontend dashboard list page** — T-0087 owns the consumer surface.
- **Admin / Public host order-list endpoints** — not part of this bundle.
- **Outbox-event surfaces on the order row** (US-maker-0017) — separate ticket.

## Acceptance criteria

- **AC-1** Given a logged-in maker with a Maker row, when `GET /api/v1/maker/orders?page=1&pageSize=20` is called with a valid maker JWT, then it returns `200 OK` with body `GetMakerOrdersResponse { Orders: PagedData<MakerOrderListItemDto> }` where every item has `MakerId` (cross-checked via DB) equal to the requesting maker's id.
- **AC-2** Given a logged-in user with NO Maker row, when the endpoint is called, then it returns `404` with error code `maker.notFound`. `IOrderQueries.GetMakerOrdersPagedAsync` is NOT invoked (asserted via mock at the handler-test layer).
- **AC-3** Given two makers (makerA + makerB) with 2 orders each, when makerA's user requests `GET /api/v1/maker/orders?pageSize=50`, then the response contains exactly the 2 orders owned by makerA. None of makerB's orders surface (IDOR shield enforced at the projection's `Where(o => o.MakerId == makerId)` predicate).
- **AC-4** Given the filter `?state=Paid`, when the endpoint is called, then only orders with `State == Paid` appear in the response. The captured `OrderFilter.State` at the handler→queries boundary is `OrderState.Paid` (asserted via mock).
- **AC-5** Given the filter `?createdAtMin=2026-01-01T00:00:00Z&createdAtMax=2026-06-30T23:59:59Z`, when the endpoint is called, then only orders with `CreatedAt` in `[Min, Max]` appear. The captured `OrderFilter.CreatedAtMin` / `CreatedAtMax` match.
- **AC-6** Given `?page=2&pageSize=10`, when the endpoint is called against 25 seeded orders, then the response has `Page == 2`, `PageSize == 10`, `TotalCount == 25`, `Items.Count == 10`, and the items are the 11th-20th items under the default sort (`CreatedAt DESC, Id DESC`).
- **AC-7** Given `?page=0` or `?pageSize=0` or `?pageSize=51`, when the endpoint is called, then it returns `400` with validation error (`Page >= 1`, `PageSize` in `[1, 50]` per the validator). Defaults apply when params are omitted (`page=1, pageSize=20`).
- **AC-8** Given the `MakerOrderListItemDto` type definition, when read, then it carries `MakerPayoutAmountMinor` (long), `CustomerContactName` (string), `UnreadMessageCount` (int?), `ProductTitle` (string?) and does NOT carry any field named `CustomerEmail` or `PlatformFeeAmountMinor`. Compile-time gate.
- **AC-9** Given the `OrderQueries.GetMakerOrdersPagedAsync` projection source, when grepped, then it contains zero references to `Customer.User.Email` (or any equivalent path). The EF expression tree never even names the column, so a future SELECT-* refactor cannot accidentally leak it.
- **AC-10** Given any response from the endpoint, when read, then every `MakerOrderListItemDto.UnreadMessageCount` field is `null`. T-0081 reserves the field; T-0079 will flip the projection logic without touching the wire contract.
- **AC-11** Build clean. Unit tests: baseline (after T-0080 in the same PR sequence) + ~8 new (`GetMakerOrdersHandlerTests`). Integration tests: baseline + 3 new (`GetMakerOrdersIntegrationTests` — happy path + cross-maker isolation + `UnreadMessageCount` null pin). `node scripts/check-consistency.mjs` exit 0 (no new T1–T7 violations vs the bundle's running baseline). NSwag regen committed in the same PR; `frontend/src/lib/api-client/` types the new `/maker/orders` endpoint with `GetMakerOrdersResponse { orders: PagedData<MakerOrderListItemDto> }`. No manual edits to the api-client folder (pre-commit hook enforces).
- **AC-12** Given the Maker host's NSwag-generated TS client, when inspected, then the `MakerOrderListItemDto` TypeScript interface has `unreadMessageCount: number | null | undefined` (NSwag default for `int?` is nullable) and does NOT have a `customerEmail` or `platformFeeAmountMinor` member. Mirrors AC-8 at the wire layer.

## Technical notes

### Why the maker DTO is separate from the customer DTO (not shared)

Customer and maker views surface different concerns: the customer needs the full money breakdown (subtotal, shipping, total) to validate "did I pay the right amount", while the maker needs the net payout figure ("how much will land in my account when this batch processes"). Sharing a DTO would force either (a) every field to be present on every view (bloat + accidental PII surface) or (b) conditional emission via inheritance / discriminator (complicated wire contract). Two flat records — one per view — is the simplest shape and makes the contract self-documenting. The cost is duplicating ~5 fields across two DTOs; the win is two surfaces that can evolve independently without coordination.

### Why customer EMAIL is deliberately absent from the projection's expression tree

PII surfaces should be opt-in, not opt-out. If the EF projection includes `o.Customer.User.Email` and a future refactor accidentally widens the projection (e.g. someone changes the `Select` to `Select(o => new { o, … })` and the DTO mapping is moved to a post-query step), the email would leak. By keeping the email reference out of the projection entirely — neither selected nor named in the expression tree — the accident is impossible. The grep-friendly absence is also a review handhold: any future PR that adds `Email` to this projection becomes a discussion. T-0079's message-thread feature is the right channel for maker-customer contact; direct-email exchange would also defeat the bundle's data-minimization stance.

### Why `UnreadMessageCount` is reserved today (not deferred to T-0079)

Adding a field to a generated client triggers a contract change (NSwag diff, frontend recompile, downstream consumer ripple). The MVP launch sequence is T-0081 (today) → T-0087 (frontend list view) → T-0079 (messages, weeks later). If T-0081 ships without the field and T-0079 adds it, T-0087 must be redeployed when T-0079 lands. By reserving the nullable field today and populating as null until T-0079 ships, the wire contract is stable through the launch window: T-0079 flips only the projection's `Select` expression — the DTO shape, the generated client, and the frontend rendering logic are untouched. The cost (one nullable int on every order row's wire shape, ~4 bytes worst-case JSON) is negligible.

### Why the IDOR shield is enforced TWICE (handler + projection)

Defence in depth. The handler resolves `makerId` from the session and forwards it; the projection then filters on `o.MakerId == makerId`. Either layer alone would suffice for a correctly-routed call, but the two-layer setup means a future admin tool that legitimately needs to bypass the handler (e.g. read another maker's orders for support escalation) cannot accidentally bypass the projection — the `Where` clause is non-negotiable from the projection's perspective. Same pattern T-0049a uses for the maker product queries. The handler-layer test (`Resolved_makerId_is_forwarded_to_OrderQueries`) pins layer 1; the integration test `GET_orders_cross_maker_isolation` pins layer 2.

### Why `AsNoTracking` + `IgnoreAutoIncludes` are both present

`AsNoTracking()` disables EF's change tracker for the result set — cheaper memory, faster materialization, no risk of stale entity references leaking into a later write transaction. `IgnoreAutoIncludes()` defeats the global query-filter / auto-include configurations that might be added later for write-side conveniences (e.g. auto-loading `OrderItems` on every `Order` read). For a projection-only query that selects exactly the columns it needs, both are mandatory: `IgnoreAutoIncludes` prevents the projection from pulling unwanted navigation properties (which would bloat the SQL + hit indexes the projection doesn't need), and `AsNoTracking` prevents EF from registering proxies for the bits the projection does pull. T-0049a + T-0080 already use this pattern — bundle-wide consistency.

### Why the fixed sort isn't exposed as a query parameter

A sort-by selector is a wire-contract obligation: every value in the enum becomes a guaranteed-supported sort key forever. At MVP the maker dashboard has one workflow ("show me my latest orders") and one sort makes sense. Exposing the selector today would be speculative complexity; adding it later when a real workflow needs a second sort is straightforward (extend the `OrderSort` type with a discriminator + the validator's enum range, regen the client). The bundle convention is fixed-sort, which keeps the handler + projection paths simple and the query-parameter surface minimal.

### Why `GetMakerOrdersResponse` is the response name (not `Response`)

The shipping bundle (T-0070–T-0075) hit an NSwag client-gen collision: multiple `Response` classes from different features generate the same TS class name and the build breaks. T-0081 sidesteps the collision proactively by naming the response record `GetMakerOrdersResponse` instead of `Response`. The nested-type convention `GetMakerOrders.Response` is preserved in C# (no source-code reader confusion), but the wire-type name carries the feature prefix. Same convention T-0080's `GetCustomerOrdersResponse` follows + T-0076's `MarkOrderDeliveredResponse` established.

## Files touched (expected)

### New
- `backend/src/Makables.Core.AppServices/Features/Orders/DTOs/MakerOrderListItemDto.cs`
- `backend/src/Makables.Core.AppServices/Features/Orders/GetMakerOrders.cs`
- `backend/src/Makables.Tests/AppServices/Features/Orders/GetMakerOrdersHandlerTests.cs`
- `backend/src/Makables.IntegrationTests/Orders/GetMakerOrdersIntegrationTests.cs`

### Modified
- `backend/src/Makables.Core.Domain/Orders/IOrderQueries.cs` — extend with `GetMakerOrdersPagedAsync` method signature (interface created by T-0080 in the same PR).
- `backend/src/Makables.Infra.Database/Orders/OrderQueries.cs` — add the maker projection implementation (class created by T-0080 in the same PR).
- `backend/src/Makables.Web.Maker/Controllers/OrdersController.cs` — new `[HttpGet("")]` `List` action (create file if not present, otherwise extend).
- `frontend/src/lib/api-client/*` — NSwag-regenerated (maker host); committed in the same PR.
- `docs/architecture/roles/order.md` — note the new maker-scoped paged read query + the deliberate email absence + the reserved `UnreadMessageCount` field.

## Test plan reference

Inline above (see Scope > Tests). No separate `docs/test-plans/T-0081.md`.

## Status log

- 2026-06-09 `draft` by PM. Created as part of the order-queries bundle (T-0080 customer list + T-0081 maker list + T-0082 details). Reference precedents merged or in the same bundle PR: T-0043 GetPagedMakers (paged-read precedent), T-0049a GetMyProducts (maker paged-read precedent + IDOR-twice-enforcement pattern), T-0046 catalog listing (customer-facing paged-read precedent), T-0080 GetCustomerOrders (immediate sibling — creates the shared `IOrderQueries` interface + `OrderFilter` + `OrderSort` + `OrderQueries.cs` impl class that T-0081 extends). Existing `PagedData<T>` sealed record (already on master per T-0043) is the bundle's pagination envelope. Slice scope: `GetMakerOrders` one-file feature + `MakerOrderListItemDto` + `IOrderQueries.GetMakerOrdersPagedAsync` extension + `OrderQueries.cs` maker projection + Maker host `GET /api/v1/maker/orders` endpoint + NSwag regen (maker host only).
- 2026-06-09 `draft → ready` by PM. User answered 3 blocking AskUserQuestion items per `/feature` workflow step 3: **A.1** mirror T-0080 at pagination/filter/GET/flat-DTO levels for bundle-wide consistency (rejected cursor / free-text-search / POST-body / nested-shape alternatives); **A.2** customer EMAIL never exposed in maker responses, contact mediated by T-0079 message thread (rejected include-on-detail + conditional-include); **A.3** no backend "needs action" pseudo-state, frontend composes via multi-state queries (rejected backend magic value). PM-absorbed decisions captured in `## Locked design decisions §C` (sort options, read-side interface extension shape, EF projection details with deliberate email absence, repository scope + makerId resolution, `MakerOrderListItemDto` field set, `UnreadMessageCount` forward-compat reservation, globally-unique `GetMakerOrdersResponse` name, PageSize clamp, maker authorization, NSwag regen scope, no new error codes / migrations / outbox events). ADR-locked items extracted in §B (ADR 0013 per-audience JWT + scoped repo split, ADR 0014 UoW pipeline non-relevance for reads, ADR 0023 read-side interface separation, one-file feature shape, `BusinessResult<T>`, AsNoTracking + IgnoreAutoIncludes). No manual_steps. **Ready for dotnet-backend.** The implementer processes T-0080 → T-0081 → T-0082 sequentially in the same branch; all three ship in one PR.
