using System.Text;
using FluentAssertions;
using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Invoices;
using Makables.Core.Domain.Rendering;
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

    private static Invoice BuildInvoiceInMode(
        InvoicingMode mode,
        string number = "FV-CZ-20260042",
        string? orderNumber = "OBJ-20260819-0001",
        DateOnly? paidOn = null,
        string? paymentMethod = null)
    {
        return mode == InvoicingMode.StandardVat
            ? Invoice.Issue(
                id: "inv-1",
                invoiceNumber: number,
                type: InvoiceType.Customer,
                orderId: "ord-1",
                orderNumber: orderNumber,
                payoutBatchId: null,
                makerId: "maker-1",
                issuerName: "JVM YORE s.r.o.",
                issuerIco: "12345678",
                issuerDic: "CZ12345678",
                issuerBankAccount: "1234567890/0100",
                issuerAddress: null,
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
                countryCode: "CZ",
                paidOn: paidOn,
                paymentMethod: paymentMethod)
            : Invoice.Issue(
                id: "inv-1",
                invoiceNumber: number,
                type: InvoiceType.Customer,
                orderId: "ord-1",
                orderNumber: orderNumber,
                payoutBatchId: null,
                makerId: "maker-1",
                issuerName: "JVM YORE s.r.o.",
                issuerIco: "00000000",
                issuerDic: null,
                issuerBankAccount: null,
                issuerAddress: null,
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
                countryCode: "CZ",
                paidOn: paidOn,
                paymentMethod: paymentMethod);
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
            issuerIco: "29633443",
            platformIban: platformIban,
            issuerAddress: "Příčná 1892/4, Nové Město, 110 00 Praha 1");

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
    public async Task PDF_metadata_carries_the_invoice_number()
    {
        var sut = new QuestPdfInvoiceRenderer();
        var invoice = BuildInvoiceInMode(InvoicingMode.None, number: "FV-CZ-20260042");

        var bytes = await sut.RenderAsync(invoice, BuildCzConfig(), CancellationToken.None);

        // The document Title / Subject are written to the PDF info
        // dictionary uncompressed, so they ARE greppable. The page text is
        // not: QuestPDF Flate-compresses its content streams and writes
        // glyph indices with no ToUnicode CMap, so nothing on the page can
        // be read back out of the bytes. Assertions about what the reader
        // sees therefore live on InvoiceFormatting (the strings) and on the
        // branch tests below (the structure) — not here.
        var latin1 = Encoding.Latin1.GetString(bytes);
        latin1.Should().Contain("20260042",
            "the invoice number is the document's Subject and Title, which " +
            "is what a file manager and a PDF reader show in the title bar");
    }

    // === Settled vs outstanding ===

    [Fact]
    public async Task A_settled_invoice_renders_a_different_document_than_an_outstanding_one()
    {
        // The reported defect: a paid order's receipt printed "Celkem k
        // úhradě 739 Kč" with a due date. Settlement now drives the whole
        // lower half of the page — the totals label, the UHRAZENO stamp,
        // the metadata band, and whether payment instructions appear at
        // all — so the two branches cannot produce the same bytes.
        var sut = new QuestPdfInvoiceRenderer();
        var outstanding = BuildInvoiceInMode(InvoicingMode.None);
        var settled = BuildInvoiceInMode(
            InvoicingMode.None,
            paidOn: IssueDate,
            paymentMethod: "CARD_CZ_CSOB_2");

        var outstandingBytes = await sut.RenderAsync(outstanding, BuildCzConfig(), CancellationToken.None);
        var settledBytes = await sut.RenderAsync(settled, BuildCzConfig(), CancellationToken.None);

        settledBytes.Should().NotEqual(outstandingBytes);
    }

    [Fact]
    public async Task An_outstanding_invoice_renders_the_SPAYD_block_when_an_IBAN_is_configured()
    {
        // Payment instructions are gated on BOTH an IBAN and the invoice
        // still being outstanding.
        var sut = new QuestPdfInvoiceRenderer();
        var outstanding = BuildInvoiceInMode(InvoicingMode.None);

        var withIban = await sut.RenderAsync(
            outstanding, BuildCzConfig("CZ5520100000002702345678"), CancellationToken.None);
        var withoutIban = await sut.RenderAsync(
            outstanding, BuildCzConfig(), CancellationToken.None);

        withIban.Should().NotEqual(withoutIban);
    }

    [Fact]
    public async Task A_settled_invoice_ignores_the_IBAN_so_no_receipt_asks_for_payment()
    {
        // A receipt must never carry a pay-me QR: the customer already paid.
        var sut = new QuestPdfInvoiceRenderer();
        var settled = BuildInvoiceInMode(
            InvoicingMode.None, paidOn: IssueDate, paymentMethod: "CARD_CZ_CSOB_2");

        var withIban = await sut.RenderAsync(
            settled, BuildCzConfig("CZ5520100000002702345678"), CancellationToken.None);
        var withoutIban = await sut.RenderAsync(
            settled, BuildCzConfig(), CancellationToken.None);

        withIban.Should().Equal(withoutIban);
    }

    // === The order the document names ===

    [Fact]
    public async Task The_line_item_names_the_snapshotted_order_number()
    {
        // The customer looks the order up as OBJ-20260819-0001. Before the
        // snapshot existed the templates printed the invoice number's own
        // numeric tail ("Objednávka 20260042"), which matches nothing in
        // the customer's order list.
        var sut = new QuestPdfInvoiceRenderer();
        var named = BuildInvoiceInMode(InvoicingMode.None, orderNumber: "OBJ-20260819-0001");
        var unnamed = BuildInvoiceInMode(InvoicingMode.None, orderNumber: null);

        var namedBytes = await sut.RenderAsync(named, BuildCzConfig(), CancellationToken.None);
        var unnamedBytes = await sut.RenderAsync(unnamed, BuildCzConfig(), CancellationToken.None);

        namedBytes.Should().NotEqual(unnamedBytes,
            "the order number reaches the page, so dropping it changes the render");
    }

    [Fact]
    public async Task An_invoice_issued_before_the_snapshot_still_renders()
    {
        // Rows predating the order_number column carry null and fall back
        // to the invoice-number tail — the reference those documents were
        // already printing. It must not throw or blank the line.
        var sut = new QuestPdfInvoiceRenderer();
        var legacy = BuildInvoiceInMode(InvoicingMode.StandardVat, orderNumber: null);

        var bytes = await sut.RenderAsync(legacy, BuildCzConfig(), CancellationToken.None);

        bytes.Should().NotBeNullOrEmpty();
        bytes[..4].Should().Equal([(byte)0x25, (byte)0x50, (byte)0x44, (byte)0x46]);
    }

    // === Determinism (the blob-overwrite contract) ===

    [Theory]
    [InlineData(InvoicingMode.None)]
    [InlineData(InvoicingMode.StandardVat)]
    public async Task Re_rendering_the_same_invoice_produces_identical_bytes(InvoicingMode mode)
    {
        // What makes IssueInvoice.Handler's blob overwrite safe on retry.
        var sut = new QuestPdfInvoiceRenderer();
        var invoice = BuildInvoiceInMode(mode, paidOn: IssueDate, paymentMethod: "CARD_CZ_CSOB_2");

        var first = await sut.RenderAsync(invoice, BuildCzConfig(), CancellationToken.None);
        var second = await sut.RenderAsync(invoice, BuildCzConfig(), CancellationToken.None);

        second.Should().Equal(first);
    }

    // === Fee invoice ===

    [Fact]
    public async Task Fee_invoice_renders_one_line_per_claimed_order()
    {
        var sut = new QuestPdfInvoiceRenderer();
        var fee = BuildFeeInvoice();
        var config = BuildCzConfig();

        var one = await sut.RenderFeeAsync(
            fee, [new FeeInvoiceLineItem("20260118", 51_73)], config, CancellationToken.None);
        var three = await sut.RenderFeeAsync(
            fee,
            [
                new FeeInvoiceLineItem("20260118", 51_73),
                new FeeInvoiceLineItem("20260121", 103_60),
                new FeeInvoiceLineItem("20260129", 1_540_00),
            ],
            config, CancellationToken.None);

        three.Should().NotEqual(one);
        one[0].Should().Be(0x25);
    }

    [Fact]
    public async Task Fee_invoice_paginates_a_long_claim_without_throwing()
    {
        // A busy maker's weekly batch runs past one page; the masthead and
        // footer repeat and the table header carries over.
        var sut = new QuestPdfInvoiceRenderer();
        var lineItems = Enumerable.Range(1, 60)
            .Select(i => new FeeInvoiceLineItem($"2026{1000 + i}", 51_73 + i))
            .ToList();

        var bytes = await sut.RenderFeeAsync(
            BuildFeeInvoice(), lineItems, BuildCzConfig(), CancellationToken.None);

        bytes.Should().NotBeNullOrEmpty();
        Encoding.Latin1.GetString(bytes).Should().Contain("/Type /Page");
    }

    private static Invoice BuildFeeInvoice() =>
        Invoice.Issue(
            id: "inv-fee-1",
            invoiceNumber: "FV-CZ-20260044",
            type: InvoiceType.Fee,
            orderId: null,
            orderNumber: null,
            payoutBatchId: "pb-1",
            makerId: "maker-1",
            issuerName: "JVM Yore, s.r.o.",
            issuerIco: "29633443",
            issuerDic: null,
            issuerBankAccount: null,
            issuerAddress: "Příčná 1892/4, Nové Město, 110 00 Praha 1",
            recipientName: "Dřevěné hračky Krkonoše s.r.o.",
            recipientEmail: "dilna@example.cz",
            recipientTaxId: "12345678",
            recipientVatId: null,
            issueDate: IssueDate,
            taxableSupplyDate: null,
            dueDate: DueDate,
            invoicingMode: InvoicingMode.None,
            amountWithoutVatMinor: 1_842_50,
            vatRateBp: 0,
            vatAmountMinor: 0,
            amountWithVatMinor: 1_842_50,
            currency: "CZK",
            countryCode: "CZ",
            paidOn: IssueDate,
            paymentMethod: SettlementMethods.PayoutDeduction);

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
