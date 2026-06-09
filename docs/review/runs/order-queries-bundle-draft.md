# Order-queries bundle — Reviewer preliminary verdict (draft)

> Bundle-scope draft per docs/process/routing.md §"Bundle workflow" step 4 (one draft per bundle, not per ticket). Structural read BEFORE the diff lands; final verdict happens after the implementer reports done and supersedes this file.

## Bundle scope (T-0080 + T-0081 + T-0082)

Read-only bundle. Three tickets ship four endpoints (`GET /api/v1/customer/orders`, `GET /api/v1/maker/orders`, `GET /api/v1/customer/orders/{id}`, `GET /api/v1/maker/orders/{id}`) behind a new `IOrderQueries` read-side seam at `Core.Domain/Orders/IOrderQueries.cs`. New types: `OrderSort` enum, `OrderFilter` record, four DTOs (`CustomerOrderListItemDto`, `MakerOrderListItemDto`, `CustomerOrderDetailDto`, `MakerOrderDetailDto`) + the shared `OrderAttachmentSummaryDto`. EF impl at `Infra.Database/Orders/OrderQueries.cs` (NEW class). Zero migrations, zero outbox events, zero new `BusinessErrorMessage` codes, zero state-machine touches. Unblocks T-0086 customer dashboard + T-0087 maker dashboard. Dep chain T-0080 → T-0081 → T-0082, all three on `feat/order-queries-bundle` per docs/process/routing.md §"Bundling related tickets into one PR".

## Patterns / ADRs the diff must honour

Walked against `docs/architecture/patterns.md` + ADRs in ticket frontmatter:

