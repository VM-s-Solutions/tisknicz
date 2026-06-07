---
id: T-0068b
title: IssueInvoice command + QuestPdfInvoiceRenderer + blob upload + MarkOrderPaid InvoiceGenerate enqueue
status: ready
size: M
owner: dotnet-backend
created: 2026-06-06
updated: 2026-06-07
depends_on: [T-0067, T-0068a]
blocks: [T-0069, T-0086, T-0102]
user_stories: [US-customer-0010, US-customer-0017, US-admin-0012]
adrs: [0003, 0009, 0011, 0013, 0014, 0019, 0020]
phase: 4
manual_steps: [questpdf-license-pin, country-config-issuer-seed]
security_touching: false
layers: [domain, appservices, infra-pdfrendering, infra-storage, database]
---

# T-0068b — IssueInvoice command + QuestPdfInvoiceRenderer + blob upload + MarkOrderPaid InvoiceGenerate enqueue

## Context

Second half of the T-0068 L-split (sister: T-0068a). T-0068a shipped the Invoice entity, EF migration, scoped `IInvoiceRepository`, and TZ-aware `IInvoiceNumberGenerator`. T-0068b picks up everything that requires those to already exist: the PDF renderer, the orchestrating use-case, the blob upload, and the third `outbox.Enqueue` call in `MarkOrderPaid.Handler` (the one T-0067 explicitly deferred via decision Q2 with a marker comment at `MarkOrderPaid.cs:204`).

Cannot start until BOTH T-0068a AND T-0067 are merged. T-0068a's interface migrations (`IInvoiceNumberGenerator.NextAsync(string, CancellationToken)`) AND T-0067's marker comment are hard prerequisites; without them, the diff hits trivial merge conflicts.

This is also the ticket where the **Czech-invoice legal format compliance** lands end-to-end — both `InvoicingMode.None` (Doklad o prodeji, non-VAT-payer receipt) and `InvoicingMode.StandardVat` (full § 29 daňový doklad). § 29 zákona č. 235/2004 Sb. o DPH dictates the mandatory fields; the renderer's two `IDocument` templates are the legal-format compliance surface.

## Locked design decisions (from `/feature` deliberation)

Captured per `docs/process/deliberation.md`. The user answered 8 blocking AskUserQuestion items before this ticket transitioned to ready. These are non-negotiable for the implementing agent; revisiting requires a new ADR + a follow-up ticket.

### A. Library choices (locked at grooming, not negotiable in implementation)

1. **QuestPDF license tier = Community (MIT, free).** JVM YORE s.r.o. qualifies at MVP (revenue < $1M USD AND employees < 10 AND not state-funded). Pin `QuestPDF.Settings.License = LicenseType.Community` at the `Infra.PdfRendering` project's startup hook (referenced from each `Web.*` host's `Program.cs` via the new `AddMakablesPdfRendering()` extension). **ADR amendment required:** the new ADR (0025 or next number) documents the qualification criteria so a future revenue milestone triggers a license review. **Rejected alternative:** QuestPDF Pro (€699/yr) — premature optimization; Community is sufficient.
2. **Embedded font = Noto Sans (subsetted to ASCII + Latin Extended-A + currency symbols).** SIL Open Font License; free + redistributable. Czech-glyph coverage is excellent. Subset size target: ~80 KB embedded in the assembly. Lives at `Infra.PdfRendering/Fonts/NotoSans-Regular-CzechSubset.ttf` + `NotoSans-Bold-CzechSubset.ttf`. Subset generation is one-time manual prep (using `pyftsubset` or fonttools); the resulting `.ttf` is committed binary. **Rejected alternatives:** DejaVu Sans (larger embedded size, no subsetting), system Arial/Helvetica (zero embed but PDF degrades on Linux servers without the system font).
3. **Renderer interface scope = invoice-specific `IInvoicePdfRenderer`.** Single-purpose: takes an `Invoice` + `InvoicingMode` + `CountryConfiguration` + (optional `SpaydQrCode`) and returns a PDF `byte[]`. Lives at `Core.Domain/Rendering/IInvoicePdfRenderer.cs`. **Rejected alternative:** generic `IPdfRenderer<TPayload>` — speculative abstraction; when T-0074 ships shipping-label PDFs, it gets its own `IShippingLabelRenderer`. YAGNI per CLAUDE.md.

