# T-0064 — Order attachments: upload endpoint + streaming download

**Phase:** 4 (Orders)
**Size:** M
**State:** `ready`
**Depends on:** T-0042 (`IBlobStorageClient`), T-0049a/049c (multipart upload + OpenAPI transformer precedent), T-0063 (`Order` + customer-host controller pattern), T-0033 (`Maker` for download scoping)
**Owner:** `dotnet-backend`
**ADRs:** 0011 (File storage), 0013 (Data scoping), 0014 (Audit)
**Stories:** US-customer-0010 AC-1 ("attachments (optional)"), US-maker-0010 (maker order detail surfaces customer-uploaded specs)
**Role doc:** [docs/architecture/roles/order-attachments.md](../architecture/roles/order-attachments.md) (to be added)

## Why now

T-0063 explicitly deferred attachments to T-0064 per user decision Q3: `CreateOrder` stays JSON-only and ships in `PendingPayment`; attachments are added afterward via a dedicated multipart endpoint. Until T-0064 lands:
- The customer order-placement UI (T-0099) cannot let the user attach the reference sketch / spec sheet that US-customer-0010 AC-1 lists as optional.
- The maker order-detail dashboard (US-maker-0010) cannot show the spec sheets the maker needs to read before clicking Accept → Ship.

T-0064 closes both gaps in one ticket — upload on customer host, download on **both** customer and maker hosts per user decision (see Status log).

## Scope

### User decisions captured upfront (research workflow + synthesis)

1. **MIME allow-list:** PDF + JPEG + PNG + WebP only. STL / 3MF / OBJ deferred to a follow-up ticket when 3D-printing makers are onboarded — they need bespoke magic-byte sniffs (STL has ASCII + binary variants; OBJ has none; 3MF is ZIP-with-manifest) and no current customer story requires them.
2. **Both hosts ship the download endpoint:** `Web.Customer` (customer reviews own uploads) **and** `Web.Maker` (maker reads specs before shipping). Same handler-less controller pattern; ADR-0013 scoping via `IOrderRepository.ForCustomer` / `ForMaker`. Splitting across tickets would block US-maker-0010.
3. **Storage shape: child entity.** New `order_attachments` table with FK to `orders.id`. Individual addressability for `/attachments/{attachmentId}`, granular soft-delete per ADR 0013, indexable for the admin "all attachments uploaded today" query (T-0118).
4. **State gate:** uploads allowed in `PendingPayment | Paid | Accepted` only. Forbid after `Shipped` (snapshot frozen for the maker), `Delivered | Completed` (finalised), `Cancelled | Refunded | Disputed` (dead). Returns `409 Conflict` with `order.stateForbidsAttachment`.

Secondary defaults baked in (not separately confirmed; safe to amend on review):
- **Attachment mutability:** append-only at MVP. No `DELETE /attachments/{id}` endpoint until a customer story requests it.
- **Customer download after final states:** allowed (audit history; the `ForCustomer` scoped queryable already excludes soft-deleted orders).
- **Maker upload:** **not in scope.** No `POST` on the maker host. If a future ticket adds maker-side document upload (e.g., proof-of-shipment photos), it's a separate command.

### Domain entity (`Core.Domain/Orders/OrderAttachment.cs`)

New sealed `Auditable` child entity. Belongs to `Order` aggregate (one-to-many).

```csharp
public sealed class OrderAttachment : Auditable
{
    public string Id { get; private set; } = default!;
    public string OrderId { get; private set; } = default!;
    public string BlobPath { get; private set; } = default!;        // {country}/orders/{orderId}/{ulid}.{ext}
    public string OriginalFilename { get; private set; } = default!; // sanitized, for Content-Disposition
    public string ContentType { get; private set; } = default!;     // validated MIME
    public long SizeBytes { get; private set; }
    public string UploadedByUserId { get; private set; } = default!;

    public static OrderAttachment Create(
        string id, string orderId, string blobPath, string originalFilename,
        string contentType, long sizeBytes, string uploadedByUserId, string countryCode);
}
```

