---
id: T-0044
title: Maker profile query GetMakerBySlug
status: done
size: M
owner: dotnet-backend
created: 2026-05-28
updated: 2026-05-28
depends_on: [T-0033, T-0041, T-0043]
blocks: [T-0047]
adrs: []
phase: 3
---

# T-0044 — GetMakerBySlug

## Scope

Public maker-profile page query (US-customer-0008), on the `ICatalogQueries` read-side established by T-0043.

- `ICatalogQueries.GetMakerBySlugAsync(slug, ct)` → `MakerProfile?`. Null when the slug doesn't resolve to a publicly-listable maker (same active + email-confirmed gate as the list — a hidden maker isn't probeable by slug).
- `MakerProfile` DTO: header (slug, company name, bio, legal form, city, verified, pickup toggle/note, rating stats) + `Products` (active products, newest first, each with its primary image) + `Reviews` (empty — deferred to T-0050; kept on the contract for forward-compat).
- `MakerProductItem` / `MakerReviewItem` DTOs.
- `GetMakerBySlug` AppServices query → `MakerNotFound` on null.
- `CatalogController` `GET /api/v1/catalog/makers/{slug}`.

Implementation notes:
- Products ordered by `Id` desc (ULID = time-ordered) rather than `CreatedAt` so the query stays SQLite-testable (SQLite can't ORDER BY a DateTimeOffset).
- Primary image = lowest `SortOrder` of the product's auto-included images.

## Acceptance criteria
- **AC-1** Returns bio + rating + active products for a publicly-listable maker.
- **AC-2** Soft-deleted products excluded from the product list.
- **AC-3** Unconfirmed / inactive maker → null → `maker.notFound` (not probeable).
- **AC-4** Reviews empty (forward-compatible until T-0050).
- **AC-5** 827 tests pass.

## Status log
- 2026-05-28 done. Build clean, 827 tests pass.
