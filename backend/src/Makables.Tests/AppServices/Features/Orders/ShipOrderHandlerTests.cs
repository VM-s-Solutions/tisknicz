using System.Text.Json;
using FluentAssertions;
using Makables.Core.AppServices.Common;
using Makables.Core.AppServices.Features.Orders;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Makers;
using MakerEntity = Makables.Core.Domain.Makers.Maker;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Outbox;
using Makables.Core.Domain.Shipping;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Orders;

/// <summary>
/// Pins T-0072 <see cref="ShipOrder.Handler"/> contract: Zásilkovna-only
/// path, Packeta CreateShipmentAsync call, Order.Ship signature extension
/// (carrier ref + tracking URL), atomic 2-event outbox emission, error
/// classification propagation.
/// </summary>
public class ShipOrderHandlerTests
{
    private const string OrderId = "ord-1";
    private const string MakerUserId = "user-maker-1";
    private const string MakerId = "maker-1";
    private const string CustomerUserId = "user-cust-1";
    private const string CarrierRef = "9876543210";
    private const string TrackingUrl = "https://tracking.packeta.com/Z9876543210";
    private const string WebBaseUrl = "https://makables.test";

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-08T10:00:00Z");

    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IMakerRepository _makers = Substitute.For<IMakerRepository>();
    private readonly IUserSessionProvider _session = Substitute.For<IUserSessionProvider>();
    private readonly IShippingCarrierFactory _carrierFactory = Substitute.For<IShippingCarrierFactory>();
    private readonly IShippingCarrier _carrier = Substitute.For<IShippingCarrier>();
    private readonly IOutbox _outbox = Substitute.For<IOutbox>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly ILanguageResolver _languageResolver = Substitute.For<ILanguageResolver>();
    private readonly ShipOrder.Handler _sut;

    public ShipOrderHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _session.GetUserId().Returns(MakerUserId);
        _languageResolver.ResolveForUserAsync(Arg.Any<User>(), Arg.Any<CancellationToken>())
            .Returns("cs-CZ");

