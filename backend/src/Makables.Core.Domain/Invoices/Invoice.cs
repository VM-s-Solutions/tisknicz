using Makables.Core.Domain.Common;
using Makables.Core.Domain.Configuration;

namespace Makables.Core.Domain.Invoices;

/// <summary>
/// Legal record of a payment between two parties — platform↔customer for
/// a paid <c>Order</c> (<see cref="InvoiceType.Customer"/>), or
/// platform↔maker for the platform fee on a payout batch
/// (<see cref="InvoiceType.Fee"/>). Per role/invoice.md + ADR 0009.
///
/// <para>
/// <b>Snapshot semantics.</b> Issuer (JVM YORE s.r.o. — name, IČO, DIČ,
/// bank account), recipient (name / email / tax id / VAT id), money
/// breakdown, dates, and the invoicing mode are captured at
/// <see cref="Issue"/> time and are IMMUTABLE for the life of the row.
/// Errata flow through credit-notes (post-MVP). GDPR right-to-erasure
/// anonymises but does not delete — legal records remain. Per T-0068a
/// locked decisions 1 + 2: the platform itself is the legal issuer; the
/// schema carries the issuer columns so a future "JVM YORE crossed the
/// VAT-registration threshold" event populates <see cref="IssuerDic"/>
/// without re-migration (it ships nullable at the schema level for that
/// reason).
/// </para>
///
/// <para>
/// <b>Aggregate link (XOR).</b> Exactly one of <see cref="OrderId"/> /
/// <see cref="PayoutBatchId"/> is non-null. The other column ships now
/// per T-0068a locked decision so T-0101 only has to add the FK; the
/// XOR factory invariant prevents <see cref="PayoutBatchId"/> from being
/// set on any T-0068a or T-0068b code path.
/// </para>
///
/// <para>
/// <b>No state machine, no <c>UpdateAsync</c>.</b> The only mutation is
/// <see cref="AttachPdfBlobPath"/> (set-once), called by T-0068b's
/// renderer after the PDF is uploaded to blob storage. Repository
/// surface intentionally omits Update + Delete to make the policy
/// explicit (role/invoice.md: "no updates after issuance").
/// </para>
/// </summary>
public sealed class Invoice : Auditable
{
    /// <summary>Wire-shape ceilings — bounded so admin queries stay sub-ms.</summary>
    public const int MaxInvoiceNumberLength = 40;
    public const int MaxOrderNumberLength = 40;

    public const int MaxIssuerNameLength = 200;
    public const int MaxIssuerIcoLength = 40;
    public const int MaxIssuerDicLength = 40;
    public const int MaxIssuerBankAccountLength = 60;
    public const int MaxIssuerAddressLength = 200;
    public const int MaxPaymentMethodLength = 60;

    public const int MaxRecipientNameLength = 200;
    public const int MaxRecipientEmailLength = 200;
    public const int MaxRecipientTaxIdLength = 40;
    public const int MaxRecipientVatIdLength = 40;

    public const int MaxPdfBlobPathLength = 500;

    // === Identity ===

    /// <summary>
    /// Customer-facing invoice number, immutable after creation. Allocated
    /// by <see cref="Numbering.IInvoiceNumberGenerator"/> inside the
    /// surrounding command's transaction per ADR 0009 — gap-free.
    /// </summary>
    public string InvoiceNumber { get; private set; } = default!;

    public InvoiceType Type { get; private set; }

    // === Aggregate link (XOR) ===

    /// <summary>
    /// For <see cref="InvoiceType.Customer"/> invoices: FK to the paid
    /// order. Non-null XOR <see cref="PayoutBatchId"/>.
    /// </summary>
    public string? OrderId { get; private set; }

    /// <summary>
    /// The customer-facing order number (<c>Order.OrderNumber</c>),
    /// snapshotted so the document can name the order the way the
    /// customer knows it. Null on <see cref="InvoiceType.Fee"/> invoices,
    /// which cover many orders and carry their numbers per line instead.
    ///
    /// <para>
    /// Before this column existed the templates printed the invoice
    /// number's own numeric tail as "Objednávka {tail}" — a reference
    /// that matches no order the customer can look up. The renderer falls
    /// back to that tail only for rows issued before the snapshot.
    /// </para>
    /// </summary>
    public string? OrderNumber { get; private set; }

