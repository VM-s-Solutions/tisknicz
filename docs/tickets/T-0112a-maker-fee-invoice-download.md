---
id: T-0112a
title: Maker fee-invoice PDF download endpoint
status: ready
size: S
owner: dotnet-backend
created: 2026-06-13
updated: 2026-06-13
depends_on: [T-0102b, T-0088]
blocks: [T-0116]
user_stories: [US-maker-0013]
adrs: [0009, 0011, 0013, 0014, 0025]
phase: 5
manual_steps: []
security_touching: true
layers: [domain, infra-database, web-maker]
---

# T-0112a — Maker fee-invoice PDF download endpoint

## Context

T-0112a closes the **one gap T-0088 left open** for Fee invoices. T-0088 shipped invoice-PDF streaming on both audience hosts, but its lookup chain is `GetByOrderIdAsync` / `GetByOrderIdReadOnlyAsync` — which **only ever returns `InvoiceType.Customer` invoices** (T-0088 §"Out of scope": *"Fee-invoice (payout batch) downloads — `GetByOrderIdAsync` only ever returns Customer invoices"*). A maker who receives a weekly payout has a **Fee invoice** (`InvoiceType.Fee`, linked via `Invoice.PayoutBatchId`, recipient = the maker) that their accountant must book as a platform-fee expense — and there is **no route a maker can call to download it**. US-maker-0013 names the exact route the future T-0116 payout UI will hit: `GET /api/v1/maker/files/invoices/{invoiceId}`. This ticket makes that route real.

This is the BA-discovered backend slice that unblocks **T-0116** (the maker `/dashboard/maker/vyplaty` payout list + drill-into-batch UI, which renders a "Stáhnout fakturu" CTA per Fee invoice). It rides the same NSwag regen commit as **T-0112** (the maker payout queries) — both land on the maker host client in one PR.

The precedent is **T-0088** verbatim: controller-direct blob streaming per ADR 0014 §"Handler-free read paths" (no MediatR feature for a passthrough read with no validation rule and no transaction), `private, no-store` + `Content-Disposition` + ETag/304 per the T-0064 PII policy family, `File(stream, "application/pdf", enableRangeProcessing: false)`. T-0112a differs from T-0088's order-invoice action in exactly **one dimension**: the lookup is **invoice-id-scoped to maker ownership** (the resource key is `invoiceId`, not `orderId`), and the result **MUST be `InvoiceType.Fee`** — a maker pointing this route at a Customer-invoice id (theirs or anyone's) gets a `404`, indistinguishable from a nonexistent id.

The endpoint slots into the **existing** `Web.Maker/Controllers/FilesController.cs` (`[Route("api/v{version:apiVersion}/maker/files")]`, created by T-0075 for the label download). The action route is `[HttpGet("invoices/{invoiceId}")]` → `GET /api/v1/maker/files/invoices/{invoiceId}` — the literal route US-maker-0013 AC-1 names. No new controller.

The IDOR shield is **the lookup predicate itself**. `IInvoiceRepository.GetByIdForMakerAsync` already filters `i.Id == invoiceId && i.MakerId == makerId` against the denormalised `Invoice.MakerId` column (populated for **both** invoice families — see `Invoice.cs:88-95`: *"for Fee invoices it is the target maker of the payout batch"*). That predicate already returns Fee invoices today; the only addition T-0112a needs is a **read-only (`AsNoTracking`) variant** of it per ADR 0025 (this action never mutates the aggregate) plus the `InvoiceType.Fee` gate. No change to the `ForMaker` **queryable** is required for a single-id download — the `:57` TODO on `IInvoiceRepository.ForMaker` is about surfacing Fee invoices in the **maker "my invoices" list**, which is T-0112/T-0116 territory, not this single-resource download. T-0112a leaves that TODO standing.

No new `BusinessErrorMessage` codes, no migrations, no outbox events, no i18n keys. `OrderNotFound` ("not found / not yours" IDOR-resistant 404) and `InvoiceNotYetRendered` ("no row, or row with `PdfBlobPath` still null") are both reused verbatim from T-0088/T-0069 — both already carry cross-stack i18n parity.

## Locked design decisions

