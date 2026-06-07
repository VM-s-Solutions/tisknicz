---
id: T-0068b
title: IInvoiceService.IssueAsync + QuestPDF renderer + blob upload + MarkOrderPaid InvoiceGenerate enqueue
status: draft
size: M
owner: dotnet-backend
created: 2026-06-06
updated: 2026-06-06
depends_on: [T-0067, T-0068a]
blocks: [T-0069, T-0102]
user_stories: [US-customer-0010, US-customer-0017, US-admin-0012]
adrs: [0003, 0009, 0011, 0013, 0014, 0019, 0020]
phase: 4
manual_steps: [questpdf-license-confirmation, sendgrid-template-id]
security_touching: false
layers: [domain, appservices, infra-pdfrendering, infra-storage]
---

# T-0068b — IInvoiceService.IssueAsync + QuestPDF renderer + blob upload + MarkOrderPaid InvoiceGenerate enqueue

## Context

Second half of the T-0068 L-split (sister: T-0068a). T-0068a shipped the Invoice entity, EF migration, scoped repository, and TZ-aware `IInvoiceNumberGenerator`. T-0068b picks up everything that requires those to already exist: the PDF renderer, the orchestrating service, the blob upload, and the third `outbox.Enqueue` call in `MarkOrderPaid.Handler` (the one T-0067 explicitly deferred via decision Q2).

Cannot start until T-0068a AND T-0067 are merged.

## Scope

- **IPdfRenderer** at `Core.Domain/Rendering/IPdfRenderer.cs` — abstraction so the AppServices layer never depends on QuestPDF directly.
- **QuestPdfInvoiceRenderer** at new `Infra.PdfRendering` project. `QuestPDF.Settings.License = LicenseType.Community` pinned at startup. Embedded Czech-glyph font (Noto Sans subset). Two `IDocument` templates:
  - `InvoicingMode.None` → Doklad o prodeji with "Nejsem plátce DPH" footer (per T-0068a locked decision 2).
  - `InvoicingMode.StandardVat` → full § 29 daňový doklad with per-line + summary VAT breakdown.
- **SPAYD QR** via QrCoder + SkiaSharp on every invoice (account number + amount + variable symbol = order number).
- **IInvoiceService.IssueAsync** at `Core.AppServices/Services/InvoiceService.cs`. Switch on `InvoicingMode`: `None` + `StandardVat` ship; `ReverseCharge` + `StrictFiscalReporting` return `BusinessResult.Failure(InvoicingModeNotImplemented)`.
- **Blob upload** to `invoices/{cc}/orders/{orderId}/{invoiceNumber}.pdf` with idempotent overwrite-guard (renderer is deterministic; same invoice number → same content → safe to re-upload on retry per T-0068a decision 5).
- **`OutboxEventTypes.InvoiceGenerate` constant + `InvoiceGenerateOutboxPayload`** record. Pre-bake `BlobPath` and customer/maker addresses.
- **MarkOrderPaid.Handler** gains the third `outbox.Enqueue` call (T-0067 marker comment is the insertion site).
- **ADR amendment** documenting QuestPDF + SPAYD + font choices.
- **l10n batch** — Czech keys for new BusinessErrorMessage codes (`InvoicingModeNotImplemented`, etc.) plus the `InvoiceBlobPathAlreadySet` key deferred from T-0068a.

## Out of scope

- GenerateInvoice Function + queue-triggered dispatcher routing — T-0069.
- Customer-facing PDF download endpoint — downstream ticket (T-0086 or similar).
- Admin-facing PDF download endpoint — downstream.
- Fee invoices (`InvoiceType.Fee` rendering + PayoutBatch FK) — T-0101 / T-0102.
- ReverseCharge + StrictFiscalReporting renderers — post-MVP.

## Acceptance criteria

Will be expanded when T-0068b transitions from draft to ready (after T-0068a merges). At minimum the AC will cover:
- Renderer produces a Czech-compliant PDF for both modes that opens in Adobe Acrobat + Apple Preview.
- Blob upload is idempotent (same invoice number → no duplicate row, no overwrite collision).
- MarkOrderPaid enqueues exactly 3 outbox rows (customer email + maker email + invoice.generate).
- T-0067 negative-pin test ("does NOT enqueue invoice.generate") is removed in the same PR.

## Status log

- 2026-06-06 `draft` by PM. Created as part of T-0068 L-split. Will transition to ready after T-0068a merges (Invoice entity + numbering must exist before this slice can lock its public API).
