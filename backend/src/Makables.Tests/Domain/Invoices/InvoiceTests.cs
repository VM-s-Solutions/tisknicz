using FluentAssertions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Invoices;

namespace Makables.Tests.Domain.Invoices;

/// <summary>
/// Exhaustive coverage for the <see cref="Invoice"/> aggregate per T-0068a:
/// <see cref="Invoice.Issue"/> factory invariants (money balance, XOR
/// aggregate link, currency length, blank inputs, None + zero-VAT,
/// StandardVat happy path) and <see cref="Invoice.AttachPdfBlobPath"/>
/// set-once semantics.
///
/// <para>
/// Invoices are legal records — once issued, the only mutation path is
/// <see cref="Invoice.AttachPdfBlobPath"/>. There is no state machine, no
/// <c>UpdateAsync</c> on the repository, no field setter after the
/// factory. These tests pin that boundary.
/// </para>
/// </summary>
public class InvoiceTests
{
    private const string ValidId = "inv-1";
    private const string ValidNumber = "FV-CZ-20260001";
    private const string ValidOrderId = "ord-1";
    private const string ValidMakerId = "maker-1";

    private static readonly DateOnly IssueDate = new(2026, 6, 1);
    private static readonly DateOnly DueDate = new(2026, 6, 15);
    private static readonly DateOnly TaxableSupplyDate = new(2026, 6, 1);

    private static Invoice ValidCustomerInvoice(
        InvoicingMode mode = InvoicingMode.None,
        long amountWithoutVatMinor = 100_00,
        int vatRateBp = 0,
        long vatAmountMinor = 0,
        long amountWithVatMinor = 100_00,
        string? pdfBlobPath = null,
        string? issuerAddress = null,
        DateOnly? paidOn = null,
        string? paymentMethod = null) =>
        Invoice.Issue(
            id: ValidId,
            invoiceNumber: ValidNumber,
            type: InvoiceType.Customer,
            orderId: ValidOrderId,
            orderNumber: "OBJ-20260819-0001",
            payoutBatchId: null,
            makerId: ValidMakerId,
            issuerName: "JVM YORE s.r.o.",
            issuerIco: "12345678",
            issuerDic: mode == InvoicingMode.StandardVat ? "CZ12345678" : null,
            issuerBankAccount: "1234567890/0100", issuerAddress: issuerAddress,
            recipientName: "Anna Nováková",
            recipientEmail: "anna@example.cz",
            recipientTaxId: null,
            recipientVatId: null,
            issueDate: IssueDate,
            taxableSupplyDate: mode == InvoicingMode.None ? null : TaxableSupplyDate,
            dueDate: DueDate,
            invoicingMode: mode,
            amountWithoutVatMinor: amountWithoutVatMinor,
            vatRateBp: vatRateBp,
            vatAmountMinor: vatAmountMinor,
            amountWithVatMinor: amountWithVatMinor,
            currency: "CZK",
            countryCode: "CZ",
            paidOn: paidOn,
            paymentMethod: paymentMethod,
            pdfBlobPath: pdfBlobPath);

    // === Factory happy paths ===

