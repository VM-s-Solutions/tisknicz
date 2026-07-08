using FluentAssertions;
using Makables.Core.AppServices.Features.Shipping;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Shipping;
using Makables.Core.Domain.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Shipping;

/// <summary>
/// T-0146 <see cref="FetchAndStoreReturnLabel.Handler"/> — verbatim reuse
/// of the T-0074 <see cref="FetchAndStoreShippingLabel"/> cache→carrier→cache
/// shape, pointed at the dispute-scoped blob path (AC-3: cache-hit/miss
/// download paths).
/// </summary>
public class FetchAndStoreReturnLabelHandlerTests
{
    private const string DisputeId = "disp-1";
    private const string CarrierRef = "555444333";
    private static readonly string ExpectedBlobPath = $"cz/disputes/{DisputeId}/return-label.pdf";

    private readonly IDisputeRepository _disputes = Substitute.For<IDisputeRepository>();
    private readonly IShippingCarrierFactory _carrierFactory = Substitute.For<IShippingCarrierFactory>();
    private readonly IShippingCarrier _carrier = Substitute.For<IShippingCarrier>();
    private readonly IBlobStorageClient _blobStorage = Substitute.For<IBlobStorageClient>();
    private readonly FetchAndStoreReturnLabel.Handler _sut;

    public FetchAndStoreReturnLabelHandlerTests()
    {
        _carrierFactory.ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(_carrier));
        _sut = new FetchAndStoreReturnLabel.Handler(
            _disputes, _carrierFactory, _blobStorage,
            NullLogger<FetchAndStoreReturnLabel.Handler>.Instance);
    }

    private static Dispute BuildDisputeWithReturnShipment(string carrierRef = CarrierRef)
    {
        var dispute = Dispute.Open(
            id: DisputeId, orderId: "ord-1", category: DisputeCategory.DamagedItem,
            description: "Cracked on arrival.", source: DisputeSource.Customer, countryCode: "CZ");
        dispute.SetReturnShipment(carrierRef, $"https://tracking.packeta.com/Z{carrierRef}");
        return dispute;
    }

    [Fact]
    public async Task Happy_path_fetches_carrier_label_and_uploads_to_blob()
    {
        var dispute = BuildDisputeWithReturnShipment();
        _disputes.GetByIdUnscopedReadOnlyAsync(DisputeId, Arg.Any<CancellationToken>()).Returns(dispute);
        _blobStorage.ExistsAsync(BlobContainer.Invoices, ExpectedBlobPath, Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(false));
        _carrier.GetLabelPdfAsync(CarrierRef, Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success<Stream>(new MemoryStream(new byte[] { 1, 2, 3 })));
        _blobStorage.UploadAsync(
                BlobContainer.Invoices, ExpectedBlobPath, Arg.Any<Stream>(),
                "application/pdf", Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success());

        var result = await _sut.Handle(
            new FetchAndStoreReturnLabel.Command(DisputeId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.BlobPath.Should().Be(ExpectedBlobPath);
        await _blobStorage.Received(1).UploadAsync(
            BlobContainer.Invoices, ExpectedBlobPath, Arg.Any<Stream>(),
            "application/pdf", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Idempotent_when_blob_already_exists_carrier_not_called()
    {
        var dispute = BuildDisputeWithReturnShipment();
        _disputes.GetByIdUnscopedReadOnlyAsync(DisputeId, Arg.Any<CancellationToken>()).Returns(dispute);
        _blobStorage.ExistsAsync(BlobContainer.Invoices, ExpectedBlobPath, Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(true));

        var result = await _sut.Handle(
            new FetchAndStoreReturnLabel.Command(DisputeId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _carrierFactory.DidNotReceive().ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _carrier.DidNotReceive().GetLabelPdfAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _blobStorage.DidNotReceive().UploadAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dispute_not_found_returns_Permanent_OrderDisputeNotFound()
    {
        _disputes.GetByIdUnscopedReadOnlyAsync(DisputeId, Arg.Any<CancellationToken>()).Returns((Dispute?)null);

        var result = await _sut.Handle(
            new FetchAndStoreReturnLabel.Command(DisputeId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderDisputeNotFound);
        result.Error.Type.Should().Be(ErrorType.Permanent);
    }

    [Fact]
    public async Task Carrier_transient_error_propagates_no_upload_AC4()
    {
        // AC-4: a transient Packeta failure classifies as Transient +
        // ShippingCarrierUnavailable — same table as the forward path.
        var dispute = BuildDisputeWithReturnShipment();
        _disputes.GetByIdUnscopedReadOnlyAsync(DisputeId, Arg.Any<CancellationToken>()).Returns(dispute);
        _blobStorage.ExistsAsync(BlobContainer.Invoices, ExpectedBlobPath, Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(false));
        _carrier.GetLabelPdfAsync(CarrierRef, Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Failure<Stream>(
                Error.Transient(BusinessErrorMessage.ShippingCarrierUnavailable)));

        var result = await _sut.Handle(
            new FetchAndStoreReturnLabel.Command(DisputeId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.ShippingCarrierUnavailable);
        result.Error.Type.Should().Be(ErrorType.Transient);
        await _blobStorage.DidNotReceive().UploadAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
