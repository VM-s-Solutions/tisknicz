---
id: T-0041
title: Product entity + IProductRepository + Create/Update/Delete commands + image upload
status: done
size: L
owner: dotnet-backend
created: 2026-05-27
updated: 2026-05-27
depends_on: [T-0040, T-0042]
blocks: [T-0043, T-0045, T-0049]
adrs: [0003, 0011]
phase: 3
---

# T-0041 — Product entity + CRUD + image upload

## Scope

Per role/product.md + US-maker-0004. The catalog write path: a maker creates/edits/deletes products and uploads images through the backend to the `product-images` blob container.

### Domain (`Core.Domain/Products/`)
- `Product.cs` (`Auditable`) — `MakerId`, `CategoryId`, `Title`, `Description?`, price as `(PriceAmountMinor, PriceCurrency)` + `PriceType` enum, `WeightGrams`, owned `Images` collection. Invariants: price ≥ 0; free products require `PriceType.OnRequest`; currency immutable after creation; ≤10 images. `Create` / `Update` / `AddImage` / `RemoveImage` (compacts SortOrder).
- `ProductImage.cs` — owned entity (id + blob path + sort order).
- `PriceType.cs` — `Fixed | From | OnRequest`.
- `Validators/ImageUploadValidator.cs` — pure helper: size ≤5 MB, MIME allow-list (jpeg/png/webp), magic-byte sniff (don't trust `Content-Type`), `ExtensionFor`. ADR 0011 §"Uploads".
- `IProductRepository.cs` — `Add`, `GetByIdAsync` (tracked, loads images). IDOR doc: caller MUST check `MakerId`.
- `Money` referenced as `global::Makables.Core.Domain.Money.Money` because the type's short name clashes with the sibling namespace from inside `Core.Domain.Products`.

### Core.Domain.Common
- `BusinessErrorMessage.{ProductImageLimitReached, ProductImageNotFound, ProductPriceNegative, ProductFreeRequiresOnRequest, ProductCurrencyMismatch}` + `FileUnsupportedType`.

### Core.AppServices (`Features/Products/`)
- `CreateProduct` — resolves the owning maker from session (no makerId in command). Currency from the maker's tenant `CountryConfiguration.DefaultCurrencyCode` (never caller-supplied). Validates category active. Returns new id.
- `UpdateProduct` / `DeleteProduct` — IDOR shield: load by id, verify `MakerId` == session maker, else NotFound (ids not enumerable across makers). Delete is soft (`MarkDeactivated`).
- `AddProductImage` — attaches an already-uploaded blob path; image-cap pre-checked → `ProductImageLimitReached` Conflict.
- `RemoveProductImage` — removes from aggregate + best-effort blob delete (orphan blob on failure is logged, not fatal — the user-visible outcome already succeeded).

### Infra.Database
- `Configurations/ProductConfiguration.cs` — `products` table + owned `product_images` child table (cascade delete). Indexes on `maker_id` + `category_id` (the latter feeds the T-0043 catalog filter). `PriceType` stored as string.
- `Products/ProductRepository.cs` — tracked reads; owned images loaded with the principal.
- `Migrations/20260527213044_Products.cs`.

### Web hosts
- `Web.Maker/Controllers/ProductController.cs` — `[Authorize]` maker CRUD + `POST {productId}/images` multipart upload. Upload flow: validate (size/MIME/magic bytes) → stream to `{country}/products/{productId}/{ulid}.{ext}` in `product-images` → `AddProductImage` to attach. `[RequestSizeLimit]` caps the request body. `DELETE {productId}/images/{imageId}` → `RemoveProductImage`.
- `Web.Public/Controllers/ProductImageController.cs` — anonymous `GET /api/v1/files/products/{country}/{productId}/{filename}` streaming endpoint. `Cache-Control: public, max-age=86400` + strong ETag from the blob (ADR 0011 §"Caching"). All access server-mediated — no direct browser→blob links.

### DI
- `AddMakablesInfrastructure` registers `IProductRepository → ProductRepository` (scoped).

### Tests (+41 facts; 797 total = 715 unit + 82 integration)
- `Domain/Products/ProductTests.cs` — 11 facts (trim/normalise, price invariants, currency-immutable update, image add/cap/remove/compaction).
- `Domain/Products/ImageUploadValidatorTests.cs` — 13 facts (valid jpeg/png/webp, oversize, zero, unsupported type, magic-byte mismatch incl. spoofed jpeg-with-png-bytes + truncated webp header, ExtensionFor).
- `AppServices/Features/Products/CreateProductHandlerTests.cs` — 6 facts (Unauthorized, no-maker, inactive category, free-fixed rejection, tenant-currency happy path, IDOR-shield reflection pin).
- `AppServices/Features/Products/ProductMutationHandlerTests.cs` — 11 facts (Update/Delete/AddImage/RemoveImage ownership IDOR + happy paths + image-cap + best-effort-blob-delete-tolerance + unknown-image NotFound).

### Out of scope
- Catalog read queries (`GetPagedMakers` T-0043, `GetProductById` T-0045) — project rather than load the aggregate.
- Maker dashboard UI (T-0049).
- Azurite-backed integration test of the real multipart upload → blob round-trip (the validator + handler tests cover the logic; the SDK round-trip is deferred with T-0042's).
- Product reactivation + hard-delete/GDPR cascade of blobs (future tickets — soft-delete keeps images addressable).

## Acceptance criteria
- **AC-1** `CreateProduct` stores a product with `IsActive=true`, tenant currency, and the maker resolved from session; returns the new id so the frontend can upload images to it.
- **AC-2** Image upload rejects >5 MB or non-jpeg/png/webp with `file.tooLarge` / `file.unsupportedType`; a spoofed content-type (header bytes don't match) is rejected as `file.invalid` (magic-byte sniff).
- **AC-3** `UpdateProduct` persists changes; currency cannot change; another maker's product id returns NotFound.
- **AC-4** `DeleteProduct` soft-deletes (`IsActive=false`, audit stamped); the product leaves the public catalog (global query filter) but orders keep their FK.
- **AC-5** Images stored at `{country}/products/{productId}/{filename}` in `product-images`; cap of 10 enforced with `product.imageLimitReached`.
- **AC-6** Product images served only through the backend streaming endpoint (ADR 0011); public, day-cached, ETag'd.
- **AC-7** All mutation commands fail-closed on missing session and IDOR-shield by maker ownership.
- **AC-8** 797 tests pass (715 unit + 82 integration; +41 new).
- **AC-9** CLAUDE.md hygiene: no `SaveChangesAsync` in handlers; all error codes from `BusinessErrorMessage`; `Core.Domain` no third-party packages; blob I/O kept out of the UoW transaction (upload in controller, attach in command).

## Status log
- 2026-05-27 done. Build clean, 797 tests pass. Stacked on T-0042 (rebased onto 18965cb so BlobOperationFailed is available). Awaiting dual reviewer per workflow.
