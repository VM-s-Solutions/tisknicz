using FluentAssertions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Invoices;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Shipping;
using Makables.Core.Domain.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using MakerEntity = Makables.Core.Domain.Makers.Maker;

namespace Makables.Tests.Web.Maker.Controllers;

/// <summary>
/// T-0112a maker fee-invoice download — controller-direct stream per T-0088.
/// Pins: no-session 401; no-maker-row 404 order.notFound (invoice repo never
/// touched); ownership happy path with private/no-store + ETag + disposition;
/// cross-maker id 404 (IDOR shield); Customer-invoice-via-this-route 404 (Fee
/// gate fires before the blob read); null PdfBlobPath / blob-miss
/// invoice.notYetRendered.
/// </summary>
public class FilesControllerInvoiceDownloadTests
{
    private const string MakerUserId = "user-maker-1";
    private const string MakerId = "maker-1";
    private const string InvoiceId = "inv-1";
    private const string InvoiceNumber = "FV-CZ-20260042";
    private const string PdfBlobPath = "cz/payouts/pb-1/FV-CZ-20260042.pdf";

    private static readonly byte[] PdfBytes = "%PDF-1.7 fee-invoice-bytes"u8.ToArray();

    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IInvoiceRepository _invoices = Substitute.For<IInvoiceRepository>();
    private readonly IMakerRepository _makers = Substitute.For<IMakerRepository>();
    private readonly IBlobStorageClient _blobs = Substitute.For<IBlobStorageClient>();
    private readonly IShippingCarrierFactory _carriers = Substitute.For<IShippingCarrierFactory>();
    private readonly IUserSessionProvider _session = Substitute.For<IUserSessionProvider>();

    private Makables.Web.Maker.Controllers.FilesController BuildController() =>
        new(_orders, _invoices, _makers, _blobs, _carriers, _session,
            NullLogger<Makables.Web.Maker.Controllers.FilesController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    private static MakerEntity BuildMaker() => MakerEntity.Create(
        id: MakerId, userId: MakerUserId, registrationNumber: "27074358", vatId: null,
        companyName: "Avast s.r.o.", legalForm: null, registeredAddressId: "addr-1",
        incorporatedOn: null, isActiveInRegistry: true, sourceRegistry: "ares",
        snapshotFetchedAt: new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero),
        snapshotIsStale: false, countryCode: "CZ", slug: "avast");

    private static Invoice BuildFeeInvoice(bool withBlobPath)
    {
        var invoice = Invoice.Issue(
            id: InvoiceId, invoiceNumber: InvoiceNumber, type: InvoiceType.Fee,
            orderId: null, payoutBatchId: "pb-1", makerId: MakerId,
            orderNumber: "OBJ-20260819-0001",
            issuerName: "JVM YORE s.r.o.", issuerIco: "12345678", issuerDic: null,
            issuerBankAccount: null, issuerAddress: null, recipientName: "Avast s.r.o.",
            recipientEmail: "m@b.cz", recipientTaxId: "27074358", recipientVatId: null,
            issueDate: new DateOnly(2026, 6, 11), taxableSupplyDate: null,
            dueDate: new DateOnly(2026, 6, 25), invoicingMode: InvoicingMode.None,
            amountWithoutVatMinor: 6000, vatRateBp: 0, vatAmountMinor: 0,
            amountWithVatMinor: 6000, currency: "CZK", countryCode: "CZ");
        if (withBlobPath) invoice.AttachPdfBlobPath(PdfBlobPath);
        return invoice;
    }

    private static Invoice BuildCustomerInvoiceOwnedByMaker()
    {
        var invoice = Invoice.Issue(
            id: InvoiceId, invoiceNumber: "FV-CZ-20260001", type: InvoiceType.Customer,
            orderId: "ord-9", payoutBatchId: null, makerId: MakerId,
            orderNumber: "OBJ-20260819-0001",
            issuerName: "JVM YORE s.r.o.", issuerIco: "12345678", issuerDic: null,
            issuerBankAccount: null, issuerAddress: null, recipientName: "Anna", recipientEmail: "a@b.cz",
            recipientTaxId: null, recipientVatId: null,
            issueDate: new DateOnly(2026, 5, 6), taxableSupplyDate: new DateOnly(2026, 5, 5),
            dueDate: new DateOnly(2026, 5, 20), invoicingMode: InvoicingMode.None,
            amountWithoutVatMinor: 50000, vatRateBp: 0, vatAmountMinor: 0,
            amountWithVatMinor: 50000, currency: "CZK", countryCode: "CZ");
        invoice.AttachPdfBlobPath(PdfBlobPath);
        return invoice;
    }

