using FluentAssertions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Orders;
using NSubstitute;

namespace Makables.Tests.Domain.Orders;

/// <summary>
/// Exhaustive coverage for the <see cref="Order"/> aggregate per T-0060:
/// factory invariants, every legal state-machine edge, every illegal
/// edge per the role-doc graph, set-once invariants on the provider
/// refs, and the <see cref="Order.AutoDeliverAt"/> arithmetic.
///
/// <para>
/// Tests use a stubbed <see cref="IClock"/> so the timestamp assertions
/// can pin time exactly — the entity never reads <c>DateTimeOffset.UtcNow</c>
/// directly.
/// </para>
/// </summary>
public class OrderTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-03T10:00:00Z");

    private static IClock FixedClock(DateTimeOffset? at = null)
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(at ?? Now);
        return clock;
    }

    private static Order ValidDefaults() => Order.Create(
        id: "ord-1",
        orderNumber: "CZ-2026-000001",
        customerUserId: "user-1",
        makerId: "maker-1",
        productId: "prod-1",
        contactName: "Jan Novák",
        contactEmail: "jan@example.cz",
        contactPhone: "+420777123456",
        productPriceAmountMinor: 25000,
        shippingPriceAmountMinor: 7900,
        platformFeeAmountMinor: 3290,
        makerPayoutAmountMinor: 29610,
        totalAmountMinor: 32900,
        currency: "CZK",
        vatRateBp: 2100,
        shippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
        zasilkovnaPickupPointId: "branch-79",
        countryCode: "cz");

    // === Factory ===

    [Fact]
    public void Create_initialises_pending_payment_and_trims_strings()
    {
        var o = Order.Create(
            id: "ord-1",
            orderNumber: "  CZ-2026-000001  ",
            customerUserId: "user-1",
            makerId: "maker-1",
            productId: "prod-1",
            contactName: "  Jan Novák  ",
            contactEmail: "  jan@example.cz  ",
            contactPhone: "  +420777123456  ",
            productPriceAmountMinor: 10000,
            shippingPriceAmountMinor: 5000,
            platformFeeAmountMinor: 1500,
            makerPayoutAmountMinor: 13500,
            totalAmountMinor: 15000,
            currency: "czk",
            vatRateBp: 2100,
            shippingMethod: ShippingMethod.PersonalPickup,
            zasilkovnaPickupPointId: null,
            countryCode: "cz",
            customerNotes: "  rychle prosím  ");

        o.State.Should().Be(OrderState.PendingPayment);
        o.OrderNumber.Should().Be("CZ-2026-000001");
        o.ContactName.Should().Be("Jan Novák");
        o.ContactEmail.Should().Be("jan@example.cz");
        o.ContactPhone.Should().Be("+420777123456");
        o.Currency.Should().Be("CZK");
        o.CountryCode.Should().Be("CZ");
        o.CustomerNotes.Should().Be("rychle prosím");
        o.IsActive.Should().BeTrue();
        o.PaymentProviderRef.Should().BeNull();
        o.ShippingCarrierRef.Should().BeNull();
        o.AutoDeliverAt.Should().BeNull();
    }

    [Fact]
    public void Create_accepts_null_product_id_for_custom_orders()
    {
        var o = Order.Create(
            id: "ord-1", orderNumber: "CZ-1", customerUserId: "user-1", makerId: "maker-1",
            productId: null,
            contactName: "X", contactEmail: "x@y.cz", contactPhone: "+420",
            productPriceAmountMinor: 1000, shippingPriceAmountMinor: 0,
            platformFeeAmountMinor: 100, makerPayoutAmountMinor: 900,
            totalAmountMinor: 1000, currency: "CZK", vatRateBp: 2100,
            shippingMethod: ShippingMethod.PersonalPickup,
            zasilkovnaPickupPointId: null, countryCode: "CZ");

        o.ProductId.Should().BeNull();
    }

    // Each row drives exactly ONE money field negative; the other four
    // satisfy both pricing-consistency invariants (product + shipping ==
    // total, AND maker + fee == product + shipping) so the per-field
    // non-negativity guard MUST fire before either consistency check.
    // Asserting on ParamName pins the right guard and isolates regressions.
    // T-0060 Copilot review R4-1.
    [Theory]
    [InlineData(-1L, 101L, 50L, 50L, 100L, "productPriceAmountMinor")]
    [InlineData(100L, -1L, 50L, 49L, 99L, "shippingPriceAmountMinor")]
    [InlineData(100L, 0L, -1L, 101L, 100L, "platformFeeAmountMinor")]
    [InlineData(100L, 0L, 101L, -1L, 100L, "makerPayoutAmountMinor")]
    [InlineData(100L, 0L, 0L, 100L, -1L, "totalAmountMinor")]
    public void Create_rejects_negative_money_columns(
        long productPriceAmountMinor,
        long shippingPriceAmountMinor,
        long platformFeeAmountMinor,
        long makerPayoutAmountMinor,
        long totalAmountMinor,
        string expectedParamName)
    {
        var act = () => Order.Create(
            "ord-1", "CZ-1", "user-1", "maker-1", null,
            "X", "x@y.cz", "+420",
            productPriceAmountMinor,
            shippingPriceAmountMinor,
            platformFeeAmountMinor,
            makerPayoutAmountMinor,
            totalAmountMinor,
            "CZK", 2100, ShippingMethod.PersonalPickup, null, "CZ");

        act.Should().Throw<ArgumentException>()
            .Which.ParamName.Should().Be(expectedParamName);
    }

    [Fact]
    public void Create_rejects_when_product_plus_shipping_does_not_equal_total()
    {
        var act = () => Order.Create(
            "ord-1", "CZ-1", "user-1", "maker-1", null,
            "X", "x@y.cz", "+420",
            productPriceAmountMinor: 100, shippingPriceAmountMinor: 50,
            platformFeeAmountMinor: 10, makerPayoutAmountMinor: 140,
            totalAmountMinor: 999, // wrong
            currency: "CZK", vatRateBp: 2100,
            shippingMethod: ShippingMethod.PersonalPickup,
            zasilkovnaPickupPointId: null, countryCode: "CZ");
        act.Should().Throw<ArgumentException>().WithMessage("*Total*Product*Shipping*");
    }

    [Fact]
    public void Create_rejects_when_payout_plus_fee_does_not_equal_gross()
    {
        var act = () => Order.Create(
            "ord-1", "CZ-1", "user-1", "maker-1", null,
            "X", "x@y.cz", "+420",
            productPriceAmountMinor: 100, shippingPriceAmountMinor: 50,
            platformFeeAmountMinor: 10, makerPayoutAmountMinor: 999, // doesn't sum to 150
            totalAmountMinor: 150,
            currency: "CZK", vatRateBp: 2100,
            shippingMethod: ShippingMethod.PersonalPickup,
            zasilkovnaPickupPointId: null, countryCode: "CZ");
        act.Should().Throw<ArgumentException>().WithMessage("*MakerPayout*PlatformFee*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("CZ")]
    [InlineData("CZKK")]
    public void Create_rejects_invalid_currency(string currency)
    {
        var act = () => Order.Create(
            "ord-1", "CZ-1", "user-1", "maker-1", null,
            "X", "x@y.cz", "+420", 100, 0, 10, 90, 100, currency, 2100,
            ShippingMethod.PersonalPickup, null, "CZ");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_rejects_zasilkovna_method_without_pickup_point_id()
    {
        var act = () => Order.Create(
            "ord-1", "CZ-1", "user-1", "maker-1", null,
            "X", "x@y.cz", "+420", 100, 0, 10, 90, 100, "CZK", 2100,
            ShippingMethod.ZasilkovnaPickupPoint, zasilkovnaPickupPointId: null,
            countryCode: "CZ");
        act.Should().Throw<ArgumentException>().WithMessage("*PickupPoint*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("C")]
    [InlineData("CZE")]
    public void Create_rejects_invalid_country_code(string country)
    {
        var act = () => Order.Create(
            "ord-1", "CZ-1", "user-1", "maker-1", null,
            "X", "x@y.cz", "+420", 100, 0, 10, 90, 100, "CZK", 2100,
            ShippingMethod.PersonalPickup, null, country);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Create_rejects_blank_id_or_order_number(string blank)
    {
        var byId = () => Order.Create(
            blank, "CZ-1", "u", "m", null, "X", "x@y.cz", "+420",
            100, 0, 10, 90, 100, "CZK", 2100,
            ShippingMethod.PersonalPickup, null, "CZ");
        byId.Should().Throw<ArgumentException>();

        var byNumber = () => Order.Create(
            "ord-1", blank, "u", "m", null, "X", "x@y.cz", "+420",
            100, 0, 10, 90, 100, "CZK", 2100,
            ShippingMethod.PersonalPickup, null, "CZ");
        byNumber.Should().Throw<ArgumentException>();
    }

    // === MarkAsPaid ===

    [Fact]
    public void MarkAsPaid_transitions_pending_to_paid_and_sets_ref()
    {
        var o = ValidDefaults();
        var result = o.MarkAsPaid(FixedClock(), "comgate-tx-1");

        result.IsSuccess.Should().BeTrue();
        o.State.Should().Be(OrderState.Paid);
        o.PaidAt.Should().Be(Now);
        o.PaymentProviderRef.Should().Be("comgate-tx-1");
    }

    [Fact]
    public void MarkAsPaid_from_non_pending_returns_invalid_transition()
    {
        var o = ValidDefaults();
        o.MarkAsPaid(FixedClock(), "comgate-tx-1");

        var second = o.MarkAsPaid(FixedClock(), "comgate-tx-2");

        second.IsSuccess.Should().BeFalse();
        second.Error!.Code.Should().Be(BusinessErrorMessage.OrderInvalidTransition);
        second.Error.Type.Should().Be(ErrorType.Conflict);
        // The original ref + state must be preserved.
        o.State.Should().Be(OrderState.Paid);
        o.PaymentProviderRef.Should().Be("comgate-tx-1");
    }

    [Fact]
    public void MarkAsPaid_with_matching_pre_set_PaymentProviderRef_succeeds()
    {
        // T-0066 reviewer L-5 — positive domain pin for the belt-and-braces
        // relaxation. The normal customer-pays flow is:
        //   T-0065 ReservePaymentSession(transId) → PaymentProviderRef = transId
        //   T-0066 webhook fires → MarkAsPaid(clock, SAME transId)
        // The matching-ref case must succeed; only a DIFFERENT ref trips
        // the set-once guard (covered by
        // MarkOrderPaidHandlerTests.Existing_PaymentProviderRef_set_to_different_ref_…
        // and by the existing OrderReservePaymentSessionTests cluster for
        // the reserve side).
        var o = ValidDefaults();
        o.ReservePaymentSession("comgate-tx-1", "https://payments.comgate.cz/r/01HX", FixedClock());

        var paid = o.MarkAsPaid(FixedClock(), "comgate-tx-1");

        paid.IsSuccess.Should().BeTrue();
        o.State.Should().Be(OrderState.Paid);
        o.PaymentProviderRef.Should().Be("comgate-tx-1");
        o.PaidAt.Should().Be(Now);
    }

    [Fact]
    public void MarkAsPaid_with_DIFFERENT_pre_set_PaymentProviderRef_trips_set_once()
    {
        // T-0066 reviewer L-5 — companion negative test. The relaxation
        // accepts the matching ref but the security property (no overwrite
        // of a different ref) MUST still hold.
        var o = ValidDefaults();
        o.ReservePaymentSession("comgate-tx-original", "https://payments.comgate.cz/r/01HX", FixedClock());

        var paid = o.MarkAsPaid(FixedClock(), "comgate-tx-different");

        paid.IsSuccess.Should().BeFalse();
        paid.Error!.Code.Should().Be(BusinessErrorMessage.OrderInvalidTransition);
        paid.Error.Type.Should().Be(ErrorType.Conflict);
        // The original ref + state (PendingPayment) must be preserved.
        o.State.Should().Be(OrderState.PendingPayment);
        o.PaymentProviderRef.Should().Be("comgate-tx-original");
    }

    // === T-0067 — extended MarkAsPaid signature ===

    [Fact]
    public void MarkAsPaid_with_PaymentMethod_persists_the_value()
    {
        var o = ValidDefaults();

        var result = o.MarkAsPaid(FixedClock(), "comgate-tx-1", paymentMethod: "CARD_CZ");

        result.IsSuccess.Should().BeTrue();
        o.PaymentMethod.Should().Be("CARD_CZ");
    }

    [Fact]
    public void MarkAsPaid_with_whitespace_PaymentMethod_normalises_to_null()
    {
        var o = ValidDefaults();

        var result = o.MarkAsPaid(FixedClock(), "comgate-tx-1", paymentMethod: "   ");

        result.IsSuccess.Should().BeTrue();
        o.PaymentMethod.Should().BeNull();
    }

    [Fact]
    public void MarkAsPaid_with_PaidAtOverride_uses_override_not_clock()
    {
        var o = ValidDefaults();
        var providerPaidAt = Now.AddSeconds(-30);

        var result = o.MarkAsPaid(FixedClock(), "comgate-tx-1",
            paymentMethod: null, paidAtOverride: providerPaidAt);

        result.IsSuccess.Should().BeTrue();
        o.PaidAt.Should().Be(providerPaidAt,
            "Q1 — provider-authoritative timestamp wins over clock.UtcNow when supplied");
    }

    [Fact]
    public void MarkAsPaid_with_null_PaidAtOverride_falls_back_to_clock()
    {
        var o = ValidDefaults();

        var result = o.MarkAsPaid(FixedClock(), "comgate-tx-1",
            paymentMethod: null, paidAtOverride: null);

        result.IsSuccess.Should().BeTrue();
        o.PaidAt.Should().Be(Now,
            "null override preserves T-0066 clock-wins semantic");
    }

    [Fact]
    public void MarkAsPaid_second_call_after_method_stamped_is_refused_by_state_guard()
    {
        // T-0067 reviewer M-1: honest naming — today's state graph makes the
        // belt-and-braces FIELD guard on PaymentMethod unreachable (the state
        // guard at MarkAsPaid trips first because the second call's State is
        // already Paid). What we CAN prove here is the original PaymentMethod
        // survives the rejected second call. If a future state-graph change
        // allows a Paid order to revisit PendingPayment, the field guard
        // becomes reachable and a dedicated test should land alongside.
        var o = ValidDefaults();
        o.MarkAsPaid(FixedClock(), "comgate-tx-1", paymentMethod: "CARD_CZ");

        var second = o.MarkAsPaid(FixedClock(), "comgate-tx-1", paymentMethod: "CARD_DE");

        second.IsSuccess.Should().BeFalse();
        second.Error!.Code.Should().Be(BusinessErrorMessage.OrderInvalidTransition);
        o.PaymentMethod.Should().Be("CARD_CZ",
            "the rejected second call must not overwrite the stamped method");
    }

    // === Accept ===

    [Fact]
    public void Accept_transitions_paid_to_accepted()
    {
        var o = ValidDefaults();
        o.MarkAsPaid(FixedClock(), "tx-1");

        var acceptedAt = Now.AddHours(2);
        var result = o.Accept(FixedClock(acceptedAt));

        result.IsSuccess.Should().BeTrue();
        o.State.Should().Be(OrderState.Accepted);
        o.AcceptedAt.Should().Be(acceptedAt);
    }

    [Fact]
    public void Accept_from_pending_returns_invalid_transition()
    {
        var o = ValidDefaults();
        var result = o.Accept(FixedClock());
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderInvalidTransition);
        o.State.Should().Be(OrderState.PendingPayment);
        o.AcceptedAt.Should().BeNull();
    }

    // === Ship ===

    [Fact]
    public void Ship_transitions_accepted_to_shipped_and_sets_auto_deliver_at_plus_seven_days()
    {
        var o = ValidDefaults();
        o.MarkAsPaid(FixedClock(), "tx-1");
        o.Accept(FixedClock(Now.AddHours(1)));

        var shippedAt = Now.AddDays(1);
        var result = o.Ship(FixedClock(shippedAt), "PKT-123", autoDeliverWindowDays: 7);

        result.IsSuccess.Should().BeTrue();
        o.State.Should().Be(OrderState.Shipped);
        o.ShippedAt.Should().Be(shippedAt);
        o.ShippingCarrierRef.Should().Be("PKT-123");
        o.AutoDeliverAt.Should().Be(shippedAt.AddDays(7));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(14)]
    public void Ship_auto_deliver_at_is_shipped_at_plus_window_days(int windowDays)
    {
        var o = ValidDefaults();
        o.MarkAsPaid(FixedClock(), "tx-1");
        o.Accept(FixedClock());

        var shippedAt = Now;
        o.Ship(FixedClock(shippedAt), "PKT-xxx", autoDeliverWindowDays: windowDays);

        o.AutoDeliverAt.Should().Be(shippedAt.AddDays(windowDays));
    }

    [Fact]
    public void Ship_allows_null_carrier_ref_for_personal_pickup_path()
    {
        var o = ValidDefaults();
        o.MarkAsPaid(FixedClock(), "tx-1");
        o.Accept(FixedClock());

        var result = o.Ship(FixedClock(), shippingCarrierRef: null, autoDeliverWindowDays: 7);

        result.IsSuccess.Should().BeTrue();
        o.ShippingCarrierRef.Should().BeNull();
    }

    [Fact]
    public void Ship_throws_when_window_days_not_positive()
    {
        var o = ValidDefaults();
        o.MarkAsPaid(FixedClock(), "tx-1");
        o.Accept(FixedClock());

        var act = () => o.Ship(FixedClock(), "PKT-1", autoDeliverWindowDays: 0);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Ship_from_paid_state_returns_invalid_transition()
    {
        var o = ValidDefaults();
        o.MarkAsPaid(FixedClock(), "tx-1");

        var result = o.Ship(FixedClock(), "PKT-1", 7);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderInvalidTransition);
        o.State.Should().Be(OrderState.Paid);
        o.ShippingCarrierRef.Should().BeNull();
        o.AutoDeliverAt.Should().BeNull();
    }

    [Fact]
    public void Ship_called_twice_returns_invalid_transition_from_state_guard()
    {
        // The state guard fires first on a duplicate Ship call (the order
        // is in Shipped state after the first call), so the surfaced error
        // is the generic state conflict — NOT the shippingCarrierRef
        // set-once conflict. The set-once guard on ShippingCarrierRef is a
        // belt-and-braces secondary check that mirrors MarkAsPaid's pattern
        // and is unreachable in the current state graph. T-0060 Copilot
        // review R2-2 — test promised in Scope > Tests but missing.
        var o = ValidDefaults();
        o.MarkAsPaid(FixedClock(), "tx-1");
        o.Accept(FixedClock(Now.AddHours(1)));
        o.Ship(FixedClock(Now.AddDays(1)), "PKT-123", autoDeliverWindowDays: 7);

        var second = o.Ship(FixedClock(Now.AddDays(2)), "PKT-456", autoDeliverWindowDays: 7);

        second.IsSuccess.Should().BeFalse();
        second.Error!.Code.Should().Be(BusinessErrorMessage.OrderInvalidTransition);
        second.Error.Type.Should().Be(ErrorType.Conflict);
        o.State.Should().Be(OrderState.Shipped);
        o.ShippingCarrierRef.Should().Be("PKT-123");
    }

    // === MarkAsDelivered ===

    [Fact]
    public void MarkAsDelivered_transitions_shipped_to_delivered()
    {
        var o = ValidDefaults();
        o.MarkAsPaid(FixedClock(), "tx-1");
        o.Accept(FixedClock());
        o.Ship(FixedClock(), "PKT-1", 7);

        var deliveredAt = Now.AddDays(2);
        var result = o.MarkAsDelivered(FixedClock(deliveredAt));

        result.IsSuccess.Should().BeTrue();
        o.State.Should().Be(OrderState.Delivered);
        o.DeliveredAt.Should().Be(deliveredAt);
    }

    [Fact]
    public void MarkAsDelivered_from_accepted_returns_invalid_transition()
    {
        var o = ValidDefaults();
        o.MarkAsPaid(FixedClock(), "tx-1");
        o.Accept(FixedClock());

        var result = o.MarkAsDelivered(FixedClock());
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderInvalidTransition);
    }

    // === Complete ===

    [Fact]
    public void Complete_transitions_delivered_to_completed()
    {
        var o = DeliveredOrder();
        var completedAt = Now.AddDays(8);

        var result = o.Complete(FixedClock(completedAt));

        result.IsSuccess.Should().BeTrue();
        o.State.Should().Be(OrderState.Completed);
        o.CompletedAt.Should().Be(completedAt);
    }

    [Fact]
    public void Complete_from_shipped_returns_invalid_transition()
    {
        var o = ValidDefaults();
        o.MarkAsPaid(FixedClock(), "tx-1");
        o.Accept(FixedClock());
        o.Ship(FixedClock(), "PKT-1", 7);

        var result = o.Complete(FixedClock());
        result.IsSuccess.Should().BeFalse();
    }

    // === Cancel ===

    [Theory]
    [InlineData(OrderState.PendingPayment)]
    [InlineData(OrderState.Paid)]
    [InlineData(OrderState.Accepted)]
    public void Cancel_allowed_from_pending_paid_or_accepted(OrderState from)
    {
        var o = OrderInState(from);

        var cancelledAt = Now.AddHours(3);
        var result = o.Cancel(FixedClock(cancelledAt));

        result.IsSuccess.Should().BeTrue();
        o.State.Should().Be(OrderState.Cancelled);
        o.CancelledAt.Should().Be(cancelledAt);
    }

    [Theory]
    [InlineData(OrderState.Shipped)]
    [InlineData(OrderState.Delivered)]
    [InlineData(OrderState.Completed)]
    public void Cancel_refused_after_shipped(OrderState from)
    {
        var o = OrderInState(from);
        var stateBefore = o.State;

        var result = o.Cancel(FixedClock());

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderInvalidTransition);
        o.State.Should().Be(stateBefore);
        o.CancelledAt.Should().BeNull();
    }

    // === Refund ===

    [Theory]
    [InlineData(OrderState.Paid)]
    [InlineData(OrderState.Accepted)]
    [InlineData(OrderState.Shipped)]
    [InlineData(OrderState.Delivered)]
    [InlineData(OrderState.Completed)]
    public void Refund_allowed_from_paid_through_completed(OrderState from)
    {
        var o = OrderInState(from);

        var result = o.Refund(FixedClock());

        result.IsSuccess.Should().BeTrue();
        o.State.Should().Be(OrderState.Refunded);
        o.RefundedAt.Should().Be(Now);
    }

    [Fact]
    public void Refund_refused_from_pending_payment()
    {
        var o = ValidDefaults();
        var result = o.Refund(FixedClock());
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderInvalidTransition);
        o.State.Should().Be(OrderState.PendingPayment);
    }

    // === OpenDispute ===

    [Theory]
    [InlineData(OrderState.Shipped)]
    [InlineData(OrderState.Delivered)]
    [InlineData(OrderState.Completed)]
    public void OpenDispute_allowed_from_shipped_through_completed(OrderState from)
    {
        var o = OrderInState(from);

        var result = o.OpenDispute(FixedClock());

        result.IsSuccess.Should().BeTrue();
        o.State.Should().Be(OrderState.Disputed);
        o.DisputedAt.Should().Be(Now);
    }

    [Theory]
    [InlineData(OrderState.PendingPayment)]
    [InlineData(OrderState.Paid)]
    [InlineData(OrderState.Accepted)]
    public void OpenDispute_refused_before_shipped(OrderState from)
    {
        var o = OrderInState(from);
        var result = o.OpenDispute(FixedClock());
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderInvalidTransition);
    }

    // === Null-guard arguments ===

    [Fact]
    public void Transition_methods_reject_null_clock()
    {
        var o = ValidDefaults();

        ((Action)(() => o.MarkAsPaid(null!, "tx-1"))).Should().Throw<ArgumentNullException>();
        ((Action)(() => o.Accept(null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => o.Ship(null!, "PKT", 7))).Should().Throw<ArgumentNullException>();
        ((Action)(() => o.MarkAsDelivered(null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => o.Complete(null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => o.Cancel(null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => o.Refund(null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => o.OpenDispute(null!))).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void MarkAsPaid_throws_on_blank_provider_ref()
    {
        var o = ValidDefaults();
        var act = () => o.MarkAsPaid(FixedClock(), "  ");
        act.Should().Throw<ArgumentException>();
    }

    // === Helpers ===

    private static Order DeliveredOrder()
    {
        var o = ValidDefaults();
        o.MarkAsPaid(FixedClock(), "tx-1");
        o.Accept(FixedClock());
        o.Ship(FixedClock(), "PKT-1", 7);
        o.MarkAsDelivered(FixedClock());
        return o;
    }

    /// <summary>
    /// Drive a fresh order through the happy path until it reaches
    /// <paramref name="target"/>. Centralises the state-machine plumbing
    /// so the matrix tests above can focus on the edge under test.
    /// </summary>
    private static Order OrderInState(OrderState target)
    {
        var o = ValidDefaults();
        if (target == OrderState.PendingPayment) return o;

        o.MarkAsPaid(FixedClock(), "tx-1");
        if (target == OrderState.Paid) return o;

        o.Accept(FixedClock());
        if (target == OrderState.Accepted) return o;

        o.Ship(FixedClock(), "PKT-1", 7);
        if (target == OrderState.Shipped) return o;

        o.MarkAsDelivered(FixedClock());
        if (target == OrderState.Delivered) return o;

        o.Complete(FixedClock());
        if (target == OrderState.Completed) return o;

        throw new ArgumentOutOfRangeException(
            nameof(target), target, "Helper only handles the linear happy-path states.");
    }
}