    /// <summary>
    /// For <see cref="InvoiceType.Fee"/> invoices: FK to the maker's
    /// weekly payout batch. Non-null XOR <see cref="OrderId"/>. The FK
    /// constraint is added in T-0101 (the migration that creates the
    /// <c>payout_batches</c> table); T-0068a's migration ships the column
    /// only.
    /// </summary>
    public string? PayoutBatchId { get; private set; }

    /// <summary>
    /// Denormalised maker id for the <c>(maker_id, created_at DESC)</c>
    /// composite index that backs T-0068b's maker-host "my invoices" list.
    /// For Customer invoices it is set from <c>Order.MakerId</c> at issue
    /// time; for Fee invoices it is the target maker of the payout batch
    /// (populated by T-0101). Required — every invoice has a maker, even
    /// the Fee variant, so the index covers both families.
    /// </summary>
    public string MakerId { get; private set; } = default!;

    // === Issuer snapshot (platform JVM YORE s.r.o.) ===

    public string IssuerName { get; private set; } = default!;

    /// <summary>Czech IČO (8 digits). Always required — the platform is a registered entity.</summary>
    public string IssuerIco { get; private set; } = default!;

    /// <summary>
    /// Czech DIČ (e.g. <c>CZ12345678</c>). Nullable: the platform is not
    /// VAT-registered at MVP launch per T-0068a locked decision 2, so the
    /// column ships nullable. When JVM YORE crosses the 2M CZK threshold
    /// and registers for VAT, this gets populated for new invoices —
    /// historical rows keep the null they were issued with.
    /// </summary>
    public string? IssuerDic { get; private set; }

    public string? IssuerBankAccount { get; private set; }

    /// <summary>
    /// Registered seat of the issuer, snapshotted from
    /// <see cref="CountryConfiguration.IssuerAddress"/>. Nullable for the
    /// same reason the config column is, and because rows issued before
    /// the column existed carry no address.
    /// </summary>
    public string? IssuerAddress { get; private set; }

    // === Recipient snapshot ===

    public string RecipientName { get; private set; } = default!;
    public string RecipientEmail { get; private set; } = default!;
    public string? RecipientTaxId { get; private set; }
    public string? RecipientVatId { get; private set; }

    // === Dates (country-local DateOnly) ===

    public DateOnly IssueDate { get; private set; }

    /// <summary>
    /// DUZP — datum uskutečnění zdanitelného plnění. Per T-0068a locked
    /// decision 3 + T-0068b service: set to
    /// <c>Order.PaidAt.DateOfDayInCountryTz(...)</c>. Nullable because
    /// <see cref="InvoicingMode.None"/> documents (Doklad o prodeji) do
    /// not require a DUZP under § 29 zákon o DPH — the column accepts a
    /// null in that mode and is populated for
    /// <see cref="InvoicingMode.StandardVat"/> rows.
    /// </summary>
    public DateOnly? TaxableSupplyDate { get; private set; }

    public DateOnly DueDate { get; private set; }

    // === Settlement snapshot ===

    /// <summary>
    /// Country-local date the invoice was settled, or null if it is still
    /// outstanding. Both invoice families the platform issues today are
    /// settled BEFORE the document exists — a
    /// <see cref="InvoiceType.Customer"/> invoice is issued off
    /// <c>Order.PaidAt</c>, and a <see cref="InvoiceType.Fee"/> invoice is
    /// netted out of the payout in the same batch — so this is populated
    /// on every row the current code paths produce.
    ///
    /// <para>
    /// The renderer reads it as the switch between "here is how to pay
    /// me" (due date, variable symbol, SPAYD QR) and "this is a receipt"
    /// (UHRAZENO stamp, settlement date, no payment instructions).
    /// Printing "Celkem k úhradě 739 Kč" on a document the customer had
    /// already paid was the reported defect; the field is what makes the
    /// distinction data rather than an assumption about
    /// <see cref="Type"/>.
    /// </para>
    /// </summary>
    public DateOnly? PaidOn { get; private set; }

    /// <summary>
    /// How the invoice was settled, snapshotted at issuance. Free-form
    /// because the payment provider owns the vocabulary — Comgate returns
    /// codes like <c>CARD_CZ_CSOB_2</c>; the platform's own fee invoices
    /// carry <see cref="SettlementMethods.PayoutDeduction"/>. Null when
    /// <see cref="PaidOn"/> is null, or when the channel is unknown.
    /// Presentation maps it to a human label — the domain does not.
    /// </summary>
    public string? PaymentMethod { get; private set; }