`OrderAttachment.Create` validates: `Id`/`OrderId`/`BlobPath` non-empty; `SizeBytes > 0`; `ContentType` in the allow-list; `OriginalFilename` ≤ 255 chars. Throws `ArgumentException` on programmer-error inputs (per the existing `Order.Create` / `Product.Create` precedent).

### Order aggregate edit (`Core.Domain/Orders/Order.cs`)

Add private collection + accessor + invariant method:

```csharp
private readonly List<OrderAttachment> _attachments = new();
public IReadOnlyCollection<OrderAttachment> Attachments => _attachments;
public const int MaxAttachmentCount = 10;

public BusinessResult AddAttachment(OrderAttachment attachment)
{
    if (!AllowsAttachmentUpload())
        return BusinessResult.Failure(Error.Conflict("order", BusinessErrorMessage.OrderStateForbidsAttachment));
    if (_attachments.Count >= MaxAttachmentCount)
        return BusinessResult.Failure(Error.Conflict("attachments", BusinessErrorMessage.OrderAttachmentLimitReached));
    _attachments.Add(attachment);
    return BusinessResult.Success();
}

public bool AllowsAttachmentUpload() =>
    State is OrderState.PendingPayment or OrderState.Paid or OrderState.Accepted;
```

### Repository edit (`IOrderRepository` + `OrderRepository`)

Add `Task<OrderAttachment?> GetAttachmentForCustomerAsync(string orderId, string attachmentId, string customerUserId, CancellationToken ct)` and the maker analogue. Both compose `ForCustomer` / `ForMaker` scoping with `.Include(o => o.Attachments)` and return the matched child or `null`.

### Validator (`Core.Domain/Orders/Validators/OrderAttachmentValidator.cs`)

New parallel to `ImageUploadValidator.cs`. **Do not extend the image validator** — ADR 0011 §"Uploads" specifies "allowed MIME types per concern". Owns the allow-list + size cap + magic-byte sniff:

```csharp
public static class OrderAttachmentValidator
{
    public const long MaxSizeBytes = 10 * 1024 * 1024;          // 10 MiB
    public const int RequiredHeaderBytes = 12;                  // enough for all 4 formats
    public enum Result { Valid, TooLarge, UnsupportedType, MagicByteMismatch }

    public static Result Validate(string contentType, long sizeBytes, ReadOnlySpan<byte> header);
    public static string ExtensionFor(string contentType);  // canonical: ".pdf" / ".jpg" / ".png" / ".webp"
}
```

Magic-byte signatures:
- PDF: `25 50 44 46` (`%PDF`)
- JPEG: `FF D8 FF`
- PNG: `89 50 4E 47 0D 0A 1A 0A`
- WebP: `52 49 46 46 ?? ?? ?? ?? 57 45 42 50` (`RIFF****WEBP`)

Three of these already exist in `ImageUploadValidator.cs:60-79` — keep their copies in `OrderAttachmentValidator` (the role doc explicitly forbids cross-concern coupling); if a third site is added later, extract to `Core.Domain/Common/FileSignatures.cs`.

### CQRS feature (`Core.AppServices/Features/Orders/AddOrderAttachment.cs`)

Single static class with nested `Command`, `Response`, `Validator`, `Handler` per `patterns.md` §A.7. Mirrors `AddProductImage.cs` precedent.

```csharp
public sealed record Command(
    string OrderId,
    string BlobPath,
    string OriginalFilename,
    string ContentType,
    long SizeBytes) : ICommand<Response>;

public sealed record Response(
    string AttachmentId,
    string OriginalFilename,
    long SizeBytes,
    DateTimeOffset UploadedOn);
```

Handler (8-step):

1. Resolve `customerUserId` from `IUserSessionProvider`. Null → `Error.Unauthorized()`.
2. Load order via `orders.GetByIdForCustomerAsync(orderId, customerUserId, ct)`. Null → `Error.NotFound("orderId", OrderNotFound)`. **404 not 403** for IDOR resistance (per T-0063 AC-2 precedent).
3. Re-check `order.AllowsAttachmentUpload()` and `order.Attachments.Count < MaxAttachmentCount` under the UoW transaction (defence-in-depth — controller checks were optimistic).
4. Build `OrderAttachment.Create(...)` with a fresh id from `IIdGenerator`.
5. `order.AddAttachment(attachment)` — surfaces the state/count `BusinessResult.Failure` if step 3's race lost.
6. No explicit repository call — EF Core change-tracking persists the new child through the aggregate.
7. Pipeline behaviour commits.
8. Return `Response`.

