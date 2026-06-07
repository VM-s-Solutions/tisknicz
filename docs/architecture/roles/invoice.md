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

**T-0068b will ship:** `Core.AppServices/Services/InvoiceService.cs` (orchestrates the enforcement-mode branch per ADR 0013), `IPdfRenderer` + `QuestPdfInvoiceRenderer`, blob upload to `invoices/{cc}/orders/{orderId}/{invoiceNumber}.pdf`, `MarkOrderPaid.Handler` third outbox enqueue.

## Related

- ADRs: 0003, 0009, 0013 (enforcement modes), 0019 (PDF attached to email via outbox)
- Roles: `order`, `payout-batch`, `invoice-numbering`, `blob-storage`
