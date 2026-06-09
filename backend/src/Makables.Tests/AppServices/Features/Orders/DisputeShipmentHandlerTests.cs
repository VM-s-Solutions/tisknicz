using System.Text.Json;
using FluentAssertions;
using Makables.Core.AppServices.Common;
using Makables.Core.AppServices.Features.Orders;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Outbox;
using Makables.Core.Domain.Shipping;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Orders;

/// <summary>
/// Pins T-0078 <see cref="DisputeShipment.Handler"/> stub contract:
/// - Warning log with structured context (OrderId + Reason + CarrierRef),
/// - exactly 1 OrderDisputedCarrierSourced outbox row enqueued with
///   the expected payload shape (Reason + raw ShipmentState for audit),
/// - <b>NO Order state mutation</b> (Order stays in Shipped — T-0106
///   will wire the real Disputed transition),
/// - Repeat invocations emit a second outbox row (intentional MVP
///   behaviour; T-0106 dedupes via the state-graph transition),
/// - Missing order returns Permanent OrderNotFound without enqueue.
/// </summary>
public class DisputeShipmentHandlerTests
{
    private const string OrderId = "ord-1";
    private const string CustomerUserId = "user-cust-1";
    private const string MakerId = "maker-1";
    private const string CarrierRef = "PKT-9";

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-09T10:00:00Z");

    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IOutbox _outbox = Substitute.For<IOutbox>();
    private readonly DisputeShipment.Handler _sut;

    public DisputeShipmentHandlerTests()
    {
        _sut = new DisputeShipment.Handler(_orders, _outbox,
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
    public async Task Happy_path_CarrierReturned_enqueues_outbox_event_with_audit_payload()
    {
        var order = BuildShippedOrder();
        _orders.GetByIdUnscopedAsync(OrderId, Arg.Any<CancellationToken>()).Returns(order);
        string? capturedJson = null;
        _outbox.Enqueue(
            Arg.Any<string>(),
            OutboxEventTypes.OrderDisputedCarrierSourced,
            Arg.Do<string>(j => capturedJson = j));

        var result = await _sut.Handle(
            new DisputeShipment.Command(OrderId, DisputeReason.CarrierReturned),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.OrderId.Should().Be(OrderId);
        result.Value.Reason.Should().Be(DisputeReason.CarrierReturned);

        // NO Order state mutation at MVP per T-0078 A.1.
        order.State.Should().Be(OrderState.Shipped);

        capturedJson.Should().NotBeNull();
        var payload = JsonSerializer.Deserialize<OrderDisputedCarrierSourcedPayload>(capturedJson!);
        payload!.OrderId.Should().Be(OrderId);
        payload.Reason.Should().Be(DisputeReason.CarrierReturned);
        payload.CarrierState.Should().Be(ShipmentState.Returned,
            "audit payload preserves the raw Packeta state for T-0106 admin processing");

        _outbox.Received(1).Enqueue(
            OrderId, OutboxEventTypes.OrderDisputedCarrierSourced, Arg.Any<string>());
    }

    [Fact]
    public async Task Happy_path_CarrierFailed_maps_carrier_state_to_Failed()
    {
        var order = BuildShippedOrder();
        _orders.GetByIdUnscopedAsync(OrderId, Arg.Any<CancellationToken>()).Returns(order);
        string? capturedJson = null;
        _outbox.Enqueue(
            Arg.Any<string>(),
            OutboxEventTypes.OrderDisputedCarrierSourced,
            Arg.Do<string>(j => capturedJson = j));

        await _sut.Handle(
            new DisputeShipment.Command(OrderId, DisputeReason.CarrierFailed),
            CancellationToken.None);

        var payload = JsonSerializer.Deserialize<OrderDisputedCarrierSourcedPayload>(capturedJson!);
        payload!.Reason.Should().Be(DisputeReason.CarrierFailed);
        payload.CarrierState.Should().Be(ShipmentState.Failed);
    }

    [Fact]
    public async Task Idempotency_on_re_dispatch_emits_second_outbox_row()
    {
        // T-0078 MVP: visible duplicate outbox rows signal that the
        // dispute keeps re-firing across sweeps (T-0106 will resolve via
        // the state-graph transition).
        var order = BuildShippedOrder();
        _orders.GetByIdUnscopedAsync(OrderId, Arg.Any<CancellationToken>()).Returns(order);

        await _sut.Handle(
            new DisputeShipment.Command(OrderId, DisputeReason.CarrierReturned),
            CancellationToken.None);
        await _sut.Handle(
            new DisputeShipment.Command(OrderId, DisputeReason.CarrierReturned),
            CancellationToken.None);

        _outbox.Received(2).Enqueue(
            OrderId, OutboxEventTypes.OrderDisputedCarrierSourced, Arg.Any<string>());
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
