---
role: Invoice
kind: aggregate
status: accepted
---

# Invoice

## Responsibility

Be the legal record of a payment between two parties (platform↔customer for an order; platform↔maker for the platform fee on a payout batch).

## Collaborators

- **Order** (referenced by customer invoices; one invoice per paid order)
- **PayoutBatch** (referenced by fee invoices; one fee invoice per maker per batch)
- **InvoiceNumbering** (asks: next gap-free number)
- **InvoiceService** (creates: orchestrates issuance)
- **BlobStorage** (writes: rendered PDF)

## Knows

- `InvoiceNumber` (immutable, gap-free)
- `Type` (`Customer` | `Fee`)
- Issuer (JVM YORE s.r.o.) snapshot
- Recipient snapshot (customer or maker; from-ARES data for makers)
- Line items
- `AmountWithoutVatMinor`, `VatRateBp`, `VatAmountMinor`, `AmountWithVatMinor`, `Currency`
- `IssueDate`, `DueDate`
- `PdfBlobPath` once rendered

## Does NOT know

- The payment mechanics (Comgate is on Order)
- How the customer/maker is notified (outbox event)
- Whether the recipient has paid (for customer invoices, the order's payment state is the signal; for fee invoices, the payout batch transfer is)

## Lifecycle

- **Created by:** `IssueInvoice.Command` — rendered via T-0068b's `IssueInvoice.Handler`, dispatched by T-0069's `GenerateInvoiceFunction` (queue-triggered) from the `invoice.generate` outbox event that `MarkOrderPaid.Handler` enqueues alongside the customer + maker email events. Same shape for fee invoices once `CreatePayoutBatch` lands (T-0101 / T-0102).
- **Modified by:** no updates after issuance. Errata require a credit-note invoice (separate invoice with negative amounts, post-MVP).
- **Persisted by:** `IInvoiceRepository`
- **Destroyed by:** never. Even GDPR delete anonymizes — the legal record remains.
- **Attached to email:** the customer's `order.paid.customerEmail` event picks up the rendered PDF by `OrderId` at send time via `EmailSendService.SendOrderPaidCustomerEmailAsync` — language-aware filename (`faktura-{n}.pdf` for cs-CZ + `invoice-{n}.pdf` for en-US) → SendGrid `AddAttachment`. Eventual consistency: if the invoice.generate queue lost the FIFO race, the email returns `Transient(InvoiceNotYetRendered)` and the outbox retry re-delivers (T-0069 locked decision 1).

## Invariants

- `InvoiceNumber` immutable after creation.
- Issuer + recipient snapshots are immutable (legal: the invoice carries the data as it was at issue time).
- `AmountWithoutVatMinor + VatAmountMinor == AmountWithVatMinor`.
- One customer invoice per paid order.
- One fee invoice per maker per payout batch.

## Implementation pointer

**T-0068a shipped (entity + repository + numbering migration; pure-domain + DB slice):**

- Entity + factory + set-once: `backend/src/Makables.Core.Domain/Invoices/Invoice.cs` (sealed `Auditable`, static `Issue(...)` factory enforcing money balance + XOR aggregate link + currency length + None+zero-VAT; `AttachPdfBlobPath(string)` set-once with idempotent same-value semantics).
- Type discriminator: `backend/src/Makables.Core.Domain/Invoices/InvoiceType.cs` (`Customer = 0`, `Fee = 1`).
- Repository surface: `backend/src/Makables.Core.Domain/Invoices/IInvoiceRepository.cs` (scoped `ForCustomer` / `ForMaker` / `Unscoped`, `GetByIdFor*`, `GetByInvoiceNumberAsync`, `GetByOrderIdAsync` for T-0068b idempotency; no `UpdateAsync` / `DeleteAsync` per invariant "no updates after issuance").
- Repository impl: `backend/src/Makables.Infra.Database/Invoices/InvoiceRepository.cs`.
- EF mapping + migration: `backend/src/Makables.Infra.Database/Configurations/InvoiceConfiguration.cs` + `Migrations/20260606203317_Invoices.cs` (snake_case columns, unique partial indexes on `invoice_number` + `order_id`, composite `(maker_id, created_at DESC)`, single on `type`; FK `order_id → orders(id)` ON DELETE RESTRICT; `payout_batch_id` column ships now, FK deferred to T-0101).
- Numbering generator migration: `backend/src/Makables.Core.Domain/Numbering/IInvoiceNumberGenerator.cs` + `backend/src/Makables.Infra.Database/Numbering/InvoiceNumberGenerator.cs` (signature drops `int year`; computes country-local year via `TimeZoneInfo.ConvertTimeFromUtc(clock.UtcNow.UtcDateTime, TimeZoneInfo.FindSystemTimeZoneById(config.TimeZoneId))` — mirrors T-0062 `OrderNumberGenerator` verbatim).
- Error code: `BusinessErrorMessage.InvoiceBlobPathAlreadySet` (Czech i18n key deferred to T-0068b).

**T-0068b shipped (renderer + handler + outbox wiring; ADR 0025):**

- Renderer interface: `backend/src/Makables.Core.Domain/Rendering/IInvoicePdfRenderer.cs` (invoice-specific per ADR 0025 locked decision 3 — NOT a generic `IPdfRenderer<T>`).
- Renderer impl + project: `backend/src/Makables.Infra.PdfRendering/QuestPdfInvoiceRenderer.cs` + `Makables.Infra.PdfRendering.csproj` (new project added to `Makables.Api.slnx`). Pins `QuestPDF.Settings.License = LicenseType.Community` at static ctor + via `AddMakablesPdfRendering()` DI extension. Two nested `IDocument` templates: `DokladOProdejiDocument` (None mode) + `DanovyDokladDocument` (StandardVat). ReverseCharge / StrictFiscalReporting throw `NotImplementedException` — caller translates.
- One-file feature: `backend/src/Makables.Core.AppServices/Features/Invoices/IssueInvoice.cs` (nested `Command` + `Response` + `Validator` + `Handler` per locked decision 7 — tisknicz precedent, not `Services/InvoiceService.cs`). 10-step Handler: load Order → load CountryConfiguration → idempotency pre-check (incl. stalled-mid-flow recovery) → InvoicingMode switch → allocate number → build aggregate → persist → render → upload blob → attach blob path.
- SPAYD value object: `backend/src/Makables.Core.Domain/Payments/Spayd.cs` (static `ForInvoice` factory; format `SPD*1.0*ACC:<iban>*AM:<amount>*CC:<ccy>*X-VS:<vs>`; IBAN whitespace strip + uppercase, currency uppercase, amount `F2` invariant, VS digits-only). Pinned by 10 unit tests.
- DI extension: `backend/src/Makables.Config/Extensions/AddMakablesPdfRendering.cs` (Singleton — renderer is stateless). Called from every `Web.*` host's `Program.cs` AND `Makables.Functions/Program.cs` (T-0069 `GenerateInvoiceFunction` dispatches via Mediator).
- CountryConfiguration columns: 4 new (`issuer_name VARCHAR(200) NOT NULL`, `issuer_ico CHAR(8) NOT NULL`, `issuer_dic VARCHAR(15) NULL`, `platform_iban VARCHAR(34) NULL`). Migration `20260607104759_AddCountryConfigurationIssuerAndIban.cs`. CZ seed: `issuer_name='JVM YORE s.r.o.'`, `issuer_ico='00000000'` (placeholder per user direction; manual_step `country-config-ico-replace-placeholder-pre-launch` tracks the pre-prod-launch update), `issuer_dic=NULL`, `platform_iban=NULL`. Renderer skips SPAYD QR rendering when `platform_iban IS NULL`.
- Outbox event + payload: `OutboxEventTypes.InvoiceGenerate = "invoice.generate"` (separate routing from email events); `InvoiceGenerateOutboxPayload(OrderId, LanguageCode)`. Wired into `MarkOrderPaid.Handler` as the third enqueue. The T-0067 negative-pin test `Handler_does_NOT_enqueue_invoice_generate_yet` was flipped to positive `Handler_enqueues_invoice_generate_outbox_row` per locked decision 10.
- Error codes: `InvoicingModeNotImplemented`, `InvoiceRenderFailed`, `InvoiceBlobUploadFailed` + the deferred `InvoiceBlobPathAlreadySet` get Czech i18n keys in `frontend/src/lib/i18n/cs-CZ.ts`.
- Font deviation: locked-decision-2 Noto Sans subset deferred — renderer uses QuestPDF default (DejaVu Sans, decent Czech-glyph coverage) pending the `pyftsubset` toolchain in the build env. Documented in `QuestPdfInvoiceRenderer.cs` class XML doc + the status log of T-0068b.
- SPAYD QR rendering: at T-0068b the renderer emits the SPAYD payload as visible plain text in a "Platba QR kódem" box; full QR-image generation (QRCoder + SkiaSharp) lands in a follow-up. Format-compliance surface (`Spayd.ForInvoice`) ships now.

**T-0069 shipped (queue-trigger dispatcher + customer email PDF attachment):**

- Queue-triggered Function: `backend/src/Makables.Functions/Outbox/GenerateInvoiceFunction.cs` — thin MediatR dispatch wrapper per locked decision 3. Loads the outbox row by id, deserialises `InvoiceGenerateOutboxPayload`, dispatches `IssueInvoice.Command(payload.OrderId)`, throws on every failure path so the queue retry policy fires (idempotency owned by `IssueInvoice.Handler` per locked decision 5).
- Dispatcher routing branch: `backend/src/Makables.Core.AppServices/Features/Outbox/IOutboxDispatcher.cs` — `OutboxDispatcher.DispatchDueAsync` now classifies each event into `SendEmail` / `GenerateInvoice` / `Unknown` (disjoint by construction since `OutboxEventTypes.IsEmailSend` and `IsInvoiceGenerate` share zero values). Email events → `PublishSendEmailAsync`; invoice.generate events → `PublishGenerateInvoiceAsync`. Unknown still stalls Permanent.
- Per-queue config: `OutboxQueueOptions.GenerateInvoiceQueueName` (default `"generate-invoice"`) + validator regex `^[a-z0-9]([a-z0-9-]{1,61}[a-z0-9])?$` so typo'd queue names crash the host at boot.
- Storage queue publisher: `StorageQueueOutboxPublisher.PublishGenerateInvoiceAsync` mirrors the send-email path; independent semaphores so a Storage outage that blocks one queue's `CreateIfNotExistsAsync` doesn't stall the other.
- Email attachment seam: `EmailSendService.SendOrderPaidCustomerEmailAsync` extends with Invoice lookup → blob download → `Attachment` construction → wired into `EmailMessage`. Race against the invoice.generate queue is handled via `BusinessResult.Failure(Error.Transient(InvoiceNotYetRendered))` — the outbox retries, the second attempt almost always succeeds.
- Domain shape: new sealed record `backend/src/Makables.Core.Domain/Email/Attachment.cs` (`Filename` + `Bytes` + `MimeType` with non-empty invariants). `EmailMessage.Attachment` optional field defaulting to null per locked decision 8 (single attachment, not a list).
- SendGrid adapter: `SendGridEmailProvider.SendAsync` calls `sgMessage.AddAttachment` with base64-encoded bytes when `EmailMessage.Attachment` is non-null. 30 MB cap surfaces as `Error.Permanent(InvoicePdfAttachmentTooLarge)` — sniffed via HTTP 413 or 4xx body containing "too large" / "exceeds" / similar (locked decision 4 — outbox stalls for ops, retry can't resolve a fixed cap).
- 3 new error codes + Czech i18n: `InvoiceNotYetRendered` (Transient), `InvoicePdfAttachmentDownloadFailed` (Permanent), `InvoicePdfAttachmentTooLarge` (Permanent). All three are admin / log surface only — customer never sees them directly.

**T-0088 shipped (read-side download surface — customer + maker hosts):**

- Endpoints: `GET /api/v1/orders/{orderId}/invoice` on the Customer host AND the Maker host (host-relative; audience = host per ADR 0013). The routes are the literal strings T-0082's detail projections emit as `InvoicePdfUrl` — changing the emitted URL shape is forbidden (T-0088 §A.1).
- Shape: controller-direct streaming actions on the existing `OrdersController` of each host per ADR 0014 §"Handler-free read paths" (T-0075/T-0064 precedent — no MediatR feature). Lookup chain: session → ownership-scoped read-only order load (`GetByIdForCustomerReadOnlyAsync` NEW / `GetByIdForMakerReadOnlyAsync`, ADR 0025) → `IInvoiceRepository.GetByOrderIdAsync` (Unscoped — safe ONLY after the ownership pre-check) → `IBlobStorageClient.DownloadAsync(BlobContainer.Invoices, Invoice.PdfBlobPath)` verbatim.
- Headers: `Content-Disposition: attachment; filename="faktura-{InvoiceNumber}.pdf"`, `Content-Type: application/pdf`, `Cache-Control: private, no-store` + ETag/304 conditional GET (T-0064 PII policy — invoices carry recipient name/address/tax ids; NOT the T-0075 label `public, immutable` family). No range processing.
- 404 semantics: cross-tenant / unknown order → `order.notFound` (IDOR-oracle-free, same shape as nonexistent); owned order with no Invoice row, null `PdfBlobPath`, or blob-purged race → `invoice.notYetRendered` (transient-shaped; FE retry per the existing i18n copy). No re-render fallback inside the web request — rendering stays owned by the queue pipeline.

**T-0112a shipped (maker Fee-invoice download — read surface):**

- Read surface: `IInvoiceRepository.GetForMakerReadOnlyAsync(invoiceId, makerId, ct)` — read-only (`AsNoTracking`) mirror of `GetByIdForMakerAsync` with the same `i.Id == invoiceId && i.MakerId == makerId` IDOR predicate and the same null-for-unknown/cross-maker return shape (no oracle). Surfaces BOTH invoice families (the `InvoiceType.Fee` gate lives in the caller, so the repo stays type-agnostic). Declared on `backend/src/Makables.Core.Domain/Invoices/IInvoiceRepository.cs:110`; impl `backend/src/Makables.Infra.Database/Invoices/InvoiceRepository.cs:92`.
- Controller: backs the maker-host controller-direct Fee-invoice download (`Makables.Web.Maker/Controllers/FilesController.cs:215`) — the caller inspects only `Invoice.Type` (rejects non-`Fee`) + `Invoice.PdfBlobPath` + `Invoice.InvoiceNumber` and never mutates. Maker resolved from session → `IMakerRepository.GetByUserIdAsync`, NEVER from a request param. Analogous to the T-0088 `GetByOrderIdReadOnlyAsync` read-only-mirror precedent.
- `ForMaker` queryable now surfaces Fee invoices: the `:57 TODO (T-0101)` on `IInvoiceRepository.ForMaker` — "extend to also surface `InvoiceType.Fee` invoices targeting the maker via the PayoutBatch → MakerId join" — is **closed**. With `payout_batches` landed and the denormalised `Invoice.MakerId` populated for Fee invoices at issue time, the maker queryable + read-only single-load both cover Customer and Fee families. (The maker Fee-list projection itself rides T-0112/T-0116.)

**Out of scope at T-0069 (deferred):**

- Customer-facing PDF download endpoint (T-0086 per T-0068b locked decision 9 — strict OOS). *Backend endpoints shipped by T-0088 (see above); the FE CTA lands in T-0086b/T-0087b.*
- Fee invoices (`InvoiceType.Fee` rendering + PayoutBatch FK) — T-0101 / T-0102.
- ReverseCharge / StrictFiscalReporting renderers — post-MVP.
- Noto Sans subset .ttf embedding — follow-up.
- QR-image rendering (QRCoder + SkiaSharp) — follow-up.
- Admin UI for outbox / poison-queue triage — follow-up admin ticket.
- SendGrid bounce webhook (forwarded email failures) — deferred from T-0028.

## Related

- ADRs: 0003, 0009, 0013 (enforcement modes), 0019 (PDF attached to email via outbox), **0025 (QuestPDF + invoice rendering posture)**
- Roles: `order`, `payout-batch`, `invoice-numbering`, `blob-storage`
