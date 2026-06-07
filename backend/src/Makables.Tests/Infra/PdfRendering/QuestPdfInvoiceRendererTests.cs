using System.Text;
using FluentAssertions;
using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Invoices;
using Makables.Infra.PdfRendering;

namespace Makables.Tests.Infra.PdfRendering;

/// <summary>
/// Pins <see cref="QuestPdfInvoiceRenderer"/>'s mode-switch + the basic
/// PDF-byte invariants (magic header bytes, contains the invoice
/// number string) per T-0068b AC-1 / AC-2 / AC-11 + tests directive.
///
/// <para>
/// These are unit tests on a real QuestPDF renderer (not a mock) so they
/// exercise the QuestPDF DSL composition end-to-end. PDF byte output is
/// 50-150 KB per render; tests assert on the magic header + the
/// inclusion of the invoice number as a substring of the decoded
/// content (the PDF content stream stores text verbatim in PDF 1.4).
/// </para>
/// </summary>
public class QuestPdfInvoiceRendererTests
{
    private static readonly DateOnly IssueDate = new(2026, 6, 7);
    private static readonly DateOnly DueDate = new(2026, 6, 21);
    private static readonly DateOnly TaxableSupplyDate = new(2026, 6, 7);

    private static Invoice BuildInvoiceInMode(InvoicingMode mode, string number = "FV-CZ-20260042")
    {
        return mode == InvoicingMode.StandardVat
            ? Invoice.Issue(
                id: "inv-1",
                invoiceNumber: number,
                type: InvoiceType.Customer,
                orderId: "ord-1",
                payoutBatchId: null,
                makerId: "maker-1",
                issuerName: "JVM YORE s.r.o.",
                issuerIco: "12345678",
                issuerDic: "CZ12345678",
                issuerBankAccount: "1234567890/0100",
                recipientName: "Anna Nováková",
                recipientEmail: "anna@example.cz",
                recipientTaxId: null,
                recipientVatId: null,
                issueDate: IssueDate,
                taxableSupplyDate: TaxableSupplyDate,
                dueDate: DueDate,
                invoicingMode: mode,
                amountWithoutVatMinor: 100_00,
                vatRateBp: 2100,
                vatAmountMinor: 21_00,
                amountWithVatMinor: 121_00,
                currency: "CZK",
                countryCode: "CZ")
            : Invoice.Issue(
                id: "inv-1",
                invoiceNumber: number,
                type: InvoiceType.Customer,
                orderId: "ord-1",
                payoutBatchId: null,
                makerId: "maker-1",
                issuerName: "JVM YORE s.r.o.",
                issuerIco: "00000000",
                issuerDic: null,
                issuerBankAccount: null,
                recipientName: "Anna Nováková",
                recipientEmail: "anna@example.cz",
                recipientTaxId: null,
                recipientVatId: null,
                issueDate: IssueDate,
                taxableSupplyDate: null,
                dueDate: DueDate,
                invoicingMode: mode,
                amountWithoutVatMinor: 100_00,
                vatRateBp: 0,
                vatAmountMinor: 0,
                amountWithVatMinor: 100_00,
                currency: "CZK",
                countryCode: "CZ");
    }

    private static CountryConfiguration BuildCzConfig(string? platformIban = null) =>
        CountryConfiguration.Create(
            countryId: "CZ",
            defaultCurrencyCode: "CZK",
            defaultLanguageCode: "cs-CZ",
            timeZoneId: "Europe/Prague",
            phonePrefix: "+420",
            dateFormat: "d. M. yyyy",
            standardVatRateBp: 2100,
            taxIdLabel: "DIČ",
            vatIdLabel: "DIČ",
            registrationNumberLabel: "IČO",
            defaultPaymentProvider: "comgate",
            defaultShippingCarrier: "packeta",
            defaultRegistry: "ares",
            defaultEmailProvider: "sendgrid",
            issuerName: "JVM YORE s.r.o.",
            issuerIco: "00000000",
            platformIban: platformIban);

    [Fact]
    public async Task None_mode_produces_a_valid_PDF_with_magic_header()
    {
        var sut = new QuestPdfInvoiceRenderer();
        var invoice = BuildInvoiceInMode(InvoicingMode.None);

        var bytes = await sut.RenderAsync(invoice, BuildCzConfig(), CancellationToken.None);

        bytes.Should().NotBeNullOrEmpty();
        // PDF magic header: "%PDF" = 0x25 0x50 0x44 0x46.
        bytes[0].Should().Be(0x25);
        bytes[1].Should().Be(0x50);
        bytes[2].Should().Be(0x44);
        bytes[3].Should().Be(0x46);
    }

    [Fact]
    public async Task StandardVat_mode_produces_a_valid_PDF_with_magic_header()
    {
        var sut = new QuestPdfInvoiceRenderer();
        var invoice = BuildInvoiceInMode(InvoicingMode.StandardVat);

        var bytes = await sut.RenderAsync(invoice, BuildCzConfig(), CancellationToken.None);

        bytes.Should().NotBeNullOrEmpty();
        bytes[0].Should().Be(0x25);
        bytes[1].Should().Be(0x50);
        bytes[2].Should().Be(0x44);
        bytes[3].Should().Be(0x46);
    }

    [Fact]
    public async Task PDF_contains_the_invoice_number_string()
    {
        var sut = new QuestPdfInvoiceRenderer();
        var invoice = BuildInvoiceInMode(InvoicingMode.None, number: "FV-CZ-20260042");

        var bytes = await sut.RenderAsync(invoice, BuildCzConfig(), CancellationToken.None);

        // PDF content streams store ASCII text verbatim in uncompressed
        // form; QuestPDF emits text via PDF "Tj" operators using the
        // literal characters. Scan the raw bytes for the invoice number
        // as a Latin-1 string.
        var latin1 = Encoding.Latin1.GetString(bytes);
        latin1.Should().Contain("20260042",
            "the PDF must carry the invoice number visibly to the reader; " +
            "encoded text appears in the PDF content stream");
    }

    [Fact]
    public async Task ReverseCharge_throws_NotImplementedException()
    {
        var sut = new QuestPdfInvoiceRenderer();
        var invoice = BuildInvoiceInMode(InvoicingMode.None);
        // Force the mode field directly via reflection — Invoice.Issue
        // refuses ReverseCharge if VAT is set, but for this renderer
        // test we need an instance in the mode to exercise the renderer's
        // mode-switch throw.
        typeof(Invoice).GetProperty(nameof(Invoice.InvoicingMode))!
            .SetValue(invoice, InvoicingMode.ReverseCharge);

        var act = async () => await sut.RenderAsync(invoice, BuildCzConfig(), CancellationToken.None);

        await act.Should().ThrowAsync<NotImplementedException>();
    }
}
