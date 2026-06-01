---
id: T-0049a
title: Maker-side product read queries (GetMyProducts + GetMyProductById)
status: done
size: M
owner: dotnet-backend
created: 2026-06-01
updated: 2026-06-01
depends_on: [T-0041, T-0044]
blocks: [T-0049]
user_stories: [US-maker-0004]
adrs: []
phase: 3
---

# T-0049a — Maker-side product read queries

## Context

T-0049 (frontend maker product CRUD dashboard) needs to list and read the logged-in maker's own products. The Public host's `GetMakerBySlug` (T-0044) returns only *active* products via the publicly-listable gate — wrong for an owner dashboard where drafts and deactivated items must still appear. This ticket adds maker-scoped read queries that bypass that gate and IDOR-shield by `userId → makerId` resolution.

## Scope

- `IMakerProductQueries` interface in `Core.Domain.Products` (new file). Two methods: `GetMyProductsAsync(makerId, page, pageSize, ct)` → `PagedData<MakerProductListItem>`; `GetMyProductByIdAsync(makerId, productId, ct)` → `MakerProductDetail?`. Session-free; the handler resolves `makerId` from `IUserSessionProvider` + `IMakerRepository` first.
- `MakerProductListItem` DTO: `ProductId`, `Title`, `PriceAmountMinor`, `PriceCurrency`, `PriceType`, `WeightGrams`, `CategoryId`, `IsActive`, `PrimaryImageBlobPath?`, `ImageCount`, `CreatedOn`. Distinct from the public catalog's `MakerProductItem` which omits the draft/dashboard fields.
- `MakerProductDetail` DTO: every field the `UpdateProduct` command can mutate + `IsActive` + `CreatedOn` + ordered `Images: IReadOnlyList<ProductImageItem>`. Reuses `Catalog.ProductImageItem` (same shape — `Id`, `BlobPath`, `SortOrder`); no premature fork.
- `GetMyProducts` AppServices query (`Features/Products/GetMyProducts.cs`) — nested `Query`/`Response`/`Validator`/`Handler`. Validator clamps page-size to 48 (matches T-0043). Handler resolves maker from session → `maker.notFound` if no row; forwards session-resolved `makerId` to the projection.
- `GetMyProductById` AppServices query — same shape. Null projection → `product.notFound` (IDOR-safe — no oracle for cross-maker probes).
- `MakerProductQueries` EF impl in `Infra.Database/Products/`. `IgnoreQueryFilters()` to bypass the global `Auditable` soft-delete filter (deliberate, called out in the type's XML doc and at each call site). `AsNoTracking()` on every read.
- DI registration in `AddMakablesInfrastructure.cs`.
- `Web.Maker.ProductController` endpoints: `GET /api/v1/products` (list) and `GET /api/v1/products/{productId}` (detail), both `[Authorize]` (inherited from the controller-level attribute) and both annotated with `[ProducesResponseType]` so NSwag generates typed return shapes (the rest of the controller's `[ProducesResponseType]` rollout is T-0049b).

## Out of scope

- The rest of the Maker host's `[ProducesResponseType]` rollout — T-0049b.
- The frontend dashboard pages — T-0049 (the consumer ticket).
- Any new `BusinessErrorMessage` codes. `MakerNotFound` and `ProductNotFound` already exist and are reused.
- The other three Web hosts.

## Acceptance criteria

- **AC-1** Given a logged-in maker, when `GET /api/v1/products` is called, then the response is a `PagedData<MakerProductListItem>` containing every product owned by that maker — including soft-deleted ones — sorted active-first then newest-first.
- **AC-2** Given a logged-in maker requesting a product owned by another maker, when `GET /api/v1/products/{productId}` is called, then the response is `404 product.notFound` (IDOR shield — same shape as "doesn't exist"; no oracle).
- **AC-3** Given a user with no maker row, when either endpoint is called, then the response is `404 maker.notFound`.
- **AC-4** Given an anonymous request, when either endpoint is called, then the response is `401 auth.required`.
- **AC-5** Given a product with images, when the detail endpoint is called, then `Images` is ordered by `SortOrder` ascending.
- **AC-6** Build clean, all tests pass. 23 new tests: 12 handler unit tests + 11 EF projection tests (SQLite harness).

## Technical notes

### Option B chosen for the read-side interface

`Core.Domain.Catalog.ICatalogQueries` was the alternative, but its XML doc explicitly says "publicly-listable" — mixing maker-scoped reads that bypass the email-confirmed gate and the soft-delete filter would muddy that contract. The new `IMakerProductQueries` interface lives next to `IProductRepository` in `Core.Domain.Products`, semantically owned by the same aggregate. The EF impl is in `Infra.Database/Products/` alongside `ProductRepository`. Same shape as the existing read-side split: catalog queries in `Catalog/`, maker queries in `Products/`.

### Sort key — `Id desc` proxy

The dashboard contract is "active first, newest first". The implementation sorts by `IsActive desc, Id desc` because SQLite (the test harness DB) can't ORDER BY a `DateTimeOffset`. ULIDs are lexicographically time-ordered, so `Id desc` is a faithful "newest first" proxy. The DTO still returns the real `CreatedOn` so the frontend can render the timestamp. Same workaround as `CatalogQueries` (T-0044 doc explicitly calls this out).

### Tests landed in `Makables.Tests/Infra/Products/`

The ticket asked for them in `Makables.IntegrationTests/Infra/Products/`. The established codebase pattern is the opposite — every EF projection test for the catalog read-side lives in `Makables.Tests/Infra/Catalog/` against the SQLite `TestDbHarness`. `Makables.IntegrationTests` is reserved for Testcontainers-Postgres + `WebApplicationFactory` end-to-end tests. I followed the established pattern; the EF coverage is equivalent. If the team wants a future end-to-end controller test (Web.Maker + JWT + Postgres), that's a separate file in `Makables.IntegrationTests`.

### IDOR shield is enforced twice — by design

The handler resolves `makerId` from the session and forwards it to the projection. The projection then filters on `p.MakerId == makerId`. Either layer alone would be sufficient; both is belt-and-braces — if a future caller (e.g. an admin tool that legitimately needs to read another maker's products) skips the handler, the projection still enforces the predicate. The unit test `GetMyProductById_passes_session_resolved_makerId_to_queries` pins the handler layer; the infra test `GetMyProductById_returns_null_for_cross_maker_id` pins the projection layer.

## Files touched

- `backend/src/Makables.Core.Domain/Products/IMakerProductQueries.cs` (new) — interface + `MakerProductListItem` + `MakerProductDetail` DTOs.
- `backend/src/Makables.Core.AppServices/Features/Products/GetMyProducts.cs` (new) — paged-list query.
- `backend/src/Makables.Core.AppServices/Features/Products/GetMyProductById.cs` (new) — single-detail query.
- `backend/src/Makables.Infra.Database/Products/MakerProductQueries.cs` (new) — EF projection.
- `backend/src/Makables.Config/Extensions/AddMakablesInfrastructure.cs` — register `IMakerProductQueries`.
- `backend/src/Makables.Web.Maker/Controllers/ProductController.cs` — two new endpoints (`List`, `GetById`) with `[ProducesResponseType]`.
- `backend/src/Makables.Tests/AppServices/Features/Products/MakerProductQueryHandlerTests.cs` (new) — 12 handler unit tests.
- `backend/src/Makables.Tests/Infra/Products/MakerProductQueriesTests.cs` (new) — 11 EF projection tests against SQLite.

## Status log

- 2026-06-01 done. Build clean, 773 unit tests pass (23 new), 82 integration tests pass.
