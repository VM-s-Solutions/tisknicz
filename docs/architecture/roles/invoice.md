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

- **Created by:** `IssueInvoice.Command` — called as part of `MarkOrderPaid` (customer invoice) or `CreatePayoutBatch` (fee invoices)
- **Modified by:** no updates after issuance. Errata require a credit-note invoice (separate invoice with negative amounts, post-MVP).
- **Persisted by:** `IInvoiceRepository`
- **Destroyed by:** never. Even GDPR delete anonymizes — the legal record remains.

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

**Out of scope at T-0068b (deferred):**

- Customer-facing PDF download endpoint (T-0086 per locked decision 9 — strict OOS).
- `GenerateInvoiceFunction` queue-trigger dispatcher (T-0069).
- Fee invoices (`InvoiceType.Fee` rendering + PayoutBatch FK) — T-0101 / T-0102.
- ReverseCharge / StrictFiscalReporting renderers — post-MVP.
- Noto Sans subset .ttf embedding — follow-up.
- QR-image rendering (QRCoder + SkiaSharp) — follow-up.

## Related

- ADRs: 0003, 0009, 0013 (enforcement modes), 0019 (PDF attached to email via outbox), **0025 (QuestPDF + invoice rendering posture)**
- Roles: `order`, `payout-batch`, `invoice-numbering`, `blob-storage`
