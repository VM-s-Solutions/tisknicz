using FluentAssertions;
using Makables.Core.AppServices.Features.Shipping;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Shipping;
using Makables.Core.Domain.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Shipping;

/// <summary>
/// Pins T-0074 <see cref="FetchAndStoreShippingLabel.Handler"/>: load
/// unscoped Order → HEAD-check blob idempotency → carrier label fetch →
/// blob upload. No Order state mutation.
/// </summary>
public class FetchAndStoreShippingLabelHandlerTests
{
    private const string OrderId = "ord-1";
    private const string CarrierRef = "9876543210";
    private static readonly string ExpectedBlobPath = $"cz/orders/{OrderId}/label.pdf";

    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IShippingCarrierFactory _carrierFactory = Substitute.For<IShippingCarrierFactory>();
    private readonly IShippingCarrier _carrier = Substitute.For<IShippingCarrier>();
    private readonly IBlobStorageClient _blobStorage = Substitute.For<IBlobStorageClient>();
    private readonly FetchAndStoreShippingLabel.Handler _sut;

    public FetchAndStoreShippingLabelHandlerTests()
    {
        _carrierFactory.ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(_carrier));
        _sut = new FetchAndStoreShippingLabel.Handler(
            _orders, _carrierFactory, _blobStorage,
            NullLogger<FetchAndStoreShippingLabel.Handler>.Instance);
    }

    private static Order BuildShippedOrder(string carrierRef = CarrierRef)
    {
        var o = Order.Create(
            id: OrderId, orderNumber: "M-CZ-20260042",
            customerUserId: "user-1", makerId: "maker-1", productId: "prod-1",
            contactName: "Anna", contactEmail: "a@b.cz", contactPhone: "+420",
            productPriceAmountMinor: 50000, shippingPriceAmountMinor: 7900,
            platformFeeAmountMinor: 7500, makerPayoutAmountMinor: 50400,
            totalAmountMinor: 57900, currency: "CZK", vatRateBp: 2100,
            shippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
            zasilkovnaPickupPointId: "pp-42", countryCode: "CZ");
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.Parse("2026-06-08T10:00:00Z"));
        o.MarkAsPaid(clock, "tx-1");
        o.Accept(clock);
        o.Ship(clock, carrierRef, 7, "https://tracking.packeta.com/Z" + carrierRef);
        return o;
    }

    [Fact]
    public async Task Happy_path_fetches_carrier_label_and_uploads_to_blob()
    {
        var order = BuildShippedOrder();
        _orders.GetByIdUnscopedReadOnlyAsync(OrderId, Arg.Any<CancellationToken>()).Returns(order);
        _blobStorage.ExistsAsync(BlobContainer.Invoices, ExpectedBlobPath, Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(false));
        _carrier.GetLabelPdfAsync(CarrierRef, Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success<Stream>(new MemoryStream(new byte[] { 1, 2, 3 })));
        _blobStorage.UploadAsync(
                BlobContainer.Invoices, ExpectedBlobPath, Arg.Any<Stream>(),
                "application/pdf", Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success());

        var result = await _sut.Handle(
            new FetchAndStoreShippingLabel.Command(OrderId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.BlobPath.Should().Be(ExpectedBlobPath);
        await _blobStorage.Received(1).UploadAsync(
            BlobContainer.Invoices, ExpectedBlobPath, Arg.Any<Stream>(),
            "application/pdf", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Idempotent_when_blob_already_exists_carrier_not_called()
    {
        var order = BuildShippedOrder();
        _orders.GetByIdUnscopedReadOnlyAsync(OrderId, Arg.Any<CancellationToken>()).Returns(order);
        _blobStorage.ExistsAsync(BlobContainer.Invoices, ExpectedBlobPath, Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(true));

        var result = await _sut.Handle(
            new FetchAndStoreShippingLabel.Command(OrderId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _carrierFactory.DidNotReceive().ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _carrier.DidNotReceive().GetLabelPdfAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _blobStorage.DidNotReceive().UploadAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Order_not_found_returns_Permanent_OrderNotFound()
    {
        _orders.GetByIdUnscopedReadOnlyAsync(OrderId, Arg.Any<CancellationToken>()).Returns((Order?)null);

        var result = await _sut.Handle(
            new FetchAndStoreShippingLabel.Command(OrderId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderNotFound);
        result.Error.Type.Should().Be(ErrorType.Permanent);
    }

    [Fact]
    public async Task Order_with_null_carrier_ref_returns_Permanent_Configuration()
    {
        // Personal-pickup orders have no carrier ref. Defensive — T-0072 should
        // have prevented enqueue, but if a label event ever arrives, refuse.
        var order = Order.Create(
            id: OrderId, orderNumber: "M-CZ-20260042",
            customerUserId: "user-1", makerId: "maker-1", productId: "prod-1",
            contactName: "Anna", contactEmail: "a@b.cz", contactPhone: "+420",
            productPriceAmountMinor: 50000, shippingPriceAmountMinor: 7900,
            platformFeeAmountMinor: 7500, makerPayoutAmountMinor: 50400,
            totalAmountMinor: 57900, currency: "CZK", vatRateBp: 2100,
            shippingMethod: ShippingMethod.PersonalPickup,
            zasilkovnaPickupPointId: null, countryCode: "CZ");
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.Parse("2026-06-08T10:00:00Z"));
        order.MarkAsPaid(clock, "tx-1");
        order.Accept(clock);
        order.Ship(clock, null, 7, null);
        _orders.GetByIdUnscopedReadOnlyAsync(OrderId, Arg.Any<CancellationToken>()).Returns(order);

        var result = await _sut.Handle(
            new FetchAndStoreShippingLabel.Command(OrderId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.ShippingCarrierConfigurationError);
        await _carrierFactory.DidNotReceive().ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Carrier_transient_propagates_no_upload()
    {
        var order = BuildShippedOrder();
        _orders.GetByIdUnscopedReadOnlyAsync(OrderId, Arg.Any<CancellationToken>()).Returns(order);
        _blobStorage.ExistsAsync(BlobContainer.Invoices, ExpectedBlobPath, Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(false));
        _carrier.GetLabelPdfAsync(CarrierRef, Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Failure<Stream>(
                Error.Transient(BusinessErrorMessage.ShippingCarrierUnavailable)));

        var result = await _sut.Handle(
            new FetchAndStoreShippingLabel.Command(OrderId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.ShippingCarrierUnavailable);
        result.Error.Type.Should().Be(ErrorType.Transient);
        await _blobStorage.DidNotReceive().UploadAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