Captured per `docs/process/deliberation.md`. The user locked the reversibility + UI-surface dimensions in the 2026-06-13 payout-completion deliberation (Q1–Q5); the streaming mechanics are ADR-locked (precedent T-0088) and PM-absorbed.

### A. User-locked (2026-06-13 deliberation) — non-negotiable

1. **CSV is NEVER served to makers.** The operator bank file (CSV) carries every maker's account number — cross-maker PII. Makers download only their own **Fee invoice PDF** (Q4). This endpoint serves PDFs only; there is no CSV path here.
2. **Completion is financially terminal — Fee invoices are immutable.** Per the reversibility lock (no un-complete; errata corrected forward via T-0105/T-0107). T-0112a is a pure read against the already-issued, immutable Fee invoice (ADR 0009); it never writes.

### B. ADR-locked (no relitigation)

- **ADR 0014 §"Handler-free read paths" — controller-direct, NO MediatR feature.** T-0088 shipped controller-direct; T-0075's sibling label download in this very `FilesController` is controller-direct; a passthrough blob read with no validation rule and no transaction gains nothing from handler ceremony. T-0112a is controller-direct, beside `GetShippingLabel`.
- **ADR 0013 (per-audience JWT + scoped reads).** Maker resolved from the session principal (`session.GetUserId()` → `IMakerRepository.GetByUserIdAsync`), never from a request param. The invoice lookup is scoped to `MakerId`. Cross-maker probes surface as `404 order.notFound` — same shape as nonexistent, no IDOR oracle.
- **ADR 0025 (read-only repository variants).** The lookup only inspects `Type` + `PdfBlobPath` + `InvoiceNumber`; use a `AsNoTracking` variant. `GetByIdForMakerAsync` is tracked; T-0112a adds the read-only mirror.
- **ADR 0009 (invoicing).** Fee invoices are immutable legal records; `PdfBlobPath` is set-once by T-0102b's artifact pipeline. T-0112a never writes.
- **ADR 0011 (file storage).** All blob access through the backend; no direct browser → blob links; `Invoice.PdfBlobPath` used verbatim against `BlobContainer.Invoices`.
- **No `SaveChangesAsync` anywhere** (no mutation, no UoW commit point).
- **`BusinessErrorMessage` reuse:** `OrderNotFound` + `InvoiceNotYetRendered`. No new codes.

### C. PM-absorbed (no user input needed)