    // === Invoicing mode (per-row snapshot, not config lookup) ===

    /// <summary>
    /// Snapshot at issuance time. If
    /// <see cref="CountryConfiguration.InvoicingMode"/> changes
    /// post-launch (e.g. JVM YORE registers for VAT), historical
    /// invoices still render under the mode they were issued in. Per
    /// role/invoice.md invariant.
    /// </summary>
    public InvoicingMode InvoicingMode { get; private set; }

    // === Money breakdown (minor units; ADR 0003) ===

    public long AmountWithoutVatMinor { get; private set; }

    /// <summary>VAT rate in basis points (2100 = 21%).</summary>
    public int VatRateBp { get; private set; }

    public long VatAmountMinor { get; private set; }

    public long AmountWithVatMinor { get; private set; }

    public string Currency { get; private set; } = default!;

    // === Rendered PDF reference (set-once via AttachPdfBlobPath) ===

    /// <summary>
    /// Blob path of the rendered PDF. Null until T-0068b's
    /// <c>IInvoiceService.IssueAsync</c> calls
    /// <see cref="AttachPdfBlobPath"/> after a successful upload. Format
    /// per role doc: <c>invoices/{cc}/orders/{orderId}/{invoiceNumber}.pdf</c>.
    /// </summary>
    public string? PdfBlobPath { get; private set; }

    // EF Core needs a parameterless ctor.
    private Invoice() { }

