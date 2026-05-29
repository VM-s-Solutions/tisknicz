---
id: T-0043
title: Catalog query GetPagedMakers + Maker catalog fields (slug, denormalized stats)
status: done
size: M
owner: dotnet-backend
created: 2026-05-28
updated: 2026-05-28
depends_on: [T-0033, T-0041]
blocks: [T-0044, T-0046]
adrs: []
phase: 3
---

# T-0043 — GetPagedMakers catalog query

## Scope

The public catalog list (US-customer-0007). Establishes the read-side query pattern + the first paginated endpoint, and adds the deferred Maker fields the catalog needs.

### Prerequisite: Maker catalog fields
T-0033/T-0034 deferred these; T-0043 adds them because the catalog query depends on them.
- `Maker.Slug` — derived from `CompanyName` at registration via the shared `SlugGenerator` (IČO fallback when the name has no sluggable chars). Immutable across ARES refresh (`UpdateSnapshot` doesn't touch it — public URLs must survive a name change). Unique across active makers (`ix_makers_slug` partial index).
- `Maker.RatingAverageBp` (0..50000 = 0.0..5.0 stars) + `RatingCount` + `TotalOrders` — denormalized catalog stats, default 0, set via `Maker.SetCatalogStats` (wired by the review/order flows in their tickets). Composite `ix_makers_catalog_sort` index over `(rating_average_bp, total_orders) WHERE is_active`.
- `RegisterMaker` derives a unique slug, disambiguating a collision by appending the IČO.
- Migration `20260528222420_MakerCatalogFields` (empty-table-safe defaults).

### Shared primitives (Core.Domain/Common)
- `SlugGenerator` — extracted from `Category.Slugify` (NFD-decompose + strip diacritics + lowercase + collapse). `Category` now delegates to it (removed the duplicate).
- `PagedData<T>` — `Items`, `Page` (1-based), `PageSize`, `TotalCount`, computed `TotalPages` / `HasNext` / `HasPrevious`. First paginated read; reused by every later list endpoint.

### Read-side (Core.Domain/Catalog + Infra.Database/Catalog)
- `ICatalogQueries.GetPagedMakersAsync(CatalogFilter, ct)` → `PagedData<MakerListItem>`. Interface in Core.Domain, projection in Infra (keeps EF/LINQ out of AppServices per CLAUDE.md).
- `CatalogFilter` (country + optional category-slug / city / min-rating + page/pageSize) + `MakerListItem` DTO.
- `CatalogQueries` — `AsNoTracking` join Maker→User→Address. Publicly-listable gate = global soft-delete filter (active maker + active user + active address) PLUS `EmailConfirmedAt is not null`. Category filter via the `maker_categories` membership. City filter `ToLower().Contains` (provider-portable Postgres + SQLite, not `ILike`). Sort: rating desc → total orders desc → id (stable). Unknown category slug → empty (stale filter chip doesn't 500).
- `MakerCategory` lightweight join entity + config mapping the existing T-0040 `maker_categories` table (no migration — columns already exist; `created_by` mapped as a shadow default).

### AppServices (Features/Catalog)
- `GetPagedMakers` — anonymous query. Validates country (2 chars), page ≥ 1, pageSize 1..48 (default 24 per AC-1), rating 1..5. Delegates to `ICatalogQueries`.

### Web.Public
- `CatalogController` — anonymous `GET /api/v1/catalog/makers?country=&category=&city=&minRating=&page=&pageSize=`.

### Tests (+12; 816 total = 734 unit + 82 integration; +5 Maker-field facts from the prerequisite included)
- `Infra/Catalog/CatalogQueriesTests.cs` — 7 DB round-trips (publicly-listable gate, rating→orders sort, partial/case-insensitive city, min-rating, category membership, unknown-category-empty, paging slice + totals).
- `AppServices/Features/Catalog/GetPagedMakersHandlerTests.cs` — 5 (forward-to-query, paging/rating validation rejections, valid accept).
- `Domain/Makers/MakerTests.cs` — 5 new (slug derive, override, IČO fallback, stats default 0, slug survives snapshot refresh).

## Acceptance criteria
- **AC-1** Default page = 24 makers, sorted rating-avg desc then total-orders desc.
- **AC-2** Category filter shows only makers offering that category (via `maker_categories`).
- **AC-3** City filter is partial + case-insensitive ("Praha" matches "Praha 2").
- **AC-4** Paging is URL-state-driven: `PagedData` carries Page/PageSize/TotalCount/TotalPages for the frontend to drive next/prev.
- **AC-5** Inactive maker / inactive user / unconfirmed email → excluded.
- **AC-6** Maker has a stable public `Slug` (immutable across ARES refresh); unique across active makers.
- **AC-7** 816 tests pass.
- **AC-8** CLAUDE.md hygiene: no EF in AppServices (read-side interface in Core.Domain, impl in Infra); query is `AsNoTracking` projection; no aggregate materialised.

## Out of scope
- Rating/order producers (`SetCatalogStats` callers) — review-creation (T-0050) + order-completion tickets.
- `/katalog` frontend (T-0046).
- Map/geo "near me" (post-MVP).

## Status log
- 2026-05-28 done. Build clean, 816 tests pass. Awaiting dual reviewer per workflow.
