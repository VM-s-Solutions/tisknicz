using System.Text.Json;
using FluentAssertions;
using Makables.Core.AppServices.Common;
using Makables.Core.AppServices.Features.Orders;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Outbox;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Orders;

/// <summary>
/// T-0106 REWRITE of the T-0078 stub pins: <see cref="DisputeShipment.Handler"/>
/// now drives the REAL dispute flow — Shipped → Disputed (stamping
/// PreDisputeState), a Dispute row (Source: Carrier, §C.5 wire→domain
/// category mapping), and ONE <c>order.disputed.adminEmail</c> outbox
/// emission. A re-fire against the now-Disputed order is Silent-Success
/// with no new rows (the stub's intentional repeat-emission is gone).
/// </summary>
public class DisputeShipmentHandlerTests
{
    private const string OrderId = "ord-1";
    private const string CustomerUserId = "user-cust-1";
    private const string MakerId = "maker-1";
    private const string CarrierRef = "PKT-9";

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-12T10:00:00Z");

    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IDisputeRepository _disputes = Substitute.For<IDisputeRepository>();
    private readonly IOutbox _outbox = Substitute.For<IOutbox>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly IIdGenerator _ids = Substitute.For<IIdGenerator>();
    private readonly ILanguageResolver _languageResolver = Substitute.For<ILanguageResolver>();
    private readonly DisputeShipment.Handler _sut;

    public DisputeShipmentHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _ids.Next().Returns("dsp-1");
        _languageResolver.ResolveAsync(null, "CZ", Arg.Any<CancellationToken>())
            .Returns("cs-CZ");
        var urls = Options.Create(new PublicAppUrlsOptions { WebBaseUrl = "https://makables.test" });
        _sut = new DisputeShipment.Handler(
            _orders, _disputes, _outbox, _clock, _ids, _languageResolver, urls,
            NullLogger<DisputeShipment.Handler>.Instance);
    }

    private static Order BuildShippedOrder()
    {
        var o = Order.Create(
            id: OrderId, orderNumber: "M-CZ-20260042",
            customerUserId: CustomerUserId, makerId: MakerId, productId: "prod-1",
            contactName: "Anna", contactEmail: "anna@example.cz", contactPhone: "+420",
            productPriceAmountMinor: 50000, shippingPriceAmountMinor: 7900,
            platformFeeAmountMinor: 7500, makerPayoutAmountMinor: 50400,
            totalAmountMinor: 57900, currency: "CZK", vatRateBp: 2100,
            shippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
            zasilkovnaPickupPointId: "pp-42", countryCode: "CZ");
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now.AddDays(-5));
        o.MarkAsPaid(clock, "tx-1");
        o.Accept(clock);
        o.Ship(clock, CarrierRef, 7);
        return o;
    }

    [Fact]
    public async Task CarrierReturned_disputes_the_order_with_carrier_source_and_admin_email()
    {
        var order = BuildShippedOrder();
        _orders.GetByIdUnscopedAsync(OrderId, Arg.Any<CancellationToken>()).Returns(order);
        Dispute? addedDispute = null;
        await _disputes.AddAsync(Arg.Do<Dispute>(d => addedDispute = d), Arg.Any<CancellationToken>());
        string? capturedJson = null;
        _outbox.Enqueue(
            Arg.Any<string>(),
            OutboxEventTypes.OrderDisputedAdminEmail,
            Arg.Do<string>(j => capturedJson = j));

        var result = await _sut.Handle(
            new DisputeShipment.Command(OrderId, DisputeReason.CarrierReturned),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.OrderId.Should().Be(OrderId);
        result.Value.Reason.Should().Be(DisputeReason.CarrierReturned);

        // T-0106: the REAL state transition (stub behaviour gone).
        order.State.Should().Be(OrderState.Disputed);
        order.PreDisputeState.Should().Be(OrderState.Shipped);
        order.DisputedAt.Should().Be(Now);

        addedDispute.Should().NotBeNull();
        addedDispute!.Source.Should().Be(DisputeSource.Carrier);
        addedDispute.Category.Should().Be(DisputeCategory.CarrierReturned,
            "§C.5 maps the carrier wire enum onto the domain category");
        addedDispute.Description.Should().Contain(CarrierRef,
            "the canned carrier text carries the carrier ref as the evidence pointer");

        capturedJson.Should().NotBeNull();
        var payload = JsonSerializer.Deserialize<OrderDisputedAdminEmailPayload>(capturedJson!);
        payload!.OrderId.Should().Be(OrderId);
        payload.DisputeId.Should().Be("dsp-1");
        payload.Source.Should().Be(DisputeSource.Carrier);
        payload.ActionUrl.Should().Be($"https://makables.test/dashboard/admin/orders/{OrderId}");
        _outbox.Received(1).Enqueue(
            OrderId, OutboxEventTypes.OrderDisputedAdminEmail, Arg.Any<string>());
    }

    [Fact]
    public async Task CarrierFailed_maps_to_the_CarrierFailed_category()
    {
        var order = BuildShippedOrder();
        _orders.GetByIdUnscopedAsync(OrderId, Arg.Any<CancellationToken>()).Returns(order);
        Dispute? addedDispute = null;
        await _disputes.AddAsync(Arg.Do<Dispute>(d => addedDispute = d), Arg.Any<CancellationToken>());

        await _sut.Handle(
            new DisputeShipment.Command(OrderId, DisputeReason.CarrierFailed),
            CancellationToken.None);

        addedDispute!.Category.Should().Be(DisputeCategory.CarrierFailed);
    }

    [Fact]
    public async Task Re_fire_on_disputed_order_is_silent_success_without_new_rows_or_emission()
    {
        // T-0106 kills the stub's intentional repeat-emission: the state
        // flip already drops the order out of the carrier sweep, and a
        // race re-fire must be a no-op.
        var order = BuildShippedOrder();
        order.OpenDispute(_clock);
        _orders.GetByIdUnscopedAsync(OrderId, Arg.Any<CancellationToken>()).Returns(order);

        var result = await _sut.Handle(
            new DisputeShipment.Command(OrderId, DisputeReason.CarrierReturned),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _disputes.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        _outbox.DidNotReceive().Enqueue(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Order_not_found_returns_NotFound_OrderNotFound_without_enqueue()
    {
        _orders.GetByIdUnscopedAsync(OrderId, Arg.Any<CancellationToken>()).Returns((Order?)null);

        var result = await _sut.Handle(
            new DisputeShipment.Command(OrderId, DisputeReason.CarrierReturned),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderNotFound);
        _outbox.DidNotReceive().Enqueue(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }
}