### Upload controller (`Web.Customer/Controllers/OrdersController.cs` — extend, do not split)

Add the `UploadAttachment` action to the existing `OrdersController`. Pattern mirrors `ProductController.UploadImage` (`Maker/Controllers/ProductController.cs:157-245`):

```csharp
[HttpPost("{orderId}/attachments")]
[RequestSizeLimit(OrderAttachmentValidator.MaxSizeBytes + 4096)]
[ProducesResponseType(typeof(UploadOrderAttachmentResponse), StatusCodes.Status200OK)]
[ProducesResponseType(typeof(Error), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(Error), StatusCodes.Status403Forbidden)]
[ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
[ProducesResponseType(typeof(Error), StatusCodes.Status409Conflict)]
public async Task<IActionResult> UploadAttachment(
    string orderId, IFormFile file, CancellationToken ct);
```

Body (mirror `ProductController.UploadImage` `:157-245` step-by-step):

1. Null/empty guard → `400 file.invalid`.
2. Resolve `userId` from `IUserSessionProvider` → `401` if missing.
3. Order ownership pre-check via `orders.GetByIdForCustomerAsync(orderId, userId)` → `404` if null. Loads order so we can read `CountryCode` + state for the next two checks.
4. State gate via `order.AllowsAttachmentUpload()` → `409 order.stateForbidsAttachment`.
5. Count gate via `order.Attachments.Count >= Order.MaxAttachmentCount` → `409 order.attachmentLimitReached`.
6. Read header bytes, call `OrderAttachmentValidator.Validate`, switch on `Result` → `400 file.tooLarge | file.unsupportedType | file.invalid`.
7. Compute blob path: `{country}/orders/{orderId}/{ulid}.{ext}` where `{country} = order.CountryCode.ToLowerInvariant()`, `{ulid} = ids.Next()`, `{ext} = OrderAttachmentValidator.ExtensionFor(contentType)`.
8. Sanitize the **original filename** for display: strip path separators + control chars + truncate to 255 chars (the original is never used in the blob path; only displayed in `Content-Disposition` on download).
9. Upload to blob: `blobs.UploadAsync(BlobContainer.OrderAttachments, blobPath, stream, contentType, ct)` → on failure return `HandleResult(upload)`.
10. Dispatch `Mediator.Send(new AddOrderAttachment.Command(...))`.
11. On handler failure call `blobs.DeleteAsync(...)` to clean up the orphaned blob (mirror `ProductController.cs:228-232`).
12. Return 200 with `UploadOrderAttachmentResponse`.

### Customer download (`Web.Customer/Controllers/OrdersController.cs`)

Mirror `Web.Public/Controllers/ProductImageController.cs:30-64` — same `ETag` conditional-GET shape, **different cache policy**:

```csharp
[HttpGet("{orderId}/attachments/{attachmentId}")]
[ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status304NotModified)]
[ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(Error), StatusCodes.Status403Forbidden)]
[ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
public async Task<IActionResult> DownloadAttachment(
    string orderId, string attachmentId, CancellationToken ct);
```

Body:
1. Resolve `userId` → 401 if missing.
2. `orders.GetAttachmentForCustomerAsync(orderId, attachmentId, userId, ct)` → `404 order.attachmentNotFound` if null.
3. `blobs.DownloadAsync(BlobContainer.OrderAttachments, attachment.BlobPath, ct)` → `404 order.attachmentNotFound` if failed (covers the rare blob-deleted-but-row-remains case).
4. Set headers: `ETag = download.ETag`; `Cache-Control = "private, no-store"` (private files, never cached by intermediaries — DIFFERENT from `ProductImageController` which uses a public day-cache); `Content-Disposition: attachment; filename="<sanitized-original>"`.
5. Conditional-GET: if `If-None-Match` matches → dispose the stream + return `304 NoContent` (mirror `ProductImageController:54-61`).
6. Return `File(download.Content, download.ContentType, enableRangeProcessing: true)` — `enableRangeProcessing` supports resume for the larger PDFs.