        var customer = User.Create(
            id: CustomerUserId, email: "a@b.cz", role: UserRole.Customer,
            fullName: "Anna", countryCodePrimary: "CZ",
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        _users.GetByIdAsync(CustomerUserId, Arg.Any<CancellationToken>()).Returns(customer);

        var maker = MakerEntity.Create(
            id: MakerId, userId: MakerUserId,
            registrationNumber: "27074358", vatId: null,
            companyName: "Maker s.r.o.", legalForm: null,
            registeredAddressId: "addr-1",
            incorporatedOn: null, isActiveInRegistry: true,
            sourceRegistry: "ares",
            snapshotFetchedAt: Now, snapshotIsStale: false,
            countryCode: "CZ", slug: "maker");
        _makers.GetByUserIdAsync(MakerUserId, Arg.Any<CancellationToken>()).Returns(maker);

        _carrierFactory.ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(_carrier));
        _carrier.CreateShipmentAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(new Shipment(CarrierRef, TrackingUrl)));

        var urls = Options.Create(new PublicAppUrlsOptions
        {
            WebBaseUrl = WebBaseUrl,
            MagicLinkPath = "/auth/magic?token={token}",
            EmailConfirmationPath = "/auth/confirm?token={token}",
            PasswordResetPath = "/auth/reset?token={token}",
        });

        _sut = new ShipOrder.Handler(
            _orders, _users, _makers, _session, _carrierFactory, _outbox,
            _clock, _languageResolver, urls,
            NullLogger<ShipOrder.Handler>.Instance);
    }

    private static Order BuildAcceptedOrder(
        ShippingMethod shippingMethod = ShippingMethod.ZasilkovnaPickupPoint,
        string? pickupPointId = "pp-42")
    {
        var o = Order.Create(
            id: OrderId, orderNumber: "M-CZ-20260042",
            customerUserId: CustomerUserId, makerId: MakerId, productId: "prod-1",
            contactName: "Anna", contactEmail: "anna@example.cz", contactPhone: "+420",
            productPriceAmountMinor: 50000, shippingPriceAmountMinor: 7900,
            platformFeeAmountMinor: 7500, makerPayoutAmountMinor: 50400,
            totalAmountMinor: 57900, currency: "CZK", vatRateBp: 2100,
            shippingMethod: shippingMethod,
            zasilkovnaPickupPointId: shippingMethod == ShippingMethod.ZasilkovnaPickupPoint ? pickupPointId : null,
            countryCode: "CZ");
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        o.MarkAsPaid(clock, "tx-1");
        o.Accept(clock);
        return o;
    }

    [Fact]
    public async Task Happy_path_transitions_Accepted_to_Shipped_stamps_carrier_and_tracking_and_enqueues_2_events()
    {
        var order = BuildAcceptedOrder();
        _orders.GetByIdForMakerAsync(OrderId, MakerId, Arg.Any<CancellationToken>())
            .Returns(order);

        var result = await _sut.Handle(new ShipOrder.Command(OrderId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.OrderId.Should().Be(OrderId);
        result.Value.CarrierRef.Should().Be(CarrierRef);
        result.Value.TrackingUrl.Should().Be(TrackingUrl);
        order.State.Should().Be(OrderState.Shipped);
        order.ShippingCarrierRef.Should().Be(CarrierRef);
        order.ShippingCarrierTrackingUrl.Should().Be(TrackingUrl);
        order.AutoDeliverAt.Should().Be(Now.AddDays(7));
        _outbox.Received(1).Enqueue(
            OrderId, OutboxEventTypes.OrderShippedCustomerEmail, Arg.Any<string>());
        _outbox.Received(1).Enqueue(
            OrderId, OutboxEventTypes.ShippingGenerateLabel, Arg.Any<string>());
    }

    [Fact]
    public async Task PersonalPickup_order_returns_ShippingMethodNotEligible_no_carrier_call_no_outbox()
    {
        var order = BuildAcceptedOrder(ShippingMethod.PersonalPickup, pickupPointId: null);
        _orders.GetByIdForMakerAsync(OrderId, MakerId, Arg.Any<CancellationToken>())
            .Returns(order);

        var result = await _sut.Handle(new ShipOrder.Command(OrderId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.ShippingMethodNotEligible);
        await _carrierFactory.DidNotReceive().ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        _outbox.DidNotReceive().Enqueue(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Carrier_transient_failure_propagates_and_blocks_outbox()
    {
        var order = BuildAcceptedOrder();
        _orders.GetByIdForMakerAsync(OrderId, MakerId, Arg.Any<CancellationToken>())
            .Returns(order);
        _carrier.CreateShipmentAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Failure<Shipment>(
                Error.Transient(BusinessErrorMessage.ShippingCarrierUnavailable)));

        var result = await _sut.Handle(new ShipOrder.Command(OrderId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.ShippingCarrierUnavailable);
        result.Error.Type.Should().Be(ErrorType.Transient);
        order.State.Should().Be(OrderState.Accepted);
        _outbox.DidNotReceive().Enqueue(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Order_not_owned_by_maker_returns_OrderNotFound_no_carrier_call()
    {
        _orders.GetByIdForMakerAsync(OrderId, MakerId, Arg.Any<CancellationToken>())
            .Returns((Order?)null);

        var result = await _sut.Handle(new ShipOrder.Command(OrderId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderNotFound);
        await _carrierFactory.DidNotReceive().ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        _outbox.DidNotReceive().Enqueue(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Customer_payload_carries_tracking_url_and_action_url()
    {
        var order = BuildAcceptedOrder();
        _orders.GetByIdForMakerAsync(OrderId, MakerId, Arg.Any<CancellationToken>())
            .Returns(order);
        string? customerJson = null;
        _outbox.Enqueue(
            Arg.Any<string>(),
            OutboxEventTypes.OrderShippedCustomerEmail,
            Arg.Do<string>(j => customerJson = j));

        await _sut.Handle(new ShipOrder.Command(OrderId), CancellationToken.None);

        customerJson.Should().NotBeNull();
        var payload = JsonSerializer.Deserialize<OrderShippedCustomerEmailPayload>(customerJson!);
        payload!.TrackingUrl.Should().Be(TrackingUrl);
        payload.ActionUrl.Should().Be($"{WebBaseUrl}/objednavka/{OrderId}");
        payload.LanguageCode.Should().Be("cs-CZ");
    }

    [Fact]
    public async Task Label_payload_carries_OrderId()
    {
        var order = BuildAcceptedOrder();
        _orders.GetByIdForMakerAsync(OrderId, MakerId, Arg.Any<CancellationToken>())
            .Returns(order);
        string? labelJson = null;
        _outbox.Enqueue(
            Arg.Any<string>(),
            OutboxEventTypes.ShippingGenerateLabel,
            Arg.Do<string>(j => labelJson = j));

        await _sut.Handle(new ShipOrder.Command(OrderId), CancellationToken.None);

        labelJson.Should().NotBeNull();
        var payload = JsonSerializer.Deserialize<GenerateLabelOutboxPayload>(labelJson!);
        payload!.OrderId.Should().Be(OrderId);
    }
}
