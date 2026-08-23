using FluentAssertions;
using Makables.Core.Domain.Auditing;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Invoices;
using Makables.Core.Domain.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Makables.Tests.Web.Admin.Controllers;

/// <summary>
/// T-0126 / Q-0026 admin invoice-PDF download — controller-direct stream per
/// the T-0088 precedent, with an Unscoped-by-id lookup (admin sees any invoice).
/// Pins: happy-path byte-equal stream + private/no-store + ETag + faktura-{n}.pdf
/// disposition; 404 unknown-invoice; 404 null-PdfBlobPath; 404 blob-miss;
/// ETag/304 conditional GET. The admin-only audience is enforced by the host
/// <c>[Authorize]</c> + the Unscoped repository (covered in the integration
/// cross-host probe).
/// </summary>
public class AdminInvoiceDownloadTests
{
    private const string InvoiceId = "inv-1";
    private const string InvoiceNumber = "FV-CZ-20260042";
    private const string PdfBlobPath = "cz/orders/ord-1/FV-CZ-20260042.pdf";

    private static readonly byte[] PdfBytes = "%PDF-1.7 admin-invoice-bytes"u8.ToArray();

    private readonly IInvoiceRepository _invoices = Substitute.For<IInvoiceRepository>();
    private readonly IBlobStorageClient _blobs = Substitute.For<IBlobStorageClient>();
    private readonly IAdminReadAuditWriter _readAudit = Substitute.For<IAdminReadAuditWriter>();

    private Makables.Web.Admin.Controllers.AdminInvoicesController BuildController() =>
        new(_invoices, _blobs, _readAudit)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    private static Invoice BuildInvoice(bool withBlobPath, InvoiceType type = InvoiceType.Customer)
    {
        var invoice = Invoice.Issue(
            id: InvoiceId, invoiceNumber: InvoiceNumber, type: type,
            orderId: type == InvoiceType.Customer ? "ord-1" : null,
            orderNumber: "OBJ-20260819-0001",
            payoutBatchId: type == InvoiceType.Fee ? "pb-1" : null,
            makerId: "maker-1",
            issuerName: "JVM YORE s.r.o.", issuerIco: "12345678", issuerDic: null,
            issuerBankAccount: null, issuerAddress: null, recipientName: "Anna", recipientEmail: "a@b.cz",
            recipientTaxId: null, recipientVatId: null,
            issueDate: new DateOnly(2026, 5, 6), taxableSupplyDate: new DateOnly(2026, 5, 5),
            dueDate: new DateOnly(2026, 5, 20), invoicingMode: InvoicingMode.None,
            amountWithoutVatMinor: 50000, vatRateBp: 0, vatAmountMinor: 0,
            amountWithVatMinor: 50000, currency: "CZK", countryCode: "CZ");
        if (withBlobPath) invoice.AttachPdfBlobPath(PdfBlobPath);
        return invoice;
    }

    [Fact]
    public async Task DownloadInvoice_HappyPath_StreamsPdfWithHeaders()
    {
        _invoices.GetByIdUnscopedReadOnlyAsync(InvoiceId, Arg.Any<CancellationToken>())
            .Returns(BuildInvoice(withBlobPath: true));
        _blobs.DownloadAsync(BlobContainer.Invoices, PdfBlobPath, Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(new BlobDownload(
                Content: new MemoryStream(PdfBytes), ContentType: "application/pdf",
                ContentLength: PdfBytes.LongLength, ETag: "\"etag-admin\"")));
        var controller = BuildController();

        var result = await controller.DownloadInvoice(InvoiceId, CancellationToken.None);

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
        headers.ETag.ToString().Should().Be("\"etag-admin\"");

        // T-0137: the 200-stream path audits the privileged PII read exactly once.
        await _readAudit.Received(1).AuditReadAsync(
            "invoice.pdf.download", "invoice", InvoiceId,
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DownloadInvoice_UnknownInvoice_Returns404_NotYetRendered()
    {
        _invoices.GetByIdUnscopedReadOnlyAsync(InvoiceId, Arg.Any<CancellationToken>())
            .Returns((Invoice?)null);
        var controller = BuildController();

        var result = await controller.DownloadInvoice(InvoiceId, CancellationToken.None);

        result.Should().BeOfType<NotFoundObjectResult>()
            .Which.Value.Should().BeOfType<Error>()
            .Which.Code.Should().Be(BusinessErrorMessage.InvoiceNotYetRendered);
        await _blobs.Received(0).DownloadAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        // T-0137: a 404 is not a disclosure — no audit row.
        await _readAudit.Received(0).AuditReadAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DownloadInvoice_NullBlobPath_Returns404_NotYetRendered()
    {
        _invoices.GetByIdUnscopedReadOnlyAsync(InvoiceId, Arg.Any<CancellationToken>())
            .Returns(BuildInvoice(withBlobPath: false));
        var controller = BuildController();

        var result = await controller.DownloadInvoice(InvoiceId, CancellationToken.None);

        result.Should().BeOfType<NotFoundObjectResult>()
            .Which.Value.Should().BeOfType<Error>()
            .Which.Code.Should().Be(BusinessErrorMessage.InvoiceNotYetRendered);
        await _blobs.Received(0).DownloadAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DownloadInvoice_BlobMiss_Returns404_NotYetRendered_NoCacheControl()
    {
        _invoices.GetByIdUnscopedReadOnlyAsync(InvoiceId, Arg.Any<CancellationToken>())
            .Returns(BuildInvoice(withBlobPath: true));
        _blobs.DownloadAsync(BlobContainer.Invoices, PdfBlobPath, Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Failure<BlobDownload>(
                Error.NotFound("blob", BusinessErrorMessage.BlobNotFound)));
        var controller = BuildController();

        var result = await controller.DownloadInvoice(InvoiceId, CancellationToken.None);

        result.Should().BeOfType<NotFoundObjectResult>()
            .Which.Value.Should().BeOfType<Error>()
            .Which.Code.Should().Be(BusinessErrorMessage.InvoiceNotYetRendered);
        controller.Response.Headers.CacheControl.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task DownloadInvoice_MatchingIfNoneMatch_Returns304()
    {
        _invoices.GetByIdUnscopedReadOnlyAsync(InvoiceId, Arg.Any<CancellationToken>())
            .Returns(BuildInvoice(withBlobPath: true));
        _blobs.DownloadAsync(BlobContainer.Invoices, PdfBlobPath, Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(new BlobDownload(
                Content: new MemoryStream(PdfBytes), ContentType: "application/pdf",
                ContentLength: PdfBytes.LongLength, ETag: "\"etag-admin\"")));
        var controller = BuildController();
        controller.Request.Headers.IfNoneMatch = "\"etag-admin\"";

        var result = await controller.DownloadInvoice(InvoiceId, CancellationToken.None);

        result.Should().BeOfType<StatusCodeResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status304NotModified);
        // T-0137: a 304 means the admin already holds the bytes — no NEW
        // disclosure, so no audit row on the conditional-GET hit.
        await _readAudit.Received(0).AuditReadAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }
}
