using FluentAssertions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Invoices;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Makables.Tests.Web.Customer.Controllers;

/// <summary>
/// T-0088 customer-host invoice download — controller-direct action, so
/// the tests construct <see cref="Makables.Web.Customer.Controllers.OrdersController"/>
/// directly with NSubstitute mocks + a <see cref="DefaultHttpContext"/>
/// (same harness shape as the Web.Public filter tests). Pins the §C
/// lookup-chain ordering (<c>Received(0)</c> IDOR-shield wiring per
/// AC-3), the 404 code split (order.notFound vs invoice.notYetRendered),
/// and the streaming happy path's header policy.
/// </summary>
public class OrdersControllerInvoiceDownloadTests
{
    private const string OrderId = "ord-1";
    private const string CustomerUserId = "user-customer-1";
    private const string InvoiceNumber = "FV-CZ-20260001";
    private const string PdfBlobPath = "cz/orders/ord-1/FV-CZ-20260001.pdf";

    private static readonly byte[] PdfBytes = "%PDF-1.7 unit-test-bytes"u8.ToArray();

    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IInvoiceRepository _invoices = Substitute.For<IInvoiceRepository>();
    private readonly IBlobStorageClient _blobs = Substitute.For<IBlobStorageClient>();
    private readonly IUserSessionProvider _session = Substitute.For<IUserSessionProvider>();
    private readonly IIdGenerator _ids = Substitute.For<IIdGenerator>();

