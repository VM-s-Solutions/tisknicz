---
id: T-0088
title: Invoice PDF download endpoints (customer + maker hosts)
status: ready
size: S
owner: dotnet-backend
created: 2026-06-09
updated: 2026-06-09
depends_on: [T-0068b, T-0069, T-0082]
blocks: [T-0086b, T-0087b]
user_stories: [US-customer-0012, US-maker-0010]
adrs: [0009, 0013, 0014, 0025]
phase: 4
manual_steps: []
security_touching: true
layers: [domain, infra-database, web-customer, web-maker]
---

# T-0088 — Invoice PDF download endpoints (customer + maker hosts)

## Context

T-0088 is the **first ticket in the order-dashboards bundle** (`feat/order-dashboards-bundle`: T-0088 → T-0089 → T-0086a → T-0086b → T-0087a → T-0087b). The two backend slices (T-0088 + T-0089) ship first and gate the NSwag regen the four frontend slices consume.

T-0082's detail projections already emit `InvoicePdfUrl` whenever `Invoice.PdfBlobPath` is non-null — the URL is built inline in `Infra.Database/Orders/OrderQueries.cs` (lines 221-224 customer, 285-288 maker) as `"/api/v1/orders/" + o.Id + "/invoice"`. **That route does not exist yet.** A customer clicking "Stáhnout fakturu" on the T-0086b detail page today would hit a 404 on a URL the backend itself handed out. T-0088 makes the placeholder real: one streaming GET action per host, added to the **existing** `OrdersController` on each host.

**Route reality note (corrects the bundle-plan shorthand):** the plan named the routes `/api/v1/customer/orders/{orderId}/invoice` + `/api/v1/maker/orders/{orderId}/invoice`. The shipped controllers are mounted host-relative — both `Web.Customer/Controllers/OrdersController.cs` and `Web.Maker/Controllers/OrdersController.cs` carry `[Route("api/v{version:apiVersion}/orders")]`; the audience lives in the **host** (per-host JWT audience, ADR 0013), not the path. The T-0082 projection emits the host-relative form. The §A.1 lock is "the EXACT routes the projection emits", so the literal routes are `GET /api/v1/orders/{orderId}/invoice` on each host. No projection change, no regen churn.

The closest precedents are both already on master in the **same controllers**: T-0064's `DownloadAttachment` actions (controller-direct blob streaming, ownership-scoped lookup, `private, no-store` + `Content-Disposition` + ETag/304) and T-0075's `FilesController.GetShippingLabel` (handler-free read path per ADR 0014, maker resolution → ownership-scoped read-only order load → blob stream). T-0088 mirrors the T-0064 action body with three swaps: lookup chain, container path source (`Invoice.PdfBlobPath` verbatim against `BlobContainer.Invoices` — the exact pattern `IEmailSendService` already uses at line 425 for e-mail attachments), and a fixed `application/pdf` content type with a `faktura-{InvoiceNumber}.pdf` download filename.

