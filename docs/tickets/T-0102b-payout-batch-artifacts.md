---
id: T-0102b
title: Payout batch artifacts — per-maker Fee invoices + bank CSV (IPayoutCsvFormatter seam) + fee-invoice maker emails + admin CSV download
status: ready
size: M
owner: dotnet-backend
created: 2026-06-12
updated: 2026-06-12
depends_on: [T-0101, T-0102a, T-0068b, T-0069]
blocks: [T-0103, T-0104, T-0112, T-0116, T-0118]
user_stories: [US-admin-0007, US-maker-0012]
adrs: [0003, 0009, 0011, 0014, 0019, 0020]
phase: 5
manual_steps: [bank-native-csv-exporter-follow-up-when-operator-names-bank]
security_touching: true
layers: [domain, appservices, infra-pdfrendering, infra-storage, infra-database, web-admin, functions]
---

# T-0102b — Payout batch artifacts: per-maker Fee invoices + bank CSV + fee-invoice maker emails + admin CSV download

## Context

Second slice of the T-0102 L-split (sister: T-0102a). T-0102a ships the `CreatePayoutBatch` command's claim core: the Delivered-order claim predicate (Q3/Q5 exclusions), the `PayoutBatch` insert with `IPayoutBatchNumberGenerator` (VYP-CZ-YYYY-Www, TZ-aware local-date derivation per T-0062/T-0068a precedent), the `payoutBatch.empty` guard, the re-run-returns-existing-batch idempotency, and `MakablesMeters.Payouts` instrumentation. T-0102b picks up everything the batch produces once it exists — the **financial artifacts**: one `InvoiceType.Fee` invoice per maker per batch (the first code path ever to exercise `Invoice.PayoutBatchId` on the XOR aggregate link), a bank-transfer CSV behind a new `IPayoutCsvFormatter` format seam, fee-invoice emails to makers (T-0069 PDF-attachment pattern), and the admin CSV download endpoint.

This ticket is **security_touching: true** — it generates legally immutable financial documents (Fee invoices in the shared FV-CZ sequence per ADR 0009) and the payment file an operator uploads to a bank. Per Q4, the batch is IMMUTABLE once created: Fee invoices are issued at batch creation and cannot be retracted by removing orders; whole-batch-cancel is a deferred follow-up.

The artifacts run **inside the same `CreatePayoutBatch` handler invocation, after the claim**, via a new internal service `IPayoutArtifactService.GenerateAsync(batch)` (T-0068b service-precedent shape; the handler stays thin). Because the UoW pipeline commits once per command (ADR 0014 — no `SaveChangesAsync()` in handlers), an artifact failure must NOT unclaim the batch: the handler catches the artifact failure, logs **Critical**, and still returns success so the claim + completed artifacts commit. The admin re-triggers via the existing re-run path, which detects missing artifacts and completes them (re-entrancy contract in §C.4 + AC-7/AC-8).

A **leading data-fix migration** also rides in this ticket per Q-0017: 16 previously seeded email-template-translation subject rows carry single-brace placeholders (`{order_number}`-style) that the double-brace substitution engine never expands. They are UPDATEd to double-brace before the new payout template seeds (which are double-brace from birth).

## Locked design decisions

Captured per `docs/process/deliberation.md`. The user locked 5 dimensions at the 2026-06-12 deliberation; PM absorbed the remainder from T-0068b/T-0069/T-0088 precedents.

### A. User-locked (non-negotiable)