- **Route = the literal US-maker-0013 AC-1 route on the existing controller.** `GET /api/v1/maker/files/invoices/{invoiceId}`. Action `[HttpGet("invoices/{invoiceId}")]` on `FilesController` (`[Route("api/v{version:apiVersion}/maker/files")]`). Audience = host (per ADR 0013). T-0116 consumes the NSwag-generated file-download method.
- **Lookup chain:** (1) `session.GetUserId()` → null → `401 auth.required`; (2) `makers.GetByUserIdAsync(userId, ct)` → null → `404 order.notFound` (maker-audience token with no maker row; invoice repo never touched — T-0088 AC-6 parity); (3) `invoices.GetForMakerReadOnlyAsync(invoiceId, maker.Id, ct)` (NEW read-only mirror of `GetByIdForMakerAsync`) → null → `404 order.notFound` (unknown OR cross-maker id — no IDOR oracle); (4) `invoice.Type != InvoiceType.Fee` → `404 order.notFound` (a maker pointing this Fee route at a Customer-invoice id gets the same not-found shape; the Customer-invoice download path is T-0088's order-scoped route, not this one); (5) `invoice.PdfBlobPath` null → `404 invoice.notYetRendered` (T-0102b artifact pipeline hasn't rendered yet); (6) `blobs.DownloadAsync(BlobContainer.Invoices, invoice.PdfBlobPath, ct)` failure (blob-purged race) → `404 invoice.notYetRendered`; (7) stream.
- **`InvoiceType.Fee` gate placement:** the type check is its own step AFTER the ownership-scoped load and returns `order.notFound` (NOT `invoice.notYetRendered`) — a wrong-type id is "no such Fee invoice for you", not "yours but not rendered". This keeps the route single-purpose (Fee only) per US-maker-0013's title without leaking that a Customer invoice with that id exists.
- **Headers:** `Content-Disposition: attachment; filename="faktura-{InvoiceNumber}.pdf"` (run through the file-local `EscapeFilenameForHeader` helper — defensive even though numbers are platform-generated `FV-CZ-NNNNNNNN`); `Cache-Control: private, no-store` + ETag / `If-None-Match` → `304` — mirror T-0064/T-0088 exactly (Fee invoices carry recipient PII: maker name, IČO/DIČ, address; a logged-out request must miss every cache and 401). NOT the label's `public, immutable` policy.
- **`File(stream, "application/pdf", enableRangeProcessing: false)`** — invoices are ≤ ~100 KB platform artifacts; range support is attack surface for zero benefit (T-0075/T-0088 rationale). Reuse the file-local `EscapeFilenameForHeader` + `ETagMatches` helpers already on `OrdersController` (copy them into `FilesController` if not already present, or factor — implementer's call; do NOT introduce a shared static utility class for two 5-line helpers).
- **Repository surface delta:** ONE new method on `IInvoiceRepository` — `Task<Invoice?> GetForMakerReadOnlyAsync(string invoiceId, string makerId, CancellationToken ct)`. Body = `GetByIdForMakerAsync` with `.AsNoTracking()` added. Returns null for unknown / cross-maker ids (IDOR shield). Returns BOTH families (the `Type` gate lives in the controller, not the repo — the repo stays type-agnostic so a future admin/customer reuse isn't blocked). The `ForMaker` **queryable** and its `:57` TODO are left untouched (list surfacing is T-0112/T-0116).
- **`Invoice.PdfBlobPath` used verbatim** against `BlobContainer.Invoices` (container-relative; precedent `IEmailSendService.cs:425`, T-0088). No path construction in the controller.
- **No new wrapper/response records** — the 200 response is a binary file; NSwag generates a `FileResponse`-returning client method (T-0088's order-invoice action already proved file-response generation on the maker client). No schema-collision concern, no globally-unique-response-name concern (no JSON body).
- **NSwag regen:** maker host only. **Rides T-0112's regen commit** in the same PR.
- **No migrations, no outbox, no i18n keys** (`order.notFound` + `invoice.notYetRendered` already carry cross-stack parity).

## Scope

### Domain layer

- **`Core.Domain/Invoices/IInvoiceRepository.cs`** — add ONE method directly below `GetByIdForMakerAsync` (line 96):
  ```csharp
  Task<Invoice?> GetForMakerReadOnlyAsync(string invoiceId, string makerId, CancellationToken cancellationToken);
  ```
  XML doc mirrors `GetByIdForMakerAsync` + the read-only note: `AsNoTracking` per ADR 0025, IDOR-shielded null for unknown / cross-maker ids, surfaces BOTH invoice families (the caller applies the `InvoiceType.Fee` gate), backs T-0112a's controller-direct Fee-invoice download which only inspects `Type` + `PdfBlobPath` + `InvoiceNumber` and never mutates. Reference the analogous T-0088 `GetByOrderIdReadOnlyAsync` read-only-mirror precedent. **Do NOT touch the `ForMaker` `:57` TODO** — single-id download does not need the list-surfacing extension; note in the doc that the queryable's Fee gap stays for T-0112/T-0116.

### Infrastructure / Database layer

- **`Infra.Database/Invoices/InvoiceRepository.cs`** — implement `GetForMakerReadOnlyAsync`: copy the `GetByIdForMakerAsync` body (lines 78-90) with `.AsNoTracking()` prepended to the `db.Set<Invoice>()` chain. Same `i.Id == invoiceId && i.MakerId == makerId` predicate (the IDOR shield), same null-guard on blank ids, same global soft-delete filter (no `IgnoreQueryFilters`). Comment: "read-only mirror of GetByIdForMakerAsync — T-0112a Fee-invoice download only reads; ADR 0025 §Performance item 2".

### Web.Maker host

- **`Web.Maker/Controllers/FilesController.cs`** — new action below `GetShippingLabel`:
  - `[HttpGet("invoices/{invoiceId}")]` → resolves to `GET /api/v1/maker/files/invoices/{invoiceId}`.
  - `[ProducesResponseType(StatusCodes.Status200OK)]`, `[ProducesResponseType(StatusCodes.Status304NotModified)]`, plus typed `Error` `401` / `404`.
  - Controller ctor gains `IInvoiceRepository invoices` (already registered in DI by T-0068a).
  - Body per §C lookup chain + headers. Add the file-local `EscapeFilenameForHeader` + `ETagMatches` helpers if not already on `FilesController` (mirror `OrdersController:315-329`).

### Tests

#### Unit (~6, NSubstitute, mirror `OrdersControllerInvoiceDownloadTests` (maker) harness shape)

`backend/src/Makables.Tests/Web/Maker/Controllers/FilesControllerInvoiceDownloadTests.cs`:
1. `DownloadFeeInvoice_NoSession_Returns401` — `GetUserId()` null → `401`; `IMakerRepository` + `IInvoiceRepository` + `IBlobStorageClient` not called.
2. `DownloadFeeInvoice_UserWithoutMakerRow_Returns404_OrderNotFound` — `GetByUserIdAsync` null → `404 order.notFound`; invoice repo + blob client `Received(0)`.
3. `DownloadFeeInvoice_MakerOwnedFee_HappyPath_StreamsPdfWithHeaders` — maker owns a Fee invoice with `PdfBlobPath` set; blob returns seeded bytes → `200`; body byte-equal; `Content-Type: application/pdf`; `Content-Disposition: attachment; filename="faktura-FV-CZ-20260042.pdf"`; `Cache-Control: private, no-store`; ETag echoed when present.
4. `DownloadFeeInvoice_CrossTenantInvoiceId_Returns404_OrderNotFound` — `GetForMakerReadOnlyAsync` returns null (id belongs to another maker OR nonexistent) → `404 order.notFound`; blob client `Received(0)`. **IDOR shield assertion.**
5. `DownloadFeeInvoice_CustomerInvoiceViaThisRoute_Returns404_OrderNotFound` — `GetForMakerReadOnlyAsync` returns a maker-owned invoice whose `Type == InvoiceType.Customer` → `404 order.notFound`; blob client `Received(0)` (the `Fee` gate fires before the blob read). **Route-purpose assertion.**
6. `DownloadFeeInvoice_FeeWithNullBlobPathOrBlobMiss_Returns404_NotYetRendered` — (a) Fee invoice with `PdfBlobPath` null → `404 invoice.notYetRendered`, blob client not called; (b) Fee invoice with `PdfBlobPath` set but `DownloadAsync` fails (blob-purged race) → `404 invoice.notYetRendered`.

#### Integration (2, Testcontainers Postgres + `FakeBlobStorageClient`)

`backend/src/Makables.IntegrationTests/Invoices/MakerFeeInvoiceDownloadTests.cs`:
1. `GET_fee_invoice_streams_pdf_for_owning_maker` — seed a `PayoutBatch` + a Fee invoice (`Type=Fee`, `PayoutBatchId` set, `MakerId` = maker A, `PdfBlobPath` set) + fake blob bytes; call `GET /api/v1/maker/files/invoices/{id}` as maker A → `200`, byte-equal, `private, no-store`, `faktura-{InvoiceNumber}.pdf` disposition.
2. `GET_fee_invoice_404_paths_are_oracle_free` — (a) maker B requests maker A's Fee-invoice id → `404 order.notFound`; (b) maker A requests a Customer-invoice id that IS denormalised to maker A (the per-order Customer invoice) via THIS Fee route → `404 order.notFound` (type gate — same shape, no oracle that the Customer invoice exists); (c) unknown invoiceId → `404 order.notFound` (same shape as (a)); (d) maker A's own Fee invoice with `PdfBlobPath` null → `404 invoice.notYetRendered`.

### NSwag regen

The new endpoint is a contract change → **regen REQUIRED in the same PR (maker host)**. **Rides T-0112's regen commit** (`npm run generate:api`; pre-commit hook blocks manual edits). The generated method returns a file response; T-0116 consumes it for the Fee-invoice "Stáhnout fakturu" CTA.

### Docs

- **`docs/architecture/roles/invoice.md`** — append T-0112a as the **Fee-invoice** read-side download surface (maker host; `private, no-store`; `order.notFound` for cross-maker / wrong-type, `invoice.notYetRendered` for render race). Note this complements T-0088's Customer-invoice download.
- **`docs/tickets/INDEX.md`** — PM flips T-0112a to `**done**` post-merge.

## Alternatives Considered

- **Option A — Do nothing (rely on T-0088).** *Rejected* — T-0088's lookup chain is `GetByOrderId*`, which structurally only returns Customer invoices (Fee invoices have a null `OrderId`, non-null `PayoutBatchId`). A maker has no route to their Fee invoice; US-maker-0013 is unsatisfiable and T-0116's CTA would 404 on a URL the backend itself emits. The gap is real, not hypothetical — this is the "do nothing" baseline the slice exists to close.
- **Option B — One-file MediatR feature `GetMakerFeeInvoice`.** *Rejected per B (ADR 0014 §"Handler-free read paths")* — T-0088 + T-0075 both shipped controller-direct for exactly this passthrough-blob-read shape (no validation rule, no transaction). Handler ceremony adds a file and a pipeline pass for zero gain; the sibling `GetShippingLabel` in the same controller is controller-direct. Consistency wins.
- **Option C — Reuse T-0088's order-invoice route with a type switch.** *Rejected* — T-0088's route is `GET /api/v1/orders/{orderId}/invoice`, keyed on `orderId`; Fee invoices have a null `OrderId` and are keyed on `invoiceId`. US-maker-0013 AC-1 names a different route (`/maker/files/invoices/{invoiceId}`). Bolting a Fee branch onto an order-keyed route requires inventing an order link Fee invoices don't have.
- **Option D — Serve BOTH Fee and Customer invoices through this route (drop the `Fee` gate).** *Rejected* — US-maker-0013 is fee-invoices-specifically; the maker's Customer-invoice download already exists at T-0088's order-scoped route (and is reachable from the order detail, not the payout list). A dual-purpose route blurs the two surfaces and risks a maker downloading a Customer invoice from the payout UI where only the Fee invoice is contextually meaningful. The `Type == Fee` gate (→ `order.notFound` otherwise) keeps the route single-purpose without leaking existence.
- **Option E — New `MakerFeeInvoiceNotFound` / `InvoiceNotFound` error code.** *Rejected* — verified: no `InvoiceNotFound` code exists; `OrderNotFound` ("not found / not yours", IDOR-resistant) + `InvoiceNotYetRendered` ("no row or null `PdfBlobPath`") are semantically exact and both carry i18n parity. A parallel code is maintenance for zero gain (T-0088 Option E rationale).
- **Option F — Extend the `ForMaker` queryable to surface Fee invoices now (close the `:57` TODO here).** *Rejected* — the `:57` TODO is about the maker "my invoices" **list** (T-0112/T-0116 territory); a single-id download needs only the scoped `GetForMakerReadOnlyAsync` predicate, which already returns Fee invoices via the denormalised `MakerId`. Closing the list TODO in a download ticket scope-creeps and risks an untested list path. T-0112a leaves the TODO standing for its owning ticket.
- **Option G — Reuse the tracked `GetByIdForMakerAsync` (no read-only variant).** *Rejected per ADR 0025* — the action only inspects `Type` + `PdfBlobPath` + `InvoiceNumber` and never mutates; tracking the aggregate is permanent change-tracking + snapshot overhead on a dashboard download path. T-0088 added exactly this read-only mirror for the same reason (`GetByOrderIdReadOnlyAsync`).
- **Option H — `public, max-age, immutable` cache like the sibling label download.** *Rejected per §C* — labels are deterministic system artifacts; Fee invoices carry recipient PII (maker name, IČO/DIČ, address). A logged-out or cross-maker request must miss every cache and 401/404. T-0064/T-0088's `private, no-store` is the correct policy family.
- **Option I — 302 redirect to a SAS-tokened blob URL.** *Rejected* — leaks storage structure, bypasses the audited audience checks, violates CLAUDE.md / ADR 0011 "no direct browser → blob storage links".
- **Option J — Live re-render fallback on blob miss (mirror the label's Packeta fallback).** *Rejected* — Fee-invoice rendering is owned by T-0102b's artifact pipeline; re-rendering inside a web request drags QuestPDF into the host and risks duplicate-number allocation. Blob-miss is a rare race; honest `404 invoice.notYetRendered` + FE retry is correct (T-0088 Option F).

## Out of scope

- **Maker payout LIST + drill-into-batch queries** — T-0112 owns `GetMakerPayouts` (paged) + `GetMakerOutboxEventsForOrder`. T-0112a is single-resource download only.
- **Frontend payout UI / Fee-invoice CTA** — T-0116 owns `/dashboard/maker/vyplaty`, the per-order breakdown, and the click handling of the download link.
- **CSV / bank-file download for makers** — explicitly forbidden per A.1 (cross-maker PII). The CSV is the operator's bank file (admin-only).
- **Customer-invoice download** — T-0088 owns it (order-scoped route on both hosts). This route 404s on Customer-invoice ids.
- **Surfacing Fee invoices in the `ForMaker` queryable / maker "my invoices" list** — the `:57` TODO stays for T-0112/T-0116; not needed for single-id download.
- **Admin Fee-invoice download / search** — admin host parity is a later ticket (`Unscoped` / `GetByIdUnscopedAsync` exist for it).
- **Invoice re-issuance / credit notes** — post-MVP per ADR 0009.
- **New error codes / i18n keys / migrations / outbox events** — none ship.

## Acceptance criteria

- **AC-1** Given a maker who owns a Fee invoice (`Type == InvoiceType.Fee`, `MakerId` == the maker, `PdfBlobPath` set), when `GET /api/v1/maker/files/invoices/{invoiceId}` is called with a valid maker JWT, then the response is `200 OK`, body byte-equal to the blob at `BlobContainer.Invoices/{PdfBlobPath}`, `Content-Type: application/pdf`, `Content-Disposition: attachment; filename="faktura-{InvoiceNumber}.pdf"`, `Cache-Control: private, no-store`.
- **AC-2** Given an invoiceId that is unknown OR belongs to a different maker, when the endpoint is called, then `404` with code `order.notFound` — indistinguishable from nonexistent (no IDOR oracle), and the blob client is never invoked (pinned by `Received(0)` in unit tests).
- **AC-3** Given an invoiceId that resolves to a maker-owned invoice whose `Type == InvoiceType.Customer`, when this Fee route is called, then `404 order.notFound` (same not-found shape; the blob client is never invoked) — the route serves Fee invoices only, and does not leak that a Customer invoice with that id exists.
- **AC-4** Given a maker-owned Fee invoice whose `PdfBlobPath` is null, OR a blob download failure (purged-blob race), when the endpoint is called, then `404` with code `invoice.notYetRendered`. No `Cache-Control` header on any 404.
- **AC-5** Given an anonymous request or a wrong-audience JWT, when the endpoint is called, then `401 auth.required` (ADR 0013 host audience enforcement); no repository or blob call is made.
- **AC-6** Given a maker-audience user with no maker row, when the endpoint is called, then `404 order.notFound` and the invoice lookup is never performed.
- **AC-7** Build clean. Unit tests: baseline + ~6 new (`FilesControllerInvoiceDownloadTests`); integration: baseline + 2 new (`MakerFeeInvoiceDownloadTests`); `node scripts/check-consistency.mjs` exit 0. NSwag regen committed in the same PR (maker host, riding T-0112's regen commit); `frontend/src/lib/api-client/maker-api.v1.ts` gains the typed file-download method; no manual api-client edits (pre-commit hook enforces). No new `BusinessErrorMessage` codes, no migrations, no i18n keys, no change to the `ForMaker` `:57` TODO.

## Risk / mitigation

- **Risk: the `InvoiceType.Fee` gate is forgotten and the route serves any maker-owned invoice.** Mitigation: AC-3 + unit test 5 pin the Customer-invoice-via-this-route → `404` path with `Received(0)` on the blob client; the gate fires before the blob read.
- **Risk: `GetForMakerReadOnlyAsync` is copy-pasted later without the `MakerId` predicate.** Mitigation: AC-2 + unit test 4 + integration (b) pin the cross-maker `404`; the predicate IS the IDOR shield, mirrored 1:1 from the tracked `GetByIdForMakerAsync`.
- **Risk: blob-purged-but-row-remains race surfaces as a confusing 404.** Mitigation: the code is `invoice.notYetRendered`, whose i18n copy already describes a transient state; the T-0116 link only renders when the Fee invoice exists with a non-null `PdfBlobPath`, so the race window is cache-staleness only (T-0088 risk parity).
- **Risk: NSwag file-response generation for the maker host.** Mitigation: T-0088's order-invoice action already proved file-response generation on the maker client; AC-7 regen-in-same-PR + CI parity check.

## Test plan reference

Inline above (see Scope > Tests). No separate `docs/test-plans/T-0112a.md`.

## Files touched (expected)

### New
- `backend/src/Makables.Tests/Web/Maker/Controllers/FilesControllerInvoiceDownloadTests.cs`
- `backend/src/Makables.IntegrationTests/Invoices/MakerFeeInvoiceDownloadTests.cs`

### Modified
- `backend/src/Makables.Core.Domain/Invoices/IInvoiceRepository.cs` — add `GetForMakerReadOnlyAsync`.
- `backend/src/Makables.Infra.Database/Invoices/InvoiceRepository.cs` — implement it (AsNoTracking mirror of `GetByIdForMakerAsync`).
- `backend/src/Makables.Web.Maker/Controllers/FilesController.cs` — `GET invoices/{invoiceId}` action + `IInvoiceRepository` ctor param + (if absent) `EscapeFilenameForHeader` / `ETagMatches` helpers.
- `frontend/src/lib/api-client/maker-api.v1.ts` — NSwag-regenerated (maker host), committed same PR (rides T-0112's regen).
- `docs/architecture/roles/invoice.md` — Fee-invoice read-side download surface note.

## Commits hint

1. `feat(T-0112a): maker fee-invoice download action + read-only ForMaker invoice lookup`
2. `test(T-0112a): controller unit + integration coverage (ownership, type gate, 404 paths, byte-equality)`
3. `chore(T-0112,T-0112a): NSwag regen (maker host)` — shared regen commit with T-0112.

## Status log

- 2026-06-13 `draft` by PM. Created as the BA-discovered backend gap slice unblocking T-0116. Precedents: T-0088 invoice download (controller-direct stream, cache/disposition/ETag policy, `GetByOrderIdReadOnlyAsync` read-only mirror, `OrderNotFound` + `InvoiceNotYetRendered` reuse), T-0075 label download (`FilesController` host, maker-resolution lookup chain), T-0102b Fee-invoice `PdfBlobPath` writer, `Invoice.MakerId` denormalised for both families (`Invoice.cs:88-95`), `IInvoiceRepository.GetByIdForMakerAsync` IDOR-scoped lookup. Slice scope: one read-only repository mirror + one controller action + ~6 unit + 2 integration. No new error codes, migrations, outbox events, or i18n keys; `ForMaker` `:57` TODO untouched.
- 2026-06-13 `draft → ready` by BA/PM. Reality checks closed: route = literal US-maker-0013 AC-1 `GET /api/v1/maker/files/invoices/{invoiceId}` on the existing T-0075 `FilesController`; `GetByIdForMakerAsync` already returns Fee invoices via denormalised `MakerId` (only the `AsNoTracking` mirror + `InvoiceType.Fee` controller gate are new); no `InvoiceNotFound` code exists → `OrderNotFound`/`InvoiceNotYetRendered` reused; `Invoice.PdfBlobPath` container-relative against `BlobContainer.Invoices`. **Ready for dotnet-backend.** Ships in the maker-payout PR; NSwag regen rides T-0112.

## Definition of Ready

- [x] Route verified against US-maker-0013 AC-1 + INDEX (`/maker/files/invoices/{invoiceId}` on the existing `FilesController`).
- [x] Precedent shape read and locked (T-0088 controller-direct stream; T-0064 header policy; T-0075 maker lookup chain).
- [x] IDOR shield identified — `GetByIdForMakerAsync`'s `MakerId` predicate (denormalised, covers Fee family); read-only mirror is the only repo delta.
- [x] `InvoiceType.Fee` route-purpose gate located and its 404 shape decided (`order.notFound`, no existence oracle).
- [x] Error-code reuse verified (`order.notFound`, `invoice.notYetRendered` — both with i18n parity; no `InvoiceNotFound` exists).
- [x] AC are observable proofs (status codes, headers, byte-equality, `Received(0)` pins).
- [x] No open questions in `docs/questions/open.md` for this slice.