### B. SPAYD QR code + bank-account source

4. **Add `platform_iban VARCHAR(34) NULL` to `country_configurations`.** Schema change (small EF migration). NULL at MVP — JVM YORE's bank-account decision is open; renderer skips SPAYD when `platform_iban IS NULL` and renders the invoice without QR. When admin later populates the IBAN (via DB seed or admin UI in a downstream ticket), SPAYD QR code automatically appears on new invoices. Existing already-issued invoices are unaffected (PDFs are blob-stored and frozen). **Rejected alternatives:** defer SPAYD entirely (loses the wiring), hard-code in CZ seed (blocks T-0068b on bank-account decision).

### C. Czech-invoice legal compliance

5. **Two `IDocument` templates per `InvoicingMode`:**
   - **`InvoicingMode.None`** → `DokladOProdejiDocument` — Czech-language "Doklad o prodeji" (sale receipt) with footer "Nejsem plátce DPH" ("Not a VAT payer"). Fields per Czech accounting practice (not § 29 — that's VAT-only): issuer (JVM YORE name/IČO), recipient (customer name/email), invoice number (FV-CZ-YYYY...), issue date, item description, total amount (no VAT breakdown), payment reference (variable symbol = order number), optional SPAYD QR.
   - **`InvoicingMode.StandardVat`** → `DanovyDokladDocument` — full § 29 daňový doklad. All § 29 mandatory fields: issuer name + IČO + DIČ, recipient name + email + (optional IČO/DIČ), invoice number, issue date, **DUZP** (datum uskutečnění zdanitelného plnění = `Order.PaidAt` per T-0068a locked decision 3), due date, item rows with per-line VAT rate + base + VAT amount, summary VAT block (sum of bases per rate + sum of VAT amounts), total without VAT, total VAT, total with VAT, payment reference, optional SPAYD QR.
6. **InvoicingMode switch:** `IssueInvoice.Handler` reads `InvoicingMode` from `Order.CountryConfiguration` (resolved at issuance time, snapshotted onto the new `Invoice` row per T-0068a locked decision). Switch:
   - `None` → renders `DokladOProdejiDocument`, succeeds.
   - `StandardVat` → renders `DanovyDokladDocument`, succeeds.
   - `ReverseCharge` → `BusinessResult.Failure(Error.Permanent(BusinessErrorMessage.InvoicingModeNotImplemented))` with descriptive message. Outbox event consumed (the failure is logged via the outbox retry policy stall mechanism); a follow-up ticket implements ReverseCharge rendering.
   - `StrictFiscalReporting` → same as above (Czech EET was repealed in 2023; the mode exists for non-Czech country expansion).

### D. Implementation shape (non-Cleansia-port)

7. **`InvoiceService.IssueAsync` shape = one-file feature `Features/Invoices/IssueInvoice.cs`** — NOT `Services/InvoiceService.cs`. The dotnet-backend charter §C says cross-feature services go in `Services/`, BUT every existing tisknicz use case from T-0050 onwards lives as a one-file feature (`MarkOrderPaid.cs`, `CreatePaymentSession.cs`, etc.). The `consistency-script` T1 rule enforces the one-file shape under `Features/`. The "Service" framing in the original T-0068 ticket was Cleansia-port language; tisknicz precedent overrides. Implementer follows the one-file pattern: `public static class IssueInvoice` containing nested `record Command`, `record Response`, `class Validator`, `class Handler`.
8. **Issuer values from `country_configurations` columns + CZ seed migration.** Schema change: add 3 columns to `country_configurations`: `issuer_name VARCHAR(200) NOT NULL`, `issuer_ico CHAR(8) NOT NULL`, `issuer_dic VARCHAR(15) NULL` (nullable per T-0068a locked decision 2 — JVM YORE is not VAT-registered). CZ seed in the same migration: `issuer_name='JVM YORE s.r.o.'`, `issuer_ico='<actual IČO — to be confirmed at PR time>'`, `issuer_dic=NULL`. The implementer flags the IČO value as a `manual_steps: [country-config-issuer-seed]` PR-blocker until the user supplies it.

### E. Customer-facing PDF download

9. **Strict out-of-scope at T-0068b.** Customer PDF download endpoint lands in T-0086 (or wherever the customer-side reads order details). T-0068b ships rendering + blob storage only; the blob path is recorded on `Invoice.PdfBlobPath` so T-0086 can stream it from the blob. **Rejected alternative:** stub controller returning 501 — violates CLAUDE.md "no mocks during build phase" rule.

### F. T-0067 test diff

10. **Convert `Handler_does_NOT_enqueue_invoice_generate_yet` (T-0067) to positive pin.** Rename to `Handler_enqueues_invoice_generate_outbox_row`, flip assertions: `_outbox.Received(1).Enqueue(OrderId, OutboxEventTypes.InvoiceGenerate, Arg.Any<string>())` instead of `_outbox.DidNotReceive()`; total Enqueue count 3 instead of 2. Preserves test slot + lineage. **Rejected alternative:** delete the test outright — loses the lineage; the positive pin tells future readers what the right shape is.

## Scope

### Domain

- **`IInvoicePdfRenderer`** at `Core.Domain/Rendering/IInvoicePdfRenderer.cs`. Interface: `Task<byte[]> RenderAsync(Invoice invoice, CountryConfiguration country, CancellationToken ct)`. Reads `InvoicingMode` off the snapshotted `Invoice.InvoicingMode` (not `country.DefaultInvoicingMode` — invoice mode is frozen at issuance per T-0068a locked decision 2).
- **`Spayd`** value-object at `Core.Domain/Payments/Spayd.cs` (helper for SPAYD format string + amount serialization). Static factory `Spayd.ForInvoice(string iban, long amountMinor, string currency, string variableSymbol)`. Renders the standardised `SPD*1.0*ACC:<iban>*AM:<amount>*CC:<ccy>*X-VS:<vs>` string per the SPAYD spec.
- **`CountryConfiguration` extensions** — add 4 columns: `IssuerName`, `IssuerIco`, `IssuerDic`, `PlatformIban` per decisions 4 + 8.

### AppServices

- **`Features/Invoices/IssueInvoice.cs`** — one-file feature per locked decision 7. `Command(string OrderId)`. `Response(string InvoiceId, string InvoiceNumber, string PdfBlobPath)`. `Validator` checks `OrderId` non-empty. `Handler` (primary-ctor DI):
  1. Loads `Order` via `IOrderRepository.GetByIdUnscopedAsync` (handler is called by an outbox consumer, no audience scope).
  2. Loads `CountryConfiguration` via `ICountryConfigurationRepository.GetByCodeAsync(order.CountryCode)`.
  3. **Idempotency pre-check** (per T-0068a `IInvoiceRepository.GetByOrderIdAsync` XML doc): if an invoice already exists for this order, returns success with the existing values. Webhook re-delivery sees the same response, no duplicate render or upload.
  4. Allocates invoice number via `IInvoiceNumberGenerator.NextAsync(order.CountryCode, ct)`.
  5. Builds the `Invoice` aggregate via `Invoice.Issue(...)` with snapshotted issuer values (from CountryConfiguration) + recipient values (from Order) + money (from Order) + mode (from CountryConfiguration.DefaultInvoicingMode) + DUZP (from Order.PaidAt per T-0068a locked decision 3).
  6. Persists the row via `IInvoiceRepository.AddAsync` — UoW pipeline commits.
  7. Renders the PDF via `IInvoicePdfRenderer.RenderAsync`.
  8. Uploads to blob: `IBlobStorageClient.UploadAsync(BlobContainer.Invoices, "{cc}/orders/{orderId}/{invoiceNumber}.pdf", pdfBytes, overwrite: true)`. Overwrite is safe because the renderer is deterministic (same invoice → same PDF) per T-0068a locked decision 5.
  9. Calls `Invoice.AttachPdfBlobPath(blobPath)` — set-once succeeds first time, idempotent on retry.
- **`Core.AppServices/Common/BusinessErrorMessage.cs`** — add `InvoicingModeNotImplemented = "invoice.invoicingModeNotImplemented"`, `InvoiceRenderFailed = "invoice.renderFailed"`, `InvoiceBlobUploadFailed = "invoice.blobUploadFailed"`. (Plus the `InvoiceBlobPathAlreadySet` deferred from T-0068a is referenced here for the first time.)
- **`Core.Domain/Outbox/OutboxEventTypes.cs`** — add `InvoiceGenerate = "invoice.generate"`. Extend `IsEmailSend` enumeration check (NOT an email).
- **`Core.Domain/Outbox/InvoiceGenerateOutboxPayload.cs`** — new sealed record. Fields: `OrderId`, `LanguageCode` (pre-resolved for downstream invoice-email attachment in T-0069). NO recipient email — that's resolved by T-0069's email-attachment step.

### Infrastructure

- **New project `Makables.Infra.PdfRendering`** under `backend/src/`. Adds projects: NuGet refs `QuestPDF` (Community), `QRCoder` (optional — needed only for SPAYD QR), `SkiaSharp` (for QRCoder Bitmap output). References `Core.Domain` (read-only). Project sets `<ItemGroup><EmbeddedResource Include="Fonts/NotoSans-Regular-CzechSubset.ttf" /><EmbeddedResource Include="Fonts/NotoSans-Bold-CzechSubset.ttf" /></ItemGroup>`.
- **`QuestPdfInvoiceRenderer` at `Infra.PdfRendering/QuestPdfInvoiceRenderer.cs`** — implements `IInvoicePdfRenderer`. Loads embedded fonts at startup; pins `QuestPDF.Settings.License = LicenseType.Community`. Branches on `invoice.InvoicingMode`:
  - `None` → constructs + renders `DokladOProdejiDocument`.
  - `StandardVat` → constructs + renders `DanovyDokladDocument`.
  - Other → throws `NotImplementedException` (caller catches and translates to `InvoicingModeNotImplemented`).
- **`DokladOProdejiDocument` + `DanovyDokladDocument`** as nested `IDocument` types within `QuestPdfInvoiceRenderer.cs`. Czech-language layout. Bold for headings + totals; regular for body. NBSP thousands separator for amounts per `MoneyFormatter` precedent. Date format `d. M. yyyy` per Czech convention.
- **DI**: `AddMakablesPdfRendering()` extension at `Makables.Config/Extensions/AddMakablesPdfRendering.cs`. Pins QuestPDF license + registers `IInvoicePdfRenderer → QuestPdfInvoiceRenderer` as Singleton (renderer is stateless + reusable). Called from each `Web.*` host's `Program.cs`.
- **EF migration `AddCountryConfigurationIssuerAndIban`** at `Makables.Infra.Database/Migrations/`. Adds 4 columns. Updates CZ seed row. Migration name singular per T-0067 precedent.

### MarkOrderPaid wiring

- **`MarkOrderPaid.Handler`** (at `Features/Orders/MarkOrderPaid.cs`):
  - Replace the `// T-0068: enqueue invoice.generate here` marker comment with the actual third `outbox.Enqueue` call.
  - Payload = `new InvoiceGenerateOutboxPayload(OrderId: order.Id, LanguageCode: customerLanguage)`.
  - Event type = `OutboxEventTypes.InvoiceGenerate`.
- **`MarkOrderPaidHandlerTests`** (Tests file from T-0067):
  - **Convert** `Handler_does_NOT_enqueue_invoice_generate_yet` → `Handler_enqueues_invoice_generate_outbox_row` (positive pin per locked decision 10). Flip assertions, total Enqueue count 3.
  - Update `Customer_user_missing_returns_OrderCustomerUserMissing_and_skips_outbox` + similar negative-path tests: the `DidNotReceive().Enqueue` assertions still hold (all 3 events skipped on failure).

### Tests

- **`Makables.Tests/AppServices/Features/Invoices/IssueInvoiceHandlerTests.cs`** — NSubstitute mocks for `IOrderRepository`, `ICountryConfigurationRepository`, `IInvoiceRepository`, `IInvoiceNumberGenerator`, `IInvoicePdfRenderer`, `IBlobStorageClient`, `IClock`. ~12 tests covering: happy path (None mode → blob path returned), happy path (StandardVat → blob path returned), `ReverseCharge` → `InvoicingModeNotImplemented` Permanent failure, `StrictFiscalReporting` → `InvoicingModeNotImplemented` Permanent failure, idempotent re-issue returns existing values without re-rendering or re-uploading, order-not-found returns `OrderNotFound`, country-config-not-found returns `CountryConfigurationNotFound`, renderer throws → `InvoiceRenderFailed`, blob upload throws transient → `Transient` failure, blob upload throws permanent → `Permanent` failure, `Invoice.AttachPdfBlobPath` set-once succeeds first time.
- **`Makables.Tests/Domain/Payments/SpaydTests.cs`** — 6 tests: happy path (formats per SPAYD spec); IBAN normalisation (strips spaces); amount conversion (`amountMinor` → major with 2 decimals); variable symbol (digits only); empty IBAN throws; mismatched currency normalisation.
- **`Makables.Tests/Infra/PdfRendering/QuestPdfInvoiceRendererTests.cs`** — 4 tests: `None` mode produces a valid PDF (byte[0] = 0x25, 0x50, 0x44, 0x46 = "%PDF"); `StandardVat` mode produces a valid PDF; PDF contains Czech glyphs (decode + grep for "Děkujeme"); rendering an InvoicingMode.ReverseCharge throws `NotImplementedException`.
- **`Makables.IntegrationTests/Invoices/IssueInvoiceIntegrationTests.cs`** — 3 tests: end-to-end happy path against Postgres + faked `IBlobStorageClient`; renderer produces a PDF, blob is uploaded, `Invoice.PdfBlobPath` is set; idempotent retry doesn't double-upload (asserts faked blob client's `UploadAsync` called once); MarkOrderPaid → outbox → IssueInvoice end-to-end (via direct dispatch of the outbox payload, not via the Function).
- **Updated `MarkOrderPaidHandlerTests.cs`** per locked decision 10 — positive pin replaces negative pin; total Enqueue count flipped to 3.

