using FluentAssertions;
using Makables.Core.AppServices.Common;
using Makables.Core.AppServices.Features.Orders;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Shipping;
using Makables.Functions.Delivery;
using MediatR;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Makables.Tests.Functions.Delivery;

/// <summary>
/// Pins T-0078 <see cref="SyncShipmentStatusesFunction"/> as a thin
/// MediatR-dispatch wrapper that branches on the carrier's reported
/// <see cref="ShipmentState"/>:
/// - Delivered → MarkOrderDelivered(Carrier, status.DeliveredAt),
/// - Delivered with null timestamp → MarkOrderDelivered(Carrier, null) + Warning,
/// - Returned → DisputeShipment(CarrierReturned),
/// - Failed → DisputeShipment(CarrierFailed),
/// - Created / InTransit → no-op + Debug,
/// - Carrier Transient failure → log Warning + continue.
/// </summary>
public class SyncShipmentStatusesFunctionTests
{
    private static readonly TimerInfo Timer = new();

    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IShippingCarrierFactory _carrierFactory = Substitute.For<IShippingCarrierFactory>();
    private readonly IShippingCarrier _carrier = Substitute.For<IShippingCarrier>();
    private readonly ISender _mediator = Substitute.For<ISender>();
    private readonly ILogger<SyncShipmentStatusesFunction> _logger =
        Substitute.For<ILogger<SyncShipmentStatusesFunction>>();
    private readonly SyncShipmentStatusesFunction _sut;

