---
id: T-0045
title: Product detail query GetProductById
status: done
size: S
owner: dotnet-backend
created: 2026-05-28
updated: 2026-05-28
depends_on: [T-0041, T-0043]
blocks: [T-0048]
adrs: []
phase: 3
---

# T-0045 — GetProductById

## Scope

Public product-detail query (US-customer-0009), on the `ICatalogQueries` read-side.

- `ICatalogQueries.GetProductByIdAsync(productId, ct)` → `ProductDetail?`. Null when the product is inactive OR its maker isn't publicly-listable (active + email-confirmed) — a hidden product isn't probeable by id.
- `ProductDetail` DTO: product fields (title, description, price, type, weight, category) + owning maker display info (id, slug, company name, verified — for the "by {maker}" link) + ordered `Images`.
- `ProductImageItem` DTO.
- `GetProductById` AppServices query → `ProductNotFound` on null.
- `CatalogController` `GET /api/v1/catalog/products/{productId}`.

## Acceptance criteria
- **AC-1** Returns product + images + maker display info for an active product under a listable maker.
- **AC-2** Soft-deleted product → null → `product.notFound`.
- **AC-3** Product under an unconfirmed/inactive maker → null (not probeable).
- **AC-4** Images ordered by sort order.
- **AC-5** 827 tests pass.

## Status log
- 2026-05-28 done. Build clean, 827 tests pass.
