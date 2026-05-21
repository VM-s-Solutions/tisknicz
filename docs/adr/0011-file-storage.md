---
id: 0011
title: File storage — Azure Blob; all access through the backend; no direct browser links
status: accepted
date: 2026-05-21
deciders: [Architect, user]
---

# 0011 — File storage

## Context

Makables stores three categories of files:
- **Product images** — public-facing catalog, must render fast.
- **Order attachments** — STL/3MF/PDF files customers upload with custom orders. Private; only the customer, the assigned maker, and admin should see them.
- **Invoices and other PDFs** (shipping labels, fee invoices) — private; only the relevant maker, customer, or admin should access.
- **Maker documents** (future: tax IDs, contracts) — private; only the maker and admin.

We need a storage strategy that is multi-country-safe, vendor-portable, and consistent with the "the .NET backend is the only data path" principle established by ADR 0007.

## Decision

### One Azure Blob Storage account, multiple containers

| Container | Visibility | Read access | Write access |
|---|---|---|---|
| `product-images` | Public read (CDN-cacheable) | Anyone | Backend only |
| `order-attachments` | Private | Backend only | Backend only |
| `invoices` | Private | Backend only | Backend only |
| `maker-documents` | Private | Backend only | Backend only |

Blob path convention: `{country_code}/{entity}/{entity_id}/{filename}` — e.g. `cz/orders/01HX.../label.pdf`. This keeps a country-prefix in every path so cross-country exports are trivial.

### All access through the backend — no direct browser → blob links

Even for the public `product-images` container, the backend serves the URL. Reasons:
- Uniform access control surface: revoking access is a code change in one place.
- Image transformations (responsive sizes, format conversion) can be added later via a backend image-proxy without changing storage layout.
- The frontend code doesn't know the underlying storage provider — swapping Azure for S3 is mechanical.

Endpoints (mounted on `Web.Public` for product images, on the audience-appropriate host for private files):

```
GET /api/public/files/products/{productId}/{filename}    # product image, no auth, ETag-cached
GET /api/customer/files/orders/{orderId}/attachments/{filename}  # auth required, ownership check
GET /api/customer/files/invoices/{invoiceId}             # auth required, ownership check
GET /api/maker/files/orders/{orderId}/attachments/{filename}     # auth required, maker ownership check
GET /api/maker/files/orders/{orderId}/label              # auth required, maker ownership check
GET /api/admin/files/...                                  # admin can access everything; audited
```

The backend reads the blob via `IBlobStorageClient`, streams the response, and sets appropriate `Cache-Control` headers.

### Uploads

Uploads go to the backend as `multipart/form-data` POST. The backend validates type + size + virus-scan-stub (post-MVP), stores in the appropriate container, returns the blob path (not a URL). Subsequent reads use the path-based endpoints above.

```
POST /api/customer/orders/{orderId}/attachments  # multipart upload
POST /api/maker/products/{productId}/images      # multipart upload
```

Server-side validation:
- Allowed MIME types per concern (e.g. order attachments: `image/jpeg`, `image/png`, `image/webp`, `application/pdf`, `model/stl`, `model/3mf`, `model/obj`).
- Max size per concern (order attachments: 10 MB; product images: 5 MB; invoices generated server-side).
- File magic-byte sniffing (don't trust the `Content-Type` header alone).
- Filename sanitization: strip path traversal, normalize Unicode, append a random suffix to prevent collisions.

### Caching

- `product-images`: backend sets `Cache-Control: public, max-age=86400` (1 day) plus a strong ETag. Frontend uses `next/image` which caches at the edge.
- Private files: `Cache-Control: private, no-store`. No CDN caching.

### Multi-country

Blob paths begin with the country code. The Azure Blob Storage account is single-tenant for MVP; if we ever need country-specific data residency, we add per-country accounts and route via `CountryConfiguration` lookup (post-MVP).

### Image transformations (post-MVP, but designed for)

Backend image-proxy endpoint can transform on the fly:
```
GET /api/public/files/products/{productId}/{filename}?w=480&fmt=webp
```
Implementation deferred. The endpoint shape is reserved.

## Alternatives considered

- **Public product-images via direct Blob URLs + CDN; private files via SAS URLs** — rejected by user. Cleaner separation but loses uniform access control. Decided to spend CPU/bandwidth on backend serving in exchange for tighter control.
- **Azure CDN in front of `product-images`** — deferred. The backend can sit behind Azure Front Door if performance demands it; not at MVP scale.
- **AWS S3 / Cloudflare R2 instead of Azure Blob** — rejected. We're on Azure for the rest of the stack (ADR 0007); single-cloud reduces ops surface.
- **MinIO self-hosted** — rejected. Adds operational burden; Azure Blob is cheap and managed.
- **Store invoices in the database as `bytea`** — rejected. Postgres isn't a great BLOB store; backups balloon.

## Consequences

### Positive
- Uniform access control: every file path goes through a `[Authorize]` controller with ownership checks.
- Vendor portability: `IBlobStorageClient` interface; swap Azure for S3 = new adapter.
- Future-proof for image transforms, virus scanning, watermarking, audit logging on file access.
- No URL leakage risk: there are no long-lived URLs to leak.

### Negative
- Higher backend bandwidth than direct-from-storage. At MVP scale (< 10k files/day), trivial.
- Image proxy is a bottleneck if not cached. Backend must set strong cache headers.
- Backend code must stream blobs (don't buffer entire files in memory). Use `Stream`-based APIs (`Azure.Storage.Blobs` `OpenReadAsync`).

## Compliance / verification

- Reviewer checklist: file endpoints use `[Authorize]` and check resource ownership.
- Reviewer checklist: file upload endpoints validate MIME type AND size server-side AND sniff magic bytes.
- Reviewer checklist: no `BlobClient.GenerateSasUri` calls — there are no SAS URLs in MVP.
- Reviewer checklist: file streaming uses `Stream`-based APIs, not `byte[]`.
- SecOps: blob containers have public access set to None (private) except `product-images` which is public-blob (backend acts as cache barrier).
- Integration test: customer A cannot fetch customer B's order attachment.
- Integration test: maker A cannot fetch maker B's order label.

## Related
- Patterns: §A.15 provider adapter (`IBlobStorageClient`)
- ADR 0007 — Stack pivot (Azure Blob is the storage choice)
- ADR 0019 (planned) — Image proxy and transformations (post-MVP)