No new `BusinessErrorMessage` codes (grooming checked: there is **no** `InvoiceNotFound`; T-0069's `InvoiceNotYetRendered` = "no Invoice row yet OR `PdfBlobPath` still null" is semantically exact and already has the `invoice.notYetRendered` i18n key at `cs-CZ.ts:422`). No migrations, no outbox events, no new i18n keys. One trivial repository addition: a customer-side read-only order lookup mirroring the maker variant that already exists.

## Locked design decisions

Captured per `docs/process/deliberation.md`. The user locked 2 dimensions in the bundle plan (2026-06-09 grooming session); the rest is ADR-locked or PM-absorbed from the T-0064/T-0075 streaming precedents.

### A. User-locked in bundle plan (non-negotiable)

1. **Routes = the EXACT routes T-0082's projection already emits.** `GET /api/v1/orders/{orderId}/invoice` on the Customer host + the same path on the Maker host (host-relative; audience = host per ADR 0013 — see Context route-reality note). Changing the emitted URL shape is forbidden — the projection shipped in T-0082 and the FE consumes `InvoicePdfUrl` verbatim.
2. **Streams the PDF from blob via `Invoice.PdfBlobPath`; 404 when no Invoice or `PdfBlobPath` null; ADR 0013 ownership scoping** (customer: order ownership via `CustomerUserId`; maker: order ownership via `MakerId`) — mirror the T-0075 label-download precedent's lookup discipline.

### B. ADR-locked (no relitigation)

- **ADR 0014 §"Handler-free read paths" — controller-direct, NO MediatR feature.** The grooming left "one-file features OR controller-direct per T-0075's actual shape" open pending a read of T-0075 as shipped. **T-0075 shipped controller-direct** (`FilesController.GetShippingLabel` — no handler), and T-0064's `DownloadAttachment` in the very controllers T-0088 extends is also controller-direct. A single passthrough read with no validation rule, no business rule, and no transaction does not justify handler ceremony. T-0088 is controller-direct.
- **ADR 0013 (per-audience JWT + scoped reads).** Ownership resolved from the session principal, never from a request param. Cross-tenant probes surface as `404 order.notFound` — same shape as nonexistent, no IDOR oracle.
- **ADR 0025 (read-only repository variants).** The order load only verifies ownership/existence; use the AsNoTracking variants (`GetByIdForMakerReadOnlyAsync` exists; the customer mirror is added here).
- **ADR 0009 (invoicing).** Invoices are immutable legal records; the endpoint is a pure read. `PdfBlobPath` is set-once by T-0068b's pipeline; T-0088 never writes.
- **No `SaveChangesAsync` anywhere in this slice** (no mutation, no UoW commit point).
- **`BusinessErrorMessage` reuse:** `OrderNotFound` + `InvoiceNotYetRendered`. No new codes.

### C. PM-absorbed (no user input needed)

- **Lookup chain (customer host):** (1) `session.GetUserId()` → null → 401; (2) `orders.GetByIdForCustomerReadOnlyAsync(orderId, userId, ct)` (NEW — mirrors the existing maker variant at `IOrderRepository.cs:106`) → null → `404 order.notFound`; (3) `invoices.GetByOrderIdAsync(orderId, ct)` (existing; unscoped-but-safe — ownership already established in step 2) → null OR `PdfBlobPath` null → `404 invoice.notYetRendered`; (4) `blobs.DownloadAsync(BlobContainer.Invoices, invoice.PdfBlobPath, ct)` → failure (blob purged race) → `404 invoice.notYetRendered`; (5) stream.
- **Lookup chain (maker host):** identical with the T-0075 maker-resolution prefix: session → `makers.GetByUserIdAsync` → null → `404 order.notFound`; then `orders.GetByIdForMakerReadOnlyAsync(orderId, maker.Id, ct)`; steps 3-5 identical.
- **Headers:** `Content-Disposition: attachment; filename="faktura-{InvoiceNumber}.pdf"` (run through the existing `EscapeFilenameForHeader` helper — defensive even though invoice numbers are platform-generated `FV-CZ-NNNNNNNN`); `Cache-Control: private, no-store` + ETag/`If-None-Match` → 304 conditional GET — **mirror T-0064's attachment policy exactly** (invoices carry recipient PII: name, address, tax ids; a logged-out request must miss every cache and 401). NOT the label's `public, immutable` policy.
- **`File(stream, "application/pdf", enableRangeProcessing: false)`** — invoices are ≤ ~100 KB platform artifacts; range support adds attack surface for zero benefit (T-0075 rationale).
- **`Invoice.PdfBlobPath` used verbatim** against `BlobContainer.Invoices` (container-relative; precedent `IEmailSendService.cs:425`). No path construction in the controller.
- **No new wrapper/response records** — the 200 response is a binary file; NSwag generates `FileResponse`-returning client methods. No schema-collision concern.
- **Tests:** ~8 unit (4 per host) + 2 integration (ownership isolation + 404 paths; happy-path byte-equality).
- **NSwag regen:** BOTH hosts, same PR.
- **No migrations, no outbox, no i18n keys** (`invoice.notYetRendered` + `order.notFound` keys already exist with cross-stack parity).

## Scope

### Domain layer

- **`Core.Domain/Orders/IOrderRepository.cs`** — add `Task<Order?> GetByIdForCustomerReadOnlyAsync(string orderId, string customerUserId, CancellationToken cancellationToken);` directly below the existing maker variant (line 106). XML doc mirrors it: AsNoTracking, IDOR-shielded null for unknown/cross-customer ids, read-only callers per ADR 0025.

### Infrastructure / Database layer

- **`Infra.Database/Orders/OrderRepository.cs`** — implement the new method; copy the `GetByIdForMakerReadOnlyAsync` body with the predicate swapped to `o.Id == orderId && o.CustomerUserId == customerUserId`.

### Web.Customer host

- **`Web.Customer/Controllers/OrdersController.cs`** — new action below `DownloadAttachment`:
  - `[HttpGet("{orderId}/invoice")]` → resolves to `GET /api/v1/orders/{orderId}/invoice`.
  - `[ProducesResponseType(StatusCodes.Status200OK)]`, `[ProducesResponseType(StatusCodes.Status304NotModified)]`, plus typed `Error` 401/403/404.
  - Controller ctor gains `IInvoiceRepository invoices` (already registered in DI by T-0068a).
  - Body per §C lookup chain + headers. Reuse the file-local `EscapeFilenameForHeader` + `ETagMatches` helpers.

### Web.Maker host

- **`Web.Maker/Controllers/OrdersController.cs`** — new action below `DownloadAttachment`, same body with the maker-resolution prefix and `GetByIdForMakerReadOnlyAsync`. Controller ctor gains `IInvoiceRepository invoices`.

### Tests

#### Unit (~8, NSubstitute, mirror `FilesControllerLabelDownloadTests` harness shape)

`backend/src/Makables.Tests/Web/Customer/Controllers/OrdersControllerInvoiceDownloadTests.cs` (4):
1. `DownloadInvoice_NoSession_Returns401` — `GetUserId()` null → 401; `IOrderRepository` + `IInvoiceRepository` not called.
2. `DownloadInvoice_OrderNotOwnedOrMissing_Returns404_OrderNotFound` — read-only order lookup returns null → `404 order.notFound`; asserts `Received(0)` on `IInvoiceRepository.GetByOrderIdAsync` AND `IBlobStorageClient.DownloadAsync` (IDOR shield wiring).
3. `DownloadInvoice_NoInvoiceOrNullBlobPath_Returns404_NotYetRendered` — order owned; invoice repo returns null (then re-run with an Invoice whose `PdfBlobPath` is null) → `404 invoice.notYetRendered` both times; blob client not called.
4. `DownloadInvoice_HappyPath_StreamsPdfWithHeaders` — blob returns seeded bytes → 200; body byte-equal; `Content-Type: application/pdf`; `Content-Disposition: attachment; filename="faktura-FV-CZ-20260001.pdf"`; `Cache-Control: private, no-store`; ETag echoed when present.

`backend/src/Makables.Tests/Web/Maker/Controllers/OrdersControllerInvoiceDownloadTests.cs` (4):
1. `DownloadInvoice_UserWithoutMakerRow_Returns404_OrderNotFound` — `GetByUserIdAsync` null → 404; order + invoice repos not called.
2. `DownloadInvoice_OrderNotOwnedByMaker_Returns404_OrderNotFound` — `GetByIdForMakerReadOnlyAsync` null → 404; invoice repo not called.
3. `DownloadInvoice_BlobDownloadFails_Returns404_NotYetRendered` — invoice + `PdfBlobPath` present but `DownloadAsync` fails (blob-purged race) → `404 invoice.notYetRendered`.
4. `DownloadInvoice_HappyPath_StreamsPdfWithHeaders` — mirror customer test 4 on the maker host.

#### Integration (2, Testcontainers Postgres + `FakeBlobStorageClient`)

`backend/src/Makables.IntegrationTests/Orders/OrderInvoiceDownloadTests.cs`:
1. `GET_invoice_streams_pdf_for_owning_customer_and_maker` — seed an Order with an Invoice (`PdfBlobPath` set) + fake blob bytes; call the customer endpoint as the owner AND the maker endpoint as the assigned maker → both 200, byte-equal, `private, no-store`, `faktura-{InvoiceNumber}.pdf` disposition.
2. `GET_invoice_404_paths_are_oracle_free` — (a) customer B probes customer A's order → `404 order.notFound`; (b) owner probes an owned order with NO invoice row → `404 invoice.notYetRendered`; (c) unknown orderId → `404 order.notFound` (same shape as (a) — no existence oracle).

### NSwag regen

Both new endpoints are contract changes → **regen REQUIRED in the same PR for BOTH hosts** (`npm run generate:api`; pre-commit hook blocks manual edits). The generated methods return file responses; T-0086b/T-0087b consume them for the invoice-download CTA.

### Docs

- **`docs/architecture/roles/invoice.md`** — append T-0088 as the read-side download surface (customer + maker hosts; `private, no-store`; `invoice.notYetRendered` race semantics).
- **`docs/tickets/INDEX.md`** — PM flips T-0088 to `**done**` post-merge.

## Alternatives Considered

- **Option A — One-file MediatR features `GetCustomerOrderInvoice` + `GetMakerOrderInvoice`.** *Rejected per B (ADR 0014 §"Handler-free read paths")* — the grooming explicitly deferred to T-0075's actual shape, and T-0075 shipped controller-direct. A passthrough read with no validation rule and no transaction gains nothing from handler ceremony; T-0064's sibling streaming actions in the same controllers are also controller-direct. Consistency wins.
- **Option B — New `FilesController` per host (`/api/v1/maker/files/orders/{id}/invoice`).** *Rejected per A.1* — the T-0082 projection already emits `/api/v1/orders/{orderId}/invoice`; a different route requires editing the shipped projection + regen churn + FE retest. The `OrdersController` already hosts the sibling attachment streaming action; the invoice action belongs beside it.
- **Option C — 302 redirect to a SAS-tokened blob URL.** *Rejected* — leaks storage structure, bypasses the audited audience checks, violates CLAUDE.md "no direct browser → blob storage links".
- **Option D — `public, max-age=31536000, immutable` cache like T-0075 labels.** *Rejected per §C* — labels are deterministic system artifacts; invoices carry recipient PII. A logged-out request must miss every cache and 401. T-0064's `private, no-store` is the correct policy family.
- **Option E — New `InvoiceNotFound` error code.** *Rejected* — verified during grooming: no such code exists; T-0069's `InvoiceNotYetRendered` ("no row yet, or row with `PdfBlobPath` still null") is semantically exact, already has the `invoice.notYetRendered` i18n key, and adding a parallel code is maintenance for zero gain.
- **Option F — Live re-render fallback on blob miss (mirror T-0075's Packeta fallback).** *Rejected* — invoice rendering is owned by the queue pipeline (GenerateInvoiceFunction → `IssueInvoice`, T-0068b); re-rendering inside a web request drags QuestPDF into the hosts and risks duplicate-number allocation. Blob-miss is a rare race; honest `404 invoice.notYetRendered` + FE retry is correct.
- **Option G — Single shared endpoint with a runtime audience flag.** *Rejected per ADR 0013* — audience is the host; runtime audience branches are the leak surface the per-host split exists to prevent.
- **Option H — Tracked order load via existing `GetByIdForCustomerAsync`.** *Rejected per ADR 0025* — the read only verifies ownership; the maker host already has the read-only variant and T-0075 uses it for exactly this purpose. One mirrored method is cheaper than permanent change-tracking overhead on a hot dashboard path.

## Out of scope

- **Frontend invoice-download CTA** — T-0086b (customer detail) + T-0087b (maker detail) own the rendering and click handling of `InvoicePdfUrl`.
- **Admin invoice download / search** — admin host parity is a later ticket (`IInvoiceRepository.Unscoped` exists for it).
- **Maker "my invoices" list endpoint** — the `ForMaker` queryable + the T-0068b denormalized `MakerId` index back a future list ticket; T-0088 is single-resource download only.
- **Fee-invoice (payout batch) downloads** — T-0101 territory; `GetByOrderIdAsync` only ever returns Customer invoices.
- **Invoice re-issuance / credit notes** — post-MVP per ADR 0009.
- **Changing the projection-emitted URL shape** — explicitly forbidden per A.1.
- **New error codes / i18n keys / migrations / outbox events** — none ship.

## Acceptance criteria

- **AC-1** Given an order owned by the requesting customer whose invoice has `PdfBlobPath` set, when `GET /api/v1/orders/{orderId}/invoice` is called on the Customer host with a valid customer JWT, then the response is `200 OK`, body byte-equal to the blob at `BlobContainer.Invoices/{PdfBlobPath}`, `Content-Type: application/pdf`, `Content-Disposition: attachment; filename="faktura-{InvoiceNumber}.pdf"`, `Cache-Control: private, no-store`.
- **AC-2** Given the assigned maker of the same order, when the Maker-host endpoint is called with a valid maker JWT, then the response is identical in shape (200, byte-equal, same headers).
- **AC-3** Given an order NOT owned by the requester (or a nonexistent orderId), when either endpoint is called, then `404` with code `order.notFound` — indistinguishable from nonexistent (no IDOR oracle), and the invoice repository + blob client are never invoked (pinned by `Received(0)` in unit tests).
- **AC-4** Given an owned order with no Invoice row, OR an Invoice whose `PdfBlobPath` is null, OR a blob download failure (purged-blob race), when either endpoint is called, then `404` with code `invoice.notYetRendered`. No `Cache-Control` header on any 404.
- **AC-5** Given an anonymous request or a wrong-audience JWT, when either endpoint is called, then `401 auth.required` (ADR 0013 host audience enforcement).
- **AC-6** Given a maker-audience user with no maker row, when the Maker-host endpoint is called, then `404 order.notFound` and the order lookup is not performed.
- **AC-7** Given a repeat request carrying `If-None-Match` matching the blob's ETag, when either endpoint is called, then `304 Not Modified` with no body (mirror of T-0064 conditional-GET behaviour).
- **AC-8** Build clean. Unit tests: baseline + ~8 new; integration: baseline + 2 new; `node scripts/check-consistency.mjs` exit 0. NSwag regen committed in the same PR for BOTH hosts; `frontend/src/lib/api-client/` gains the typed file-download method on each client; no manual api-client edits (pre-commit hook enforces). No new `BusinessErrorMessage` codes, no migrations, no i18n keys.

## Risk / mitigation

- **Risk: blob-purged-but-row-remains race surfaces as a confusing 404.** Mitigation: the code is `invoice.notYetRendered`, whose i18n copy already describes a transient state; the FE link only renders when `InvoicePdfUrl` is non-null, so the race window is cache-staleness only.
- **Risk: `GetByOrderIdAsync` is documented Unscoped — future copy-paste into a path without an ownership pre-check.** Mitigation: AC-3's `Received(0)` pins call ordering; the controller comment must state "safe ONLY after the ownership-scoped order load above".
- **Risk: NSwag file-response generation differs between hosts.** Mitigation: AC-8 regen-in-same-PR + CI parity check; T-0075's label download already proved the file-response generation path on the maker client.

## Test plan reference

Inline above (see Scope > Tests). No separate `docs/test-plans/T-0088.md`.

## Files touched (expected)

### New
- `backend/src/Makables.Tests/Web/Customer/Controllers/OrdersControllerInvoiceDownloadTests.cs`
- `backend/src/Makables.Tests/Web/Maker/Controllers/OrdersControllerInvoiceDownloadTests.cs`
- `backend/src/Makables.IntegrationTests/Orders/OrderInvoiceDownloadTests.cs`

### Modified
- `backend/src/Makables.Core.Domain/Orders/IOrderRepository.cs` — add `GetByIdForCustomerReadOnlyAsync`.
- `backend/src/Makables.Infra.Database/Orders/OrderRepository.cs` — implement it.
- `backend/src/Makables.Web.Customer/Controllers/OrdersController.cs` — `GET {orderId}/invoice` action + `IInvoiceRepository` ctor param.
- `backend/src/Makables.Web.Maker/Controllers/OrdersController.cs` — same.
- `frontend/src/lib/api-client/*` — NSwag-regenerated (both hosts), committed same PR.
- `docs/architecture/roles/invoice.md` — read-side download surface note.

## Commits hint

1. `feat(T-0088): invoice download actions on both hosts + customer read-only order lookup`
2. `test(T-0088): controller unit + integration coverage (ownership, 404 paths, byte-equality)`
3. `chore(T-0088): NSwag regen (customer + maker hosts)`

## Status log

- 2026-06-09 `draft` by PM. Created as the first ticket in the order-dashboards bundle. Precedents on master: T-0064 attachment streaming (same controllers, cache/disposition/ETag policy), T-0075 label download (handler-free read path, maker lookup chain, read-only order variant), T-0068b `Invoice.PdfBlobPath` writer, T-0069 `InvoiceNotYetRendered` code + i18n key, T-0082 projection emitting the target URLs.
- 2026-06-09 `draft → ready` by BA/PM. Reality checks closed the two open grooming items: (1) **controller-direct streaming locked** — T-0075 as shipped is controller-direct, T-0064's sibling actions likewise; (2) **routes are host-relative** `GET /api/v1/orders/{orderId}/invoice` on each host — the literal strings the T-0082 projection emits (the bundle plan's `/customer/...`, `/maker/...` shorthand named the host, not the path). Confirmed: no `InvoiceNotFound` code exists; `InvoiceNotYetRendered` reused; `Invoice.PdfBlobPath` is container-relative (download precedent `IEmailSendService.cs:425`). **Ready for dotnet-backend.** T-0088 → T-0089 ship in the bundle's backend PR; frontend slices follow.

## Definition of Ready

- [x] Routes verified against the shipped T-0082 projection (emitted-URL match, zero projection churn).
- [x] Precedent shape read and locked (T-0075 controller-direct; T-0064 header policy).
- [x] Error-code reuse verified (`order.notFound`, `invoice.notYetRendered` — both with i18n parity).
- [x] Repository surface delta minimal and precedented (one read-only mirror method).
- [x] AC are observable proofs (status codes, headers, byte-equality, `Received(0)` pins).
- [x] No open questions in `docs/questions/open.md` for this slice.
