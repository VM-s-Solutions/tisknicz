---
id: T-0195
title: Invoice documents — receipt semantics, brand design, real issuer identity
status: in_review
size: M
owner: claude
created: 2026-08-23
updated: 2026-08-23
depends_on: [T-0068a, T-0068b, T-0102b]
blocks: []
user_stories: []
adrs: [0009, 0025]
phase: 8
manual_steps: []
security_touching: false
layers: [domain, appservices, database, pdf]
---

# T-0195 — Invoice documents

## Context

Operator, on the generated PDFs: *"faktury by chtěly lepší design, přidej tam
makables logo, ičo jvm yore s.r.o. znáš nebo ho dohledej. Formátování nic moc.
Uhrazená věc je tam že má zaplatit např. 739 přitom to uhradil"*.

Four separate defects in one document, and the last one is the serious one.

**The document asked for money that was already paid.** `IssueInvoice.Handler`
only ever runs off the outbox row `MarkOrderPaid` enqueues, and
`PayoutArtifactService` pays out `Order.MakerPayoutAmountMinor`, which is
already net of `Order.PlatformFeeAmountMinor`. Both invoice families are
therefore settled *before* their PDF exists. The templates nonetheless printed
"Celkem k úhradě", a due date, a variable symbol and (when an IBAN was
configured) a SPAYD payment string. A customer who had paid 739 Kč by card
received a document telling them to pay 739 Kč.

**The issuer was a placeholder.** `issuer_ico = '00000000'` shipped at T-0068b
behind the `country-config-ico-replace-placeholder-pre-launch` manual_step, and
the documents carried no registered seat at all — which § 29 zákona č. 235/2004
Sb. requires on a daňový doklad.

**The line item named nothing.** Customer templates printed
`Objednávka {invoice-number-tail}` — "Objednávka 20260042" — a reference that
matches no order in the customer's account. The fee template already printed
real order numbers, which is what exposed the inconsistency.

**The layout was the QuestPDF default.** No logo, no letterhead, flat text
rows, a raw SPAYD payload dumped as body text.

## Decisions

1. **Settlement is data, not an inference from `InvoiceType`.** New snapshot
   columns `invoices.paid_on` + `invoices.payment_method`. The renderer reads
   `PaidOn` as the switch between a payment request and a receipt. Deriving it
   from `Type` would have been shorter and would have encoded "Customer implies
   paid" in the presentation layer, where the next invoice family to be added
   would silently inherit it.
2. **The outstanding branch stays.** Nothing the platform issues today reaches
   it. It is kept because it is the honest else-branch of a nullable column,
   not speculative generality — and it is pinned by tests so it cannot rot.
3. **`payment_method` is free-form.** Comgate owns the vocabulary and returns
   codes like `CARD_CZ_CSOB_2`; a closed enum here would reject a code the
   provider added yesterday. `SettlementMethods.PayoutDeduction` is the one
   value the platform originates. Presentation maps codes to Czech labels by
   family prefix and falls back to a truthful generic.
4. **The issuer identity comes from ARES, spelled as the registry spells it.**
   IČO `29633443`, `JVM Yore, s.r.o.`, seat `Příčná 1892/4, Nové Město,
   110 00 Praha 1`, verified 2026-08-23. Not VAT-registered
   (`stavZdrojeDph = NEEXISTUJICI`), so `issuer_dic` stays NULL and
   `InvoicingMode` stays `None`.
5. **Already-issued rows carrying the placeholder are corrected, not
   preserved.** Their snapshot names an IČO that belongs to nobody; leaving it
   is keeping a wrong legal record, not preserving history. The UPDATE is
   guarded on `issuer_ico = '00000000'` so a real snapshot is never rewritten.
6. **The PDF uses the LIGHT palette.** Paper is white; a PDF cannot follow a
   theme, and the dark ramp would put near-black fills on the page.
   `InvoiceTheme` transcribes nine values from `globals.css` — a deliberate
   duplication, since a PDF has no CSS layer to resolve through.

