using FluentAssertions;
using Makables.Core.AppServices.Features.Invoices;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Invoices;
using Makables.Core.Domain.Numbering;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Rendering;
using Makables.Core.Domain.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Invoices;

/// <summary>
/// Pins T-0068b <see cref="IssueInvoice.Handler"/>: load order → load
/// country config → idempotency pre-check → InvoicingMode switch →
/// allocate number → build aggregate → persist → render → upload →
/// attach blob path → return Response. NSubstitute over the 8
/// dependencies; the domain invariants on <see cref="Invoice.Issue"/>
/// are pinned by the domain tests.
/// </summary>
public class IssueInvoiceHandlerTests
{
    private const string OrderId = "ord-1";
    private const string MakerId = "maker-1";
    private const string CustomerUserId = "user-1";
    private const string InvoiceNumber = "FV-CZ-20260042";
    private const string InvoiceId = "inv-generated";

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-07T10:00:00Z");
    private static readonly DateTimeOffset PaidAt = DateTimeOffset.Parse("2026-06-07T09:00:00Z");

    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly ICountryConfigurationRepository _countries = Substitute.For<ICountryConfigurationRepository>();
    private readonly IInvoiceRepository _invoices = Substitute.For<IInvoiceRepository>();
    private readonly IInvoiceNumberGenerator _invoiceNumbers = Substitute.For<IInvoiceNumberGenerator>();
    private readonly IInvoicePdfRenderer _renderer = Substitute.For<IInvoicePdfRenderer>();
    private readonly IBlobStorageClient _blob = Substitute.For<IBlobStorageClient>();
    private readonly IIdGenerator _ids = Substitute.For<IIdGenerator>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly IssueInvoice.Handler _sut;