1. **Q1 — Generic documented CSV behind a FORMAT SEAM.** New `IPayoutCsvFormatter` interface, keyed-service-ready. Columns: maker bank account, amount in CZK decimal display, VS = numeric part of the batch number, message = batch number + maker company name. Bank-native exporters (Fio/ČSOB/KB formats) are follow-up tickets once the operator names the bank — the seam means each is one new keyed implementation, zero handler changes. **Rejected:** picking a bank-native format now (operator hasn't named the bank; guessing wrong wastes the work); no seam, inline CSV building (locks the format into the service body).
2. **Q2 — Fee invoices per-batch at CreatePayoutBatch.** One `InvoiceType.Fee` invoice per maker per batch. DUZP = batch creation date (country-local). Shared FV-CZ sequence per T-0068a lock 4 — Customer and Fee invoices interleave in one gap-free sequence. **Rejected:** per-order fee invoices (invoice count explodes; makers reconcile per payout, not per order); monthly fee invoices decoupled from batches (breaks the payout↔invoice 1:1 audit trail).
3. **Q3 — Partially-refunded Delivered orders EXCLUDED from auto-claim.** `RefundedAmountMinor > 0` ⇒ stays unclaimed; surfaced in the batch response + audit; rides the next batch after admin resolution. Enforced by T-0102a's claim predicate; T-0102b's fee math therefore never needs refund proration — every claimed order's `PlatformFeeAmountMinor` is whole.
4. **Q4 — Batch IMMUTABLE once created (Processing).** No order removal — Fee invoices are already issued and legally immutable. Whole-batch-cancel = deferred follow-up ticket. **Rejected:** mutable batch with invoice credit-noting (credit notes are post-MVP per T-0068b).
5. **Q5 — NULL-BankAccount makers' orders EXCLUDED from claim** (T-0102a predicate); excluded-maker count surfaced in response + audit. T-0102b's formatter may therefore treat a null/blank bank account as a programmer error (throw, not BusinessResult — the claim invariant was violated upstream).

### B. ADR-locked (no relitigation)

- **ADR 0009 (numbering).** Fee invoices allocate from the same FV-CZ `NumberingSequence` row (FOR UPDATE lock, gap-free) via the existing `IInvoiceNumberGenerator`. Batch number comes from T-0102a; T-0102b only reads it.
- **ADR 0014 (UoW pipeline).** One command, one transaction. Claim + batch insert + Fee invoice rows + outbox rows commit together. Blob uploads are non-transactional side effects — safe because rendering is deterministic and uploads are overwrite-safe (T-0068b precedent).
- **ADR 0019/0020 (outbox + email chokepoint).** Fee-invoice maker emails go through the outbox; `EmailSendService` is the only `IEmailProvider` consumer; the attachment is looked up at SEND time (T-0069 pattern), never baked into the payload.
- **ADR 0011 (blob storage).** All file access through the backend. CSV download streams through the admin host; no direct browser → blob links.
- **ADR 0003 (money).** Minor units everywhere internally; the CSV's decimal display conversion happens only at the formatter edge.

### C. PM-absorbed (precedent-derived)

1. **Artifact orchestration = internal service `IPayoutArtifactService.GenerateAsync(batch, ct)`** at `Core.AppServices/Features/Payouts/`, invoked by `CreatePayoutBatch.Handler` post-claim. T-0068b precedent (service shape; handler stays thin). NOT a separate command — artifacts are not independently dispatchable; the re-run path is the only re-entry.
2. **Per-maker artifact unit is atomic-in-order:** issue Fee invoice → render PDF → upload blob → `AttachPdfBlobPath` → enqueue `payout.feeInvoice.makerEmail` outbox row. Attach + enqueue are DB-only ops committed together, so the invariant holds: **`PdfBlobPath` non-null ⇔ email row enqueued.** CSV generation runs after ALL makers complete.
3. **Artifact failure posture:** first failure aborts the artifact pipeline (skip remaining makers + CSV + emails for unprocessed makers), logs **Critical**, sets `ArtifactsComplete = false` on the response, and the handler returns SUCCESS so the claim + completed-maker artifacts commit. Never throw past the handler — a throw would roll back the claim.
4. **Re-entrancy contract (re-run path):** when `CreatePayoutBatch` re-runs and finds the open Processing batch (T-0102a returns it, never a second), `IPayoutArtifactService` resumes: (a) each batch maker WITHOUT a Fee invoice row → full per-maker unit; (b) each existing Fee invoice with `PdfBlobPath == null` → re-render (deterministic) + upload + attach + enqueue email; (c) `PayoutBatch.CsvBlobPath == null` → format + upload + attach set-once. A fully-artifacted batch re-run is a pure no-op returning the existing values (`Received(0)` on renderer/formatter/blob).
5. **CSV exact column spec (generic documented format):** UTF-8 **with BOM** (Czech bank tooling), **CRLF** line endings, **semicolon** delimiter (Czech-locale CSV convention). Header row `account;amount;vs;message`. One data row per maker, ordered by maker company name then maker id (deterministic). `account` = `Maker.BankAccount` verbatim (Czech `[prefix-]number/bankCode` format, already validated by `CzechBankAccountValidator`). `amount` = sum of the maker's `Order.MakerPayoutAmountMinor` in the batch, rendered as invariant-culture `0.00` decimal CZK (e.g. `123456` minor → `1234.56`). `vs` = digits of the batch number (`VYP-CZ-2026-W24` → `202624`, ≤ 10 digits — Czech VS limit). `message` = `{batchNumber} {makerCompanyName}` truncated to 140 chars. Spec is frozen as formatter golden-file tests; bank-native follow-ups get their own keyed formatter + spec.
6. **CSV blob:** new `BlobContainer.Payouts = "payouts"` (private; added to `BlobContainer.All`; `IsPublicRead` stays false), path `{cc}/{batchNumber}.csv` within it — full logical path `payouts/{cc}/{batchNumber}.csv` mirroring the invoice layout. `PayoutBatch.AttachCsvBlobPath` set-once mirroring `Invoice.AttachPdfBlobPath` (idempotent same-value retry succeeds; different value → `payoutBatch.csvBlobPathAlreadySet` Conflict). If T-0101/T-0102a's entity lacks the column, T-0102b adds it (additive migration).
7. **Fee invoice PDF blob path:** `{cc}/payouts/{payoutBatchId}/{invoiceNumber}.pdf` in `BlobContainer.Invoices` — mirrors the `{cc}/orders/{orderId}/{invoiceNumber}.pdf` convention.
8. **Fee invoice shape:** `Invoice.Issue` with `Type = Fee`, `OrderId = null`, `PayoutBatchId = batch.Id` (XOR side B — first user), `MakerId` = the maker, recipient = maker snapshot (company name or user full name per the T-0080 MakerName convention; email = maker user email; `RecipientTaxId` = maker IČO; `RecipientVatId` = maker DIČ), issuer = `CountryConfiguration` snapshot (T-0068b columns), `InvoicingMode` snapshot from config (None at MVP ⇒ zero VAT), amount = sum of the maker's `PlatformFeeAmountMinor` in the batch, `TaxableSupplyDate` (DUZP) = batch creation date in country TZ per Q2, `DueDate` = DUZP + 14 days.
9. **Fee QuestPDF template:** new `ProvizniDokladDocument` alongside T-0068b's two templates. Per-order line items "Provize za zprostředkování — obj. {orderNumber}". Needs line items the `Invoice` row doesn't carry ⇒ extend `IInvoicePdfRenderer` with `RenderFeeAsync(Invoice, IReadOnlyList<FeeInvoiceLineItem>, CountryConfiguration, ct)`; new `FeeInvoiceLineItem(string OrderNumber, long FeeAmountMinor)` record in `Core.Domain/Rendering/`. Determinism preserved (no `IClock` reads in the document).
10. **Email:** outbox `payout.feeInvoice.makerEmail` (added to `OutboxEventTypes` + `IsEmailSend`); payload `PayoutFeeInvoiceMakerEmailPayload(InvoiceId, MakerId, MakerEmail, BatchNumber, InvoiceNumber, FeeAmountMinor, Currency, ActionUrl, LanguageCode)`; `EmailSendService` branch reuses the T-0069 pattern — load invoice by id at send time, `PdfBlobPath` null ⇒ `Transient(InvoiceNotYetRendered)` for outbox re-delivery, `IBlobStorageClient.DownloadAsync` + `Attachment(filename, bytes, "application/pdf")`. Filename: en-US → `fee-invoice-{invoiceNumber}.pdf`, else `faktura-provize-{invoiceNumber}.pdf`. New `EmailTemplateType.PayoutFeeInvoiceMaker` (next free value) + seed migration with cs-CZ (tykání — maker audience) + en-US translations, **DOUBLE-BRACE subjects** per the Q-0017 lesson.
11. **Q-0017 data-fix:** leading migration `FixSingleBraceEmailSubjects` UPDATEs all 16 single-brace subject rows from the 4 prior seed migrations (SeedOrderEmailTemplates ×4, ShippingPipelineBundle ×4, DeliveryCloseBundle ×2, OrderCleanupBundle ×6 — implementer greps `subject` in those migration files for single-brace `{order_number}`-style values and rewrites to double-brace). Idempotent UPDATE by template type + language; Down() restores single-brace.
12. **Admin CSV download:** `GET /api/v1/admin/payout-batches/{id}/csv` — controller-direct streaming per T-0088 precedent (no MediatR query for a byte stream), `[Authorize]` admin scheme, `Content-Type: text/csv`, `Content-Disposition: attachment; filename="{batchNumber}.csv"`. 404 `payoutBatch.notFound`; 409 `payoutBatch.csvNotReady` when `CsvBlobPath` is null.
13. **`MakablesMeters.Payouts` instrumentation lives in T-0102a**; `payout-sent` settlement emails stay in T-0103 (PR #2); weekly timer Function is T-0104.
14. **New `BusinessErrorMessage` codes:** `PayoutBatchNotFound = "payoutBatch.notFound"`, `PayoutBatchCsvNotReady = "payoutBatch.csvNotReady"`, `PayoutBatchCsvBlobPathAlreadySet = "payoutBatch.csvBlobPathAlreadySet"`. Parallel cs-CZ i18n keys (l10n parallels dotnet-backend on this ticket per routing.md).

## Scope

### Domain layer

- **`Core.Domain/Payouts/IPayoutCsvFormatter.cs`** — NEW seam: `string Format(PayoutCsvBatch batch)`. Pure (string in/out; encoding + upload are the service's job). XML doc carries the §C.5 column spec verbatim.
- **`Core.Domain/Payouts/PayoutCsvBatch.cs` + `PayoutCsvLine.cs`** — NEW records: `PayoutCsvBatch(string BatchNumber, IReadOnlyList<PayoutCsvLine> Lines)`; `PayoutCsvLine(string BankAccount, long AmountMinor, string MakerCompanyName)`.
- **`Core.Domain/Rendering/IInvoicePdfRenderer.cs`** — extend with `RenderFeeAsync(...)` per §C.9; new `Core.Domain/Rendering/FeeInvoiceLineItem.cs`.
- **`Core.Domain/Storage/BlobContainer.cs`** — add `Payouts = "payouts"` (+ `All`).
- **`Core.Domain/Payouts/PayoutBatch.cs`** — `CsvBlobPath` + `AttachCsvBlobPath` set-once per §C.6 (here only if T-0101/T-0102a shipped without them).
- **`Core.Domain/Invoices/IInvoiceRepository.cs`** — add `GetByIdAsync(string id, ct)` (email-send lookup) + `GetByPayoutBatchIdAsync(string payoutBatchId, ct)` (re-entrancy detection).
- **`Core.Domain/Outbox/OutboxEventTypes.cs`** — add `PayoutFeeInvoiceMakerEmail = "payout.feeInvoice.makerEmail"` (+ `IsEmailSend` includes it).
- **`Core.Domain/Outbox/PayoutFeeInvoiceMakerEmailPayload.cs`** — NEW sealed record per §C.10.
- **`Core.Domain/Email/EmailTemplateType.cs`** — add `PayoutFeeInvoiceMaker` (next free value).
- **`Core.Domain/Common/BusinessErrorMessage.cs`** — 3 new codes per §C.14.

### AppServices layer

- **`Features/Payouts/GenericPayoutCsvFormatter.cs`** — NEW default `IPayoutCsvFormatter` implementation per §C.5. Pure; zero DI dependencies; throws `ArgumentException` on blank bank account (Q5 invariant).
- **`Features/Payouts/IPayoutArtifactService.cs` + `PayoutArtifactService.cs`** — NEW. `Task<PayoutArtifactResult> GenerateAsync(PayoutBatch batch, CancellationToken ct)` implementing §C.2–§C.4: per-maker units (steps 7+9), then CSV (step 8). Returns counts (invoices issued, emails enqueued, CSV generated) + `Complete` flag. Catches per-step failures; never throws past itself except cancellation.
- **`Features/Payouts/CreatePayoutBatch.cs`** (T-0102a file) — handler extension: after claim/lookup, call `artifactService.GenerateAsync(batch)`; map result onto the response (`ArtifactsComplete`, `FeeInvoiceCount`, `CsvReady`); Critical log on incomplete. No `SaveChangesAsync()`.
- **`Features/Email/EmailSendService.cs`** — new switch case + `SendPayoutFeeInvoiceMakerEmailAsync` branch per §C.10 (lookup-at-send + attachment; substitutions: `action_url`, `batch_number`, `invoice_number`, `fee_amount` via `FormatAmount`, `currency`, `language_code`).

### Infrastructure

- **`Infra.PdfRendering/QuestPdfInvoiceRenderer.cs`** — implement `RenderFeeAsync`; new nested `ProvizniDokladDocument` (Czech layout, "Nejsem plátce DPH" footer under InvoicingMode.None, per-order line rows, NBSP thousands separators, `d. M. yyyy` dates).
- **`Infra.Database/Invoices/InvoiceRepository.cs`** — implement the 2 new methods.
- **`Infra.Database/Migrations/<ts>_FixSingleBraceEmailSubjects.cs`** — leading Q-0017 data-fix per §C.11.
- **`Infra.Database/Migrations/<ts>_SeedPayoutFeeInvoiceEmailTemplate.cs`** — template + cs-CZ/en-US translations, double-brace subjects.
- **DI** — register `IPayoutCsvFormatter → GenericPayoutCsvFormatter` (singleton, keyed-ready) + `IPayoutArtifactService → PayoutArtifactService` (scoped) in `AddMakablesXxx` extensions.

### Web.Admin host

- **`Web.Admin/Controllers/PayoutBatchesController.cs`** — add `GET /api/v1/admin/payout-batches/{id}/csv` per §C.12 (controller-direct blob stream; `[Authorize]` admin; `FileStreamResult` text/csv).

### Tests (TDD red-first on the pure formatter)

- **`Makables.Tests/AppServices/Features/Payouts/GenericPayoutCsvFormatterTests.cs`** (~5, written RED first): golden-file exact-string match for a 2-maker batch (header, CRLF, semicolons, BOM-free string — BOM is encoding-layer); minor→decimal display (`123456` → `1234.56`); VS digit extraction (`VYP-CZ-2026-W24` → `202624`); 140-char message truncation; deterministic row ordering; blank bank account throws.
- **`Makables.Tests/AppServices/Features/Payouts/PayoutArtifactServiceTests.cs`** (~3): fee-invoice math (per-maker `PlatformFeeAmountMinor` sums; None mode ⇒ zero VAT; DUZP = batch local date); re-run skips complete makers (`Received(0)` renderer for them, missing ones completed); mid-pipeline failure ⇒ `Complete == false`, earlier makers persisted, CSV not attempted.
- **`Makables.IntegrationTests/Payouts/PayoutBatchArtifactsIntegrationTests.cs`** (~3): end-to-end CreatePayoutBatch with 2 makers ⇒ 2 Fee invoices in the shared FV-CZ sequence + 2 PDF blobs + 1 CSV blob + `CsvBlobPath` set + 2 `payout.feeInvoice.makerEmail` outbox rows; re-run after simulated artifact failure completes missing artifacts with no duplicate invoice numbers and a single CSV upload; admin CSV endpoint 200 text/csv with golden content + 401 anonymous + 409 when CSV not ready.

### NSwag regen

New admin endpoint ⇒ **regen REQUIRED in the same PR** (admin host client). File-stream response types as the generated file-response shape; no manual `lib/api-client/` edits.

## Alternatives Considered

- **Bank-native CSV format now (Fio/KB/ČSOB).** *Rejected per Q1* — the operator hasn't named the bank; the seam makes each native exporter a one-class follow-up.
- **Per-order Fee invoices.** *Rejected per Q2* — invoice volume explodes; makers reconcile per payout. Per-batch keeps the payout↔invoice audit trail 1:1.
- **Include partially-refunded Delivered orders with prorated fees.** *Rejected per Q3* — proration math on a legal document invites disputes; admin resolves first, the order rides the next batch.
- **Mutable batch (remove orders pre-payment).** *Rejected per Q4* — Fee invoices are issued + legally immutable at creation; removal would require credit notes (post-MVP).
- **Skip-the-maker-in-CSV for NULL bank accounts.** *Rejected per Q5* — excluded at CLAIM instead, so the batch totals, fee invoices, and CSV always agree.
- **Artifacts as a separate outbox-driven command (`payout.artifacts.generate`).** *Rejected per §C.1/§C.3* — splits one admin action across an async boundary the admin then has to poll; the re-run path already provides the retry surface, and the single-UoW claim is untouched either way.
- **Throw on artifact failure (roll back the claim).** *Rejected per §C.3* — unclaiming after FV-CZ numbers were allocated burns sequence numbers and re-issues invoices on retry; committed-claim + resumable artifacts is strictly safer.
- **Formatter returns `byte[]` with BOM.** *Rejected per §C.5* — string-in/string-out keeps golden-file tests trivial; the artifact service owns encoding (`UTF8Encoding(true)`).
- **MediatR query for the CSV download.** *Rejected per §C.12* — T-0088 precedent: byte streams don't fit `BusinessResult<T>` envelopes; controller-direct streaming with `[Authorize]` is the established shape.
- **Defer the Q-0017 data-fix to its own ticket.** *Rejected per PM default* — this PR seeds new templates; shipping new double-brace seeds while 16 broken rows sit in the same table invites copy-paste of the broken shape. The fix leads.

## Out of scope

- **`payout-sent` settlement emails + MarkPayoutBatchCompleted** — T-0103 (PR #2). Delivered → Completed transition included.
- **Weekly timer Function (Monday 02:00 UTC)** — T-0104.
- **Bank-native CSV exporters** — follow-up tickets once the operator names the bank (manual_step tracks it).
- **Whole-batch-cancel** — deferred follow-up per Q4.
- **Credit notes / Fee invoice errata** — post-MVP per T-0068b.
- **Maker-facing payout list + fee-invoice download** — T-0112 (queries) + T-0116 (frontend).
- **Admin frontend payout UI** — T-0118.
- **`MakablesMeters.Payouts`** — T-0102a.
- **Claim predicate changes (Q3/Q5 exclusions, response/audit surfacing of excluded counts)** — T-0102a owns the claim; T-0102b consumes the claimed set as-is.

## Acceptance criteria

- **AC-1** Given a Processing batch with orders from 2 makers (fees 3×10000 + 2×5000 minor), when artifacts generate, then exactly 2 `InvoiceType.Fee` invoice rows exist with `PayoutBatchId = batch.Id`, `OrderId IS NULL`, `MakerId` set, amounts 30000 and 10000 minor, zero VAT (InvoicingMode.None), DUZP = batch creation date in country TZ, numbers allocated from the shared FV-CZ sequence (gap-free, interleavable with Customer invoices).
- **AC-2** Given AC-1, then each Fee invoice has a rendered PDF at `invoices` container path `{cc}/payouts/{payoutBatchId}/{invoiceNumber}.pdf`, `PdfBlobPath` set, and the PDF contains "Provize za zprostředkování" with one line per claimed order (order number + fee amount) and a balanced total.
- **AC-3** Given AC-1, then one CSV exists at `payouts/{cc}/{batchNumber}.csv` (UTF-8 BOM, CRLF, semicolon-delimited) with header `account;amount;vs;message` + one row per maker matching the §C.5 spec exactly (golden-file assert), and `PayoutBatch.CsvBlobPath` is set-once.
- **AC-4** Given AC-1, then exactly 2 outbox rows with event_type `payout.feeInvoice.makerEmail` exist, payloads carrying `InvoiceId`/`MakerEmail`/`BatchNumber`/`LanguageCode`; when `EmailSendService.SendAsync` processes one, the email carries the fee-invoice PDF attachment (language-aware filename) and double-brace-substituted subject.
- **AC-5** Given a fee invoice whose `PdfBlobPath` is still null at send time, when the email branch runs, then it returns `Transient(InvoiceNotYetRendered)` for outbox re-delivery — no send, no crash.
- **AC-6** Given the fee-invoice render throws for maker 2 of 3, when `CreatePayoutBatch` completes, then the command returns SUCCESS with `ArtifactsComplete = false`, a Critical log is written, the batch + maker-1 artifacts are committed, and no CSV exists.
- **AC-7** Given AC-6, when `CreatePayoutBatch` re-runs, then the existing batch is returned (no second batch), makers 2+3 get invoices + emails, the CSV is generated, `ArtifactsComplete = true`, and maker 1 is untouched (no duplicate invoice, number, blob upload, or email row).
- **AC-8** Given a fully-artifacted batch, when `CreatePayoutBatch` re-runs, then the response returns the existing values and renderer/formatter/blob-upload mocks record zero calls.
- **AC-9** Given an admin JWT, `GET /api/v1/admin/payout-batches/{id}/csv` streams the blob with `Content-Type: text/csv` + attachment filename `{batchNumber}.csv`; anonymous → 401; unknown id → 404 `payoutBatch.notFound`; `CsvBlobPath` null → 409 `payoutBatch.csvNotReady`. A customer/maker JWT cannot be replayed against the admin host (audience enforcement).
- **AC-10** After the leading `FixSingleBraceEmailSubjects` migration, zero `email_template_translations.subject` values contain a single-brace placeholder (`{x}` not preceded/followed by another brace) — all 16 rows from the 4 prior seed migrations are double-brace; the new payout seeds are double-brace from birth.
- **AC-11** Build clean; formatter tests written red-first; unit baseline + ~8 new, integration baseline + ~3 new; consistency script exit 0; NSwag admin client regenerated in the same PR; cs-CZ i18n keys exist for the 3 new error codes.

## Risk

- **Financial-document integrity (HIGH).** Fee invoices are legally immutable; a math or DUZP bug ships into makers' accounting. Mitigated by `Invoice.Issue` balance invariants + AC-1 + reviewer spot-check on the fee-sum query.
- **Bank-file correctness (HIGH).** A malformed CSV silently mis-pays makers. Mitigated by the golden-file spec freeze (AC-3) + Q5's claim-time guarantee that every row has a validated bank account.
- **Re-entrancy gaps (MEDIUM).** A wrong resume check duplicates invoice numbers. Mitigated by the §C.4 contract + AC-7/AC-8 `Received(0)` assertions.
- **Sequence contention (LOW).** Fee issuance holds the FV-CZ FOR UPDATE lock inside the batch transaction; batches are weekly + small at MVP scale.

## Test plan reference

Inline above (Scope > Tests). No separate `docs/test-plans/T-0102b.md`.

## Files touched (expected)

**New:** `Core.Domain/Payouts/IPayoutCsvFormatter.cs`, `PayoutCsvBatch.cs`, `PayoutCsvLine.cs`; `Core.Domain/Rendering/FeeInvoiceLineItem.cs`; `Core.Domain/Outbox/PayoutFeeInvoiceMakerEmailPayload.cs`; `Core.AppServices/Features/Payouts/GenericPayoutCsvFormatter.cs`, `IPayoutArtifactService.cs`, `PayoutArtifactService.cs`; `Infra.Database/Migrations/<ts>_FixSingleBraceEmailSubjects.cs` + `<ts>_SeedPayoutFeeInvoiceEmailTemplate.cs` (+ Designers); `Web.Admin/Controllers/PayoutBatchesController.cs` (or extend T-0102a's); the 3 test files.

**Modified:** `Core.Domain/Rendering/IInvoicePdfRenderer.cs`; `Core.Domain/Storage/BlobContainer.cs`; `Core.Domain/Payouts/PayoutBatch.cs`; `Core.Domain/Invoices/IInvoiceRepository.cs`; `Core.Domain/Outbox/OutboxEventTypes.cs`; `Core.Domain/Email/EmailTemplateType.cs`; `Core.Domain/Common/BusinessErrorMessage.cs`; `Core.AppServices/Features/Payouts/CreatePayoutBatch.cs`; `Core.AppServices/Features/Email/EmailSendService.cs`; `Infra.PdfRendering/QuestPdfInvoiceRenderer.cs`; `Infra.Database/Invoices/InvoiceRepository.cs`; `Config/Extensions/AddMakables*.cs` (DI); `frontend/src/lib/api-client/*` (NSwag admin regen); `frontend/src/lib/i18n/cs-CZ.ts` (3 keys); `docs/architecture/roles/invoice.md` + `payout-batch.md` (implementation pointers); `docs/questions/open.md` (Q-0017 → answered-by-T-0102b note); `docs/tickets/INDEX.md`.

## Suggested commits

1. `fix(T-0102b): Q-0017 data-fix migration — double-brace 16 seeded email subjects`
2. `test(T-0102b): pin GenericPayoutCsvFormatter golden files (red)`
3. `feat(T-0102b): CSV formatter seam + PayoutBatch.AttachCsvBlobPath + repository extensions`
4. `feat(T-0102b): PayoutArtifactService — fee invoices + ProvizniDokladDocument + CSV + email enqueue`
5. `feat(T-0102b): EmailSendService payout branch + template seed + admin CSV endpoint + NSwag regen`
6. `test(T-0102b): artifact service + integration coverage`

## Definition of ready

- [x] T-0101 (PayoutBatch entity) + T-0102a (claim core) precede in the same PR sequence; this slice does not start until both compile on the branch.
- [x] 5 user-locked decisions captured (§A); PM defaults captured (§C); re-entrancy contract written (§C.4).
- [x] CSV column spec frozen in-ticket (§C.5).
- [x] Q-0017 absorbed with explicit row inventory.
- [x] AC traceable; security posture (admin-only download, immutable financial docs) explicit.

## Status log

- 2026-06-12 `draft` by PM. Created as the second slice of the T-0102 L-split (sister: T-0102a claim core). Source: 2026-06-12 deliberation (Q1–Q5 user-locked) + T-0068b/T-0069/T-0088 precedents.
- 2026-06-12 `draft → ready` by BA. Locked decisions §A.1–A.5 transcribed verbatim from the user deliberation; §C PM-absorbed defaults captured including the §C.4 re-entrancy contract, §C.5 frozen CSV spec, and the Q-0017 leading data-fix. Implementer processes T-0102a → T-0102b sequentially on the same branch; both ship in one PR (PR #1 of the payout bundle; T-0103 settlement = PR #2).