## Acceptance criteria

- **AC-1** A settled invoice reads "Celkem", carries an UHRAZENO stamp with
  date and channel, and shows neither due date, variable symbol nor SPAYD.
  *Proof:* `A_settled_invoice_renders_a_different_document_than_an_outstanding_one`,
  `A_settled_invoice_ignores_the_IBAN_so_no_receipt_asks_for_payment`.
- **AC-2** `IssueInvoice` snapshots `Order.PaidAt` in country-local terms and
  `Order.PaymentMethod`. *Proof:*
  `Invoice_snapshots_the_orders_settlement_so_the_PDF_reads_as_a_receipt`,
  `Settlement_date_is_the_country_local_date_of_PaidAt_not_the_UTC_one`.
- **AC-3** Fee invoices are settled by deduction on the DUZP. *Proof:*
  `PayoutArtifactServiceTests`.
- **AC-4** `Invoice.Issue` rejects a payment method without a settlement date.
  *Proof:* `InvoiceTests`.
- **AC-5** Documents carry the Makables mark, the ARES issuer identity and the
  registered seat. *Proof:* rendered PDFs reviewed at A4; migration data SQL.
- **AC-6** The line item names the customer's own order number, with a
  fallback for rows issued before the column existed. *Proof:*
  `The_line_item_names_the_snapshotted_order_number`,
  `An_invoice_issued_before_the_snapshot_still_renders`,
  `Invoice_snapshots_the_order_number_so_the_line_item_names_the_customers_order`.
- **AC-7** Re-rendering an invoice produces identical bytes (the blob-overwrite
  contract). *Proof:* `Re_rendering_the_same_invoice_produces_identical_bytes`.

## Implementation

- `Makables.Infra.PdfRendering/InvoiceDocument.cs` — `InvoiceDocumentBase`
  draws the shared chrome once; `DokladOProdejiDocument`,
  `DanovyDokladDocument` and `ProvizniDokladDocument` differ only in title,
  recipient label and line-item table.
- `Makables.Infra.PdfRendering/InvoiceTheme.cs` — light-palette tokens, the
  Makables mark as inline SVG re-inked to `--lt-brand-400` (the web asset's
  `#2dd4bf` is a 1.9:1 wash on white), and `InvoiceFormatting` (Czech money
  with non-breaking separators, dates, VAT rates, payment-method labels,
  address splitting). Unit-tested through `InternalsVisibleTo(Makables.Tests)`.
- `Makables.Core.Domain/Invoices/Invoice.cs` — `PaidOn`, `PaymentMethod`,
  `IssuerAddress`, `OrderNumber` snapshot fields + factory validation.
- `Makables.Core.Domain/Invoices/SettlementMethods.cs` — the one channel the
  platform originates.
- Migrations `20260823205941_InvoiceIssuerAddressAndSettlement` (columns, CZ
  identity, placeholder correction, settlement backfill from `issue_date`) and
  `20260823212428_InvoiceOrderNumberSnapshot` (column + backfill over the
  `order_id` FK).

## Verification

- `Makables.Tests` 2230 passed / 0 failed; `Makables.IntegrationTests`
  349 passed / 0 failed (the run applies both migrations to a fresh database).
- Four PDFs rendered and reviewed at A4: settled doklad o prodeji (the reported
  739 Kč case), the same document outstanding, a settled daňový doklad with the
  VAT breakdown, and a fee invoice with three claimed orders.
- No contract change — no NSwag regeneration required.

## Follow-ups

- SPAYD is still emitted as text rather than a QR image (carried over from
  T-0068b). It now appears only on the outstanding branch, so no receipt
  carries it.
- The Noto Sans subset deferred at T-0068b is still deferred; documents render
  in QuestPDF's bundled DejaVu Sans, which covers Czech diacritics.