- **patterns.md A.4 (one-file feature shape).** Four new feature files: `GetCustomerOrders.cs`, `GetMakerOrders.cs`, `GetCustomerOrderDetails.cs`, `GetMakerOrderDetails.cs`. Each `public static class` with nested `Query` / `Validator` / `Handler` + a globally-unique top-level response. Precedent: `backend/src/Makables.Core.AppServices/Features/Products/GetMyProducts.cs:31` (`public static class GetMyProducts` containing `Query` at :36 + `Validator` at :38 + `Handler` at :52). Mirror this shape exactly.
- **patterns.md A.7 (per-Validator FluentValidation).** Each new Query has a sibling Validator. List queries clamp Page ≥ 1 + PageSize 1..50 + Sort `IsInEnum()` + State `IsInEnum()` when set + DateFrom ≤ DateTo when both set. Detail queries clamp OrderId non-empty + length. The precedent at `GetMyProducts.Validator` lines 38-50 + `GetPagedMakers.Validator` lines 35-57 uses `int.MaxValue / MaxPageSize` upper-bound on Page to prevent Skip-offset overflow — list-query validators in this bundle should mirror that defensive cap (T-0043 Copilot review precedent).
- **patterns.md A.8 (paged query envelope).** `PagedData<T>` is the locked envelope. Verified at `backend/src/Makables.Core.Domain/Common/PagedData.cs:14` — constructor signature is `(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)`. **Note the positional order** — T-0080 ticket spec lines 148/151 use `new PagedData<CustomerOrderListItemDto>(items, totalCount, page, pageSize)` which is the WRONG positional order for the existing record. Implementer must follow the precedent at `MakerProductQueries.cs:87`: `new PagedData<MakerProductListItem>(items, page, pageSize, totalCount)`.
- **ADR 0013 (per-audience JWT enforcement + scoped repositories).** Two of the four endpoints (customer-list + customer-detail) live on `Web.Customer`; two (maker-list + maker-detail) on `Web.Maker`. JWT audience is the host-level enforcement. The session-resolution code path is `IUserSessionProvider.GetUserId()` → for the maker side, `IMakerRepository.GetByUserIdAsync(userId, ct)` (precedent: `GetMyProducts.Handler` lines 61-71). The IDOR shield is the WHERE predicate baked into the EF projection: `o.CustomerUserId == customerId` for the customer side; `o.MakerId == makerId` for the maker side. Returns null / empty result for cross-tenant probes — same shape as not-found, no IDOR oracle. Verified precedent on `IOrderRepository.GetByIdForCustomerAsync` at `IOrderRepository.cs:79` (returns null for unknown OR cross-customer ids).
- **ADR 0014 (UoW pipeline).** Read-only handlers. No `SaveChangesAsync()` anywhere. `ValidationPipelineBehavior` runs; `UnitOfWorkPipelineBehavior` is a no-op because the handlers return `IQuery<T>`. Reviewer will grep the four new handler files for `SaveChangesAsync` — any match = HARD BLOCK.
- **ADR 0023 (read-side projection performance).** Every EF query in `OrderQueries.cs` must apply `.AsNoTracking()` + `.IgnoreAutoIncludes()` before the projection. Precedent: `MakerProductQueries.cs:48-50`. Reviewer will grep `OrderQueries.cs` for these two method calls on every query.
- **T-0049a precedent for IDOR-twice handler shape.** `GetMyProducts.Handler` lines 67-71 resolves maker from session, returns `Error.NotFound("maker")` on null, then forwards `maker.Id` to the queries layer. T-0081's `GetMakerOrders.Handler` and T-0082's `GetMakerOrderDetails.Handler` must follow this same shape. Reviewer hard-blocks if any of these handlers accepts a `MakerId` query/command parameter from the request body or path.
- **Globally-unique Response naming (post-PR #38 NSwag CI fix).** Verified at commit `4497284`. The fix renamed nested `Response` records to globally-unique prefixed names. Two valid patterns coexist on master:
  - **Wrapper pattern**: `public sealed record GetCustomerOrdersResponse(PagedData<CustomerOrderListItemDto> Orders)` — what the tickets lock.
  - **Bare PagedData pattern**: `IQuery<PagedData<T>>` → handler returns `BusinessResult<PagedData<T>>` directly. `PagedData<T>` is already a unique top-level schema. Precedent: `GetMyProducts.Handler:56` (`IRequestHandler<Query, BusinessResult<PagedData<MakerProductListItem>>>`) and `GetPagedMakers` at `Features/Catalog/GetPagedMakers.cs:33` (`IQuery<PagedData<MakerListItem>>`).
  
  The bundle tickets pick the wrapper pattern (T-0080 §C.7, T-0081 §C.7, T-0082 §C.7). The wrapper pattern is the more conservative choice (gives the customer + maker variants distinct schema names that frontend code can pin). Reviewer accepts either, but the implementer must pick ONE shape and apply it uniformly across all four features. Mixed-pattern bundle = inconsistent client.

## Pre-flight risks (HIGH first)

### HIGH

1. **HIGH: IDOR shield placement.** Four IDOR-critical surfaces in one PR.
   - List endpoints: the WHERE predicate baked into the EF query (`o.CustomerUserId == customerId` for customer side; `o.MakerId == makerId` for maker side). Cross-tenant probes return `TotalCount == 0` + empty Items (NOT 404 — per T-0080 §AC-5 + T-0081 §AC-3).
   - Detail endpoints: same shield via `o.Id == orderId && o.CustomerUserId == customerUserId` (customer) and `o.Id == orderId && o.MakerId == makerId` (maker). Cross-tenant probes return null at the queries layer → `Error.NotFound("order", BusinessErrorMessage.OrderNotFound)` at the handler — same response shape as unknown id (T-0082 §AC-2 + §AC-10). **No IDOR oracle.**
   - Watch for any "for now load Unscoped" laziness — the predicate IS the shield. Reviewer hard-blocks if the EF projection ever uses `db.Set<Order>()` (or `db.Orders`) without the scoping `.Where(...)` chained immediately. Also hard-blocks if the queries layer accepts a `customerUserId` / `makerId` that came from a request param rather than the session.

2. **HIGH: Customer EMAIL exposure on maker responses.** T-0081 §A.2 + §AC-8/9 explicitly EXCLUDE customer email from `MakerOrderListItemDto`. T-0082 §A.1 + §C.2 + §AC-4 explicitly EXCLUDE `CustomerContactEmail` from `MakerOrderDetailDto`. Compile-time gate: the DTO record definition has no `Email` field of any case. Runtime gate: the EF projection's expression tree must NOT name `o.ContactEmail` (verified at `Order.cs:87` — the column exists as `ContactEmail`). Reviewer will grep both maker projection methods in `OrderQueries.cs` for `ContactEmail` — any match = HARD BLOCK + GDPR data-minimization violation citation. T-0082 AC-4 includes a reflection-based pin (`typeof(MakerOrderDetailDto).GetProperties().Should().NotContain(p => p.Name.Contains("Email"))`) — verify the test exists.

3. **HIGH: GetCustomerOrderDetails vs GetMakerOrderDetails are TWO SEPARATE QUERIES.** T-0082 §A.1 locks "compile-time IDOR shield via separate handlers + DTOs". The four files must be:
   - `Core.AppServices/Features/Orders/GetCustomerOrderDetails.cs` (with `CustomerOrderDetailDto` parameter, no audience flag)
   - `Core.AppServices/Features/Orders/GetMakerOrderDetails.cs` (with `MakerOrderDetailDto` parameter, no audience flag)
   - Two separate `IOrderQueries` methods: `GetCustomerOrderDetailsAsync(orderId, customerUserId, ct)` and `GetMakerOrderDetailsAsync(orderId, makerId, ct)`. **Watch for any single shared Query with audience parameter or runtime-branching.** Reviewer hard-blocks if a `GetOrderDetails.cs` ships with an `OrderDetailAudience` enum / boolean flag.

4. **HIGH: Globally-unique Response naming.** Bundle adds at least 4 new top-level wire types: `GetCustomerOrdersResponse`, `GetMakerOrdersResponse`, `GetCustomerOrderDetailsResponse`, `GetMakerOrderDetailsResponse`. Each must carry the feature prefix in the C# type name so NSwag's flattening rules don't collide them. Reviewer will grep the four new feature files for `public sealed record Response` — any match = HARD BLOCK (re-introduces the PR #38 collision per `fix(shipping-bundle): rename Response records to prefixed names to fix CI tsc failure` at commit `4497284`).

5. **HIGH: Order entity surface does NOT have several fields the tickets reference.** Reviewer pre-flight against `backend/src/Makables.Core.Domain/Orders/Order.cs`:
   - **No `OrderItem` / `OrderItems` collection.** Order has a single nullable `ProductId` (`Order.cs:79`). T-0081 §C.3 sample EF code references `o.OrderItems.OrderBy(i => i.SortOrder).Select(i => i.ProductTitle).FirstOrDefault()` — this does NOT compile. The right projection is `o.ProductId == null ? null : o.Product.Title` (matches T-0080 §148 sample, modulo the precedent navigation property — implementer must verify whether `Order.Product` navigation is configured or whether it requires a join via `db.Products`).
   - **No `VatAmountMinor` column.** Order carries `VatRateBp` only (`Order.cs:122`) per ADR 0003 (rate-only snapshot; line VAT derived at invoice rendering time). T-0082 §C.1 + §AC-1 list `VatAmountMinor` as a required field on `CustomerOrderDetailDto`. Implementer must either (a) compute the VAT amount inline from `VatRateBp` + `ProductPriceAmountMinor + ShippingPriceAmountMinor` at projection time, or (b) drop the field (the rate is sufficient; the FE computes display amount). Pre-flight question: confirm with PM/architect; default to (a) computing inline if the AC names the field.
   - **`MakerName` source: `Maker.CompanyName`, NOT `Maker.DisplayName`.** Verified at `Maker.cs:50`. T-0081 §C.3 sample and T-0082 §C.1 sample both reference `o.Maker.DisplayName` — this property does NOT exist. Implementer must use `o.Maker.CompanyName` (matches the catalog precedent — verify via `CatalogQueries.cs`).
   - **Price-breakdown column names.** Order entity uses `ProductPriceAmountMinor` / `ShippingPriceAmountMinor` (`Order.cs:95`/:98), NOT `ProductPriceMinor` / `ShippingPriceMinor` (T-0082 §C.1 names). DTO field names can be either, but the EF projection must reference the actual entity property names.
   - **No `ICustomerSessionContext` / `IMakerSessionContext`.** Only `IUserSessionProvider` exists per `Grep AppServices/Features` — every existing handler uses `IUserSessionProvider.GetUserId()`. T-0082 §C.4/5 sample DI references `ICustomerSessionContext sessionContext` + `sessionContext.RequireCustomerUserId()` — this does NOT exist. Implementer must use `IUserSessionProvider session` + `session.GetUserId()` + the existing null-check-as-Unauthorized pattern from `GetMyProducts.Handler:61-65`.
   - **No `Invoice` navigation property on `Order`.** Invoice has `OrderId` FK (`Invoice.cs:75`); the reverse navigation may or may not be configured. T-0082 §C.1 EF sample references `o.Invoice != null && o.Invoice.PdfBlobPath != null`. Implementer must either configure / verify the reverse navigation OR LEFT JOIN explicitly via `db.Invoices.FirstOrDefault(i => i.OrderId == o.Id)`. Reviewer accepts either as long as the resulting SQL is one round-trip.

   **The above are pre-flight ticket-spec ambiguities the implementer must resolve at code time.** None of these are blocking on their own — the tickets accept implementer judgement — but the implementer should NOT copy the sample EF expressions verbatim. Reviewer reads the actual diff against the actual entity surfaces.

### MEDIUM

6. **MEDIUM: Two-pass count + skip/take EF batching.** EF Core 10 does not auto-batch a `CountAsync` and a follow-up `Skip/Take + ToListAsync` into a single round-trip — two SQL statements per list call is the precedent (`MakerProductQueries.cs:52` + :85). Per ADR 0023 NFRs this is acceptable for MVP (target sub-100ms per page render with composite indexes on `(customer_user_id, created_at DESC)` and `(maker_id, created_at DESC)` — verify these indexes exist at the migration layer; if not, optimizer ping). T-0080 §AC-12 explicitly pins "exactly two SQL statements execute per call" — implementer must NOT add a window function variant.

7. **MEDIUM: Page-size clamp via Validator (not silent `Math.Min`).** T-0080 §A.10 (Alternative J) explicitly rejects silent clamps. The Validator enforces `PageSize` ∈ [1, 50] via `InclusiveBetween(1, 50)` → 400 response on out-of-range. Reviewer hard-blocks if the queries layer or the handler clamps silently. Precedent: `GetMyProducts.Validator:48` uses `InclusiveBetween(1, MaxPageSize)`.

8. **MEDIUM: ProductTitle nullable + custom orders.** Custom orders carry `ProductId == null` (`Order.cs:79`). The EF projection must use a conditional `o.ProductId == null ? null : ...` that translates to SQL LEFT JOIN + CASE. An INNER JOIN to Product silently drops custom orders from the list — reviewer hard-blocks if the projection's `Select` doesn't handle the null case explicitly. Same applies to the `Order.Product` navigation property if EF configures it as required (verify at `Infra.Database/Orders/OrderConfiguration.cs`).

9. **MEDIUM: Stable secondary sort for pagination.** T-0080 §148 + T-0081 §C.4 specify `OrderByDescending(o => o.CreatedAt)` then a tie-breaker on `Id` (DESC). Without the tie-breaker, two rows sharing a timestamp can flip between page boundaries — visible as duplicate rows on page 2. Precedent: `MakerProductQueries.cs:66-67` uses `ThenByDescending(p => p.Id)` for the same reason. Reviewer will grep the new sort branches for an explicit secondary `.ThenBy*(o => o.Id)` — missing tie-breaker = MEDIUM flag (not blocking but flagged for fold).

10. **MEDIUM: Attachment + Invoice inline projection round-trips.** T-0082 §C.1 specifies the projection materializes `Attachments` (max 10 per order — bounded) + `InvoicePdfUrl` (single invoice per order) inline via `Select(o => new ...Dto(...o.Attachments.OrderBy(...).Select(...).ToList()..., o.Invoice != null ? ... : null...))`. EF Core 10 translates the correlated subqueries either as LEFT JOIN + array_agg OR as separate round-trips (split-query mode). Verify at PR-open against actual SQL: T-0082 §AC-9 pins "no N+1 via `EFCore.Diagnostics`" — the implementer must add the assertion or reviewer accepts a SQL-log snapshot in the integration test.

11. **MEDIUM: `DownloadUrlsOptions` introduction.** T-0082 §C.6/7 introduces a new `DownloadUrlsOptions` options class with `AttachmentDownloadUrlBase` + `InvoiceDownloadUrlBase` properties. T-0082 §Modified line 327-330 says the implementer verifies whether T-0080/T-0081 already introduced it. Pre-flight: T-0080 + T-0081 tickets do NOT introduce this class (verified — only T-0082 needs URL construction in projection). So T-0082 owns it. Reviewer will verify (a) the options class lives at the right namespace (`Infra.Database/Configuration/` per ticket); (b) it's registered via `services.Configure<DownloadUrlsOptions>(configuration.GetSection("DownloadUrls"))` in `AddMakablesInfrastructure.cs`; (c) `appsettings.{Environment}.json` carries default values for each host (customer + maker); (d) the OrderQueries constructor takes `IOptions<DownloadUrlsOptions>` and reads `.Value` once at method entry (not per row).

12. **MEDIUM: `PostgresHarness` does NOT register `AuditableSaveChangesInterceptor`.** Verified at `backend/src/Makables.IntegrationTests/Common/PostgresHarness.cs:85-91` — the harness builds `DbContextOptionsBuilder<MakablesDbContext>` directly via `.UseNpgsql(...)`, without registering the interceptor that populates `CreatedBy` / `CreatedOn`. Per the delivery-close-bundle precedent (`docs/review/runs/delivery-close-bundle-draft.md` + commit `00eb2a2` "fix(delivery-close-bundle): integration test fixtures set CreatedBy + consistency baseline update"), integration-test fixtures must manually call `entity.MarkCreated(userId, clock)` before `db.SaveChanges()` or the seed rows fail the NOT-NULL constraint on `CreatedBy`. Implementer must remember this for the ~10 new integration tests (3 customer list + 3 maker list + 2 detail projection + 2 cross-tenant isolation). Pre-flight risk — easy to forget.

13. **MEDIUM: GetMakerOrders + GetMakerOrderDetails must NOT load Customer.User.Email anywhere.** T-0081 §C.3 explicitly forbids `o.Customer.User.Email` references in the projection expression tree. Reviewer will grep `OrderQueries.cs` post-impl for `Email` — any reference in the maker-side projection methods = HARD BLOCK + GDPR violation. T-0082 §AC-4 pins this with a JSON serialization assertion: serialize `MakerOrderDetailDto` to JSON and assert the string `"customerContactEmail"` does NOT appear.

14. **MEDIUM: Filter records re-used between T-0080 and T-0081.** T-0080 introduces `OrderFilter` (with `DateRangeStart` / `DateRangeEnd` per §107 sample). T-0081 §99 reuses the same record. T-0080 ticket §107 specifies fields `(OrderState? State, DateTimeOffset? DateRangeStart, DateTimeOffset? DateRangeEnd)`; T-0081 §99 reuses with different field names (`State`, `CreatedAtMin`, `CreatedAtMax`). Implementer must pick ONE field-naming scheme — reviewer accepts either as long as it's consistent. Pre-flight flag: T-0080's `DateRangeStart/End` naming is locked first; T-0081 must conform.

### LOW

15. **LOW: NSwag regen scope.** Two endpoints land on customer host (`GET /customer/orders` + `GET /customer/orders/{id}`); two land on maker host (`GET /maker/orders` + `GET /maker/orders/{id}`). One `npm run generate:api` covers all four. Reviewer Gate 6 verifies the generated TS client has all four methods + DTOs + the shared `OrderAttachmentSummaryDto` + the `OrderSort` enum + the `OrderFilter`-equivalent typed query params.

16. **LOW: Frontend `lib/api-client/` pre-commit hook.** Per CLAUDE.md "The generated client (`lib/api-client/`) is not edited manually." Verify NSwag regen produces a clean diff; no manual edits.

17. **LOW: `OrderState` enum already shipped.** T-0080 §193 notes "OrderState (already shipped)". Verified at `backend/src/Makables.Core.Domain/Orders/OrderState.cs` (implied by Order.cs usage). NSwag will pick up the existing enum on the customer-list endpoint regen.

## Test coverage expectations (Gate 5)

Per `docs/process/must-cover-tests.md` and `docs/process/tdd-policy.md`:

### Pure logic — TDD-first commit required (T-0067+ enforcement)

- **§5 Validators (must be test-first):**
  - `GetCustomerOrders.Validator` — positive + 1 negative per `RuleFor` (Page ≥ 1, PageSize ∈ [1,50], Sort.IsInEnum, State.IsInEnum when set, DateFrom ≤ DateTo when both set). ~6-7 tests.
  - `GetMakerOrders.Validator` — same shape. ~6-7 tests.
  - `GetCustomerOrderDetails.Validator` — OrderId non-empty + length. ~2 tests.
  - `GetMakerOrderDetails.Validator` — OrderId non-empty + length. ~2 tests.
  - **HARD FAIL if these land after the handler commit per docs/process/tdd-policy.md §"The rule".** Reviewer walks `git log --reverse feat/order-queries-bundle -- <validator-files> <handler-files>` and expects red-before-green or a status-log proof per ticket.
- **§7 Authz / ownership in scoped queries (must be test-first):**
  - T-0080 §AC-5 (cross-tenant probe returns empty result, not 404) — integration test at `GetCustomerOrdersIntegrationTests.Cross_tenant_isolation_returns_zero_results`.
  - T-0081 §AC-3 (cross-maker isolation) — `GET_orders_cross_maker_isolation_makerA_cannot_see_makerB_orders`.
  - T-0082 §AC-10 (4 cross-tenant combinations × 2 audiences) — `OrderDetailsIsolationTests.Customer_cannot_read_another_customers_order_detail` + `.Maker_cannot_read_another_makers_order_detail`.
  - Per must-cover-tests.md §7 these are the IDOR shield tests — non-negotiable.
- **§9 BusinessErrorMessage codes negative-path:**
  - T-0081 + T-0082 maker-side handlers surface `BusinessErrorMessage.MakerNotFound` (no maker for user). Each must have ≥1 negative-path test — T-0081 §GetMakerOrdersHandlerTests test 2 + T-0082 §GetMakerOrderDetailsHandlerTests test 2 cover this.
  - T-0082 handlers surface `BusinessErrorMessage.OrderNotFound` (ownership mismatch / unknown id). T-0082 §GetCustomerOrderDetailsHandlerTests test 2 + §GetMakerOrderDetailsHandlerTests test 3 cover this.
  - **Zero new codes added across the bundle** per T-0080 §C, T-0081 §C, T-0082 §C.

### Carve-out — Handler unit tests (pragmatic-alongside accepted)

Per docs/process/tdd-policy.md §"Carve-outs" table: handler unit tests are orchestration code. The carve-out allows them to land in the same commit as the handler when the handler delegates pure logic to a domain service. The four new handlers in this bundle are pure orchestration (session resolve → repo lookup → queries dispatch → response wrap) — the carve-out applies. Reviewer accepts handler-test-with-handler commits; Validator + Specification tests still require strict test-first.

### Bundle test count expectations

- Unit: ~8 (GetCustomerOrdersHandler) + ~8 (GetMakerOrdersHandler) + ~6 (GetCustomerOrderDetailsHandler) + ~6 (GetMakerOrderDetailsHandler) + ~17 (4 Validators × ~4 tests each) = **~45 new unit tests**.
- Integration: ~3 (customer list) + ~3 (maker list) + ~2 (detail projection) + ~2 (cross-tenant isolation) = **~10 new integration tests**.
- Baseline expected: current 107 → 117 integration tests; current ~unit-baseline (verify post-impl) + 45.

## Mechanical-check expectations (Gate 9)

Per `scripts/check-consistency.mjs` T1-T7 rules:

- **T1 (one-file feature shape).** Four new files (`GetCustomerOrders.cs`, `GetMakerOrders.cs`, `GetCustomerOrderDetails.cs`, `GetMakerOrderDetails.cs`) each `public static class` with nested types. Expected to pass — false-positive risk only if implementer forgets the static class wrapper.
- **T2 (Response naming).** Each new wrapper record must be `Get*Response` (globally unique). Bare `Response` = HARD FAIL per the PR #38 CI fix at commit `4497284`.
- **T3 (SaveChangesAsync in handler).** Zero matches expected — all handlers are read-only.
- **T4 (dynamic / any).** N/A.
- **T5 (BusinessErrorMessage inline strings).** No new codes; existing `OrderNotFound` + `MakerNotFound` are referenced via `BusinessErrorMessage.X` constants. Reviewer greps for `Error.NotFound("order", "order.notFound")` — any inline string = HARD FAIL.
- **T6 (money columns `_minor` + currency).** N/A — no schema changes.
- **T7 (useEffect data fetching).** N/A — backend-only bundle.

Bundle's running baseline: 107 → 111 expected (4 new one-file feature T1 false-positives that mirror existing baseline pattern). Implementer must update the baseline alongside the PR or the CI fails the bundle.

## Bundle DoR compliance check

Per docs/process/ticket-lifecycle.md §Bundle DoR + docs/process/routing.md §"Bundle DoR":

- All 3 tickets satisfy individual DoR (status: ready) ✓
- Bundle scope named in branch name (feat/order-queries-bundle) ✓
- Bundle dep chain documented in each ticket's Context section ✓
- No external blockers between tickets ✓
- Single parallel-reviewer artifact (this file) ✓
- L-split rule not triggered (all three are M) ✓
- Bundle LOC budget: ~3000 production + ~1500 tests ✓ (4 feature files + 5 DTOs + 1 query class + 2 controller-action extensions ≈ 1200 LOC production)

## Open items the implementer should confirm before coding

1. **Maker display field.** The locked decision (T-0080 §A.4 + T-0081 §C.3) says implementer picks the canonical maker label, matching whichever path the public catalog (T-0044) uses. Reviewer's verification at `Maker.cs:50` — only `CompanyName` exists; no `DisplayName`. Implementer: use `o.Maker.CompanyName` and update the ticket sample expressions during fold if helpful.
2. **Customer-host session abstraction.** No `ICustomerSessionContext` exists; only `IUserSessionProvider`. Implementer must follow the `GetMyProducts.Handler:61-65` pattern for null-userId → Unauthorized.
3. **Order.OrderItems vs Order.ProductId.** Order has a single nullable `ProductId`, no OrderItems collection. The projection's "first product title" reduces to `o.ProductId == null ? null : o.Product.Title` — verify `Order.Product` navigation property is configured at `Infra.Database/Orders/OrderConfiguration.cs` (or LEFT JOIN explicitly).
4. **Invoice navigation property.** Verify `Order → Invoice` reverse navigation is configured. If not, the T-0082 projection LEFT JOINs via `db.Invoices.Where(i => i.OrderId == o.Id).FirstOrDefault()`.
5. **VatAmountMinor field.** Order entity does NOT store VAT amount — only `VatRateBp` (rate-only snapshot per ADR 0003). T-0082 §C.1 lists `VatAmountMinor` on `CustomerOrderDetailDto`. Implementer either (a) computes inline at projection from rate × (product + shipping), or (b) drops the field. Confirm with PM during code time if AC-1 is strict; default (a).
6. **Two-pass count + skip/take roundtrip behaviour in EF Core 10.** Per ADR 0023 NFRs the cost is acceptable. Optimizer ping deferred until perf data warrants.
7. **PostgresHarness AuditableSaveChangesInterceptor not registered.** Integration test fixtures must call `MarkCreated()` manually on every seeded aggregate (Order, Maker, User, Address, Product, Invoice, OrderAttachment) — per the delivery-close-bundle precedent at commit `00eb2a2`.

## Optimizer ping (Gate 8)

This bundle ships **2 new paged-list endpoints + 2 new detail endpoints**, each on a hot path:
- Customer dashboard list view will be rendered on every login (server-rendered Next.js page).
- Maker dashboard list view rendered on every maker session.
- Detail pages rendered on every order navigation.

Per docs/process/routing.md row "Hot path / external call / heavy UI / new package" → optimizer engages. Reviewer will ping optimizer at PR-open for:
- Composite index verification on `(customer_user_id, created_at DESC)` + `(maker_id, created_at DESC)`.
- Two-round-trip count + skip/take cost target (<100ms each per ADR 0023).
- LEFT JOIN to Maker / Product + projected `o.Attachments.Select(...)` correlated subquery cost target.
- Invoice LEFT JOIN cost target (single-row per Order).

## Preliminary verdict

**STRUCTURALLY_SOUND_PENDING_DIFF** with **PRE_FLIGHT_TICKET_SPEC_NITS** captured in HIGH-5 above (Order entity surface mismatches in the ticket sample EF code).

Rationale:
- Bundle scope is read-only, well-bounded, follows precedent (`GetMyProducts` + `GetPagedMakers`).
- IDOR shield design (predicate-as-shield) is correct and matches ADR 0013 + the existing `IOrderRepository.GetByIdForCustomerAsync` precedent.
- GDPR posture (no customer email on maker responses) is locked at the DTO level + the expression-tree level + the AC-4 reflection pin.
- One pure-logic surface (4 Validators) is properly carved out for test-first commit order.
- Zero new error codes / migrations / outbox events / schema touches = low blast radius.
- The ticket EF sample code references entity properties that don't exist (`Maker.DisplayName`, `Order.OrderItems`, `Order.VatAmountMinor`, `Order.Invoice`, `ICustomerSessionContext`). These are ticket-spec rough-drafts — the locked decisions are correct, the sample code needs implementer adaptation. Reviewer accepts this as routine pre-flight tidy; the implementer adjusts as code lands. The risks are flagged here for the implementer's awareness and so reviewer's final pass against the diff cross-references the actual entity surfaces, not the sample.
- Bundle DoR satisfied. Single parallel-reviewer artifact in this file.

Final verdict will run after the implementer reports done, against the actual diff + scripts/check-consistency.mjs output + Gate 5 TDD-policy git-log walk.