    public SyncShipmentStatusesFunctionTests()
    {
        _carrierFactory.ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(_carrier));
        _sut = new SyncShipmentStatusesFunction(_orders, _carrierFactory, _mediator, _logger);
    }

    private static async IAsyncEnumerable<Order> AsAsyncEnumerable(params Order[] orders)
    {
        foreach (var o in orders)
        {
            yield return o;
            await Task.Yield();
        }
    }

    private static Order BuildShippedOrder(string id = "ord-1", string carrierRef = "PKT-9")
    {
        var o = Order.Create(
            id: id, orderNumber: $"M-CZ-{id}",
            customerUserId: "user-1", makerId: "maker-1", productId: "prod-1",
            contactName: "Anna", contactEmail: "anna@example.cz", contactPhone: "+420",
            productPriceAmountMinor: 50000, shippingPriceAmountMinor: 7900,
            platformFeeAmountMinor: 7500, makerPayoutAmountMinor: 50400,
            totalAmountMinor: 57900, currency: "CZK", vatRateBp: 2100,
            shippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
            zasilkovnaPickupPointId: "pp-42", countryCode: "CZ");
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.Parse("2026-06-04T08:00:00Z"));
        o.MarkAsPaid(clock, $"tx-{id}");
        o.Accept(clock);
        o.Ship(clock, carrierRef, 7);
        return o;
    }

    [Fact]
    public async Task Delivered_state_dispatches_MarkOrderDelivered_with_Carrier_and_carrier_timestamp()
    {
        var order = BuildShippedOrder();
        var carrierTs = new DateTimeOffset(2026, 6, 8, 14, 30, 0, TimeSpan.Zero);
        _orders.GetCarrierSyncableUnscopedReadOnlyAsync(Arg.Any<CancellationToken>())
            .Returns(AsAsyncEnumerable(order));
        _carrier.GetStatusAsync("PKT-9", Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(new ShipmentStatus(ShipmentState.Delivered, carrierTs)));
        _mediator.Send(Arg.Any<MarkOrderDelivered.Command>(), Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(
                new MarkOrderDelivered.MarkOrderDeliveredResponse(order.Id, OrderState.Delivered)));

        await _sut.RunAsync(Timer, CancellationToken.None);

        await _mediator.Received(1).Send(
            Arg.Is<MarkOrderDelivered.Command>(c =>
                c.OrderId == order.Id
                && c.Source == OrderDeliverySource.Carrier
                && c.DeliveredAtOverride == carrierTs),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delivered_state_with_null_timestamp_dispatches_null_override_and_logs_Warning()
    {
        var order = BuildShippedOrder();
        _orders.GetCarrierSyncableUnscopedReadOnlyAsync(Arg.Any<CancellationToken>())
            .Returns(AsAsyncEnumerable(order));
        _carrier.GetStatusAsync("PKT-9", Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(new ShipmentStatus(ShipmentState.Delivered, null)));
        _mediator.Send(Arg.Any<MarkOrderDelivered.Command>(), Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(
                new MarkOrderDelivered.MarkOrderDeliveredResponse(order.Id, OrderState.Delivered)));

        await _sut.RunAsync(Timer, CancellationToken.None);

        await _mediator.Received(1).Send(
            Arg.Is<MarkOrderDelivered.Command>(c => c.DeliveredAtOverride == null),
            Arg.Any<CancellationToken>());
        _logger.Received().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("without timestamp")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task Returned_state_dispatches_DisputeShipment_with_CarrierReturned()
    {
        var order = BuildShippedOrder();
        _orders.GetCarrierSyncableUnscopedReadOnlyAsync(Arg.Any<CancellationToken>())
            .Returns(AsAsyncEnumerable(order));
        _carrier.GetStatusAsync("PKT-9", Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(new ShipmentStatus(ShipmentState.Returned, null)));
        _mediator.Send(Arg.Any<DisputeShipment.Command>(), Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(
                new DisputeShipment.DisputeShipmentResponse(order.Id, DisputeReason.CarrierReturned)));

        await _sut.RunAsync(Timer, CancellationToken.None);

        await _mediator.Received(1).Send(
            Arg.Is<DisputeShipment.Command>(c =>
                c.OrderId == order.Id && c.Reason == DisputeReason.CarrierReturned),
            Arg.Any<CancellationToken>());
        await _mediator.DidNotReceive().Send(
            Arg.Any<MarkOrderDelivered.Command>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Failed_state_dispatches_DisputeShipment_with_CarrierFailed()
    {
        var order = BuildShippedOrder();
        _orders.GetCarrierSyncableUnscopedReadOnlyAsync(Arg.Any<CancellationToken>())
            .Returns(AsAsyncEnumerable(order));
        _carrier.GetStatusAsync("PKT-9", Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(new ShipmentStatus(ShipmentState.Failed, null)));
        _mediator.Send(Arg.Any<DisputeShipment.Command>(), Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(
                new DisputeShipment.DisputeShipmentResponse(order.Id, DisputeReason.CarrierFailed)));

        await _sut.RunAsync(Timer, CancellationToken.None);

        await _mediator.Received(1).Send(
            Arg.Is<DisputeShipment.Command>(c =>
                c.OrderId == order.Id && c.Reason == DisputeReason.CarrierFailed),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InTransit_state_is_noop_and_logs_Debug()
    {
        var order = BuildShippedOrder();
        _orders.GetCarrierSyncableUnscopedReadOnlyAsync(Arg.Any<CancellationToken>())
            .Returns(AsAsyncEnumerable(order));
        _carrier.GetStatusAsync("PKT-9", Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(new ShipmentStatus(ShipmentState.InTransit, null)));

        await _sut.RunAsync(Timer, CancellationToken.None);

        await _mediator.DidNotReceive().Send(
            Arg.Any<MarkOrderDelivered.Command>(), Arg.Any<CancellationToken>());
        await _mediator.DidNotReceive().Send(
            Arg.Any<DisputeShipment.Command>(), Arg.Any<CancellationToken>());
        _logger.Received().Log(
            LogLevel.Debug,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("still in transit")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task Carrier_Transient_failure_logs_Warning_and_continues_to_next_order()
    {
        var order1 = BuildShippedOrder("ord-fail", "PKT-failing");
        var order2 = BuildShippedOrder("ord-ok", "PKT-ok");
        _orders.GetCarrierSyncableUnscopedReadOnlyAsync(Arg.Any<CancellationToken>())
            .Returns(AsAsyncEnumerable(order1, order2));
        _carrier.GetStatusAsync("PKT-failing", Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Failure<ShipmentStatus>(
                Error.Transient(BusinessErrorMessage.ShippingCarrierUnavailable)));
        _carrier.GetStatusAsync("PKT-ok", Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(new ShipmentStatus(ShipmentState.Delivered, DateTimeOffset.UtcNow)));
        _mediator.Send(Arg.Any<MarkOrderDelivered.Command>(), Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(
                new MarkOrderDelivered.MarkOrderDeliveredResponse(order2.Id, OrderState.Delivered)));

        var act = async () => await _sut.RunAsync(Timer, CancellationToken.None);
        await act.Should().NotThrowAsync();

        // First order: carrier failed; logged Warning; no Mediator call.
        _logger.Received().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("retrying next sweep")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
        // Second order: Delivered dispatched even after the first failed.
        await _mediator.Received(1).Send(
            Arg.Is<MarkOrderDelivered.Command>(c => c.OrderId == "ord-ok"),
            Arg.Any<CancellationToken>());
    }
}