    /// <summary>
    /// Issue a new invoice with a fully-balanced money breakdown and the
    /// XOR aggregate link enforced. Throws <see cref="ArgumentException"/>
    /// for impossible inputs (negative amounts, blank required strings,
    /// money breakdown that does not balance, XOR violation,
    /// <see cref="InvoicingMode.None"/> with non-zero VAT, currency wrong
    /// length). Pattern mirrors <c>Order.Create</c> (T-0060):
    /// programmer-error inputs throw; user-input validation belongs in
    /// the command's <c>Validator</c>.
    ///
    /// <para>
    /// <paramref name="pdfBlobPath"/> is provided for the (unlikely)
    /// future case where the renderer is rewritten to upload before
    /// persisting; T-0068b sets it via <see cref="AttachPdfBlobPath"/>
    /// after the row is committed, so callers normally pass <c>null</c>.
    /// </para>
    /// </summary>
    public static Invoice Issue(
        string id,
        string invoiceNumber,
        InvoiceType type,
        string? orderId,
        string? orderNumber,
        string? payoutBatchId,
        string makerId,
        string issuerName,
        string issuerIco,
        string? issuerDic,
        string? issuerBankAccount,
        string? issuerAddress,
        string recipientName,
        string recipientEmail,
        string? recipientTaxId,
        string? recipientVatId,
        DateOnly issueDate,
        DateOnly? taxableSupplyDate,
        DateOnly dueDate,
        InvoicingMode invoicingMode,
        long amountWithoutVatMinor,
        int vatRateBp,
        long vatAmountMinor,
        long amountWithVatMinor,
        string currency,
        string countryCode,
        DateOnly? paidOn = null,
        string? paymentMethod = null,
        string? pdfBlobPath = null)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Id is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(invoiceNumber))
            throw new ArgumentException("InvoiceNumber is required.", nameof(invoiceNumber));
        if (string.IsNullOrWhiteSpace(makerId))
            throw new ArgumentException("MakerId is required.", nameof(makerId));

        if (string.IsNullOrWhiteSpace(issuerName))
            throw new ArgumentException("IssuerName is required.", nameof(issuerName));
        if (string.IsNullOrWhiteSpace(issuerIco))
            throw new ArgumentException("IssuerIco is required.", nameof(issuerIco));
        if (string.IsNullOrWhiteSpace(recipientName))
            throw new ArgumentException("RecipientName is required.", nameof(recipientName));
        if (string.IsNullOrWhiteSpace(recipientEmail))
            throw new ArgumentException("RecipientEmail is required.", nameof(recipientEmail));

        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3)
            throw new ArgumentException("Currency must be a 3-char ISO 4217 code.", nameof(currency));
        if (string.IsNullOrWhiteSpace(countryCode) || countryCode.Length != 2)
            throw new ArgumentException("CountryCode must be 2 chars (ISO 3166-1 alpha-2).", nameof(countryCode));

        // XOR aggregate link. Exactly one of orderId / payoutBatchId is
        // non-null per role/invoice.md. Both null OR both non-null is a
        // programmer error — the caller resolved the wrong join target.
        var hasOrder = !string.IsNullOrWhiteSpace(orderId);
        var hasPayout = !string.IsNullOrWhiteSpace(payoutBatchId);
        if (hasOrder == hasPayout)
        {
            throw new ArgumentException(
                $"Exactly one of OrderId ({orderId ?? "null"}) or PayoutBatchId " +
                $"({payoutBatchId ?? "null"}) must be non-null (XOR).",
                hasOrder ? nameof(payoutBatchId) : nameof(orderId));
        }

        if (amountWithoutVatMinor < 0)
            throw new ArgumentException("AmountWithoutVatMinor cannot be negative.", nameof(amountWithoutVatMinor));
        if (vatAmountMinor < 0)
            throw new ArgumentException("VatAmountMinor cannot be negative.", nameof(vatAmountMinor));
        if (amountWithVatMinor < 0)
            throw new ArgumentException("AmountWithVatMinor cannot be negative.", nameof(amountWithVatMinor));
        if (vatRateBp < 0)
            throw new ArgumentException("VatRateBp cannot be negative.", nameof(vatRateBp));

        // Money breakdown balance — AC-1. The legal record must reconcile
        // line-by-line; a customer querying the invoice months later sees
        // (net + VAT == gross) or the document is rejected at issuance.
        if (amountWithoutVatMinor + vatAmountMinor != amountWithVatMinor)
        {
            throw new ArgumentException(
                $"AmountWithVatMinor ({amountWithVatMinor}) must equal " +
                $"AmountWithoutVatMinor ({amountWithoutVatMinor}) + VatAmountMinor ({vatAmountMinor}).",
                nameof(amountWithVatMinor));
        }

        // None mode (Doklad o prodeji) MUST carry zero VAT — JVM YORE is
        // not VAT-registered per T-0068a locked decision 2, so a non-zero
        // VAT line on a None invoice is a programmer error (likely a
        // mis-snapshotted CountryConfiguration.InvoicingMode).
        if (invoicingMode == InvoicingMode.None && (vatAmountMinor != 0 || vatRateBp != 0))
        {
            throw new ArgumentException(
                $"InvoicingMode.None requires zero VAT (got VatAmountMinor={vatAmountMinor}, " +
                $"VatRateBp={vatRateBp}). Non-VAT-payer invoices cannot carry a VAT line.",
                nameof(vatAmountMinor));
        }

        // Length caps — defensive against an oversized snapshot that
        // would silently truncate at the column. Mirrors Order.Create.
        var trimmedNumber = invoiceNumber.Trim();
        if (trimmedNumber.Length > MaxInvoiceNumberLength)
            throw new ArgumentException($"InvoiceNumber must be at most {MaxInvoiceNumberLength} chars.", nameof(invoiceNumber));
        if (issuerName.Trim().Length > MaxIssuerNameLength)
            throw new ArgumentException($"IssuerName must be at most {MaxIssuerNameLength} chars.", nameof(issuerName));
        if (recipientName.Trim().Length > MaxRecipientNameLength)
            throw new ArgumentException($"RecipientName must be at most {MaxRecipientNameLength} chars.", nameof(recipientName));
        if (recipientEmail.Trim().Length > MaxRecipientEmailLength)
            throw new ArgumentException($"RecipientEmail must be at most {MaxRecipientEmailLength} chars.", nameof(recipientEmail));

        if (orderNumber?.Trim().Length > MaxOrderNumberLength)
            throw new ArgumentException($"OrderNumber must be at most {MaxOrderNumberLength} chars.", nameof(orderNumber));
        if (issuerAddress?.Trim().Length > MaxIssuerAddressLength)
            throw new ArgumentException($"IssuerAddress must be at most {MaxIssuerAddressLength} chars.", nameof(issuerAddress));
        if (paymentMethod?.Trim().Length > MaxPaymentMethodLength)
            throw new ArgumentException($"PaymentMethod must be at most {MaxPaymentMethodLength} chars.", nameof(paymentMethod));

        // A settlement channel without a settlement date is a half-written
        // fact — the renderer would stamp neither UHRAZENO nor payment
        // instructions and the document would silently lose both.
        if (paidOn is null && !string.IsNullOrWhiteSpace(paymentMethod))
            throw new ArgumentException("PaymentMethod requires PaidOn.", nameof(paymentMethod));

        var trimmedBlobPath = string.IsNullOrWhiteSpace(pdfBlobPath) ? null : pdfBlobPath.Trim();
        if (trimmedBlobPath is not null && trimmedBlobPath.Length > MaxPdfBlobPathLength)
            throw new ArgumentException($"PdfBlobPath must be at most {MaxPdfBlobPathLength} chars.", nameof(pdfBlobPath));

        return new Invoice
        {
            Id = id,
            InvoiceNumber = trimmedNumber,
            Type = type,
            OrderId = hasOrder ? orderId!.Trim() : null,
            OrderNumber = string.IsNullOrWhiteSpace(orderNumber) ? null : orderNumber.Trim(),
            PayoutBatchId = hasPayout ? payoutBatchId!.Trim() : null,
            MakerId = makerId.Trim(),
            IssuerName = issuerName.Trim(),
            IssuerIco = issuerIco.Trim(),
            IssuerDic = string.IsNullOrWhiteSpace(issuerDic) ? null : issuerDic.Trim(),
            IssuerBankAccount = string.IsNullOrWhiteSpace(issuerBankAccount) ? null : issuerBankAccount.Trim(),
            IssuerAddress = string.IsNullOrWhiteSpace(issuerAddress) ? null : issuerAddress.Trim(),
            RecipientName = recipientName.Trim(),
            RecipientEmail = recipientEmail.Trim(),
            RecipientTaxId = string.IsNullOrWhiteSpace(recipientTaxId) ? null : recipientTaxId.Trim(),
            RecipientVatId = string.IsNullOrWhiteSpace(recipientVatId) ? null : recipientVatId.Trim(),
            IssueDate = issueDate,
            TaxableSupplyDate = taxableSupplyDate,
            DueDate = dueDate,
            PaidOn = paidOn,
            PaymentMethod = string.IsNullOrWhiteSpace(paymentMethod) ? null : paymentMethod.Trim(),
            InvoicingMode = invoicingMode,
            AmountWithoutVatMinor = amountWithoutVatMinor,
            VatRateBp = vatRateBp,
            VatAmountMinor = vatAmountMinor,
            AmountWithVatMinor = amountWithVatMinor,
            Currency = currency.ToUpperInvariant(),
            CountryCode = countryCode.ToUpperInvariant(),
            PdfBlobPath = trimmedBlobPath,
        };
    }

    /// <summary>
    /// Attach the rendered-PDF blob path. Set-once with idempotent
    /// same-value semantics per AC-3:
    /// <list type="bullet">
    ///   <item>First call with a non-null value → set + success.</item>
    ///   <item>Second call with the SAME value → no-op + success (T-0068b's
    ///     renderer is deterministic; a retry produces the same path).</item>
    ///   <item>Second call with a DIFFERENT value →
    ///     <see cref="BusinessErrorMessage.InvoiceBlobPathAlreadySet"/>.
    ///     A real overwrite attempt indicates either a non-deterministic
    ///     renderer (bug) or a programmer-error data-corruption attempt.</item>
    /// </list>
    /// Pattern mirrors <c>Order.MarkAsPaid</c>'s set-once invariant on
    /// <c>PaymentProviderRef</c> + <c>PaymentMethod</c> (T-0067).
    /// </summary>
    public BusinessResult AttachPdfBlobPath(string pdfBlobPath)
    {
        if (string.IsNullOrWhiteSpace(pdfBlobPath))
            throw new ArgumentException("PdfBlobPath is required.", nameof(pdfBlobPath));

        var trimmed = pdfBlobPath.Trim();
        if (trimmed.Length > MaxPdfBlobPathLength)
            throw new ArgumentException(
                $"PdfBlobPath must be at most {MaxPdfBlobPathLength} chars.",
                nameof(pdfBlobPath));

        if (PdfBlobPath is not null)
        {
            // Idempotent same-value retry succeeds; a real overwrite fails.
            if (string.Equals(PdfBlobPath, trimmed, StringComparison.Ordinal))
            {
                return BusinessResult.Success();
            }

            return BusinessResult.Failure(
                Error.Conflict("pdfBlobPath", BusinessErrorMessage.InvoiceBlobPathAlreadySet));
        }

        PdfBlobPath = trimmed;
        return BusinessResult.Success();
    }
}
