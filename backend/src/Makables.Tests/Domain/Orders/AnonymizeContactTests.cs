using FluentAssertions;
using Makables.Core.Domain.Orders;

namespace Makables.Tests.Domain.Orders;

/// <summary>
/// Pure-logic transform: <see cref="Order.AnonymizeContact"/> scrubs the
/// three contact-snapshot columns to the <c>"Anonymized"</c> sentinel and
/// touches NOTHING else (pricing, state, timestamps). Written red-first per
/// the TDD policy (T-0110, locked Q-A erasure matrix — the order itself is
/// a retained commercial record; only the PII snapshot is erased).
/// </summary>
public sealed class AnonymizeContactTests
{
    private static Order BuildPaidOrder() =>
        Order.Create(
            id: "ord-1",
            orderNumber: "M-CZ-20260042",
            customerUserId: "user-1",
            makerId: "maker-1",
            productId: "prod-1",
            contactName: "Anna Nováková",
            contactEmail: "anna@example.cz",
            contactPhone: "+420 723 456 789",
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
    public void AnonymizeContact_sets_all_three_snapshot_columns_and_touches_nothing_else()
    {
        var order = BuildPaidOrder();
        var stateBefore = order.State;
        var totalBefore = order.TotalAmountMinor;
        var currencyBefore = order.Currency;
        var makerBefore = order.MakerId;

        order.AnonymizeContact();

        order.ContactName.Should().Be("Anonymized");
        order.ContactEmail.Should().Be("Anonymized");
        order.ContactPhone.Should().Be("Anonymized");

        // No pricing / state / linkage column moved.
        order.State.Should().Be(stateBefore);
        order.TotalAmountMinor.Should().Be(totalBefore);
        order.Currency.Should().Be(currencyBefore);
        order.MakerId.Should().Be(makerBefore);
    }

    [Fact]
    public void AnonymizeContact_is_idempotent()
    {
        var order = BuildPaidOrder();

        order.AnonymizeContact();
        var act = () => order.AnonymizeContact();

        act.Should().NotThrow();
        order.ContactEmail.Should().Be("Anonymized");
    }
}
