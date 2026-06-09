---
id: T-0080
title: GetCustomerOrders paged + filtered list query
status: ready
size: M
owner: dotnet-backend
created: 2026-06-09
updated: 2026-06-09
depends_on: [T-0060]
blocks: [T-0086]
user_stories: [US-customer-0016]
adrs: [0013, 0014, 0023]
phase: 4
manual_steps: []
security_touching: false
layers: [domain, appservices, infra-database, web-customer]
---

# T-0080 — GetCustomerOrders paged + filtered list query

## Context

T-0080 is the **first ticket in the order-queries bundle** (T-0080 customer list + T-0081 maker list + T-0082 customer/maker detail). All three ship under one PR with sequential implementation. T-0080 introduces the read-side seam every downstream order-query ticket builds on: a new `IOrderQueries` interface (semantically split from the write-scoped `IOrderRepository` per ADR 0013, mirroring the T-0049a CRUD/Queries split for products), a new `OrderSort` enum, a shared `OrderFilter` record, the `CustomerOrderListItemDto` shape, and the first paged-list endpoint at `GET /api/v1/customer/orders`. T-0081 will extend `IOrderQueries` with `GetMakerOrdersPagedAsync`; T-0082 will add the two scoped detail queries (`GetByIdForCustomerAsync` / `GetByIdForMakerAsync`).

This ticket directly satisfies **US-customer-0016 — View order list (customer dashboard)** AC-1 (paginated, 20 per page, sorted `CreatedAt DESC`) and AC-2 (filter by state + date range). AC-3 (empty-state CTA) is a frontend concern delivered downstream by T-0087; the backend simply returns an empty `PagedData<CustomerOrderListItemDto>` with `TotalCount = 0`. The endpoint blocks T-0086 (customer invoice-download endpoint surfacing), which consumes the same list query to render the dashboard's invoice-download CTAs.

