---
role: OrderAttachments
kind: aggregate-child + application-service
status: accepted
---

# OrderAttachments

## Responsibility

Hold the optional files the customer attaches to an order — reference sketches, spec sheets, photos of the source object — and make them readable to the assigned maker so they can ship the right item. Owns the upload endpoint on `Web.Customer`, the streaming download endpoint on **both** `Web.Customer` (uploader review) and `Web.Maker` (maker reads specs before shipping), the magic-byte-sniffed validator, and the `OrderAttachment` child entity on the `Order` aggregate. Per T-0064 / US-customer-0010 AC-1 + US-maker-0010.

## Collaborators

- **Order** (asks: `AllowsAttachmentUpload()` state gate, `Attachments.Count` against `MaxAttachmentCount = 10`, `CountryCode` for the blob path; via `IOrderRepository.GetByIdForCustomerAsync` + the aggregate's own `AddAttachment` method)
- **UserSessionProvider** (asks: the authenticated customer / maker id; IDOR-safe — never trusts request body for the caller identity)
- **MakerRepository** (asks: the maker id for the session user on the maker host — `GetByUserIdAsync`. Customer host doesn't touch this)
- **BlobStorage** (asks: upload to / download from `BlobContainer.OrderAttachments` at path `{country}/orders/{orderId}/{ulid}.{ext}`; surface delete on handler failure for cleanup)
- **IdGenerator** (asks: a new entity id for the `OrderAttachment` row + a fresh ulid for the blob filename; never `Guid.NewGuid()` directly)
- **Clock** (asks: the upload timestamp returned in `Response.UploadedOn`; ensures test-time pin)
- **OrderAttachmentValidator** (asks: per-call validation of size + MIME + magic bytes against the four-format allow-list; `ExtensionFor` for the canonical extension)
- **Logger** (asks: one structured `LogInformation` per successful upload — no PII, no filename, no blob path)

## Knows

- The four-format MIME allow-list: `application/pdf`, `image/jpeg`, `image/png`, `image/webp`. STL / 3MF / OBJ are explicitly out of scope at MVP (no production magic-byte sniff for them; no current customer story requires them; ADR 0011 §"Uploads" allow-list-per-concern policy applies)
- The 10 MiB per-file cap and the 10-files-per-order count cap (parallel to `Product.MaxImageCount` per the existing precedent)
- The four states that allow new uploads: `PendingPayment | Paid | Accepted`. After `Shipped` the snapshot freezes (the maker has committed to the contents); after `Delivered | Completed | Cancelled | Refunded | Disputed` the order is finalised or dead
- The blob path scheme: `order-attachments/{country.ToLowerInvariant()}/orders/{orderId}/{ulid}.{ext}` — the original filename NEVER appears in the path (security: no path traversal, no Unicode normalisation surprises, no collision)
- The Content-Disposition contract: original filename (sanitized for display) used in the download headers so the browser saves with a recognisable name; `attachment` disposition forces download rather than inline render
- The cache contract: `Cache-Control: private, no-store` — order attachments are private files; no public CDN, no SAS URL (ADR 0011 explicitly rejects SAS at line 86-90)
- The two-host download asymmetry: `Web.Customer` reads via `GetAttachmentForCustomerAsync` (scoped by `customerUserId`); `Web.Maker` reads via `GetAttachmentForMakerAsync` (scoped by `makerId`). Same handler-less controller pattern, different scoping
- The three new `BusinessErrorMessage` codes it owns: `OrderAttachmentLimitReached`, `OrderStateForbidsAttachment`, `OrderAttachmentNotFound`. Reuses the existing `FileInvalid` / `FileTooLarge` / `FileUnsupportedType` from T-0049

## Does NOT know

- How `Order` itself was created (T-0063 territory; the attachment endpoint only attaches to an existing order)
- How the order moves through its state machine (T-0066 / T-0067 / T-0071 / T-0072 / T-0076 / T-0083 own the transitions; `AllowsAttachmentUpload` only reads the current state)
- How invoices, payouts, or refunds work — they don't touch attachments
- How to scan attachments for malware — virus-scan-stub is deferred per ADR 0011 §"Uploads" ("post-MVP"); no clamd integration in T-0064
- How to render the customer dashboard or the maker order-detail UI — that's T-0099 / T-0118
- How to delete attachments — append-only at MVP per the secondary default in the ticket; no `DELETE /attachments/{id}` endpoint
- How to surface attachments to admin — T-0118 admin queries hit the same `order_attachments` table via `IOrderRepository.Unscoped` when the time comes

## Lifecycle

- **Created by:** the customer via `POST /api/v1/orders/{orderId}/attachments` on `Web.Customer`. The maker has no upload right at MVP — Q3 user decision codified the "customer attaches specs; maker reads specs" intent. If a future story adds proof-of-shipment photo upload by the maker, it's a separate command, not a T-0064 amendment
- **Persisted by:** `Order.AddAttachment(attachment)` mutates the aggregate; `UnitOfWorkPipelineBehavior` commits. The handler never calls `SaveChangesAsync` directly
- **Destroyed by:** soft-delete on the `Order` aggregate hides the attachment row via the global query filter on `Auditable` (ADR 0013). The only hard-delete path is GDPR right-to-erasure (T-0110), which uses `ON DELETE CASCADE` on the FK to wipe attachment rows alongside the `Order`. There is intentionally no individual-attachment delete endpoint

## Steps (the 12-step controller flow)

The flow lives mostly in `OrdersController.UploadAttachment` because of the multipart binding; the handler is a thin layer that re-checks invariants under the UoW transaction.

1. **Null / empty file guard.** `file is null || file.Length == 0` → `400 file.invalid`. ASP.NET Core returns null `IFormFile` if the multipart body is malformed; we treat both as the same user error
2. **Resolve customer identity.** `session.GetUserId()`; null → `401 auth.required`. Backstop guard — the host's `[Authorize]` + `RequireEmailConfirmedMiddleware` should have returned 401/403 already
3. **Load order with ownership pre-check.** `orders.GetByIdForCustomerAsync(orderId, userId, ct)`; null → `404 order.notFound`. **404 not 403** — leak-resistant per the T-0063 AC-2 precedent. Loading the order in this step gives us the `CountryCode` and current state for the next two checks without an extra round-trip
4. **State gate (controller fast-path).** `order.AllowsAttachmentUpload()`; false → `409 order.stateForbidsAttachment`. The aggregate's `AddAttachment` will re-check this under the transaction, but failing fast in the controller avoids buffering 10 MiB of bytes when we already know we'll reject
5. **Count gate (controller fast-path).** `order.Attachments.Count >= Order.MaxAttachmentCount` → `409 order.attachmentLimitReached`. Same race-defence reasoning as step 4
6. **Read header bytes.** `await ReadAtLeastAsync(file.OpenReadStream(), header, RequiredHeaderBytes)`. We need 12 bytes for the WebP signature (the longest of the four); fewer means the file is truncated and fails magic-byte sniff naturally
7. **Validate via `OrderAttachmentValidator`.** Switch on the `Result` enum: `TooLarge` → `400 file.tooLarge`; `UnsupportedType` → `400 file.unsupportedType`; `MagicByteMismatch` → `400 file.invalid`. The validator owns the only place where MIME + size + magic-byte decisions are made
8. **Compose the blob path.** `{order.CountryCode.ToLowerInvariant()}/orders/{orderId}/{ids.Next()}.{ext}` where `ext = OrderAttachmentValidator.ExtensionFor(file.ContentType)`. Original filename is sanitized separately (step 8b) and stored on the row — never in the path
9. **Upload to blob storage.** `blobs.UploadAsync(BlobContainer.OrderAttachments, blobPath, stream, file.ContentType, ct)`; failure → return the typed `BusinessResult.Failure` verbatim
10. **Dispatch the command.** `Mediator.Send(new AddOrderAttachment.Command(orderId, blobPath, sanitizedOriginal, file.ContentType, file.Length))`. The handler runs the same state + count gates under the UoW transaction (race-defence), builds the `OrderAttachment.Create` aggregate child, and `order.AddAttachment` mutates the order
11. **Cleanup on handler failure.** If the handler returns failure (typically a count-race loss), `blobs.DeleteAsync` removes the orphaned blob. The pattern mirrors `ProductController.UploadImage` step 11 — we never leave a blob hanging without a row
12. **Return 200.** `UploadOrderAttachmentResponse(attachmentId, originalFilename, sizeBytes, uploadedOn)` — the four fields the frontend needs to render the attachment row in the order detail page

## Invariants

- **Magic bytes are required for all four allowed types.** Declared MIME alone is never trusted (an attacker can send `Content-Type: image/png` with an `.exe` body; sniffing is what makes the allow-list real)
- **State gate is enforced THREE times.** Controller fast-path, handler re-check under transaction, aggregate `AddAttachment` final guard. Defence-in-depth against the optimistic-controller race
- **Count gate is enforced THREE times.** Same three layers as the state gate. Two concurrent uploads at count=9 are guaranteed to leave count=10 after exactly one succeeds; the loser surfaces `OrderAttachmentLimitReached`
- **Original filename is NEVER in the blob path.** Only the validator-issued ULID + extension. The original is stored on the row for `Content-Disposition` only
- **`Cache-Control: private, no-store`** on every download response — order attachments are not cacheable by any intermediary; a logged-out client must miss the cache
- **`ON DELETE CASCADE`** on the FK is intentional and matches the GDPR T-0110 contract. Soft-delete on `Order` does NOT cascade (the global query filter on `Auditable` hides both rows)

## Cross-references

- **ADR 0011** §"Uploads" — overall storage rules: backend streaming only (no SAS URLs); magic-byte sniff required; allow-list per concern; original filename sanitization
- **ADR 0013** §"Data scoping" — `ForCustomer` / `ForMaker` scoped repository methods + the leak-resistant 404-on-IDOR contract
- **ADR 0005** §"Per-audience hosts" — separate `OrdersController` on `Web.Customer` (upload + download) and `Web.Maker` (download only). Audience binding via `AddMakablesAuth` prevents a customer JWT from reaching the maker endpoint and vice versa
- **ADR 0015** §"Responsibility-Driven Design" — this role file itself; every aggregate-child + application-service surface needs one
- **Order role doc** — `Order` owns the `Attachments` collection + `MaxAttachmentCount` + `AllowsAttachmentUpload()` + `AddAttachment`. This doc covers the attachment surface; `order.md` covers the parent aggregate

## Implementation pointer

- `backend/src/Makables.Core.Domain/Orders/OrderAttachment.cs` — sealed `Auditable` child entity + `Create` factory (defence-in-depth: validates inputs even though the controller validated upstream)
- `backend/src/Makables.Core.Domain/Orders/Order.cs` — `Attachments` collection + `MaxAttachmentCount` + `AddAttachment` + `AllowsAttachmentUpload`
- `backend/src/Makables.Core.Domain/Orders/Validators/OrderAttachmentValidator.cs` — `internal static class` owning the allow-list + size cap + magic-byte signatures + `ExtensionFor`
- `backend/src/Makables.Core.AppServices/Features/Orders/AddOrderAttachment.cs` — Command / Response / Validator / Handler
- `backend/src/Makables.Web.Customer/Controllers/OrdersController.cs` — `UploadAttachment` + `DownloadAttachment` actions
- `backend/src/Makables.Web.Maker/Controllers/OrdersController.cs` — `DownloadAttachment` action (new controller; only this action for now)
- `backend/src/Makables.Infra.Database/Configurations/OrderAttachmentConfiguration.cs` — EF mapping with partial index `WHERE is_active`
- `backend/src/Makables.Infra.Database/Migrations/20260605152212_OrderAttachments.cs` — schema migration