    [Fact]
    public void Issue_creates_customer_invoice_in_None_mode_with_zero_VAT()
    {
        var invoice = ValidCustomerInvoice();

        invoice.Id.Should().Be(ValidId);
        invoice.InvoiceNumber.Should().Be(ValidNumber);
        invoice.Type.Should().Be(InvoiceType.Customer);
        invoice.OrderId.Should().Be(ValidOrderId);
        invoice.PayoutBatchId.Should().BeNull();
        invoice.MakerId.Should().Be(ValidMakerId);
        invoice.IssuerName.Should().Be("JVM YORE s.r.o.");
        invoice.IssuerIco.Should().Be("12345678");
        invoice.IssuerDic.Should().BeNull();
        invoice.InvoicingMode.Should().Be(InvoicingMode.None);
        invoice.AmountWithoutVatMinor.Should().Be(100_00);
        invoice.VatRateBp.Should().Be(0);
        invoice.VatAmountMinor.Should().Be(0);
        invoice.AmountWithVatMinor.Should().Be(100_00);
        invoice.Currency.Should().Be("CZK");
        invoice.CountryCode.Should().Be("CZ");
        invoice.TaxableSupplyDate.Should().BeNull();
        invoice.PdfBlobPath.Should().BeNull();
        invoice.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Issue_creates_customer_invoice_in_StandardVat_with_balanced_money_and_DUZP()
    {
        // 100.00 net + 21% VAT (2100 bp) = 21.00 VAT → 121.00 gross.
        var invoice = ValidCustomerInvoice(
            mode: InvoicingMode.StandardVat,
            amountWithoutVatMinor: 100_00,
            vatRateBp: 2100,
            vatAmountMinor: 21_00,
            amountWithVatMinor: 121_00);

        invoice.InvoicingMode.Should().Be(InvoicingMode.StandardVat);
        invoice.IssuerDic.Should().Be("CZ12345678");
        invoice.VatRateBp.Should().Be(2100);
        invoice.VatAmountMinor.Should().Be(21_00);
        invoice.AmountWithoutVatMinor.Should().Be(100_00);
        invoice.AmountWithVatMinor.Should().Be(121_00);
        invoice.TaxableSupplyDate.Should().Be(TaxableSupplyDate);
    }

    // === Money-balance invariant (AC-1) ===

    [Fact]
    public void Issue_rejects_when_amountWithoutVat_plus_VatAmount_does_not_equal_amountWithVat()
    {
        var act = () => ValidCustomerInvoice(
            mode: InvoicingMode.StandardVat,
            amountWithoutVatMinor: 100_00,
            vatRateBp: 2100,
            vatAmountMinor: 21_00,
            amountWithVatMinor: 999_00); // imbalanced

        act.Should().Throw<ArgumentException>()
            .WithMessage("*AmountWithVat*AmountWithoutVat*Vat*");
    }

    // === XOR aggregate-link invariant (AC-2) ===

    [Fact]
    public void Issue_rejects_when_both_OrderId_and_PayoutBatchId_are_null()
    {
        var act = () => Invoice.Issue(
            id: ValidId, invoiceNumber: ValidNumber, type: InvoiceType.Customer,
            orderId: null, payoutBatchId: null, makerId: ValidMakerId,
            orderNumber: "OBJ-20260819-0001",
            issuerName: "X", issuerIco: "1", issuerDic: null, issuerBankAccount: null, issuerAddress: null,
            recipientName: "Y", recipientEmail: "y@y.cz",
            recipientTaxId: null, recipientVatId: null,
            issueDate: IssueDate, taxableSupplyDate: null, dueDate: DueDate,
            invoicingMode: InvoicingMode.None,
            amountWithoutVatMinor: 0, vatRateBp: 0,
            vatAmountMinor: 0, amountWithVatMinor: 0,
            currency: "CZK", countryCode: "CZ", pdfBlobPath: null);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*OrderId*PayoutBatchId*");
    }

    [Fact]
    public void Issue_rejects_when_both_OrderId_and_PayoutBatchId_are_non_null()
    {
        var act = () => Invoice.Issue(
            id: ValidId, invoiceNumber: ValidNumber, type: InvoiceType.Customer,
            orderId: ValidOrderId, payoutBatchId: "batch-1", makerId: ValidMakerId,
            orderNumber: "OBJ-20260819-0001",
            issuerName: "X", issuerIco: "1", issuerDic: null, issuerBankAccount: null, issuerAddress: null,
            recipientName: "Y", recipientEmail: "y@y.cz",
            recipientTaxId: null, recipientVatId: null,
            issueDate: IssueDate, taxableSupplyDate: null, dueDate: DueDate,
            invoicingMode: InvoicingMode.None,
            amountWithoutVatMinor: 0, vatRateBp: 0,
            vatAmountMinor: 0, amountWithVatMinor: 0,
            currency: "CZK", countryCode: "CZ", pdfBlobPath: null);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*OrderId*PayoutBatchId*");
    }

    // === None mode + zero VAT invariant ===

    [Fact]
    public void Issue_rejects_None_mode_with_nonzero_VAT_amount()
    {
        var act = () => ValidCustomerInvoice(
            mode: InvoicingMode.None,
            amountWithoutVatMinor: 100_00,
            vatRateBp: 0,
            vatAmountMinor: 21_00,   // wrong: None mode must have zero VAT
            amountWithVatMinor: 121_00);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*None*VAT*");
    }

    // === Currency invariant ===

    [Theory]
    [InlineData("")]
    [InlineData("CZ")]
    [InlineData("CZKK")]
    public void Issue_rejects_invalid_currency(string currency)
    {
        var act = () => Invoice.Issue(
            id: ValidId, invoiceNumber: ValidNumber, type: InvoiceType.Customer,
            orderId: ValidOrderId, payoutBatchId: null, makerId: ValidMakerId,
            orderNumber: "OBJ-20260819-0001",
            issuerName: "X", issuerIco: "1", issuerDic: null, issuerBankAccount: null, issuerAddress: null,
            recipientName: "Y", recipientEmail: "y@y.cz",
            recipientTaxId: null, recipientVatId: null,
            issueDate: IssueDate, taxableSupplyDate: null, dueDate: DueDate,
            invoicingMode: InvoicingMode.None,
            amountWithoutVatMinor: 0, vatRateBp: 0,
            vatAmountMinor: 0, amountWithVatMinor: 0,
            currency: currency, countryCode: "CZ", pdfBlobPath: null);

        act.Should().Throw<ArgumentException>()
            .Which.ParamName.Should().Be("currency");
    }

    // === Required-string invariants ===

    [Theory]
    [InlineData("", "id")]
    [InlineData("   ", "id")]
    public void Issue_rejects_blank_id(string id, string expectedParam)
    {
        var act = () => Invoice.Issue(
            id: id, invoiceNumber: ValidNumber, type: InvoiceType.Customer,
            orderId: ValidOrderId, payoutBatchId: null, makerId: ValidMakerId,
            orderNumber: "OBJ-20260819-0001",
            issuerName: "X", issuerIco: "1", issuerDic: null, issuerBankAccount: null, issuerAddress: null,
            recipientName: "Y", recipientEmail: "y@y.cz",
            recipientTaxId: null, recipientVatId: null,
            issueDate: IssueDate, taxableSupplyDate: null, dueDate: DueDate,
            invoicingMode: InvoicingMode.None,
            amountWithoutVatMinor: 0, vatRateBp: 0,
            vatAmountMinor: 0, amountWithVatMinor: 0,
            currency: "CZK", countryCode: "CZ", pdfBlobPath: null);

        act.Should().Throw<ArgumentException>()
            .Which.ParamName.Should().Be(expectedParam);
    }

    [Fact]
    public void Issue_rejects_blank_invoiceNumber()
    {
        var act = () => Invoice.Issue(
            id: ValidId, invoiceNumber: "  ", type: InvoiceType.Customer,
            orderId: ValidOrderId, payoutBatchId: null, makerId: ValidMakerId,
            orderNumber: "OBJ-20260819-0001",
            issuerName: "X", issuerIco: "1", issuerDic: null, issuerBankAccount: null, issuerAddress: null,
            recipientName: "Y", recipientEmail: "y@y.cz",
            recipientTaxId: null, recipientVatId: null,
            issueDate: IssueDate, taxableSupplyDate: null, dueDate: DueDate,
            invoicingMode: InvoicingMode.None,
            amountWithoutVatMinor: 0, vatRateBp: 0,
            vatAmountMinor: 0, amountWithVatMinor: 0,
            currency: "CZK", countryCode: "CZ", pdfBlobPath: null);

        act.Should().Throw<ArgumentException>()
            .Which.ParamName.Should().Be("invoiceNumber");
    }

    [Fact]
    public void Issue_rejects_blank_issuer_name_or_ico()
    {
        var actNoName = () => Invoice.Issue(
            id: ValidId, invoiceNumber: ValidNumber, type: InvoiceType.Customer,
            orderId: ValidOrderId, payoutBatchId: null, makerId: ValidMakerId,
            orderNumber: "OBJ-20260819-0001",
            issuerName: "", issuerIco: "1", issuerDic: null, issuerBankAccount: null, issuerAddress: null,
            recipientName: "Y", recipientEmail: "y@y.cz",
            recipientTaxId: null, recipientVatId: null,
            issueDate: IssueDate, taxableSupplyDate: null, dueDate: DueDate,
            invoicingMode: InvoicingMode.None,
            amountWithoutVatMinor: 0, vatRateBp: 0,
            vatAmountMinor: 0, amountWithVatMinor: 0,
            currency: "CZK", countryCode: "CZ", pdfBlobPath: null);
        actNoName.Should().Throw<ArgumentException>()
            .Which.ParamName.Should().Be("issuerName");

        var actNoIco = () => Invoice.Issue(
            id: ValidId, invoiceNumber: ValidNumber, type: InvoiceType.Customer,
            orderId: ValidOrderId, payoutBatchId: null, makerId: ValidMakerId,
            orderNumber: "OBJ-20260819-0001",
            issuerName: "X", issuerIco: "", issuerDic: null, issuerBankAccount: null, issuerAddress: null,
            recipientName: "Y", recipientEmail: "y@y.cz",
            recipientTaxId: null, recipientVatId: null,
            issueDate: IssueDate, taxableSupplyDate: null, dueDate: DueDate,
            invoicingMode: InvoicingMode.None,
            amountWithoutVatMinor: 0, vatRateBp: 0,
            vatAmountMinor: 0, amountWithVatMinor: 0,
            currency: "CZK", countryCode: "CZ", pdfBlobPath: null);
        actNoIco.Should().Throw<ArgumentException>()
            .Which.ParamName.Should().Be("issuerIco");
    }

    [Fact]
    public void Issue_rejects_negative_money_amounts()
    {
        var act = () => ValidCustomerInvoice(
            mode: InvoicingMode.None,
            amountWithoutVatMinor: -1,
            vatRateBp: 0,
            vatAmountMinor: 0,
            amountWithVatMinor: -1);

        act.Should().Throw<ArgumentException>();
    }

    // === AC-3 — AttachPdfBlobPath set-once semantics ===

    [Fact]
    public void AttachPdfBlobPath_first_call_sets_path_and_returns_success()
    {
        var invoice = ValidCustomerInvoice();

        var result = invoice.AttachPdfBlobPath("invoices/cz/orders/ord-1/FV-CZ-20260001.pdf");

        result.IsSuccess.Should().BeTrue();
        invoice.PdfBlobPath.Should().Be("invoices/cz/orders/ord-1/FV-CZ-20260001.pdf");
    }

    [Fact]
    public void AttachPdfBlobPath_same_value_second_call_is_idempotent_success()
    {
        var invoice = ValidCustomerInvoice();
        const string path = "invoices/cz/orders/ord-1/FV-CZ-20260001.pdf";

        invoice.AttachPdfBlobPath(path).IsSuccess.Should().BeTrue();
        var second = invoice.AttachPdfBlobPath(path);

        second.IsSuccess.Should().BeTrue(
            because: "idempotent retry of the same blob upload is the renderer's expected " +
                     "happy path — T-0068b deterministically produces the same path on retry");
        invoice.PdfBlobPath.Should().Be(path);
    }

    [Fact]
    public void AttachPdfBlobPath_different_value_second_call_fails_with_BlobPathAlreadySet()
    {
        var invoice = ValidCustomerInvoice();

        invoice.AttachPdfBlobPath("invoices/cz/orders/ord-1/FV-CZ-20260001.pdf");
        var second = invoice.AttachPdfBlobPath("invoices/cz/orders/ord-1/DIFFERENT.pdf");

        second.IsSuccess.Should().BeFalse();
        second.Error!.Code.Should().Be(BusinessErrorMessage.InvoiceBlobPathAlreadySet);
        invoice.PdfBlobPath.Should().Be("invoices/cz/orders/ord-1/FV-CZ-20260001.pdf",
            because: "a rejected overwrite must leave the original path intact");
    }

    [Fact]
    public void AttachPdfBlobPath_rejects_blank_input()
    {
        var invoice = ValidCustomerInvoice();

        var act = () => invoice.AttachPdfBlobPath("");

        act.Should().Throw<ArgumentException>()
            .Which.ParamName.Should().Be("pdfBlobPath");
    }

    // === Fee invoice happy path (XOR resolution via PayoutBatchId) ===

    [Fact]
    public void Issue_creates_Fee_invoice_with_PayoutBatchId_and_null_OrderId()
    {
        var invoice = Invoice.Issue(
            id: ValidId, invoiceNumber: ValidNumber, type: InvoiceType.Fee,
            orderId: null, payoutBatchId: "batch-1", makerId: ValidMakerId,
            orderNumber: "OBJ-20260819-0001",
            issuerName: "JVM YORE s.r.o.", issuerIco: "12345678",
            issuerDic: null, issuerBankAccount: null, issuerAddress: null,
            recipientName: "Maker GmbH", recipientEmail: "maker@example.cz",
            recipientTaxId: "87654321", recipientVatId: null,
            issueDate: IssueDate, taxableSupplyDate: null, dueDate: DueDate,
            invoicingMode: InvoicingMode.None,
            amountWithoutVatMinor: 50_00, vatRateBp: 0,
            vatAmountMinor: 0, amountWithVatMinor: 50_00,
            currency: "CZK", countryCode: "CZ", pdfBlobPath: null);

        invoice.Type.Should().Be(InvoiceType.Fee);
        invoice.OrderId.Should().BeNull();
        invoice.PayoutBatchId.Should().Be("batch-1");
    }

    // === Settlement + issuer-address snapshot ===

    [Fact]
    public void Issue_leaves_the_invoice_outstanding_when_no_settlement_is_supplied()
    {
        var invoice = ValidCustomerInvoice();

        invoice.PaidOn.Should().BeNull();
        invoice.PaymentMethod.Should().BeNull();
    }

    [Fact]
    public void Issue_snapshots_the_settlement_date_and_channel()
    {
        var paidOn = new DateOnly(2026, 6, 1);

        var invoice = ValidCustomerInvoice(paidOn: paidOn, paymentMethod: "CARD_CZ_CSOB_2");

        invoice.PaidOn.Should().Be(paidOn);
        invoice.PaymentMethod.Should().Be("CARD_CZ_CSOB_2");
    }

    [Fact]
    public void Issue_rejects_a_payment_method_without_a_settlement_date()
    {
        // Half a settlement fact renders as neither a receipt nor a payment
        // request — the document would silently lose both.
        var act = () => ValidCustomerInvoice(paidOn: null, paymentMethod: "CARD_CZ_CSOB_2");

        act.Should().Throw<ArgumentException>()
            .WithParameterName("paymentMethod");
    }

    [Fact]
    public void Issue_accepts_a_settlement_date_without_a_channel()
    {
        // Backfilled rows know WHEN but not HOW; that is a whole fact.
        var invoice = ValidCustomerInvoice(paidOn: new DateOnly(2026, 6, 1));

        invoice.PaidOn.Should().Be(new DateOnly(2026, 6, 1));
        invoice.PaymentMethod.Should().BeNull();
    }

    [Fact]
    public void Issue_trims_the_settlement_channel_and_normalises_blank_to_null()
    {
        ValidCustomerInvoice(paidOn: new DateOnly(2026, 6, 1), paymentMethod: "  CARD  ")
            .PaymentMethod.Should().Be("CARD");
        ValidCustomerInvoice(paidOn: new DateOnly(2026, 6, 1), paymentMethod: "   ")
            .PaymentMethod.Should().BeNull();
    }

    [Fact]
    public void Issue_snapshots_and_trims_the_issuer_address()
    {
        ValidCustomerInvoice(issuerAddress: "  Příčná 1892/4, Nové Město, 110 00 Praha 1  ")
            .IssuerAddress.Should().Be("Příčná 1892/4, Nové Město, 110 00 Praha 1");
        ValidCustomerInvoice(issuerAddress: "  ").IssuerAddress.Should().BeNull();
    }

    [Fact]
    public void Issue_rejects_an_oversized_issuer_address()
    {
        var act = () => ValidCustomerInvoice(
            issuerAddress: new string('x', Invoice.MaxIssuerAddressLength + 1));

        act.Should().Throw<ArgumentException>().WithParameterName("issuerAddress");
    }

    [Fact]
    public void Issue_rejects_an_oversized_payment_method()
    {
        var act = () => ValidCustomerInvoice(
            paidOn: new DateOnly(2026, 6, 1),
            paymentMethod: new string('x', Invoice.MaxPaymentMethodLength + 1));

        act.Should().Throw<ArgumentException>().WithParameterName("paymentMethod");
    }
}