The locked precedents — `PagedData<T>` (T-0043), page-based pagination (T-0043 + T-0046), AsNoTracking projection-only EF reads (T-0049a), one-file feature shape (every Phase 4 ticket), Validator-clamped page-size, globally-unique Response naming (post-PR #38 NSwag fix) — are all already on master. T-0080's job is to apply them to the customer-order surface with one ADR-0013-compliant scoping predicate (`CustomerUserId == sessionCustomerId`) embedded directly in the EF query (no `IgnoreQueryFilters`, no `ForCustomer` builder needed at the queries layer; the predicate IS the scope). The filter predicate doubles as the IDOR shield — a customer literally cannot see another customer's row because the SQL never selects it. Soft-deleted orders are excluded by the global `Auditable` query filter.

The DTO carries denormalized `MakerName` + first `ProductTitle` (nullable for custom orders) joined inline per locked decision A.4. This keeps the list-row payload flat and renderable in a single Server Component pass without N+1 follow-ups. Custom orders (per T-0079 messages thread) have no product link; `ProductTitle` is null and the frontend renders a "Vlastní zakázka" label client-side. The bundle convention reserves nested DTO shapes (Maker {Id, Name}, Products[]) for the T-0082 detail responses where the heavier payload is justified.

No new `BusinessErrorMessage` codes, no migrations, no outbox events, no i18n keys ship in this ticket. The Validator's only failure modes are `Page < 1` and `PageSize ∉ [1, 50]`, both of which use the existing FluentValidation `GreaterThanOrEqualTo` / `InclusiveBetween` rules and surface as 400 with the standard validation envelope.

## Locked design decisions

Captured per `docs/process/deliberation.md`. The user locked 4 dimensions at `/feature` step 3 (page-based pagination vs offset/limit; State + DateRange filter set vs broader; GET with query params vs POST body; flat denormalized DTO vs nested). 9 PM-absorbed decisions follow from T-0043/T-0046/T-0049a precedents.

### A. User-locked at /feature step 3 (non-negotiable)

1. **Page-based pagination across the bundle.** `Page` (1-based) + `PageSize`. Matches T-0043 GetPagedMakers + T-0046 catalog precedent. Both list endpoints (T-0080 customer + T-0081 maker) use the same shape. **Rejected:** offset/limit (T-0049a precedent; less consistent with customer-facing pagination UI).

2. **Filter set at MVP = State + DateRange only.** US-customer-0016 AC-2 names "state, date range". Customers paste order numbers; text search adds marginal UX value at the cost of indexed-search infrastructure. **Rejected:** State + DateRange + OrderNumber text search (deferred — re-evaluate post-MVP if usage warrants); State-only (loses useful date-bucket filtering for free).

3. **GET with query-params request shape.** `GET /api/v1/customer/orders?page=1&pageSize=20&state=Paid&dateFrom=2026-01-01&dateTo=2026-06-01`. REST-pure; URL-shareable; browser-history-navigable; NSwag generates idiomatic typed query params. **Rejected:** POST with JSON body (loses URL-shareability + back/forward navigation; only justified for filters that outgrow query-string serialization, which MVP doesn't).

4. **Flat list-item DTO with denormalized MakerName + first ProductTitle.** Single join per query. T-0049a precedent. Each row carries: OrderId, OrderNumber, State, TotalAmountMinor, Currency, CreatedAt, MakerName (via join to maker.user → user.name OR maker.company_name; implementer picks the canonical maker label), ProductTitle (nullable for custom orders). **Rejected:** nested Maker {Id, Name} + Products[] (bigger payload; harder to render in compact list rows; frontend flattens anyway); minimal (only OrderId/OrderNumber/State/Total — forces every detail interaction into follow-up query).

### B. ADR-locked (no relitigation)

- **ADR 0013 (per-audience JWT enforcement + scoped repositories).** The customer endpoint runs under the `Web.Customer` host audience; a customer JWT cannot be replayed against the maker or admin hosts. The read-side scoping uses the same `ForCustomer` semantic as the write-side `IOrderRepository` (T-0060), but is enforced via the WHERE predicate `o.CustomerUserId == customerId` baked into the EF query. No `IgnoreQueryFilters` (soft-deleted orders MUST stay hidden). IDOR shield is the predicate itself — a customer literally cannot select another customer's row.
- **ADR 0014 (UoW pipeline).** Read queries are commands-or-queries; per the existing pipeline split, `ValidationPipelineBehavior` runs on every request, `UnitOfWorkPipelineBehavior` runs on commands only. The Handler is read-only — no `SaveChangesAsync()`, no state mutations.
- **ADR 0023 (read-side queries split from write-side repositories).** New `IOrderQueries` interface lives at `Core.Domain/Orders/IOrderQueries.cs` alongside `IOrderRepository`. The repository stays write-scoped (CRUD + state-machine reads). Queries are projection-only, read-side, AsNoTracking. Mirrors the T-0049a `IMakerProductQueries` / `IProductRepository` split.
- **One-file feature shape.** `Features/Orders/GetCustomerOrders.cs` contains nested `Query`, `Validator`, `Handler`, `GetCustomerOrdersResponse`. No separate files per type.
- **`BusinessResult<T>` for expected failures.** Validator failures (Page < 1, PageSize out of range) surface as 400 via the existing validation envelope; no `BusinessErrorMessage` code introduced because no domain failure mode applies (cross-tenant queries simply return empty results, not 404).

### C. PM-absorbed (no user input needed)

- **Sort options:** `CreatedAtDesc` (default) + `CreatedAtAsc` + `TotalAmountDesc` + `TotalAmountAsc` + `StateAsc`. `OrderSort` enum at `Core.Domain/Orders/Sorting/OrderSort.cs`.
- **Read-side interface location:** new `IOrderQueries` interface at `Core.Domain/Orders/IOrderQueries.cs`. `IOrderRepository` remains write-scoped per ADR 0013. Mirrors T-0049a precedent (CRUD vs Queries split).
- **EF projection:** Select projection to DTO directly in EF (no Order entity materialization). `AsNoTracking()`. `IgnoreAutoIncludes()` on the projection-only query (defensive — the projection lists every column explicitly, but the global include defaults stay disabled). Single LEFT JOIN to maker (and maker.user where MakerName resolves) for MakerName; single LEFT JOIN to product (when ProductId not null) for ProductTitle.
- **Repository scope:** `OrderQueries.GetCustomerOrdersPagedAsync(customerId, OrderFilter, OrderSort, page, pageSize, ct)` constrains via `.Where(o => o.CustomerUserId == customerId)`. `ForCustomer` scoping is implicit (no `IgnoreQueryFilters`; soft-deleted orders excluded by the global `Auditable` filter).
- **PagedData<T>:** existing sealed record on master is the envelope. Builds via separate `CountAsync` + `Skip/Take` projection (two queries by design — accurate total count for pagination UI).
- **No new BusinessErrorMessage codes** (existing Order surface unchanged; no failure modes beyond Validator clamps on page/pageSize).
- **Globally-unique Response naming:** `GetCustomerOrdersResponse` (not `Response`). NSwag CI fix from PR #38 stays honoured.
- **DI registration:** `services.AddScoped<IOrderQueries, OrderQueries>()` in `AddMakablesInfrastructure.cs`.
- **PageSize clamp:** Validator enforces `Page >= 1` and `PageSize` in `[1, 50]` (default 20). Larger pages create heavier projections; cap protects backend.
- **Customer authorization:** `[Authorize]` + customer role; resolve `CustomerUserId` from `IUserSessionProvider`. No additional IDOR shield needed (filter predicate IS the scope).
- **NSwag regen:** customer host only.

## Scope

### Domain layer

- **`Core.Domain/Orders/IOrderQueries.cs`** — NEW interface:
  ```csharp
  public interface IOrderQueries
  {
      Task<PagedData<CustomerOrderListItemDto>> GetCustomerOrdersPagedAsync(
          string customerId,
          OrderFilter filter,
          OrderSort sort,
          int page,
          int pageSize,
          CancellationToken ct);
  }
  ```
  T-0081 will extend with `GetMakerOrdersPagedAsync`; T-0082 will extend with `GetByIdForCustomerAsync` + `GetByIdForMakerAsync`. T-0080 only ships the customer-list method.
- **`Core.Domain/Orders/Sorting/OrderSort.cs`** — NEW enum:
  ```csharp
  public enum OrderSort
  {
      CreatedAtDesc = 0, // default
      CreatedAtAsc = 1,
      TotalAmountDesc = 2,
      TotalAmountAsc = 3,
      StateAsc = 4,
  }
  ```
  Explicit numeric values are stable wire codes; new sort modes (e.g., `MakerNameAsc`) append.

### AppServices layer

- **`Core.AppServices/Features/Orders/Filters/OrderFilter.cs`** — NEW sealed record:
  ```csharp
  public sealed record OrderFilter(
      OrderState? State,
      DateTimeOffset? DateRangeStart,
      DateTimeOffset? DateRangeEnd);
  ```
  All three fields nullable — caller can pass any subset. The EF projection applies each filter conditionally (`if (filter.State.HasValue) query = query.Where(o => o.State == filter.State.Value);` etc). `DateRangeStart` compares `>=` against `CreatedAt`; `DateRangeEnd` compares `<=`. The same record will be reused by T-0081's maker list (no shape divergence at MVP).
- **`Core.AppServices/Features/Orders/DTOs/CustomerOrderListItemDto.cs`** — NEW sealed record:
  ```csharp
  public sealed record CustomerOrderListItemDto(
      string OrderId,
      string OrderNumber,
      OrderState State,
      long TotalAmountMinor,
      string Currency,
      DateTimeOffset CreatedAt,
      string MakerName,
      string? ProductTitle);
  ```
  Flat shape per locked decision A.4. `ProductTitle` nullable for custom orders (no product link).
- **`Core.AppServices/Features/Orders/GetCustomerOrders.cs`** — NEW one-file feature:
  - `Query(int Page, int PageSize, OrderState? State, DateTimeOffset? DateFrom, DateTimeOffset? DateTo, OrderSort Sort) : IRequest<BusinessResult<GetCustomerOrdersResponse>>` record. `Sort` defaults to `OrderSort.CreatedAtDesc` at the controller binding layer.
  - `GetCustomerOrdersResponse(PagedData<CustomerOrderListItemDto> Orders)` record — **globally-unique name** per PR #38 NSwag fix.
  - `Validator : AbstractValidator<Query>`:
    - `Page` — `GreaterThanOrEqualTo(1)`.
    - `PageSize` — `InclusiveBetween(1, 50)`.
    - `Sort` — `IsInEnum()`.
    - `State` — `IsInEnum()` when set (`When(q => q.State.HasValue, ...)`).
    - `DateFrom`/`DateTo` — when both set, `DateFrom <= DateTo` (`When(q => q.DateFrom.HasValue && q.DateTo.HasValue, ...)`).
  - `Handler(IUserSessionProvider session, IOrderQueries orderQueries) : IRequestHandler<Query, BusinessResult<GetCustomerOrdersResponse>>` primary-constructor DI.
  - Steps (NO `SaveChangesAsync()` — read-only):
    1. **Resolve customer** — `var customerId = session.RequireUserId();` (the customer-host session provider already enforces the customer role per ADR 0013).
    2. **Build filter** — `var filter = new OrderFilter(query.State, query.DateFrom, query.DateTo);`.
    3. **Project** — `var page = await orderQueries.GetCustomerOrdersPagedAsync(customerId, filter, query.Sort, query.Page, query.PageSize, ct);`.
    4. **Return** — `BusinessResult.Success(new GetCustomerOrdersResponse(page));`.

### Infrastructure / Database layer

- **`Infra.Database/Orders/OrderQueries.cs`** — NEW file implementing `IOrderQueries.GetCustomerOrdersPagedAsync`:
  - Primary-constructor DI: `OrderQueries(MakablesDbContext db) : IOrderQueries`.
  - Method body:
    1. Build base query — `var baseQuery = db.Orders.AsNoTracking().IgnoreAutoIncludes().Where(o => o.CustomerUserId == customerId);` (soft-deleted excluded by global Auditable filter; no `IgnoreQueryFilters`).
    2. Apply `filter.State` / `filter.DateRangeStart` / `filter.DateRangeEnd` conditionally.
    3. Apply sort — `switch (sort) { case CreatedAtDesc => baseQuery.OrderByDescending(o => o.CreatedAt); ... }`. Tie-breaker on `OrderId` for stable pagination.
    4. `var totalCount = await baseQuery.CountAsync(ct);` — first round-trip.
    5. Projection — `var items = await baseQuery.Skip((page - 1) * pageSize).Take(pageSize).Select(o => new CustomerOrderListItemDto(o.Id, o.OrderNumber, o.State, o.TotalAmountMinor, o.Currency, o.CreatedAt, o.Maker.User.FullName, o.ProductId == null ? null : o.Product.Title)).ToListAsync(ct);` — second round-trip.
       - **MakerName resolution:** implementer picks the canonical maker label at code time per locked A.4. Default: `maker.User.FullName` if maker has no `CompanyName`, else `maker.CompanyName`. Match whichever path the public catalog (T-0044) uses for consistency.
       - **ProductTitle nullability:** the projection conditional handles custom orders (no ProductId). If the Order entity has `ProductId` as a string?, the EF projection translates the conditional to SQL LEFT JOIN + CASE.
    6. Return `new PagedData<CustomerOrderListItemDto>(items, totalCount, page, pageSize);` (existing PagedData<T> sealed record signature on master).
- **`Config/Extensions/AddMakablesInfrastructure.cs`** — register `services.AddScoped<IOrderQueries, OrderQueries>();`. Match the existing T-0049a registration block.

### Web.Customer host

- **`Web.Customer/Controllers/OrdersController.cs`** — extend the existing controller (created by T-0076 for the `/deliver` endpoint) with:
  - `[HttpGet("")]` action `ListAsync([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] OrderState? state = null, [FromQuery] DateTimeOffset? dateFrom = null, [FromQuery] DateTimeOffset? dateTo = null, [FromQuery] OrderSort sort = OrderSort.CreatedAtDesc, CancellationToken ct = default)`.
  - Route resolves to `GET /api/v1/customer/orders`.
  - `[Authorize]` (customer scheme) — JWT audience enforced per host per ADR 0013.
  - `[ProducesResponseType(typeof(GetCustomerOrdersResponse), StatusCodes.Status200OK)]` so NSwag generates the typed response.
  - One-liner: `var result = await mediator.Send(new GetCustomerOrders.Query(page, pageSize, state, dateFrom, dateTo, sort), ct); return HandleResult(result);`.

### Tests

#### GetCustomerOrdersHandlerTests (NEW, ~8 unit tests)

`backend/src/Makables.Tests/AppServices/Features/Orders/GetCustomerOrdersHandlerTests.cs` — NSubstitute mocks (`IOrderQueries`, `IUserSessionProvider`).

1. **Happy_path_returns_paged_data_with_default_sort** — session returns customerId; `IOrderQueries.GetCustomerOrdersPagedAsync` returns a 3-item page. Assert: response wraps the paged data; `IOrderQueries` called once with `(customerId, OrderFilter(null, null, null), OrderSort.CreatedAtDesc, 1, 20, ct)`.
2. **Filter_by_State_passes_through_to_queries** — Query has `State = OrderState.Paid`. Assert: `IOrderQueries` called with `OrderFilter(Paid, null, null)`.
3. **Filter_by_date_range_passes_through_to_queries** — Query has both `DateFrom` and `DateTo`. Assert: `IOrderQueries` called with the exact timestamps. State filter null in the OrderFilter.
4. **Sort_variant_TotalAmountDesc_passes_through** — Query has `Sort = TotalAmountDesc`. Assert: `IOrderQueries` called with `OrderSort.TotalAmountDesc`.
5. **Empty_result_returns_empty_PagedData** — `IOrderQueries` returns `PagedData<CustomerOrderListItemDto>([], 0, 1, 20)`. Assert: response wraps the empty page; TotalCount == 0; Items empty.
6. **Validator_rejects_Page_below_1** — Query with `Page = 0`. Run Validator; assert `Validate().IsValid == false` with rule on `Page`.
7. **Validator_rejects_PageSize_above_50** — Query with `PageSize = 51`. Assert validation failure on `PageSize`.
8. **Validator_rejects_inverted_date_range** — Query with `DateFrom = 2026-06-01`, `DateTo = 2026-05-01`. Assert validation failure (DateFrom > DateTo).

#### GetCustomerOrdersIntegrationTests (NEW, ~3 integration tests)

`backend/src/Makables.IntegrationTests/Orders/GetCustomerOrdersIntegrationTests.cs` — Testcontainers Postgres + `WebApplicationFactory` + seeded fixtures.

1. **GET_returns_paged_orders_for_authenticated_customer** — seed 3 orders for customer A (mixed states + timestamps), 2 orders for customer B. GET `/api/v1/customer/orders?page=1&pageSize=20` as customer A. Assert 200; response body has TotalCount == 3; Items count == 3; sorted by CreatedAt DESC; each item carries OrderId, OrderNumber, State, TotalAmountMinor, Currency, CreatedAt, MakerName, ProductTitle.
2. **Cross_tenant_isolation_returns_zero_results_for_other_customers_orders** — seed 5 orders for customer B; 0 for customer A. GET as customer A. Assert 200; response has TotalCount == 0; Items empty. (IDOR shield — predicate enforced at SQL.)
3. **Pagination_returns_correct_window** — seed 25 orders for customer A. GET `?page=2&pageSize=10` as customer A. Assert 200; response has TotalCount == 25, PageSize == 10, Page == 2, Items count == 10; first item's CreatedAt < last item from page 1 (sort stability).

### Docs

- **`docs/architecture/roles/order.md`** — note the new read-side seam: "Read-side queries live behind `IOrderQueries` (per ADR 0023). `GetCustomerOrdersPagedAsync` ships T-0080; `GetMakerOrdersPagedAsync` T-0081; `GetByIdForCustomerAsync` / `GetByIdForMakerAsync` T-0082." Reference T-0080 in the read-side row of the role's repository table.
- **`docs/tickets/INDEX.md`** — PM flips T-0080 to `**done**` post-merge.

### NSwag regen

The new `GET /api/v1/customer/orders` endpoint is a contract change → **NSwag regen REQUIRED in the same PR** (customer host client only). Per pre-commit hook (T-0013), `frontend/src/lib/api-client/` cannot be edited manually — `npm run generate:api` produces the diff. The new `GetCustomerOrdersResponse` + `CustomerOrderListItemDto` + `OrderSort` + `OrderState` (already shipped) appear in the generated client. T-0081/T-0082 will regen separately in their own commits within the same PR.

## Alternatives Considered

- **Option A — Offset/limit pagination (`offset`, `limit` query params).** *Rejected per A.1* — inconsistent with T-0043 GetPagedMakers + T-0046 catalog. Customer-facing pagination UI naturally maps to page numbers ("Stránka 3 z 12"); offset/limit forces the frontend to derive the page number for display. Mixing pagination shapes across the customer surface increases the cognitive load on every consumer.
- **Option B — Add OrderNumber text search at MVP.** *Rejected per A.2* — text search requires an indexed search infrastructure (LIKE with leading wildcard is slow at scale; trigram or pg_trgm extension needs a migration + extension enable + index). US-customer-0016 does not name text search. Customers paste order numbers from emails; the URL-shareable filter combo + state + date covers 90%+ of dashboard navigation. Re-evaluate post-MVP if usage data warrants.
- **Option C — POST with JSON filter body.** *Rejected per A.3* — loses URL-shareability and browser back/forward. Justified only when the filter envelope outgrows query-string serialization (50+ filter dimensions, embedded sub-filters), which MVP doesn't. Query params + NSwag-generated typed bindings give the same DX.
- **Option D — Nested DTO with Maker {Id, Name} + Products[] array.** *Rejected per A.4* — bigger payload over the wire; harder to render in compact list rows; the frontend flattens it anyway. Nested shapes are justified for the detail responses (T-0082), where the heavier payload is the whole point.
- **Option E — Minimal DTO (OrderId, OrderNumber, State, Total only).** *Rejected per A.4* — forces every detail interaction into a follow-up query just to show "what maker / what product". MakerName + first ProductTitle ARE the row's headline content; omitting them defeats the purpose of a list.
- **Option F — Extend `IOrderRepository` with read methods instead of new `IOrderQueries`.** *Rejected per ADR 0023 + C.2* — `IOrderRepository` is write-scoped per ADR 0013 (CRUD + state-machine methods). Mixing AsNoTracking projection reads into the same interface muddies the contract. Mirrors the T-0049a `IMakerProductQueries` split.
- **Option G — Single SQL round-trip via window function (`COUNT(*) OVER ()` alongside the projection).** *Rejected per C.5* — sets up a real performance issue at scale (window functions compute COUNT on the full result set even when `LIMIT` narrows the projection; Postgres can't optimize it as a cheap COUNT). Two round-trips (CountAsync + Skip/Take) are the standard EF Core paged-list shape; per-query plans optimize independently.
- **Option H — Eager-include Maker + Product navigations (`.Include(o => o.Maker).Include(o => o.Product)`).** *Rejected per C.3* — eager-include materializes the full entities, blowing the projection up to the entity row shape. The `Select` projection lists only the columns the DTO needs (8 scalar fields), which translates to a tight `SELECT a, b, c, ... FROM orders LEFT JOIN ...` instead of `SELECT orders.*, makers.*, products.* FROM ...`.
- **Option I — Project `MakerName` from `User.FullName` only (no `Maker.CompanyName` fallback).** *Rejected per A.4* — makers may register as legal entities (s.r.o.) where the company name IS the canonical brand. The locked decision leaves the picking to the implementer with the constraint: match whichever path T-0044 public-catalog projection uses (consistency over invention).
- **Option J — Validator clamps `PageSize` silently (e.g., `Math.Min(50, pageSize)`).** *Rejected per C.9* — silent clamps surprise callers ("I asked for 100, got 50, no error"). Fast-fail with a 400 + validation message is clearer.

## Out of scope

- **Maker-side list endpoint** — T-0081 owns `GET /api/v1/maker/orders` + `GetMakerOrdersPagedAsync` extension to `IOrderQueries`.
- **Detail endpoints** — T-0082 owns both `GET /api/v1/customer/orders/{id}` and `GET /api/v1/maker/orders/{id}` with separate DTOs per audience + inline attachments + invoice PDF URL.
- **OrderNumber text search** — explicitly rejected per A.2. Re-evaluate post-MVP.
- **"Needs action" pseudo-state filter** — frontend (T-0087) composes multi-state queries when needed; no backend pseudo-state.
- **Frontend dashboard page** — T-0087 owns the customer-orders Server Component + filter UI + empty state CTA. T-0080 only ships the backend endpoint.
- **Empty-state CTA** — frontend concern (US-customer-0016 AC-3 lives in T-0087); backend simply returns empty PagedData.
- **Customer email exposure** — never exposed in any maker response (GDPR data minimization; T-0079 messages thread is the channel). Not relevant to T-0080's customer-only surface.
- **Sorting by MakerName / ProductTitle / OrderNumber.** Not at MVP. The 5 OrderSort values cover the common use cases; new values append later if usage data warrants.
- **Filter by Maker / Product / ShippingMethod.** Not at MVP. State + DateRange covers the dashboard use cases per US-customer-0016 AC-2.
- **CSV / export.** Not at MVP. Customers download invoices individually via T-0086.

## Acceptance criteria

- **AC-1** Given a customer with 3 orders, when `GET /api/v1/customer/orders` is called with a valid customer JWT and no filter params, then the response is `200 OK` with body `{ orders: { items: [...3 items...], totalCount: 3, page: 1, pageSize: 20 } }`. Items are sorted by `CreatedAt DESC` (default sort).
- **AC-2** Given the same customer, when called with `?state=Paid`, then the response contains only orders whose State == Paid. Other states excluded. `totalCount` reflects the filtered count, not the unfiltered count.
- **AC-3** Given the same customer, when called with `?dateFrom=2026-01-01&dateTo=2026-06-01`, then only orders with `CreatedAt` in `[2026-01-01, 2026-06-01]` are returned. Filter is inclusive on both bounds.
- **AC-4** Given a customer with 25 orders, when called with `?page=2&pageSize=10`, then the response has `totalCount: 25, page: 2, pageSize: 10`, items count == 10, and the items are the 11th–20th rows under the default sort.
- **AC-5** Given a customer making a cross-tenant probe (no orders of their own; 5 orders exist for other customers), when called, then the response is `200 OK` with `totalCount: 0` and an empty items array. **No 404, no error — the filter predicate IS the IDOR shield.**
- **AC-6** Given an anonymous request, when the endpoint is called, then the response is `401 auth.required`.
- **AC-7** Given a request with `page=0`, when called, then the response is `400` with a FluentValidation error pointing at the `Page` field. Same shape for `pageSize=0` and `pageSize=51`.
- **AC-8** Given a request with `dateFrom=2026-06-01&dateTo=2026-05-01` (inverted), when called, then the response is `400` with a validation error indicating `DateFrom` must be ≤ `DateTo`.
- **AC-9** Given a request with `?sort=TotalAmountDesc`, when the customer has 3 orders with totals 1000, 5000, 2000, then the response items are ordered 5000, 2000, 1000.
- **AC-10** Given the projection runs against the seeded customer's orders, when inspected, then each `CustomerOrderListItemDto` carries: `OrderId` (non-empty), `OrderNumber` (non-empty), `State` (enum), `TotalAmountMinor` (long), `Currency` (3-char), `CreatedAt` (timestamp), `MakerName` (non-empty), `ProductTitle` (string OR null for custom orders — custom-order seed row asserts null).
- **AC-11** Build clean. Unit tests: baseline (after T-0079 in the same PR sequence) + ~8 new (GetCustomerOrdersHandlerTests). Integration tests: baseline + ~3 new (GetCustomerOrdersIntegrationTests). `node scripts/check-consistency.mjs` exit 0 (no new T1–T7 violations vs the bundle's running baseline). NSwag regen committed in the same PR; `frontend/src/lib/api-client/` types the new `GET /customer/orders` endpoint with `GetCustomerOrdersResponse { orders: PagedData<CustomerOrderListItemDto> }`. No manual edits to the api-client folder (pre-commit hook enforces).
- **AC-12** Given the EF projection runs, when inspected via SQL log, then exactly two SQL statements execute per call: one `SELECT COUNT(*)` and one `SELECT ... LIMIT @pageSize OFFSET @offset`. No N+1; no eager-include materialization of full entity rows. `AsNoTracking()` and `IgnoreAutoIncludes()` confirmed on the EF query.

## Technical notes

### Why page-based pagination (not offset/limit)

Customer-facing pagination UI naturally maps to page numbers — the dashboard renders "Stránka 3 z 12" pagination controls. Offset/limit forces the frontend to derive the page number from the offset (`Math.floor(offset / limit) + 1`) for display, then re-derive the offset on every page-click. Page-based is the same shape T-0043 GetPagedMakers and T-0046 catalog locked, and aligning across all three list endpoints (T-0080 customer + T-0081 maker + the existing two) keeps every consumer's render path identical.

### Why State + DateRange only (not OrderNumber text search)

US-customer-0016 AC-2 explicitly names "state, date range". OrderNumber text search adds an indexed-search dependency (the OrderNumber column needs a B-tree index for prefix match OR a pg_trgm GIN index for substring match; both involve a migration + extension enable). The dominant customer dashboard flow is: log in → browse recent orders → click into one. Text search shines when the customer has hundreds of orders and remembers a partial number; MVP customers have single-digit order counts. Defer to post-MVP when usage data clarifies the actual need.

### Why GET with query params (not POST with body)

URL-shareability is a free customer UX win: a customer can paste a filtered view URL into a support chat ("I see this list, but order X is missing"). Browser back/forward navigation just works. Bookmark-able views just work. POST request bodies hide the filter state in opaque request bodies that can't be linked. The query-string envelope is plenty large for the locked filter set (page, pageSize, state, dateFrom, dateTo, sort — six params, all primitives or short enums).

### Why flat denormalized DTO (not nested Maker {Id, Name} + Products[])

The list-row UI shows one row per order with the maker label and the product label inline. Nested shapes (`{maker: {id, name}}`) bloat the wire payload, force the frontend to flatten on render, and don't enable any reuse — the list-row component is not the detail page. The flat shape is also faster to project in EF: 8 scalar columns, two LEFT JOINs, single round-trip.

### Why the IDOR shield is the WHERE predicate (not a separate authorization check)

`Where(o => o.CustomerUserId == customerId)` baked into the EF query means a customer can never select another customer's row, full stop — there's no "filter, then check" race. Cross-tenant probes return TotalCount == 0 (AC-5), not 404, because the customer's row count IS zero from their perspective. This is the same pattern T-0044 (public catalog) uses for the publicly-listable gate, and it's the simplest defensible IDOR posture. No `[Authorize(Policy = "OrderOwner")]` filter is needed because the SQL never selects the row in the first place.

### Why a separate CountAsync + Skip/Take (not window function COUNT OVER)

Postgres can't optimize `COUNT(*) OVER ()` to a cheap COUNT — it computes the count over the full result set every time, even when `LIMIT` narrows the projection. The two-round-trip pattern (CountAsync, then Skip/Take projection) lets each query optimize independently: COUNT uses the index on `customer_user_id`; the projection uses the composite index on `(customer_user_id, created_at DESC)`. Both queries are sub-millisecond at MVP scale.

### Why no new BusinessErrorMessage codes

Cross-tenant queries return empty results, not 404 (the filter predicate IS the scope). The only failure modes are Validator clamps (Page < 1, PageSize out of range, inverted date range), all of which surface through the existing FluentValidation envelope. No domain failure mode applies — adding error codes for "no orders" would be wrong (success with empty result is the correct response).

### Why `IgnoreAutoIncludes()` on the projection-only query

The Order entity may register `AutoInclude` for navigations (Maker, Product, Customer) at the `OnModelCreating` level. The projection lists only the columns the DTO needs and uses explicit LEFT JOINs via the projection lambda — auto-includes would silently load extra entity data the projection doesn't use, defeating the AsNoTracking optimization. `IgnoreAutoIncludes()` is defensive: it pins the projection to "what I wrote, nothing more".

## Files touched (expected)

### New
- `backend/src/Makables.Core.Domain/Orders/IOrderQueries.cs`
- `backend/src/Makables.Core.Domain/Orders/Sorting/OrderSort.cs`
- `backend/src/Makables.Core.AppServices/Features/Orders/Filters/OrderFilter.cs`
- `backend/src/Makables.Core.AppServices/Features/Orders/DTOs/CustomerOrderListItemDto.cs`
- `backend/src/Makables.Core.AppServices/Features/Orders/GetCustomerOrders.cs`
- `backend/src/Makables.Infra.Database/Orders/OrderQueries.cs`
- `backend/src/Makables.Tests/AppServices/Features/Orders/GetCustomerOrdersHandlerTests.cs`
- `backend/src/Makables.IntegrationTests/Orders/GetCustomerOrdersIntegrationTests.cs`

### Modified
- `backend/src/Makables.Web.Customer/Controllers/OrdersController.cs` — add `GET /api/v1/customer/orders` action (extend the controller created by T-0076 for the `/deliver` endpoint).
- `backend/src/Makables.Config/Extensions/AddMakablesInfrastructure.cs` — register `IOrderQueries`.
- `frontend/src/lib/api-client/*` — NSwag-regenerated (customer host); committed in the same PR.
- `docs/architecture/roles/order.md` — note the read-side `IOrderQueries` seam + the customer-list method.

## Test plan reference

Inline above (see Scope > Tests). No separate `docs/test-plans/T-0080.md`.

## Status log

- 2026-06-09 `draft` by PM. Created as the first ticket in the order-queries bundle (T-0080 customer list + T-0081 maker list + T-0082 customer/maker detail). Reference precedents on master or in the bundle PR: T-0043 GetPagedMakers (page-based pagination + PagedData<T>), T-0046 catalog listing (customer-facing paged read), T-0049a GetMyProducts (read-side IMakerProductQueries split + AsNoTracking projection), T-0060 Order entity + IOrderRepository (write-scoped per ADR 0013). Slice scope: new `IOrderQueries` interface + `OrderSort` enum + `OrderFilter` record + `CustomerOrderListItemDto` + `GetCustomerOrders` one-file feature + `OrderQueries` EF impl + customer endpoint + 8 unit tests + 3 integration tests. No new error codes, migrations, outbox events, or i18n keys.
- 2026-06-09 `draft → ready` by PM. User answered 4 blocking AskUserQuestion items per `/feature` workflow step 3: **A.1** page-based pagination across the bundle (rejected offset/limit); **A.2** filter set = State + DateRange only (rejected adding OrderNumber text search + State-only); **A.3** GET with query params (rejected POST with JSON body); **A.4** flat list-item DTO with denormalized MakerName + first ProductTitle (rejected nested Maker/Products + minimal-only). 10 PM-absorbed decisions captured in `## Locked design decisions §C` (OrderSort enum values, IOrderQueries interface location per ADR 0023, EF projection shape with AsNoTracking + IgnoreAutoIncludes, repository scope predicate, two-query PagedData build, no new error codes, globally-unique Response name, DI registration, PageSize clamp range, customer authorization via IUserSessionProvider, NSwag regen scope). No manual_steps. **Ready for dotnet-backend.** The implementer processes T-0080 → T-0081 → T-0082 sequentially in the same branch; all three ship in one PR.
