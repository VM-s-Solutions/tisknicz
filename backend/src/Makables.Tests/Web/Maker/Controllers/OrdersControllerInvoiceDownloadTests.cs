using FluentAssertions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Invoices;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Storage;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using MakerEntity = Makables.Core.Domain.Makers.Maker;

namespace Makables.Tests.Web.Maker.Controllers;

/// <summary>
/// T-0088 maker-host invoice download — mirrors the customer-host suite
/// with the T-0075 maker-resolution prefix pinned: a maker-audience user
/// without a maker row 404s BEFORE the order lookup (AC-6), cross-maker
/// probes 404 with order.notFound before the invoice lookup (AC-3), and
/// the blob-purged race surfaces invoice.notYetRendered (AC-4).
/// </summary>
public class OrdersControllerInvoiceDownloadTests
{
    private const string OrderId = "ord-1";
    private const string MakerUserId = "user-maker-1";
    private const string MakerId = "maker-1";
    private const string InvoiceNumber = "FV-CZ-20260001";
    private const string PdfBlobPath = "cz/orders/ord-1/FV-CZ-20260001.pdf";

    private static readonly byte[] PdfBytes = "%PDF-1.7 unit-test-bytes"u8.ToArray();

    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IInvoiceRepository _invoices = Substitute.For<IInvoiceRepository>();
    private readonly IMakerRepository _makers = Substitute.For<IMakerRepository>();
    private readonly IBlobStorageClient _blobs = Substitute.For<IBlobStorageClient>();
    private readonly IUserSessionProvider _session = Substitute.For<IUserSessionProvider>();
    private readonly IMediator _mediator = Substitute.For<IMediator>();

    private Makables.Web.Maker.Controllers.OrdersController BuildController()
    {
        return new Makables.Web.Maker.Controllers.OrdersController(
            _orders, _invoices, _makers, _blobs, _session, _mediator)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };
    }

    private static MakerEntity BuildMaker() => MakerEntity.Create(
        id: MakerId, userId: MakerUserId,
        registrationNumber: "27074358", vatId: null,
        companyName: "Avast Software s.r.o.", legalForm: null,
        registeredAddressId: "addr-1", incorporatedOn: null,
        isActiveInRegistry: true, sourceRegistry: "ares",
        snapshotFetchedAt: new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero),
        snapshotIsStale: false, countryCode: "CZ", slug: "avast");

    private static Order BuildAssignedOrder() => Order.Create(
        id: OrderId, orderNumber: "M-CZ-20260042",
        customerUserId: "user-customer-1", makerId: MakerId, productId: "prod-1",
        contactName: "Anna", contactEmail: "a@b.cz", contactPhone: "+420 723 456 789",
        productPriceAmountMinor: 50000, shippingPriceAmountMinor: 7900,
        platformFeeAmountMinor: 7500, makerPayoutAmountMinor: 50400,
        totalAmountMinor: 57900, currency: "CZK", vatRateBp: 2100,
        shippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
        zasilkovnaPickupPointId: "pp-42", countryCode: "CZ");

    private static Invoice BuildInvoiceWithBlobPath()
    {
        var invoice = Invoice.Issue(
            id: "inv-1", invoiceNumber: InvoiceNumber,
            type: InvoiceType.Customer, orderId: OrderId, orderNumber: "OBJ-20260819-0001", payoutBatchId: null,
            makerId: MakerId,
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
        invoice.AttachPdfBlobPath(PdfBlobPath);
        return invoice;
    }

    [Fact]
    public async Task DownloadInvoice_UserWithoutMakerRow_Returns404_OrderNotFound()
    {
        _session.GetUserId().Returns(MakerUserId);
        _makers.GetByUserIdAsync(MakerUserId, Arg.Any<CancellationToken>())
            .Returns((MakerEntity?)null);
        var controller = BuildController();

        var result = await controller.DownloadInvoice(OrderId, CancellationToken.None);

        result.Should().BeOfType<NotFoundObjectResult>()
            .Which.Value.Should().BeOfType<Error>()
            .Which.Code.Should().Be(BusinessErrorMessage.OrderNotFound);
        // AC-6: the order lookup is never performed without a maker row.
        await _orders.Received(0).GetByIdForMakerReadOnlyAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _invoices.Received(0).GetByOrderIdReadOnlyAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DownloadInvoice_OrderNotOwnedByMaker_Returns404_OrderNotFound()
    {
        _session.GetUserId().Returns(MakerUserId);
        _makers.GetByUserIdAsync(MakerUserId, Arg.Any<CancellationToken>())
            .Returns(BuildMaker());
        _orders.GetByIdForMakerReadOnlyAsync(OrderId, MakerId, Arg.Any<CancellationToken>())
            .Returns((Order?)null);
        var controller = BuildController();

        var result = await controller.DownloadInvoice(OrderId, CancellationToken.None);

        result.Should().BeOfType<NotFoundObjectResult>()
            .Which.Value.Should().BeOfType<Error>()
            .Which.Code.Should().Be(BusinessErrorMessage.OrderNotFound);
        // AC-3 IDOR-shield wiring: the unscoped invoice lookup and the
        // blob client must NEVER run before ownership is established.
        await _invoices.Received(0).GetByOrderIdReadOnlyAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _blobs.Received(0).DownloadAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DownloadInvoice_BlobDownloadFails_Returns404_NotYetRendered()
    {
        _session.GetUserId().Returns(MakerUserId);
        _makers.GetByUserIdAsync(MakerUserId, Arg.Any<CancellationToken>())
            .Returns(BuildMaker());
        _orders.GetByIdForMakerReadOnlyAsync(OrderId, MakerId, Arg.Any<CancellationToken>())
            .Returns(BuildAssignedOrder());
        _invoices.GetByOrderIdReadOnlyAsync(OrderId, Arg.Any<CancellationToken>())
            .Returns(BuildInvoiceWithBlobPath());
        // Blob-purged-but-row-remains race: the row points at a path the
        // store no longer holds.
        _blobs.DownloadAsync(BlobContainer.Invoices, PdfBlobPath, Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Failure<BlobDownload>(
                Error.NotFound("blob", BusinessErrorMessage.BlobNotFound)));
        var controller = BuildController();

        var result = await controller.DownloadInvoice(OrderId, CancellationToken.None);

        result.Should().BeOfType<NotFoundObjectResult>()
            .Which.Value.Should().BeOfType<Error>()
            .Which.Code.Should().Be(BusinessErrorMessage.InvoiceNotYetRendered);
        // AC-4: no Cache-Control header on any 404.
        controller.Response.Headers.CacheControl.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task DownloadInvoice_HappyPath_StreamsPdfWithHeaders()
    {
        _session.GetUserId().Returns(MakerUserId);
        _makers.GetByUserIdAsync(MakerUserId, Arg.Any<CancellationToken>())
            .Returns(BuildMaker());
        _orders.GetByIdForMakerReadOnlyAsync(OrderId, MakerId, Arg.Any<CancellationToken>())
            .Returns(BuildAssignedOrder());
        _invoices.GetByOrderIdReadOnlyAsync(OrderId, Arg.Any<CancellationToken>())
            .Returns(BuildInvoiceWithBlobPath());
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