    private Makables.Web.Customer.Controllers.OrdersController BuildController()
    {
        return new Makables.Web.Customer.Controllers.OrdersController(
            _orders, _invoices, _blobs, _session, _ids)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };
    }

    private static Order BuildOwnedOrder() => Order.Create(
        id: OrderId, orderNumber: "M-CZ-20260042",
        customerUserId: CustomerUserId, makerId: "maker-1", productId: "prod-1",
        contactName: "Anna", contactEmail: "a@b.cz", contactPhone: "+420 723 456 789",
        productPriceAmountMinor: 50000, shippingPriceAmountMinor: 7900,
        platformFeeAmountMinor: 7500, makerPayoutAmountMinor: 50400,
        totalAmountMinor: 57900, currency: "CZK", vatRateBp: 2100,
        shippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
        zasilkovnaPickupPointId: "pp-42", countryCode: "CZ");

    private static Invoice BuildInvoice(bool withBlobPath)
    {
        var invoice = Invoice.Issue(
            id: "inv-1", invoiceNumber: InvoiceNumber,
            type: InvoiceType.Customer, orderId: OrderId, orderNumber: "OBJ-20260819-0001", payoutBatchId: null,
            makerId: "maker-1",
            issuerName: "JVM YORE s.r.o.", issuerIco: "12345678",
            issuerDic: null, issuerBankAccount: null, issuerAddress: null,
            recipientName: "Anna", recipientEmail: "a@b.cz",
            recipientTaxId: null, recipientVatId: null,
            issueDate: new DateOnly(2026, 5, 6),
            taxableSupplyDate: new DateOnly(2026, 5, 5),
            dueDate: new DateOnly(2026, 5, 20),
            invoicingMode: InvoicingMode.None,
            amountWithoutVatMinor: 57900, vatRateBp: 0,
            vatAmountMinor: 0, amountWithVatMinor: 57900,
            currency: "CZK", countryCode: "CZ");
        if (withBlobPath)
        {
            invoice.AttachPdfBlobPath(PdfBlobPath);
        }
        return invoice;
    }

    [Fact]
    public async Task DownloadInvoice_NoSession_Returns401()
    {
        _session.GetUserId().Returns((string?)null);
        var controller = BuildController();

        var result = await controller.DownloadInvoice(OrderId, CancellationToken.None);

        var unauthorized = result.Should().BeOfType<UnauthorizedObjectResult>().Subject;
        unauthorized.Value.Should().BeOfType<Error>()
            .Which.Code.Should().Be("auth.required");
        await _orders.Received(0).GetByIdForCustomerReadOnlyAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _invoices.Received(0).GetByOrderIdReadOnlyAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DownloadInvoice_OrderNotOwnedOrMissing_Returns404_OrderNotFound()
    {
        _session.GetUserId().Returns(CustomerUserId);
        _orders.GetByIdForCustomerReadOnlyAsync(OrderId, CustomerUserId, Arg.Any<CancellationToken>())
            .Returns((Order?)null);
        var controller = BuildController();

        var result = await controller.DownloadInvoice(OrderId, CancellationToken.None);

        var notFound = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        notFound.Value.Should().BeOfType<Error>()
            .Which.Code.Should().Be(BusinessErrorMessage.OrderNotFound);
        // AC-3 IDOR-shield wiring: the unscoped invoice lookup and the
        // blob client must NEVER run before ownership is established.
        await _invoices.Received(0).GetByOrderIdReadOnlyAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _blobs.Received(0).DownloadAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DownloadInvoice_NoInvoiceOrNullBlobPath_Returns404_NotYetRendered()
    {
        _session.GetUserId().Returns(CustomerUserId);
        _orders.GetByIdForCustomerReadOnlyAsync(OrderId, CustomerUserId, Arg.Any<CancellationToken>())
            .Returns(BuildOwnedOrder());

        // Variant 1: no Invoice row at all.
        _invoices.GetByOrderIdReadOnlyAsync(OrderId, Arg.Any<CancellationToken>())
            .Returns((Invoice?)null);
        var result = await BuildController().DownloadInvoice(OrderId, CancellationToken.None);
        result.Should().BeOfType<NotFoundObjectResult>()
            .Which.Value.Should().BeOfType<Error>()
            .Which.Code.Should().Be(BusinessErrorMessage.InvoiceNotYetRendered);

        // Variant 2: Invoice row exists but PdfBlobPath is still null
        // (T-0068b render pipeline mid-flight).
        _invoices.GetByOrderIdReadOnlyAsync(OrderId, Arg.Any<CancellationToken>())
            .Returns(BuildInvoice(withBlobPath: false));
        result = await BuildController().DownloadInvoice(OrderId, CancellationToken.None);
        result.Should().BeOfType<NotFoundObjectResult>()
            .Which.Value.Should().BeOfType<Error>()
            .Which.Code.Should().Be(BusinessErrorMessage.InvoiceNotYetRendered);

        await _blobs.Received(0).DownloadAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DownloadInvoice_HappyPath_StreamsPdfWithHeaders()
    {
        _session.GetUserId().Returns(CustomerUserId);
        _orders.GetByIdForCustomerReadOnlyAsync(OrderId, CustomerUserId, Arg.Any<CancellationToken>())
            .Returns(BuildOwnedOrder());
        _invoices.GetByOrderIdReadOnlyAsync(OrderId, Arg.Any<CancellationToken>())
            .Returns(BuildInvoice(withBlobPath: true));
        _blobs.DownloadAsync(BlobContainer.Invoices, PdfBlobPath, Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(new BlobDownload(
                Content: new MemoryStream(PdfBytes),
                ContentType: "application/pdf",
                ContentLength: PdfBytes.LongLength,
                ETag: "\"etag-1\"")));
        var controller = BuildController();

        var result = await controller.DownloadInvoice(OrderId, CancellationToken.None);

        var file = result.Should().BeOfType<FileStreamResult>().Subject;
        file.ContentType.Should().Be("application/pdf");
        file.EnableRangeProcessing.Should().BeFalse();

        using var body = new MemoryStream();
        await file.FileStream.CopyToAsync(body);
        body.ToArray().Should().Equal(PdfBytes);

        var headers = controller.Response.Headers;
        headers.CacheControl.ToString().Should().Be("private, no-store");
        headers.ContentDisposition.ToString().Should()
            .Be($"attachment; filename=\"faktura-{InvoiceNumber}.pdf\"");
        headers.ETag.ToString().Should().Be("\"etag-1\"");
    }
}