### Docs

- **ADR (next number, 0025?)** — "QuestPDF + Noto Sans + SPAYD QR + Invoice PDF rendering posture". Documents: QuestPDF Community qualification + revenue trigger for review; Noto Sans subset + SIL OFL license; SPAYD format + IBAN sourcing from CountryConfiguration; renderer interface scope (`IInvoicePdfRenderer` invoice-specific); two-template branching per InvoicingMode.
- **`docs/architecture/roles/invoice.md`** — update Implementation pointer section with T-0068b-shipped paths (renderer, service, blob upload step); update Lifecycle table with the "Issued by IssueInvoice command (queue-triggered from outbox)" entry.
- **`docs/architecture/patterns.md`** — if `Infra.PdfRendering` becomes a new project category, add §A.N "PDF rendering adapter pattern" entry. (Probably defer to a follow-up architect ticket — one project does not a pattern make.)

### NSwag regen

No public contract changes. No new controllers ship in T-0068b (per locked decision 9). NSwag regen NOT required.

## Alternatives Considered

- **Cleansia-port "Service" framing.** *Rejected per decision 7* — one-file feature shape is the tisknicz precedent enforced by consistency-script T1.
- **QuestPDF Pro license.** *Rejected per decision 1* — Community is free for JVM YORE's current revenue posture; the ADR records the trigger for a future review.
- **DejaVu Sans embed.** *Rejected per decision 2* — Noto Sans subsetted is smaller (~80 KB) and the SIL OFL is cleaner for redistribution. DejaVu would have been ~700 KB unsubsetted.
- **System fonts (Arial/Helvetica).** *Rejected per decision 2* — Linux App Service runtime doesn't have Arial; PDF would degrade unpredictably.
- **Hard-code IBAN in CZ seed.** *Rejected per decision 4* — JVM YORE's bank-account decision is open; hard-coding blocks T-0068b on a non-tech decision.
- **Defer SPAYD entirely.** *Rejected per decision 4* — losing the wiring means adding it back later via a separate migration + renderer change; cheaper to ship the schema NULLable now.
- **Generic `IPdfRenderer<TPayload>`.** *Rejected per decision 3* — speculative abstraction; T-0074 shipping labels will have a different shape.
- **Hard-code issuer values in renderer.** *Rejected per decision 8* — couples invoice rendering to a code release; admin can't update issuer name without redeploy.
- **Use Azure App Configuration for issuer values.** *Rejected per decision 8* — country_configurations is the natural home (it's per-country); App Configuration is platform-wide.
- **Stub customer download controller in T-0068b.** *Rejected per decision 9* — violates CLAUDE.md "no mocks during build phase" rule.
- **Delete T-0067 negative-pin test.** *Rejected per decision 10* — positive pin preserves test slot + lineage; future readers see the flip via git log.

## Out of scope

- `GenerateInvoice` Function + queue-triggered dispatcher routing — T-0069.
- Customer-facing PDF download endpoint — T-0086.
- Admin-facing PDF download endpoint — downstream admin ticket.
- Fee invoices (`InvoiceType.Fee` rendering + PayoutBatch FK) — T-0101 / T-0102.
- `ReverseCharge` + `StrictFiscalReporting` renderers — post-MVP (the modes return `InvoicingModeNotImplemented` failures with a TODO ticket reference).
- Credit notes (errata to issued invoices) — post-MVP.
- Czech i18n keys for the new `BusinessErrorMessage` codes — l10n agent ships them in this same PR per `routing.md` "l10n parallels frontend on same ticket"; in T-0068b's case l10n parallels dotnet-backend.
- Admin UI for editing `country_configurations.platform_iban` — downstream admin ticket.

## Acceptance criteria

- **AC-1** Given an order in state `Paid` with `InvoicingMode.None` and `Invoice` row not yet existing, when `IssueInvoice.Command(OrderId)` is dispatched, then a new `Invoice` row is created with `Type = Customer`, `InvoicingMode = None`, `VatAmountMinor = 0`, `VatRateBp = 0`, `PdfBlobPath` set to `invoices/cz/orders/{orderId}/{invoiceNumber}.pdf`, AND the blob exists in storage AND `Response.PdfBlobPath` matches.
- **AC-2** Given the same order in `InvoicingMode.StandardVat`, when `IssueInvoice.Command(OrderId)` is dispatched, then a new `Invoice` row is created with `InvoicingMode = StandardVat`, `VatRateBp` matching the order's snapshotted VAT, `VatAmountMinor` correctly computed per ADR 0003 half-up rounding (`AmountWithoutVat + Vat = AmountWithVat`), `TaxableSupplyDate` (DUZP) = `Order.PaidAt.DateOfDayInCountryTz(country.TimeZoneId)`, AND the blob exists.
- **AC-3** Given the same order in `InvoicingMode.ReverseCharge`, when `IssueInvoice.Command(OrderId)` is dispatched, then it returns `BusinessResult.Failure(Error.Permanent(BusinessErrorMessage.InvoicingModeNotImplemented))` AND no `Invoice` row is persisted AND no blob is uploaded.
- **AC-4** Same as AC-3 for `InvoicingMode.StrictFiscalReporting`.
- **AC-5** Given an `Order` that already has a `Customer` `Invoice` (idempotency case — webhook re-delivery), when `IssueInvoice.Command(OrderId)` is dispatched, then the existing `Invoice` row is returned in `Response`, no new row is allocated (numbering counter unchanged), no new render runs, no blob upload runs (assert mock `Received(0)` on renderer + blob client).
- **AC-6** Given a country_configurations row with `PlatformIban IS NULL`, when an invoice is rendered, then the PDF does NOT contain a SPAYD QR code (assert via byte-scan or PDF text extraction).
- **AC-7** Given a country_configurations row with `PlatformIban = 'CZ6508000000192000145399'`, when an invoice is rendered, then the PDF contains a SPAYD QR code AND the encoded SPAYD string matches `SPD*1.0*ACC:CZ6508000000192000145399*AM:<amount>*CC:CZK*X-VS:<order-number>`.
- **AC-8** Given the CZ country_configurations row, when read after migration, then `issuer_name = 'JVM YORE s.r.o.'`, `issuer_ico` is non-empty 8 chars, `issuer_dic IS NULL` (per T-0068a locked decision 2 — not VAT-registered).
- **AC-9** Given the `MarkOrderPaid.Handler` flow with all 3 outbox events enqueued atomically, when a Comgate webhook successfully marks an order paid, then exactly 3 outbox rows are created with event_types: `order.paid.customerEmail`, `order.placed.makerEmail`, `invoice.generate`. The previous T-0067 negative-pin test is now a positive pin that asserts the third row.
- **AC-10** Given a PDF rendered by `DanovyDokladDocument` (StandardVat), when decoded, then the PDF contains all § 29 mandatory fields rendered in Czech: issuer (Name/IČO), DIČ (or "Nejsem plátce DPH" — but per T-0068a locked decision 2, JVM YORE is non-VAT-payer at MVP so StandardVat mode is unreachable in CZ until a future pivot; this AC's StandardVat path is exercised against a test fixture with a synthetic VAT-payer issuer).
- **AC-11** Given a PDF rendered by `DokladOProdejiDocument` (None mode), when decoded, then the PDF contains "Doklad o prodeji" header, "Nejsem plátce DPH" footer, item description, total amount in CZK, payment reference (order number as VS).
- **AC-12** Build clean. Unit tests: baseline (1102 after T-0068a + 5 from review fold of T-0067 test conversion) + ~22 new. Integration tests: baseline (144 after T-0068a) + 3 new. Consistency script exit 0.
- **AC-13** ADR (next number) committed in the same PR documenting QuestPDF + Noto Sans + SPAYD posture.
- **AC-14** `docs/architecture/roles/invoice.md` Implementation pointer + Lifecycle sections updated.

## Technical notes

### Why renderer determinism matters

Per T-0068a locked decision 5, `Invoice.AttachPdfBlobPath` is set-once with idempotent same-value succeed. This relies on the renderer being deterministic — same invoice number + same data → byte-identical PDF. QuestPDF is deterministic by default (no embedded timestamps, no random IDs) AS LONG AS the renderer doesn't read `DateTime.Now` for footer rendering. **Watch:** any "rendered on {today}" footer text MUST come from the `Invoice.IssueDate` (snapshotted), NOT `IClock.UtcNow`. Reviewer will spot-check.

### Why blob overwrite is safe

Renderer determinism means same content; even if a race hits, the second uploader writes byte-identical bytes. `BlobClient.UploadAsync(overwrite: true)` is safe. If determinism is ever broken, this becomes a bug; the `IssueInvoiceIntegrationTests.idempotent_retry_does_not_double_upload` test catches it via `Received(1)` assertion on the blob client mock.

### Why `IssueInvoice` does NOT call `MarkOrderPaid.Command`

The outbox flow is: webhook → MarkOrderPaid → outbox.Enqueue(invoice.generate) → outbox processor → IssueInvoice. The handler never knows about MarkOrderPaid. This decouples retry policies: a transient blob-upload failure in IssueInvoice replays via the outbox stall mechanism (1m → 5m → 15m → 1h → 6h → 24h per T-0029), not via webhook re-delivery.

### Why `LanguageCode` is in the outbox payload

T-0069 attaches the PDF to the customer's order-paid email (which T-0067 already enqueues with the customer's resolved `LanguageCode`). T-0069 needs the same language for the attachment filename ("faktura-<orderNumber>.pdf" vs "invoice-<orderNumber>.pdf"). Pre-resolving in MarkOrderPaid and passing through the payload mirrors the T-0028 "consumer-side stays stateless" pattern.

### Why no NSwag regen

T-0068b ships zero new controllers (decision 9 strict-OOS on customer endpoint). The only public contract surface is the existing `MarkOrderPaid` handler which gets a new outbox row internally but has no DTO change. Pre-commit hook on `lib/api-client/` won't fire.

### Manual deployment steps

1. **`questpdf-license-pin`** — confirm at PR-open: QuestPDF Community License is appropriate for JVM YORE's current revenue + employee count. If revenue crosses $1M USD or employee count crosses 10, this becomes a Pro license purchase + redeploy. **Owner:** finance/legal. **Blocker:** before merge.
2. **`country-config-issuer-seed`** — confirm at PR-open: provide JVM YORE's actual 8-digit IČO for the CZ seed migration. Until provided, the migration ships with a placeholder `'00000000'` that triggers a startup-validation failure (loud-broken per CLAUDE.md). **Owner:** user. **Blocker:** before merge.

## Files touched (expected)

### New
- `backend/src/Makables.Core.Domain/Rendering/IInvoicePdfRenderer.cs`
- `backend/src/Makables.Core.Domain/Payments/Spayd.cs`
- `backend/src/Makables.Core.Domain/Outbox/InvoiceGenerateOutboxPayload.cs`
- `backend/src/Makables.Core.AppServices/Features/Invoices/IssueInvoice.cs`
- `backend/src/Makables.Infra.PdfRendering/Makables.Infra.PdfRendering.csproj` + project added to Makables.Api.slnx
- `backend/src/Makables.Infra.PdfRendering/QuestPdfInvoiceRenderer.cs`
- `backend/src/Makables.Infra.PdfRendering/Fonts/NotoSans-Regular-CzechSubset.ttf`
- `backend/src/Makables.Infra.PdfRendering/Fonts/NotoSans-Bold-CzechSubset.ttf`
- `backend/src/Makables.Config/Extensions/AddMakablesPdfRendering.cs`
- `backend/src/Makables.Infra.Database/Migrations/<timestamp>_AddCountryConfigurationIssuerAndIban.cs` (+ Designer)
- `backend/src/Makables.Tests/AppServices/Features/Invoices/IssueInvoiceHandlerTests.cs`
- `backend/src/Makables.Tests/Domain/Payments/SpaydTests.cs`
- `backend/src/Makables.Tests/Infra/PdfRendering/QuestPdfInvoiceRendererTests.cs`
- `backend/src/Makables.IntegrationTests/Invoices/IssueInvoiceIntegrationTests.cs`
- `docs/adr/0025-questpdf-and-invoice-rendering.md` (or next available number)

### Modified
- `backend/src/Makables.Core.Domain/Configuration/CountryConfiguration.cs` (4 new properties)
- `backend/src/Makables.Core.Domain/Common/BusinessErrorMessage.cs` (3 new codes)
- `backend/src/Makables.Core.Domain/Outbox/OutboxEventTypes.cs` (1 new constant + IsEmailSend update)
- `backend/src/Makables.Core.AppServices/Features/Orders/MarkOrderPaid.cs` (third outbox.Enqueue + new payload struct constructor)
- `backend/src/Makables.Infra.Database/Configurations/CountryConfigurationConfiguration.cs` (4 column mappings)
- `backend/src/Makables.Tests/AppServices/Features/Orders/MarkOrderPaidHandlerTests.cs` (convert negative pin to positive pin per decision 10)
- `backend/src/Makables.Web.Customer/Program.cs`, `Makables.Web.Maker/Program.cs`, `Makables.Web.Admin/Program.cs`, `Makables.Web.Public/Program.cs` (call `AddMakablesPdfRendering()`)
- `backend/src/Makables.Functions/Program.cs` (the GenerateInvoice Function in T-0069 will call IssueInvoice via Mediator; T-0068b ships the DI registration so the dispatch works)
- `frontend/src/lib/i18n/cs-CZ.ts` (3 new keys for the new error codes — `invoice.invoicingModeNotImplemented`, `invoice.renderFailed`, `invoice.blobUploadFailed` + the deferred `invoice.blobPathAlreadySet` from T-0068a)
- `docs/architecture/roles/invoice.md` (Implementation pointer + Lifecycle)
- `docs/tickets/INDEX.md` (status update to in_progress → in_review → done)

## Test plan reference

Inline above (see Scope > Tests). No separate `docs/test-plans/T-0068b.md` file.

## Status log

- 2026-06-06 `draft` by PM. Created as part of T-0068 L-split. Will transition to ready after T-0068a merges.
- 2026-06-07 `draft → ready` by PM. User answered 8 blocking decisions via AskUserQuestion per `/feature` workflow step 3 (QuestPDF Community license; Noto Sans subset; SPAYD IBAN as nullable column; invoice-specific renderer interface; CountryConfiguration columns for issuer + IBAN; one-file feature shape for IssueInvoice; strict-OOS on customer download endpoint; T-0067 negative-pin converted to positive). Decisions captured in `## Locked design decisions` section. Two `manual_steps` flagged (questpdf-license-pin, country-config-issuer-seed) — both PR-open blockers. **NOT YET DISPATCHABLE** — implementation must wait for T-0068a (PR #32) AND T-0067 (already done) to be merged into master.
