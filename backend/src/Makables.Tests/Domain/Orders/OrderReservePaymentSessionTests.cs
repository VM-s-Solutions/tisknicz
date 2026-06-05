using FluentAssertions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Orders;
using NSubstitute;

namespace Makables.Tests.Domain.Orders;

/// <summary>
/// Pins the T-0065 <see cref="Order.ReservePaymentSession"/> entity-level
/// contract: state-gate + Q1 overwrite-after-rejection semantics + state
/// is not mutated.
/// </summary>
public class OrderReservePaymentSessionTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-05T10:00:00Z");
    private const string TransId1 = "AB1C-D34E";
    private const string TransId2 = "AB1C-FFFF";
    private const string Redirect1 = "https://payments.comgate.cz/pay/AB1C-D34E";
    private const string Redirect2 = "https://payments.comgate.cz/pay/AB1C-FFFF";

    private static IClock FixedClock(DateTimeOffset? at = null)
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(at ?? Now);
        return clock;
    }

    private static Order PendingOrder() => Order.Create(
        id: "ord-1",
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

    [Fact]
    public void Happy_path_sets_ref_and_redirect_and_does_not_change_state()
    {
        var order = PendingOrder();

        var result = order.ReservePaymentSession(TransId1, Redirect1, FixedClock());

        result.IsSuccess.Should().BeTrue();
        order.PaymentProviderRef.Should().Be(TransId1);
        order.PaymentRedirectUrl.Should().Be(Redirect1);
        order.State.Should().Be(OrderState.PendingPayment, "state is the webhook's job");
        order.UpdatedAt.Should().Be(Now);
    }

    [Fact]
    public void Idempotent_on_same_ref_only_refreshes_redirect_url()
    {
        var order = PendingOrder();
        order.ReservePaymentSession(TransId1, Redirect1, FixedClock(Now));

        // Comgate may return a fresher URL on a same-refId retry.
        var refreshed = "https://payments.comgate.cz/pay/AB1C-D34E?rebuilt=1";
        var result = order.ReservePaymentSession(TransId1, refreshed, FixedClock(Now.AddMinutes(2)));

        result.IsSuccess.Should().BeTrue();
        order.PaymentProviderRef.Should().Be(TransId1, "ref stays put");
        order.PaymentRedirectUrl.Should().Be(refreshed);
    }

    [Fact]
    public void Allows_overwrite_when_existing_ref_differs_from_incoming()
    {
        // The handler is responsible for verifying the prior session was
        // Cancelled/Failed first. The aggregate trusts the caller — same
        // shape as Cleansia's set-once-or-replace pattern.
        var order = PendingOrder();
        order.ReservePaymentSession(TransId1, Redirect1, FixedClock(Now));

        var result = order.ReservePaymentSession(
            TransId2, Redirect2, FixedClock(Now.AddHours(6)));

        result.IsSuccess.Should().BeTrue();
        order.PaymentProviderRef.Should().Be(TransId2);
        order.PaymentRedirectUrl.Should().Be(Redirect2);
    }

    [Fact]
    public void State_Paid_returns_OrderInvalidTransition()
    {
        var order = PendingOrder();
        order.MarkAsPaid(FixedClock(), "preset-tx");

        var result = order.ReservePaymentSession(TransId1, Redirect1, FixedClock());

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderInvalidTransition);
        result.Error.Type.Should().Be(ErrorType.Conflict);
        order.PaymentProviderRef.Should().Be("preset-tx", "the original ref is preserved");
        order.PaymentRedirectUrl.Should().BeNull("no redirect was ever reserved");
    }

    [Fact]
    public void State_Cancelled_returns_OrderInvalidTransition()
    {
        var order = PendingOrder();
        order.Cancel(FixedClock());

        var result = order.ReservePaymentSession(TransId1, Redirect1, FixedClock());

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderInvalidTransition);
    }

    [Fact]
    public void State_Shipped_returns_OrderInvalidTransition()
    {
        var order = PendingOrder();
        order.MarkAsPaid(FixedClock(), "tx-1");
        order.Accept(FixedClock());
        order.Ship(FixedClock(), "PKT-1", 7);

        var result = order.ReservePaymentSession(TransId1, Redirect1, FixedClock());

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderInvalidTransition);
    }

    [Fact]
    public void State_is_unchanged_after_success()
    {
        var order = PendingOrder();
        var stateBefore = order.State;

        order.ReservePaymentSession(TransId1, Redirect1, FixedClock());

        order.State.Should().Be(stateBefore, "ReservePaymentSession does NOT transition state");
        order.PaidAt.Should().BeNull();
    }

    [Theory]
    [InlineData("", Redirect1)]
    [InlineData("  ", Redirect1)]
    [InlineData(TransId1, "")]
    [InlineData(TransId1, "  ")]
    public void Blank_ref_or_url_throws_ArgumentException(string providerRef, string redirectUrl)
    {
        var order = PendingOrder();

        var act = () => order.ReservePaymentSession(providerRef, redirectUrl, FixedClock());

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Null_clock_throws_ArgumentNullException()
    {
        var order = PendingOrder();
        var act = () => order.ReservePaymentSession(TransId1, Redirect1, null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
