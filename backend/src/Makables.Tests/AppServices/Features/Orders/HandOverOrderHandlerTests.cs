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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Orders;

/// <summary>
/// Pins T-0073 <see cref="HandOverOrder.Handler"/> — personal-pickup
/// counterpart to ShipOrder. No carrier call, null tracking URL, single
/// outbox event (NOT two — no shipping.generate.label for personal-pickup).
/// </summary>
public class HandOverOrderHandlerTests
{
    private const string OrderId = "ord-1";
    private const string MakerUserId = "user-maker-1";
    private const string MakerId = "maker-1";
    private const string CustomerUserId = "user-cust-1";
    private const string WebBaseUrl = "https://makables.test";

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-08T10:00:00Z");

    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IMakerRepository _makers = Substitute.For<IMakerRepository>();
    private readonly IUserSessionProvider _session = Substitute.For<IUserSessionProvider>();
    private readonly IOutbox _outbox = Substitute.For<IOutbox>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly ILanguageResolver _languageResolver = Substitute.For<ILanguageResolver>();
    private readonly HandOverOrder.Handler _sut;

    public HandOverOrderHandlerTests()
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

        var urls = Options.Create(new PublicAppUrlsOptions
        {
            WebBaseUrl = WebBaseUrl,
            MagicLinkPath = "/auth/magic?token={token}",
            EmailConfirmationPath = "/auth/confirm?token={token}",
            PasswordResetPath = "/auth/reset?token={token}",
        });

        _sut = new HandOverOrder.Handler(
            _orders, _users, _makers, _session, _outbox, _clock,
            _languageResolver, urls,
            NullLogger<HandOverOrder.Handler>.Instance);
    }

    private static Order BuildAcceptedOrder(ShippingMethod method = ShippingMethod.PersonalPickup)
    {
        var o = Order.Create(
            id: OrderId, orderNumber: "M-CZ-20260042",
            customerUserId: CustomerUserId, makerId: MakerId, productId: "prod-1",
            contactName: "Anna", contactEmail: "anna@example.cz", contactPhone: "+420",
            productPriceAmountMinor: 50000, shippingPriceAmountMinor: 7900,
            platformFeeAmountMinor: 7500, makerPayoutAmountMinor: 50400,
            totalAmountMinor: 57900, currency: "CZK", vatRateBp: 2100,
            shippingMethod: method,
            zasilkovnaPickupPointId: method == ShippingMethod.ZasilkovnaPickupPoint ? "pp-42" : null,
            countryCode: "CZ");
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        o.MarkAsPaid(clock, "tx-1");
        o.Accept(clock);
        return o;
    }

    [Fact]
    public async Task Happy_path_transitions_Accepted_to_Shipped_with_null_carrier_and_null_tracking()
    {
        var order = BuildAcceptedOrder();
        _orders.GetByIdForMakerAsync(OrderId, MakerId, Arg.Any<CancellationToken>())
            .Returns(order);

        var result = await _sut.Handle(new HandOverOrder.Command(OrderId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        order.State.Should().Be(OrderState.Shipped);
        order.ShippingCarrierRef.Should().BeNull();
        order.ShippingCarrierTrackingUrl.Should().BeNull();
        order.AutoDeliverAt.Should().Be(Now.AddDays(7));
    }

    [Fact]
    public async Task Happy_path_enqueues_exactly_one_customer_email_NOT_label_event()
    {
        var order = BuildAcceptedOrder();
        _orders.GetByIdForMakerAsync(OrderId, MakerId, Arg.Any<CancellationToken>())
            .Returns(order);

        await _sut.Handle(new HandOverOrder.Command(OrderId), CancellationToken.None);

        _outbox.Received(1).Enqueue(
            OrderId, OutboxEventTypes.OrderShippedCustomerEmail, Arg.Any<string>());
        _outbox.DidNotReceive().Enqueue(
            Arg.Any<string>(), OutboxEventTypes.ShippingGenerateLabel, Arg.Any<string>());
    }

    [Fact]
    public async Task Happy_path_payload_TrackingUrl_is_null()
    {
        var order = BuildAcceptedOrder();
        _orders.GetByIdForMakerAsync(OrderId, MakerId, Arg.Any<CancellationToken>())
            .Returns(order);
        string? capturedJson = null;
        _outbox.Enqueue(
            Arg.Any<string>(),
            OutboxEventTypes.OrderShippedCustomerEmail,
            Arg.Do<string>(j => capturedJson = j));

        await _sut.Handle(new HandOverOrder.Command(OrderId), CancellationToken.None);

        capturedJson.Should().NotBeNull();
        var payload = JsonSerializer.Deserialize<OrderShippedCustomerEmailPayload>(capturedJson!);
        payload!.TrackingUrl.Should().BeNull();
        payload.ActionUrl.Should().Be($"{WebBaseUrl}/objednavka/{OrderId}");
    }

    [Fact]
    public async Task Zasilkovna_order_returns_ShippingMethodNotEligible_no_outbox()
    {
        var order = BuildAcceptedOrder(ShippingMethod.ZasilkovnaPickupPoint);
        _orders.GetByIdForMakerAsync(OrderId, MakerId, Arg.Any<CancellationToken>())
            .Returns(order);

        var result = await _sut.Handle(new HandOverOrder.Command(OrderId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.ShippingMethodNotEligible);
        _outbox.DidNotReceive().Enqueue(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Order_not_owned_returns_OrderNotFound_no_outbox()
    {
        _orders.GetByIdForMakerAsync(OrderId, MakerId, Arg.Any<CancellationToken>())
            .Returns((Order?)null);

        var result = await _sut.Handle(new HandOverOrder.Command(OrderId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderNotFound);
        _outbox.DidNotReceive().Enqueue(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }
}
