using FluentAssertions;
using Makables.Core.AppServices.Features.Orders;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Orders.Queries;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Orders;

/// <summary>
/// Unit tests pinning T-0082 <see cref="GetMakerOrderDetails"/>:
/// - Two-step IDOR shield (session → makerId via IMakerRepository, then projection predicate)
/// - MakerNotFound short-circuits before orderQueries dispatch
/// - DTO type pins absence of CustomerContactEmail / PlatformFee fields (GDPR + AC-4)
/// - Passthrough of MakerPayoutAmountMinor, ZasilkovnaPickupPointId, ShippingCarrierRef.
/// </summary>
public class GetMakerOrderDetailsHandlerTests
{
    private const string OrderId = "ord-1";
    private const string UserId = "user-maker-1";
    private const string MakerId = "maker-1";

    private static readonly DateTimeOffset SnapshotAt = new(2026, 5, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-09T10:00:00Z");

    private readonly IOrderQueries _orderQueries = Substitute.For<IOrderQueries>();
    private readonly IMakerRepository _makers = Substitute.For<IMakerRepository>();
    private readonly IUserSessionProvider _session = Substitute.For<IUserSessionProvider>();
    private readonly GetMakerOrderDetails.Handler _sut;

    public GetMakerOrderDetailsHandlerTests()
    {
        _session.GetUserId().Returns(UserId);
        _makers.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(ExistingMaker());
        _sut = new GetMakerOrderDetails.Handler(_orderQueries, _makers, _session);
    }

    private static Makables.Core.Domain.Makers.Maker ExistingMaker() =>
        Makables.Core.Domain.Makers.Maker.Create(
            id: MakerId, userId: UserId, registrationNumber: "27074358",
            vatId: null, companyName: "Avast s.r.o.", legalForm: null,
            registeredAddressId: "addr-1", incorporatedOn: null,
            isActiveInRegistry: true, sourceRegistry: "ares",
            snapshotFetchedAt: SnapshotAt, snapshotIsStale: false, countryCode: "CZ");

    private static MakerOrderDetailDto BuildDto(
        string? zasilkovnaPickupPointId = "pp-42",
        string? shippingCarrierRef = "PKT-1234") =>
        new(
            OrderId: OrderId,
            OrderNumber: "M-CZ-20260001",
            State: OrderState.Shipped,
            PaidAt: Now.AddDays(-5),
            AcceptedAt: Now.AddDays(-4),
            ShippedAt: Now.AddDays(-2),
            DeliveredAt: null,
            CancelledAt: null,
            TotalAmountMinor: 57900,
            ProductPriceMinor: 50000,
            ShippingPriceMinor: 7900,
            VatAmountMinor: 10049,
            VatRateBp: 2100,
            MakerPayoutAmountMinor: 50400,
            Currency: "CZK",
            CustomerContactName: "Anna",
            CustomerContactPhone: "+420 723 456 789",
            ProductTitle: "Vase",
            ShippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
            ShippingCarrierRef: shippingCarrierRef,
            ShippingCarrierTrackingUrl: "https://tracking.packeta.com/Z1234",
            ZasilkovnaPickupPointId: zasilkovnaPickupPointId,
            Attachments: Array.Empty<OrderAttachmentSummaryDto>(),
            InvoicePdfUrl: null,
            CreatedAt: Now.AddDays(-7),
            UpdatedAt: Now.AddDays(-2));

    [Fact]
    public async Task Happy_path_returns_dto_with_payout_and_lifecycle_preserved()
    {
        var dto = BuildDto();
        _orderQueries.GetMakerOrderDetailsAsync(OrderId, MakerId, Arg.Any<CancellationToken>())
            .Returns(dto);

        var result = await _sut.Handle(new GetMakerOrderDetails.Query(OrderId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Detail.MakerPayoutAmountMinor.Should().Be(50400);
        result.Value.Detail.ShippedAt.Should().Be(dto.ShippedAt);
        result.Value.Detail.ZasilkovnaPickupPointId.Should().Be("pp-42");
        result.Value.Detail.ShippingCarrierRef.Should().Be("PKT-1234");
    }

    [Fact]
    public async Task Maker_not_found_for_user_returns_MakerNotFound()
    {
        _makers.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns((Makables.Core.Domain.Makers.Maker?)null);

        var result = await _sut.Handle(new GetMakerOrderDetails.Query(OrderId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be(BusinessErrorMessage.MakerNotFound);
        // OrderQueries MUST NOT be called when maker lookup fails.
        await _orderQueries.DidNotReceive().GetMakerOrderDetailsAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Order_ownership_mismatch_returns_OrderNotFound()
    {
        _orderQueries.GetMakerOrderDetailsAsync(OrderId, MakerId, Arg.Any<CancellationToken>())
            .Returns((MakerOrderDetailDto?)null);

        var result = await _sut.Handle(new GetMakerOrderDetails.Query(OrderId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be(BusinessErrorMessage.OrderNotFound);
    }

    [Fact]
    public void MakerOrderDetailDto_carries_no_CustomerEmail_or_PlatformFee_field()
    {
        // Compile-time + reflection guard for AC-4. Adding any of these in a
        // future PR breaks this test. Pins the GDPR data-minimization lock.
        var props = typeof(MakerOrderDetailDto).GetProperties()
            .Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        props.Should().NotContain(p => p.Contains("Email", StringComparison.OrdinalIgnoreCase));
        props.Should().NotContain(p => p.Contains("PlatformFee", StringComparison.OrdinalIgnoreCase));
        props.Should().Contain("MakerPayoutAmountMinor");
        props.Should().Contain("CustomerContactPhone");
    }

    [Fact]
    public async Task Session_userId_resolves_maker_then_makerId_passed_to_query()
    {
        _orderQueries.GetMakerOrderDetailsAsync(OrderId, MakerId, Arg.Any<CancellationToken>())
            .Returns(BuildDto());

        await _sut.Handle(new GetMakerOrderDetails.Query(OrderId), CancellationToken.None);

        // Two-step IDOR shield: session userId → IMakerRepository, then
        // resolved maker.Id forwarded to IOrderQueries.
        await _makers.Received(1).GetByUserIdAsync(UserId, Arg.Any<CancellationToken>());
        await _orderQueries.Received(1).GetMakerOrderDetailsAsync(
            OrderId, MakerId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unauthorized_when_session_has_no_user()
    {
        _session.GetUserId().Returns((string?)null);

        var result = await _sut.Handle(new GetMakerOrderDetails.Query(OrderId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);
    }
}
