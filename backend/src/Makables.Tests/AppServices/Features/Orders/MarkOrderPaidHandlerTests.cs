using FluentAssertions;
using Makables.Core.AppServices.Features.Orders;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Orders;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Orders;

/// <summary>
/// Pins the T-0066 <see cref="MarkOrderPaid.Handler"/> 5-step flow:
/// resolve by providerRef, defence-in-depth ref check, state transition
/// via <see cref="Order.MarkAsPaid"/>, ignore PaymentMethod + PaidAt
/// (T-0067 territory), no SaveChangesAsync. NSubstitute over the
/// repository + clock; the aggregate's own coverage is in the domain
/// tests.
/// </summary>
public class MarkOrderPaidHandlerTests
{
    private const string OrderId = "ord-1";
    private const string ProviderRef = "comgate-tx-1";
    private const string OtherProviderRef = "comgate-tx-2";
    private const string PaymentMethod = "CARD_CZ";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-06T10:00:00Z");
    private static readonly DateTimeOffset PaidAt = DateTimeOffset.Parse("2026-06-06T09:59:30Z");

    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly MarkOrderPaid.Handler _sut;

    public MarkOrderPaidHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _sut = new MarkOrderPaid.Handler(
            _orders, _clock, NullLogger<MarkOrderPaid.Handler>.Instance);
    }

    private static Order BuildOrderInState(
        OrderState target,
        string id = OrderId,
        string? presetRef = null)
    {
        var o = Order.Create(
            id: id,
            orderNumber: "M-CZ-20260042",
            customerUserId: "user-1",
            makerId: "maker-1",
            productId: "prod-1",
            contactName: "Anna",
            contactEmail: "a@b.cz",
            contactPhone: "+420",
            productPriceAmountMinor: 50000,
            shippingPriceAmountMinor: 7900,
            platformFeeAmountMinor: 7500,
            makerPayoutAmountMinor: 50400,
            totalAmountMinor: 57900,
            currency: "CZK",
            vatRateBp: 2100,
            shippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
            zasilkovnaPickupPointId: "pp-42",
            countryCode: "CZ");

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);

        if (target == OrderState.PendingPayment)
        {
            // Optionally seed a ReservePaymentSession so the order has
            // a (different) PaymentProviderRef already set — used by
            // the "different ref" defence-in-depth test below.
            if (presetRef is not null)
            {
                o.ReservePaymentSession(presetRef, "https://x", clock);
            }
            return o;
        }

        o.MarkAsPaid(clock, presetRef ?? "preset-tx");
        if (target == OrderState.Paid) return o;
        o.Accept(clock);
        if (target == OrderState.Accepted) return o;
        o.Cancel(clock); // unreachable here; just suppresses the warning
        if (target == OrderState.Cancelled) return o;
        throw new ArgumentOutOfRangeException(nameof(target), target, "Unsupported.");
    }

    private static MarkOrderPaid.Command ValidCommand(string orderId = OrderId) =>
        new(OrderId: orderId, ProviderRef: ProviderRef, PaymentMethod: PaymentMethod, PaidAt: PaidAt);

    [Fact]
    public async Task Happy_path_transitions_order_to_Paid_and_returns_OrderId()
    {
        var order = BuildOrderInState(OrderState.PendingPayment);
        _orders.GetByPaymentProviderRefAsync(ProviderRef, Arg.Any<CancellationToken>())
            .Returns(order);

        var result = await _sut.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.OrderId.Should().Be(OrderId);
        order.State.Should().Be(OrderState.Paid);
        order.PaymentProviderRef.Should().Be(ProviderRef);
        order.PaidAt.Should().Be(Now, "Order.MarkAsPaid uses IClock.UtcNow, not command.PaidAt");
    }

    [Fact]
    public async Task Order_not_found_returns_OrderNotFound()
    {
        _orders.GetByPaymentProviderRefAsync(ProviderRef, Arg.Any<CancellationToken>())
            .Returns((Order?)null);

        var result = await _sut.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderNotFound);
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Resolved_order_Id_does_not_match_Command_OrderId_returns_RefIdMismatch()
    {
        // Resolved order has id "ord-1"; Command says order id is "ord-different".
        var order = BuildOrderInState(OrderState.PendingPayment);
        _orders.GetByPaymentProviderRefAsync(ProviderRef, Arg.Any<CancellationToken>())
            .Returns(order);

        var command = new MarkOrderPaid.Command(
            OrderId: "ord-different",
            ProviderRef: ProviderRef,
            PaymentMethod: PaymentMethod,
            PaidAt: PaidAt);

        var result = await _sut.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.PaymentWebhookRefIdMismatch);
        result.Error.Type.Should().Be(ErrorType.Conflict);
        order.State.Should().Be(OrderState.PendingPayment, "no mutation when ref check fails");
    }

    [Fact]
    public async Task Order_already_Paid_surfaces_OrderInvalidTransition()
    {
        var order = BuildOrderInState(OrderState.Paid);
        _orders.GetByPaymentProviderRefAsync(ProviderRef, Arg.Any<CancellationToken>())
            .Returns(order);

        var result = await _sut.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderInvalidTransition);
    }

    [Fact]
    public async Task Order_already_Cancelled_surfaces_OrderInvalidTransition()
    {
        // PendingPayment → Cancelled to make this state legal.
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
        clock.UtcNow.Returns(Now);
        o.Cancel(clock);

        _orders.GetByPaymentProviderRefAsync(ProviderRef, Arg.Any<CancellationToken>())
            .Returns(o);

        var result = await _sut.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderInvalidTransition);
    }

    [Fact]
    public async Task Existing_PaymentProviderRef_set_to_different_ref_surfaces_OrderInvalidTransition()
    {
        // The set-once invariant on PaymentProviderRef belongs to
        // Order.MarkAsPaid (T-0060 R2-1). We exercise it via a
        // PendingPayment order with a pre-existing ref.
        var order = BuildOrderInState(OrderState.PendingPayment, presetRef: OtherProviderRef);
        _orders.GetByPaymentProviderRefAsync(ProviderRef, Arg.Any<CancellationToken>())
            .Returns(order);

        var result = await _sut.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderInvalidTransition);
        order.PaymentProviderRef.Should().Be(OtherProviderRef,
            "the set-once invariant must preserve the original ref");
    }

    [Fact]
    public async Task PaymentMethod_and_PaidAt_in_Command_are_NOT_persisted_at_T_0066()
    {
        var order = BuildOrderInState(OrderState.PendingPayment);
        _orders.GetByPaymentProviderRefAsync(ProviderRef, Arg.Any<CancellationToken>())
            .Returns(order);

        var commandWithMethod = new MarkOrderPaid.Command(
            OrderId: OrderId,
            ProviderRef: ProviderRef,
            PaymentMethod: "CARD_DE",       // accepted-and-ignored
            PaidAt: PaidAt);                 // accepted-and-ignored

        var result = await _sut.Handle(commandWithMethod, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // Order has no PaymentMethod column at T-0066; the only public
        // observable is PaidAt (set by IClock.UtcNow, NOT by command.PaidAt).
        order.PaidAt.Should().Be(Now,
            "T-0066 keeps PaidAt = clock.UtcNow; T-0067 will persist command.PaidAt");
    }
}