    [Fact]
    public async Task DownloadFeeInvoice_NoSession_Returns401()
    {
        _session.GetUserId().Returns((string?)null);
        var controller = BuildController();

        var result = await controller.DownloadFeeInvoice(InvoiceId, CancellationToken.None);

        result.Should().BeOfType<UnauthorizedObjectResult>();
        await _makers.Received(0).GetByUserIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _invoices.Received(0).GetForMakerReadOnlyAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _blobs.Received(0).DownloadAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DownloadFeeInvoice_UserWithoutMakerRow_Returns404_OrderNotFound()
    {
        _session.GetUserId().Returns(MakerUserId);
        _makers.GetByUserIdAsync(MakerUserId, Arg.Any<CancellationToken>()).Returns((MakerEntity?)null);
        var controller = BuildController();

        var result = await controller.DownloadFeeInvoice(InvoiceId, CancellationToken.None);

        result.Should().BeOfType<NotFoundObjectResult>()
            .Which.Value.Should().BeOfType<Error>()
            .Which.Code.Should().Be(BusinessErrorMessage.OrderNotFound);
        await _invoices.Received(0).GetForMakerReadOnlyAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _blobs.Received(0).DownloadAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DownloadFeeInvoice_MakerOwnedFee_HappyPath_StreamsPdfWithHeaders()
    {
        _session.GetUserId().Returns(MakerUserId);
        _makers.GetByUserIdAsync(MakerUserId, Arg.Any<CancellationToken>()).Returns(BuildMaker());
        _invoices.GetForMakerReadOnlyAsync(InvoiceId, MakerId, Arg.Any<CancellationToken>())
            .Returns(BuildFeeInvoice(withBlobPath: true));
        _blobs.DownloadAsync(BlobContainer.Invoices, PdfBlobPath, Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(new BlobDownload(
                Content: new MemoryStream(PdfBytes), ContentType: "application/pdf",
                ContentLength: PdfBytes.LongLength, ETag: "\"etag-fee\"")));
        var controller = BuildController();

        var result = await controller.DownloadFeeInvoice(InvoiceId, CancellationToken.None);

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
        headers.ETag.ToString().Should().Be("\"etag-fee\"");
    }

    [Fact]
    public async Task DownloadFeeInvoice_CrossTenantInvoiceId_Returns404_OrderNotFound()
    {
        _session.GetUserId().Returns(MakerUserId);
        _makers.GetByUserIdAsync(MakerUserId, Arg.Any<CancellationToken>()).Returns(BuildMaker());
        // The id belongs to another maker OR is nonexistent — same null shape.
        _invoices.GetForMakerReadOnlyAsync(InvoiceId, MakerId, Arg.Any<CancellationToken>())
            .Returns((Invoice?)null);
        var controller = BuildController();

        var result = await controller.DownloadFeeInvoice(InvoiceId, CancellationToken.None);

        result.Should().BeOfType<NotFoundObjectResult>()
            .Which.Value.Should().BeOfType<Error>()
            .Which.Code.Should().Be(BusinessErrorMessage.OrderNotFound);
        await _blobs.Received(0).DownloadAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DownloadFeeInvoice_CustomerInvoiceViaThisRoute_Returns404_OrderNotFound()
    {
        _session.GetUserId().Returns(MakerUserId);
        _makers.GetByUserIdAsync(MakerUserId, Arg.Any<CancellationToken>()).Returns(BuildMaker());
        // A maker-owned Customer invoice via the Fee route → 404, Fee gate fires
        // BEFORE the blob read.
        _invoices.GetForMakerReadOnlyAsync(InvoiceId, MakerId, Arg.Any<CancellationToken>())
            .Returns(BuildCustomerInvoiceOwnedByMaker());
        var controller = BuildController();

        var result = await controller.DownloadFeeInvoice(InvoiceId, CancellationToken.None);

        result.Should().BeOfType<NotFoundObjectResult>()
            .Which.Value.Should().BeOfType<Error>()
            .Which.Code.Should().Be(BusinessErrorMessage.OrderNotFound);
        await _blobs.Received(0).DownloadAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DownloadFeeInvoice_NullBlobPath_Returns404_NotYetRendered()
    {
        _session.GetUserId().Returns(MakerUserId);
        _makers.GetByUserIdAsync(MakerUserId, Arg.Any<CancellationToken>()).Returns(BuildMaker());
        _invoices.GetForMakerReadOnlyAsync(InvoiceId, MakerId, Arg.Any<CancellationToken>())
            .Returns(BuildFeeInvoice(withBlobPath: false));
        var controller = BuildController();

        var result = await controller.DownloadFeeInvoice(InvoiceId, CancellationToken.None);

        result.Should().BeOfType<NotFoundObjectResult>()
            .Which.Value.Should().BeOfType<Error>()
            .Which.Code.Should().Be(BusinessErrorMessage.InvoiceNotYetRendered);
        await _blobs.Received(0).DownloadAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DownloadFeeInvoice_BlobMiss_Returns404_NotYetRendered()
    {
        _session.GetUserId().Returns(MakerUserId);
        _makers.GetByUserIdAsync(MakerUserId, Arg.Any<CancellationToken>()).Returns(BuildMaker());
        _invoices.GetForMakerReadOnlyAsync(InvoiceId, MakerId, Arg.Any<CancellationToken>())
            .Returns(BuildFeeInvoice(withBlobPath: true));
        _blobs.DownloadAsync(BlobContainer.Invoices, PdfBlobPath, Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Failure<BlobDownload>(
                Error.NotFound("blob", BusinessErrorMessage.BlobNotFound)));
        var controller = BuildController();

        var result = await controller.DownloadFeeInvoice(InvoiceId, CancellationToken.None);

        result.Should().BeOfType<NotFoundObjectResult>()
            .Which.Value.Should().BeOfType<Error>()
            .Which.Code.Should().Be(BusinessErrorMessage.InvoiceNotYetRendered);
        controller.Response.Headers.CacheControl.ToString().Should().BeEmpty();
    }
}
