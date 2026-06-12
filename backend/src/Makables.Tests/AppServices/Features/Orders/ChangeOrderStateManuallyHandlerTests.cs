using FluentAssertions;
using Makables.Core.AppServices.Features.Orders;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Orders;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Orders;

/// <summary>
/// T-0107 <see cref="ChangeOrderStateManually.Handler"/> contract:
/// fail-closed session check, policy-gated routing to the semantic
/// domain methods (with the right source stamps), same-state Silent
/// Success without mutation, blocked codes naming the sanctioned
/// command, and the ≥10-char reason Validator.
/// </summary>
public class ChangeOrderStateManuallyHandlerTests
{
    private const string OrderId = "ord-1";
    private const string AdminUserId = "user-admin-1";

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-12T10:00:00Z");

    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IUserSessionProvider _session = Substitute.For<IUserSessionProvider>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly ChangeOrderStateManually.Handler _sut;

    public ChangeOrderStateManuallyHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _session.GetUserId().Returns(AdminUserId);
        _sut = new ChangeOrderStateManually.Handler(_orders, _session, _clock);
    }

    private static Order OrderInState(OrderState target, bool withProviderRef = true)
    {
        var o = Order.Create(
            id: OrderId, orderNumber: "M-CZ-20260042",
            customerUserId: "user-cust-1", makerId: "maker-1", productId: "prod-1",
            contactName: "Anna", contactEmail: "anna@example.cz", contactPhone: "+420",
            productPriceAmountMinor: 50000, shippingPriceAmountMinor: 7900,
            platformFeeAmountMinor: 7500, makerPayoutAmountMinor: 50400,
            totalAmountMinor: 57900, currency: "CZK", vatRateBp: 2100,
            shippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
            zasilkovnaPickupPointId: "pp-42", countryCode: "CZ");
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now.AddDays(-2));

        if (target == OrderState.PendingPayment)
        {
            if (withProviderRef)
                o.ReservePaymentSession("tx-1", "https://pay.test/tx-1", clock);
            return o;
        }

        o.MarkAsPaid(clock, "tx-1");
        if (target == OrderState.Paid) return o;
        o.Accept(clock);
        if (target == OrderState.Accepted) return o;
        o.Ship(clock, "PKT-1", 7);
        if (target == OrderState.Shipped) return o;
        throw new ArgumentOutOfRangeException(nameof(target));
    }

    [Fact]
    public async Task PendingPayment_to_Cancelled_stamps_Admin_cancellation_source()
    {
        var order = OrderInState(OrderState.PendingPayment, withProviderRef: false);
        _orders.GetByIdUnscopedAsync(OrderId, Arg.Any<CancellationToken>()).Returns(order);

        var result = await _sut.Handle(
            new ChangeOrderStateManually.Command(
                OrderId, OrderState.Cancelled, "Zákazník telefonicky požádal o zrušení."),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.State.Should().Be(OrderState.Cancelled);
        order.CancellationSource.Should().Be(OrderCancellationSource.Admin);
        order.CancelledAt.Should().Be(Now);
    }

    [Fact]
    public async Task PendingPayment_to_Paid_reuses_the_existing_provider_ref()
    {
        var order = OrderInState(OrderState.PendingPayment, withProviderRef: true);
        _orders.GetByIdUnscopedAsync(OrderId, Arg.Any<CancellationToken>()).Returns(order);

        var result = await _sut.Handle(
            new ChangeOrderStateManually.Command(
                OrderId, OrderState.Paid, "Webhook se ztratil, platba ověřena v Comgate portálu."),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        order.State.Should().Be(OrderState.Paid);
        order.PaymentProviderRef.Should().Be("tx-1", "the existing ref is reused, never fabricated");
        order.PaidAt.Should().Be(Now, "no authoritative provider timestamp on the manual path");
    }

    [Fact]
    public async Task Shipped_to_Delivered_stamps_AdminManual_delivery_source()
    {
        var order = OrderInState(OrderState.Shipped);
        _orders.GetByIdUnscopedAsync(OrderId, Arg.Any<CancellationToken>()).Returns(order);

        var result = await _sut.Handle(
            new ChangeOrderStateManually.Command(
                OrderId, OrderState.Delivered, "Zákazník potvrdil převzetí telefonicky."),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        order.State.Should().Be(OrderState.Delivered);
        order.DeliverySource.Should().Be(OrderDeliverySource.AdminManual);
        order.DeliveredAt.Should().Be(Now);
    }

    [Fact]
    public async Task Same_state_target_is_silent_success_without_mutation()
    {
        var order = OrderInState(OrderState.Paid);
        var paidAtBefore = order.PaidAt;
        _orders.GetByIdUnscopedAsync(OrderId, Arg.Any<CancellationToken>()).Returns(order);

        var result = await _sut.Handle(
            new ChangeOrderStateManually.Command(
                OrderId, OrderState.Paid, "Dvojité odeslání formuláře administrátorem."),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.State.Should().Be(OrderState.Paid);
        order.PaidAt.Should().Be(paidAtBefore, "a NoOp must not restamp anything");
    }

    [Fact]
    public async Task Blocked_transition_names_the_sanctioned_command()
    {
        var order = OrderInState(OrderState.Paid);
        _orders.GetByIdUnscopedAsync(OrderId, Arg.Any<CancellationToken>()).Returns(order);

        var result = await _sut.Handle(
            new ChangeOrderStateManually.Command(
                OrderId, OrderState.Refunded, "Pokus o ruční označení refundace."),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.Conflict);
        result.Error.Code.Should().Be(BusinessErrorMessage.OrderManualTransitionUseRefundOrder);
        order.State.Should().Be(OrderState.Paid);
    }

    [Fact]
    public async Task Missing_session_fails_closed_before_any_lookup()
    {
        _session.GetUserId().Returns((string?)null);

        var result = await _sut.Handle(
            new ChangeOrderStateManually.Command(
                OrderId, OrderState.Delivered, "Bez přihlášeného administrátora."),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);
        await _orders.DidNotReceiveWithAnyArgs().GetByIdUnscopedAsync(default!, default);
    }

    [Fact]
    public void Validator_rejects_short_reason_and_accepts_a_real_sentence()
    {
        var validator = new ChangeOrderStateManually.Validator();

        var nineChars = validator.Validate(new ChangeOrderStateManually.Command(
            OrderId, OrderState.Delivered, "123456789"));
        nineChars.IsValid.Should().BeFalse();
        nineChars.Errors.Should().Contain(e => e.ErrorCode == BusinessErrorMessage.MinLength);

        var empty = validator.Validate(new ChangeOrderStateManually.Command(
            OrderId, OrderState.Delivered, ""));
        empty.IsValid.Should().BeFalse();
        empty.Errors.Should().Contain(e => e.ErrorCode == BusinessErrorMessage.Required);

        var oversize = validator.Validate(new ChangeOrderStateManually.Command(
            OrderId, OrderState.Delivered, new string('x', 2001)));
        oversize.IsValid.Should().BeFalse();
        oversize.Errors.Should().Contain(e => e.ErrorCode == BusinessErrorMessage.MaxLength);

        // must-cover-tests §5: one positive per Validator.
        validator.Validate(new ChangeOrderStateManually.Command(
            OrderId, OrderState.Delivered, "Zákazník potvrdil převzetí telefonicky."))
            .IsValid.Should().BeTrue();
    }
}