    public IssueInvoiceHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _ids.Next().Returns(InvoiceId);
        _invoiceNumbers.NextAsync("CZ", Arg.Any<CancellationToken>()).Returns(InvoiceNumber);
        _renderer.RenderAsync(Arg.Any<Invoice>(), Arg.Any<CountryConfiguration>(), Arg.Any<CancellationToken>())
            .Returns(new byte[] { 0x25, 0x50, 0x44, 0x46 }); // "%PDF"
        _blob.UploadAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success());

        _sut = new IssueInvoice.Handler(
            _orders, _countries, _invoices, _invoiceNumbers,
            _renderer, _blob, _ids, _clock,
            NullLogger<IssueInvoice.Handler>.Instance);
    }

    private static Order BuildPaidOrder() =>
        Order.Create(
            id: OrderId,
            orderNumber: "M-CZ-20260042",
            customerUserId: CustomerUserId,
            makerId: MakerId,
            productId: "prod-1",
            contactName: "Anna Nováková",
            contactEmail: "anna@example.cz",
            contactPhone: "+420",
            productPriceAmountMinor: 50_000,
            shippingPriceAmountMinor: 7_900,
            platformFeeAmountMinor: 7_500,
            makerPayoutAmountMinor: 50_400,
            totalAmountMinor: 57_900,
            currency: "CZK",
            vatRateBp: 2100,
            shippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
            zasilkovnaPickupPointId: "pp-42",
            countryCode: "CZ");

    private static Order BuildPaidOrderInState()
    {
        var o = BuildPaidOrder();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        o.MarkAsPaid(clock, paymentProviderRef: "comgate-tx", paymentMethod: "CARD_CZ", paidAtOverride: PaidAt);
        return o;
    }

    private static CountryConfiguration BuildCzConfig(
        InvoicingMode mode = InvoicingMode.None,
        string? platformIban = null) =>
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
            issuerName: "JVM Yore, s.r.o.",
            issuerIco: "29633443",
            invoicingMode: mode,
            platformIban: platformIban,
            issuerAddress: "Příčná 1892/4, Nové Město, 110 00 Praha 1");

    private void ArrangeDefaults(InvoicingMode mode = InvoicingMode.None)
    {
        var order = BuildPaidOrderInState();
        var config = BuildCzConfig(mode);
        _orders.GetByIdUnscopedAsync(OrderId, Arg.Any<CancellationToken>())
            .Returns(order);
        _countries.GetByCodeAsync("CZ", Arg.Any<CancellationToken>())
            .Returns(config);
        _invoices.GetByOrderIdAsync(OrderId, Arg.Any<CancellationToken>())
            .Returns((Invoice?)null);
    }

    [Fact]
    public async Task Happy_path_None_mode_returns_blob_path_and_invoice_number()
    {
        ArrangeDefaults(InvoicingMode.None);

        var result = await _sut.Handle(new IssueInvoice.Command(OrderId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.InvoiceId.Should().Be(InvoiceId);
        result.Value.InvoiceNumber.Should().Be(InvoiceNumber);
        result.Value.PdfBlobPath.Should().Be($"cz/orders/{OrderId}/{InvoiceNumber}.pdf");
    }

    [Fact]
    public async Task Happy_path_None_mode_invoice_carries_zero_VAT()
    {
        ArrangeDefaults(InvoicingMode.None);

        await _sut.Handle(new IssueInvoice.Command(OrderId), CancellationToken.None);

        await _invoices.Received(1).AddAsync(
            Arg.Is<Invoice>(i =>
                i.InvoicingMode == InvoicingMode.None
                && i.VatAmountMinor == 0
                && i.VatRateBp == 0
                && i.AmountWithoutVatMinor == 57_900
                && i.AmountWithVatMinor == 57_900
                && i.TaxableSupplyDate == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Happy_path_StandardVat_mode_computes_balanced_VAT_breakdown()
    {
        ArrangeDefaults(InvoicingMode.StandardVat);

        await _sut.Handle(new IssueInvoice.Command(OrderId), CancellationToken.None);

        // 57900 gross @ 21% → net = round(57900 * 10000 / 12100, half-up) = 47851; vat = 10049.
        await _invoices.Received(1).AddAsync(
            Arg.Is<Invoice>(i =>
                i.InvoicingMode == InvoicingMode.StandardVat
                && i.VatRateBp == 2100
                && i.AmountWithVatMinor == 57_900
                && i.AmountWithoutVatMinor + i.VatAmountMinor == i.AmountWithVatMinor
                && i.TaxableSupplyDate != null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReverseCharge_short_circuits_with_Permanent_InvoicingModeNotImplemented()
    {
        ArrangeDefaults(InvoicingMode.ReverseCharge);

        var result = await _sut.Handle(new IssueInvoice.Command(OrderId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.InvoicingModeNotImplemented);
        result.Error.Type.Should().Be(ErrorType.Permanent);
        await _invoices.DidNotReceive().AddAsync(Arg.Any<Invoice>(), Arg.Any<CancellationToken>());
        await _renderer.DidNotReceive().RenderAsync(
            Arg.Any<Invoice>(), Arg.Any<CountryConfiguration>(), Arg.Any<CancellationToken>());
        await _blob.DidNotReceive().UploadAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StrictFiscalReporting_short_circuits_with_Permanent_InvoicingModeNotImplemented()
    {
        ArrangeDefaults(InvoicingMode.StrictFiscalReporting);

        var result = await _sut.Handle(new IssueInvoice.Command(OrderId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.InvoicingModeNotImplemented);
        result.Error.Type.Should().Be(ErrorType.Permanent);
    }

    [Fact]
    public async Task Idempotent_re_issue_returns_existing_without_re_rendering()
    {
        // Existing invoice with a blob path already set: webhook re-delivery
        // returns the existing values verbatim, no renderer call, no
        // blob call, no number allocation.
        var existing = Invoice.Issue(
            id: "existing-id",
            invoiceNumber: "FV-CZ-20260001",
            type: InvoiceType.Customer,
            orderId: OrderId,
            orderNumber: "OBJ-20260819-0001",
            payoutBatchId: null,
            makerId: MakerId,
            issuerName: "JVM YORE s.r.o.",
            issuerIco: "00000000",
            issuerDic: null,
            issuerBankAccount: null,
            issuerAddress: null,
            recipientName: "Anna Nováková",
            recipientEmail: "anna@example.cz",
            recipientTaxId: null,
            recipientVatId: null,
            issueDate: new DateOnly(2026, 6, 7),
            taxableSupplyDate: null,
            dueDate: new DateOnly(2026, 6, 21),
            invoicingMode: InvoicingMode.None,
            amountWithoutVatMinor: 57_900,
            vatRateBp: 0,
            vatAmountMinor: 0,
            amountWithVatMinor: 57_900,
            currency: "CZK",
            countryCode: "CZ");
        existing.AttachPdfBlobPath($"cz/orders/{OrderId}/FV-CZ-20260001.pdf");

        var order = BuildPaidOrderInState();
        var config = BuildCzConfig();
        _orders.GetByIdUnscopedAsync(OrderId, Arg.Any<CancellationToken>())
            .Returns(order);
        _countries.GetByCodeAsync("CZ", Arg.Any<CancellationToken>())
            .Returns(config);
        _invoices.GetByOrderIdAsync(OrderId, Arg.Any<CancellationToken>())
            .Returns(existing);

        var result = await _sut.Handle(new IssueInvoice.Command(OrderId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.InvoiceId.Should().Be("existing-id");
        result.Value.InvoiceNumber.Should().Be("FV-CZ-20260001");
        await _invoiceNumbers.DidNotReceive().NextAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _renderer.DidNotReceive().RenderAsync(
            Arg.Any<Invoice>(), Arg.Any<CountryConfiguration>(), Arg.Any<CancellationToken>());
        await _blob.DidNotReceive().UploadAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Order_not_found_returns_OrderNotFound()
    {
        _orders.GetByIdUnscopedAsync(OrderId, Arg.Any<CancellationToken>())
            .Returns((Order?)null);

        var result = await _sut.Handle(new IssueInvoice.Command(OrderId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderNotFound);
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task CountryConfiguration_missing_returns_CountryConfigurationNotFound()
    {
        var order = BuildPaidOrderInState();
        _orders.GetByIdUnscopedAsync(OrderId, Arg.Any<CancellationToken>())
            .Returns(order);
        _countries.GetByCodeAsync("CZ", Arg.Any<CancellationToken>())
            .Returns((CountryConfiguration?)null);

        var result = await _sut.Handle(new IssueInvoice.Command(OrderId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.CountryConfigurationNotFound);
    }

    [Fact]
    public async Task Renderer_throws_returns_Permanent_InvoiceRenderFailed()
    {
        ArrangeDefaults(InvoicingMode.None);
        _renderer.RenderAsync(Arg.Any<Invoice>(), Arg.Any<CountryConfiguration>(), Arg.Any<CancellationToken>())
            .Returns<byte[]>(_ => throw new InvalidOperationException("font missing"));

        var result = await _sut.Handle(new IssueInvoice.Command(OrderId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.InvoiceRenderFailed);
        result.Error.Type.Should().Be(ErrorType.Permanent);
        await _blob.DidNotReceive().UploadAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Blob_upload_throws_returns_Transient_InvoiceBlobUploadFailed()
    {
        ArrangeDefaults(InvoicingMode.None);
        _blob.UploadAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<BusinessResult>(_ => throw new IOException("network blip"));

        var result = await _sut.Handle(new IssueInvoice.Command(OrderId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.InvoiceBlobUploadFailed);
        result.Error.Type.Should().Be(ErrorType.Transient);
    }

    [Fact]
    public async Task Blob_upload_returns_Permanent_error_translates_to_Permanent_InvoiceBlobUploadFailed()
    {
        ArrangeDefaults(InvoicingMode.None);
        _blob.UploadAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Failure(Error.Permanent(BusinessErrorMessage.BlobInvalidPath)));

        var result = await _sut.Handle(new IssueInvoice.Command(OrderId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.InvoiceBlobUploadFailed);
        result.Error.Type.Should().Be(ErrorType.Permanent);
    }

    [Fact]
    public async Task Happy_path_uploads_to_invoices_container_with_canonical_path()
    {
        ArrangeDefaults(InvoicingMode.None);

        await _sut.Handle(new IssueInvoice.Command(OrderId), CancellationToken.None);

        await _blob.Received(1).UploadAsync(
            container: BlobContainer.Invoices,
            path: $"cz/orders/{OrderId}/{InvoiceNumber}.pdf",
            content: Arg.Any<Stream>(),
            contentType: "application/pdf",
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Validator_rejects_blank_order_id()
    {
        var validator = new IssueInvoice.Validator();
        var result = validator.Validate(new IssueInvoice.Command(""));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorCode == BusinessErrorMessage.Required);
    }

    [Fact]
    public void Validator_accepts_valid_order_id()
    {
        // Reviewer M-1 fold: must-cover-tests.md §5 requires one positive
        // per Validator. The negative-only test above passed without proving
        // the Validator actually allows valid input — this pins the happy
        // path.
        var validator = new IssueInvoice.Validator();
        var result = validator.Validate(new IssueInvoice.Command("ord-1"));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validator_rejects_order_id_over_max_length()
    {
        // Reviewer M-1 fold: must-cover-tests.md §5 requires one negative
        // per RuleFor clause. The MaximumLength(40) rule at
        // IssueInvoice.cs:65-66 lacked coverage — Cascade(CascadeMode.Stop)
        // means a blank-AND-too-long input would only trip NotEmpty, so
        // the MaxLength wiring was untested.
        var validator = new IssueInvoice.Validator();
        var longId = new string('x', 41);

        var result = validator.Validate(new IssueInvoice.Command(longId));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorCode == BusinessErrorMessage.MaxLength);
    }

    // === Settlement + issuer snapshot ===

    [Fact]
    public async Task Invoice_snapshots_the_orders_settlement_so_the_PDF_reads_as_a_receipt()
    {
        // The handler only ever runs off the outbox row MarkOrderPaid
        // enqueues, so the order IS paid. Recording that is what stops the
        // PDF printing "Celkem k úhradě" at a customer who already paid.
        ArrangeDefaults(InvoicingMode.None);

        await _sut.Handle(new IssueInvoice.Command(OrderId), CancellationToken.None);

        await _invoices.Received(1).AddAsync(
            Arg.Is<Invoice>(i =>
                i.PaidOn == new DateOnly(2026, 6, 7)
                && i.PaymentMethod == "CARD_CZ"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Settlement_date_is_the_country_local_date_of_PaidAt_not_the_UTC_one()
    {
        // 2026-06-07T23:30Z is already 2026-06-08 in Prague (CEST, UTC+2).
        // A receipt dated a day before the customer's bank statement is a
        // support ticket.
        var order = BuildPaidOrder();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        order.MarkAsPaid(clock, "comgate-tx", "CARD_CZ",
            paidAtOverride: DateTimeOffset.Parse("2026-06-07T23:30:00Z"));

        _orders.GetByIdUnscopedAsync(OrderId, Arg.Any<CancellationToken>()).Returns(order);
        _countries.GetByCodeAsync("CZ", Arg.Any<CancellationToken>())
            .Returns(BuildCzConfig(InvoicingMode.None));
        _invoices.GetByOrderIdAsync(OrderId, Arg.Any<CancellationToken>()).Returns((Invoice?)null);

        await _sut.Handle(new IssueInvoice.Command(OrderId), CancellationToken.None);

        await _invoices.Received(1).AddAsync(
            Arg.Is<Invoice>(i => i.PaidOn == new DateOnly(2026, 6, 8)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoice_snapshots_the_issuer_address_from_the_country_configuration()
    {
        // Snapshot, not lookup: if JVM Yore relocates, historical documents
        // must keep the seat they were issued from.
        ArrangeDefaults(InvoicingMode.None);

        await _sut.Handle(new IssueInvoice.Command(OrderId), CancellationToken.None);

        await _invoices.Received(1).AddAsync(
            Arg.Is<Invoice>(i =>
                i.IssuerAddress == "Příčná 1892/4, Nové Město, 110 00 Praha 1"
                && i.IssuerIco == "29633443"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoice_snapshots_the_order_number_so_the_line_item_names_the_customers_order()
    {
        // Without this the templates fall back to the invoice number's own
        // numeric tail, and the document points at an order the customer
        // cannot find anywhere in their account.
        ArrangeDefaults(InvoicingMode.None);

        await _sut.Handle(new IssueInvoice.Command(OrderId), CancellationToken.None);

        await _invoices.Received(1).AddAsync(
            Arg.Is<Invoice>(i => i.OrderNumber == "M-CZ-20260042"),
            Arg.Any<CancellationToken>());
    }
}
