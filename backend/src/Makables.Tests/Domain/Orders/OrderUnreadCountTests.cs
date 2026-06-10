using FluentAssertions;
using Makables.Core.Domain.OrderMessages;
using Makables.Core.Domain.Orders;

namespace Makables.Tests.Domain.Orders;

/// <summary>
/// T-0079 pure-logic surface: <see cref="Order.IncrementUnreadFor"/> +
/// <see cref="Order.ResetUnreadFor"/>. Both are clamped domain methods —
/// no infra dependency, no DB. Tested first per docs/process/tdd-policy.md.
///
/// <para>
/// Defensive clamp invariants:
/// <list type="bullet">
///   <item><description>Increment clamps at <see cref="int.MaxValue"/>: a
///     runaway producer can never overflow into negative territory.</description></item>
///   <item><description>Reset clamps at zero: idempotent — repeated calls
///     keep the counter at zero, not below.</description></item>
/// </list>
/// </para>
/// </summary>
public class OrderUnreadCountTests
{
    private static Order ValidOrder() => Order.Create(
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

    [Fact]
    public void IncrementUnreadFor_Maker_increments_maker_counter_by_one()
    {
        var o = ValidOrder();

        // Author = Customer → the recipient is the Maker; bump maker counter.
        o.IncrementUnreadFor(OrderMessageAuthorRole.Customer);

        o.MakerUnreadMessageCount.Should().Be(1);
        o.CustomerUnreadMessageCount.Should().Be(0);
    }

    [Fact]
    public void IncrementUnreadFor_Customer_increments_customer_counter_by_one()
    {
        var o = ValidOrder();

        // Author = Maker → recipient is the Customer; bump customer counter.
        o.IncrementUnreadFor(OrderMessageAuthorRole.Maker);

        o.CustomerUnreadMessageCount.Should().Be(1);
        o.MakerUnreadMessageCount.Should().Be(0);
    }

    [Fact]
    public void IncrementUnreadFor_clamps_at_MaxInt_for_maker()
    {
        var o = ValidOrder();
        // Use the seed escape hatch to push the counter to int.MaxValue
        // — we can't reach this via repeated public increments inside a
        // test budget, so the clamp is the only safety net we can prove
        // at MaxValue. Reflection is the lightest tool for a domain
        // invariant that defends against accidental overflow.
        typeof(Order)
            .GetProperty(nameof(Order.MakerUnreadMessageCount))!
            .GetSetMethod(nonPublic: true)!
            .Invoke(o, new object[] { int.MaxValue });

        o.IncrementUnreadFor(OrderMessageAuthorRole.Customer);

        o.MakerUnreadMessageCount.Should().Be(int.MaxValue);
    }

    [Fact]
    public void ResetUnreadFor_Customer_zeroes_customer_counter_only()
    {
        var o = ValidOrder();
        o.IncrementUnreadFor(OrderMessageAuthorRole.Maker);  // customer = 1
        o.IncrementUnreadFor(OrderMessageAuthorRole.Customer);  // maker = 1
        o.IncrementUnreadFor(OrderMessageAuthorRole.Customer);  // maker = 2

        // Reader = Customer: clear the customer counter; maker counter intact.
        o.ResetUnreadFor(OrderMessageAuthorRole.Customer);

        o.CustomerUnreadMessageCount.Should().Be(0);
        o.MakerUnreadMessageCount.Should().Be(2);
    }

    [Fact]
    public void ResetUnreadFor_Maker_zeroes_maker_counter_only()
    {
        var o = ValidOrder();
        o.IncrementUnreadFor(OrderMessageAuthorRole.Customer);  // maker = 1
        o.IncrementUnreadFor(OrderMessageAuthorRole.Maker);  // customer = 1

        // Reader = Maker: clear the maker counter; customer counter intact.
        o.ResetUnreadFor(OrderMessageAuthorRole.Maker);

        o.MakerUnreadMessageCount.Should().Be(0);
        o.CustomerUnreadMessageCount.Should().Be(1);
    }

    [Fact]
    public void ResetUnreadFor_is_idempotent_at_zero()
    {
        var o = ValidOrder();

        o.ResetUnreadFor(OrderMessageAuthorRole.Customer);
        o.ResetUnreadFor(OrderMessageAuthorRole.Customer);

        o.CustomerUnreadMessageCount.Should().Be(0);
        // No exception; counter never goes negative.
    }
}