### Maker download (`Web.Maker/Controllers/OrdersController.cs` — **new controller**)

Customer host shipped its `OrdersController` in T-0063; the maker host has no orders controller yet. T-0064 introduces it with the download endpoint only (no upload, no list — T-0081 ships the list). Same body as the customer download except:
- Uses `IOrderRepository.GetAttachmentForMakerAsync(orderId, attachmentId, makerId, ct)`.
- `makerId` resolved via `IMakerRepository.GetByUserIdAsync(userSession.GetUserId(), ct).Id`.
- Lives behind the maker-host `[Authorize]` + JWT-audience binding (no email-confirmed middleware needed — that's customer-host policy).

### Database migration

Add `order_attachments` table:

```sql
CREATE TABLE order_attachments (
    id              VARCHAR(40)  NOT NULL PRIMARY KEY,
    order_id        VARCHAR(40)  NOT NULL REFERENCES orders(id) ON DELETE CASCADE,
    blob_path       VARCHAR(500) NOT NULL,
    original_filename VARCHAR(255) NOT NULL,
    content_type    VARCHAR(127) NOT NULL,
    size_bytes      BIGINT       NOT NULL,
    uploaded_by_user_id VARCHAR(40) NOT NULL,
    country_code    CHAR(2)      NOT NULL,
    is_active       BOOLEAN      NOT NULL DEFAULT TRUE,
    created_at      TIMESTAMPTZ  NOT NULL,
    created_by      VARCHAR(40)  NOT NULL,
    updated_at      TIMESTAMPTZ  NOT NULL,
    updated_by      VARCHAR(40)  NOT NULL,
    deactivated_at  TIMESTAMPTZ,
    deactivated_by  VARCHAR(40)
);

CREATE INDEX ix_order_attachments_order_id ON order_attachments(order_id) WHERE is_active;
```

`ON DELETE CASCADE` is fine because the only hard-delete path on `orders` is the GDPR right-to-erasure flow (T-0110), which intends to wipe attachments too. Soft-delete on `Order` doesn't touch the FK; the global query filter hides both rows.

EF mapping in `OrderAttachmentConfiguration.cs`; navigation collection from `Order` configured with `.HasMany(o => o.Attachments).WithOne().HasForeignKey("OrderId")`.

### New `BusinessErrorMessage` codes (`Core.Domain/Common/BusinessErrorMessage.cs`)

Under the existing `// === Order ===` block (from T-0060/T-0063):

- `OrderAttachmentLimitReached = "order.attachmentLimitReached"`
- `OrderStateForbidsAttachment = "order.stateForbidsAttachment"`
- `OrderAttachmentNotFound = "order.attachmentNotFound"`

Reuse existing `FileInvalid` / `FileTooLarge` / `FileUnsupportedType` for file-shape errors (already in `BusinessErrorMessage.cs` per T-0049). No new file-shape codes.

### Frontend i18n (`frontend/src/lib/i18n/cs-CZ.ts`)

Add three new keys parallel to the new BusinessErrorMessage codes. Draft Czech wording (PM/UX to review on PR):

```ts
'order.attachmentLimitReached': 'K této objednávce lze přiložit nejvýše 10 souborů.',
'order.stateForbidsAttachment': 'V tomto stavu objednávky již nelze přidávat přílohy.',
'order.attachmentNotFound': 'Tato příloha neexistuje nebo k ní nemáte přístup.',
```

(The existing `file.invalid` / `file.tooLarge` / `file.unsupportedType` keys from T-0049 are reused.)

### NSwag regen

Regenerate both customer-host and maker-host TypeScript clients. The T-0049c multipart transformer handles the `IFormFile` parameter automatically. CI parity check enforces this.

### Tests

#### Unit — `Makables.Tests/`

- `Domain/Orders/OrderAttachmentTests.cs` — `Create` factory: rejects empty id, blank filename, zero size, oversize, unallowed MIME. ~6 tests.
- `Domain/Orders/OrderAddAttachmentTests.cs` — `Order.AddAttachment`: succeeds in `PendingPayment | Paid | Accepted`; returns `OrderStateForbidsAttachment` in `Shipped | Delivered | Completed | Cancelled | Refunded | Disputed`; returns `OrderAttachmentLimitReached` when count == 10. ~10 tests (one per illegal state + boundary).
- `Domain/Orders/OrderAttachmentValidatorTests.cs` — every MIME type happy path + 4 magic-byte mismatch cases + size boundary + unallowed type. ~10 tests.
- `AppServices/Features/Orders/AddOrderAttachmentHandlerTests.cs` — handler happy path + ownership 404 + state 409 + count 409 + race on concurrent count. NSubstitute. ~7 tests.

#### Integration — `Makables.IntegrationTests/Orders/` (uses T-0062 `PostgresHarness`)

- `OrderAttachmentUploadTests.cs`:
  - Happy-path multipart upload of a valid PDF (assert 200 + blob path persisted + `Content-Type` correct).
  - Happy-path upload of JPEG + PNG + WebP (one test, parametrised over MIME).
  - 400 on `application/zip`.
  - 400 on PDF content-type with JPEG magic bytes.
  - 400 on 0-byte file.
  - 400 on file >10 MiB (drives via the `[RequestSizeLimit]` boundary).
  - 409 on 11th attachment.
  - 409 on upload to `Shipped` order.
  - 404 on upload to other customer's order.
  - 401 on no JWT.
  - 403 on JWT without email-confirmed claim.
- `OrderAttachmentDownloadTests.cs`:
  - 200 streaming download with `Content-Type` + `Cache-Control: private, no-store` + `Content-Disposition` with original filename.
  - 304 on matching `If-None-Match`.
  - 404 on attachment owned by other customer.
  - 404 on attachment of soft-deleted order (global query filter).
  - Maker can download attachment on order assigned to them.
  - Maker gets 404 on attachment of order assigned to a different maker.

### Docs

- New role doc `docs/architecture/roles/order-attachments.md` per ADR 0015.
- Update `docs/architecture/patterns.md` if a new pattern (cross-host scoped download) emerges that's worth surfacing.

## Acceptance criteria

- **AC-1** `POST /api/v1/orders/{orderId}/attachments` exists on `Web.Customer`, accepts multipart `IFormFile file`, decorated with `[Authorize]` + `[RequestSizeLimit(OrderAttachmentValidator.MaxSizeBytes + 4096)]`. Returns `200` with `UploadOrderAttachmentResponse(AttachmentId, OriginalFilename, SizeBytes, UploadedOn)`.
- **AC-2** Calling upload without a JWT returns `401`. Calling with a customer JWT whose `sub` doesn't match `order.CustomerUserId` returns `404` (leak-resistant, per T-0063 AC-2 precedent). A maker-audience JWT is rejected by `AddMakablesAuth` audience binding.
- **AC-3** Authenticated customer without an `email_confirmed_at` claim is rejected by `RequireEmailConfirmedMiddleware` before reaching the controller.
- **AC-4** Upload to an order in `Shipped | Delivered | Completed | Cancelled | Refunded | Disputed` returns `409` with `order.stateForbidsAttachment`.
- **AC-5** Upload of a file with `Content-Type: application/pdf` but JPEG magic bytes returns `400` with `file.invalid`. Upload of `application/zip` returns `400` with `file.unsupportedType`. Upload of a valid PDF/JPEG/PNG/WebP with matching magic bytes succeeds.
- **AC-6** Upload of a file >10 MiB returns `400` with `file.tooLarge`. Upload of a 0-byte file returns `400` with `file.invalid`. The `[RequestSizeLimit]` attribute enforces the cap at the ASP.NET layer.
- **AC-7** Upload of an 11th attachment returns `409` with `order.attachmentLimitReached`. Both the controller fast-path check and the handler under-transaction check are present (race defence).
- **AC-8** Successful uploads land at `order-attachments/{country}/orders/{orderId}/{ulid}.{ext}` where `{country}` is `order.CountryCode.ToLowerInvariant()`, `{ulid}` is fresh from `IIdGenerator`, `{ext}` from `OrderAttachmentValidator.ExtensionFor(contentType)`. The original filename is stored on the `OrderAttachment` row (for `Content-Disposition`) but never appears in the blob path.
- **AC-9** If the blob upload succeeds but the `AddOrderAttachment` handler returns failure, the controller calls `IBlobStorageClient.DeleteAsync` to clean up the orphan (mirrors `ProductController.cs:228-232`).
- **AC-10** `GET /api/v1/orders/{orderId}/attachments/{attachmentId}` returns `200` with: `Content-Type` from the stored attachment; `Content-Length` from `BlobDownload`; `ETag` from blob metadata; `Cache-Control: private, no-store`; `Content-Disposition: attachment; filename="<original>"`. Body is streamed via `File(stream, ct, enableRangeProcessing: true)`.
- **AC-11** A download request with `If-None-Match` matching the current `ETag` returns `304 Not Modified` with no body, and the blob stream is disposed before the response is written.
- **AC-12** Customer download by a non-owner customer returns `404`. Download of an attachment on a soft-deleted order returns `404`. Same `Web.Maker` endpoint exists with `IOrderRepository.GetAttachmentForMakerAsync` scoping — assigned maker gets `200`, non-assigned maker gets `404`.
- **AC-13** Architectural compliance: no `BlobClient.GenerateSasUri` anywhere (ADR 0011 reviewer checklist); no `Console.*` in any new file; `OrderAttachmentValidator` lives in `Core.Domain` with no third-party references; handler dispatches no `SaveChangesAsync` (UoW pipeline owns commit); all errors use `BusinessErrorMessage` constants. NSwag clients (customer + maker) regenerated in the same PR.
- **AC-14** i18n parity: three new Czech keys (`order.attachmentLimitReached`, `order.stateForbidsAttachment`, `order.attachmentNotFound`) added to `frontend/src/lib/i18n/cs-CZ.ts`, mapping 1:1 to the new `BusinessErrorMessage` codes. Draft wording in this ticket; PM/UX may refine on review.
- **AC-15** Test count: at least 33 new unit tests + 18 new integration tests. Build clean. Full suite passes (current `master` baseline post-T-0063 = 899 unit + 95 integration).

## Out of scope

- **Maker upload.** No `POST` on `Web.Maker`. If a future story adds proof-of-shipment photo upload, it's a separate command.
- **Attachment delete / replace.** Append-only at MVP. A `DELETE` endpoint lands when a story requires it.
- **STL / 3MF / OBJ MIME types.** Deferred to a follow-up ticket once 3D-printing makers are onboarded and bespoke magic-byte sniffs are researched.
- **Virus scanning.** Per ADR 0011 §"Uploads", "virus-scan-stub (post-MVP)". No clamd integration in T-0064.
- **SAS URLs / public CDN.** ADR 0011 explicitly rejects SAS URLs (`docs/adr/0011-file-storage.md:86-90, 111`). All reads go through the .NET process.
- **Per-attachment download counter / audit log.** No `attachment_downloads` table. The admin audit log (T-0118) captures explicit admin downloads only.

## Technical notes

### Why the customer host gets BOTH upload AND download

The customer placed the attachments; they need to verify uploads succeeded by re-downloading and viewing. Separate hosts could share download via a public CDN URL, but ADR 0011 ruled out SAS URLs — backend streaming is the only path.

### Why two parallel `GET` endpoints (customer + maker) instead of one shared

Audience binding per ADR 0005. A customer JWT cannot reach `Web.Maker`; a maker JWT cannot reach `Web.Customer`. The scoping repository is also different (`ForCustomer` vs `ForMaker`). A shared endpoint would need to detect the audience and pick the scope, which violates the per-host design. Two thin controllers sharing the same `BlobDownload` pattern are cheaper than the abstraction.

### Why `enableRangeProcessing: true`

Mirrors `ProductImageController.cs:63`. Supports HTTP Range requests so a partial PDF download can be resumed. Trivial to enable; useful for the 10 MiB upper bound.

### Why `Cache-Control: private, no-store` (not `public, max-age=86400` like product images)

Order attachments are private — leaked URLs must not be cached by intermediate proxies/CDNs. A logged-out user hitting the URL must miss the cache and 401. Product images are public catalog data; the day-cache there is correct. Attachments are not.

### Why `Content-Disposition: attachment` (force-download) not `inline`

Per UX expectation — the customer/maker uploaded a spec sheet, they want to download and read it. `inline` would surface PDFs in a browser tab (acceptable) but JPEGs would render as if they're hot-linked images, which is confusing for a "download attachment" UX. `attachment` is explicit + universal.

### Magic-byte signatures

PDF (`%PDF`) + JPEG (`FF D8 FF`) + PNG (`89 50 4E 47 0D 0A 1A 0A`) + WebP (`RIFF****WEBP`). Already implemented for JPEG/PNG/WebP in `ImageUploadValidator.cs:60-79`. PDF is the only new one. The PDF spec actually allows up to 1024 bytes of "header noise" before the `%PDF` marker (PDF spec §7.5.2), but in practice every real PDF starts with `%PDF` at offset 0; defer the "lenient sniff" to a follow-up only if user reports come in.

### Original filename sanitization

Strip:
- Path separators (`/`, `\`, `:`)
- Control characters (< 0x20)
- Leading/trailing whitespace
- Null bytes

Truncate to 255 chars (Windows path limit; matches the DB column). The sanitized version is stored. Unicode is preserved (`Příloha-2026.pdf` is a fine filename).

### Why the handler re-checks state + count

The controller checks are optimistic (no transaction). Two concurrent uploads could both pass the count check at the controller and both succeed in the handler. The handler re-check inside the UoW + the database-level state of `_attachments.Count` would catch this; the second one returns `OrderAttachmentLimitReached`.

### Maker download — why the maker host gets a new `OrdersController`

The customer host has its `OrdersController` from T-0063. The maker host has no orders controller yet (orders list is T-0081, accept is T-0071, ship is T-0072). T-0064 ships an empty-shell maker `OrdersController` with the single `DownloadAttachment` action; subsequent tickets add their actions to the same controller.

## Test plan

Inline above (see Scope > Tests).

## Status log

- 2026-06-05 `draft → ready` by PM. Expanded from INDEX row after T-0063 merged. Four user decisions captured upfront via a 5-reader research workflow + synthesis judge:
  - **MIME allow-list:** PDF + JPEG + PNG + WebP only at MVP. STL/3MF/OBJ deferred (no production magic-byte sniff exists for them; no current customer story requires them).
  - **Maker download:** ship in T-0064. Both `Web.Customer` (customer reviews own uploads) and `Web.Maker` (maker reads specs before shipping) get GET endpoints. Splitting would block US-maker-0010.
  - **Storage shape:** child entity (`order_attachments` table with FK to `orders.id`). Individual addressability, granular soft-delete per ADR 0013, indexable. Adds 1 migration.
  - **State gate:** `PendingPayment | Paid | Accepted` only. Forbid after `Shipped` (snapshot frozen for the maker), `Delivered | Completed` (finalised), `Cancelled | Refunded | Disputed` (dead). Returns `409` with `order.stateForbidsAttachment`.

  Three secondary defaults baked in (PM may revisit on review): attachment append-only at MVP (no DELETE endpoint); customer download allowed after final states (audit history); no maker upload capability (out of scope).

  Verified upfront: `BlobContainer.OrderAttachments` constant already exists at `Makables.Core.Domain/Storage/BlobContainer.cs:23` (T-0042); `BlobDownload` record + `IBlobStorageClient.DownloadAsync` available at `Makables.Core.Domain/Storage/IBlobStorageClient.cs:42-47, 70`; streaming + conditional-GET precedent at `Makables.Web.Public/Controllers/ProductImageController.cs:30-64` (different cache policy though — we want `private, no-store`); ADR 0011 explicitly rejects SAS URLs (`docs/adr/0011-file-storage.md:86-90, 111`); T-0049c multipart OpenAPI transformer handles bare `IFormFile` parameters automatically.
- 2026-06-05 done. `dotnet-backend` agent implemented per ticket. Reviewer pass requested changes on **M-1 (missing role doc per ADR 0015 / RDD parity)** plus 3 informational Lows + 5 Nits. Build clean; **1080 tests pass** in the first commit (966 unit + 114 integration; baseline T-0063 = 899 + 95 = 994; net +67 unit + 19 integration). Docker daemon up; the 17 new integration tests executed end-to-end against `postgres:16-alpine`.
  - **Four agent deviations** all confirmed sound by reviewer:
    1. `Order.Attachments` configured with `Navigation.AutoInclude()` — required for the count-gate at `Order.AddAttachment` and at the controller fast-path to read a populated collection. Mirrors `Product.Images` precedent.
    2. `[RequestSizeLimit]` test-environment quirk — `WebApplicationFactory` doesn't enforce `IHttpMaxRequestBodySizeFeature`; the validator's `Result.TooLarge` path covers it. Tests assert response is NOT 200.
    3. Customer-host 404 path returns `OrderAttachmentNotFound` for both `attachment-null` and `blob-download-failed` paths — same i18n surface.
    4. `AddOrderAttachment.Handler` takes `IClock` for test-time pinning of `UploadedOn`. Matches `IClock` discipline elsewhere in the codebase.
  - **M-1 (folded in this commit) — Role doc created.** Added `docs/architecture/roles/order-attachments.md` per ADR 0015 RDD parity. Covers responsibilities, 10 collaborators, what the role knows + does NOT know, lifecycle (creation / persistence / destruction), the 12-step controller flow, 6 invariants (3-layer state-gate, 3-layer count-gate, no original filename in blob path, `Cache-Control: private, no-store`, FK cascade vs soft-delete), and cross-references to ADRs 0005 / 0011 / 0013 / 0015 + the `Order` role doc.
  - **N-3 (folded in this commit) — `OrderAttachment.Create` factory tightened.** The ticket spec at §"Domain entity" required `ContentType` allow-list validation in the factory; the agent's original implementation deferred that to the controller's `OrderAttachmentValidator` only. Tightened the factory to additionally reject any content type not in `OrderAttachmentValidator.AllowedContentTypes` (defence-in-depth: if a future non-controller caller — cron / Functions / direct handler — tries to smuggle a disallowed type past the storage layer, the aggregate child rejects it). Updated the factory XML doc to explain the layered validation seam. **Added 2 new `[Theory]`s** at `OrderAttachmentTests.cs`: `Create_rejects_content_types_outside_the_allow_list` (6 disallowed cases: `application/zip`, `text/plain`, `application/octet-stream`, `model/stl`, `image/gif`, `video/mp4`) and `Create_accepts_every_content_type_in_the_allow_list` (5 allowed cases including `APPLICATION/PDF` to pin the case-insensitive match).
  - **Three Lows + 4 remaining Nits** noted by the reviewer, deferred as informational only:
    - **L-1** — withdrawn by the reviewer (no actual leak; rechecked).
    - **L-2** — `Cache-Control: private, no-store` + `ETag`/304 logic is mildly contradictory (`no-store` says don't cache, 304 only makes sense with a cache). On-spec per ticket §"Why Cache-Control"; revisit only if bandwidth metrics demand it.
    - **L-3** — NSwag emits `attachmentsPOST` / `attachmentsGET` on customer + `attachments` on maker. Surface asymmetry the frontend ticket (T-0099) will absorb.
    - **N-1** — `.ToLowerInvariant()` in `MagicBytesMatch` is redundant (allow-list uses `OrdinalIgnoreCase`). Defensive; left as-is.
    - **N-2** — Sanitized-filename fallback `"attachment"` could append the validated extension for a better UX. Polish, deferred.
    - **N-4** — Index covers count-gate materialisation; comment is fine.
    - **N-5** — Reviewer approved the Czech wording. No change.
  - Build clean. **1091 tests pass** (977 unit + 114 integration; +11 unit from the two new N-3 theories). Reviewer ready to APPROVE after M-1; both M-1 and N-3 now folded.
