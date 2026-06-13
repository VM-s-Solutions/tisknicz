using FluentAssertions;
using Makables.Core.Domain.Orders;

namespace Makables.Tests.Domain.Orders;

/// <summary>
/// T-0101 red-first pins for <see cref="Order.AssignToPayoutBatch"/>.
/// The domain method owns the set-once invariant ONLY (lock A.4 +
/// Alternatives Option E): it does NOT assert <c>State == Delivered</c> —
/// that eligibility lives in the T-0102a claim predicate
/// (<c>PayoutEligibility.Classify</c>) as the single source of truth.
/// Claiming twice (same or different id) throws
/// <see cref="InvalidOperationException"/>; a blank id throws
/// <see cref="ArgumentException"/>.
/// </summary>
public class OrderAssignToPayoutBatchTests
{
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

    [Fact]
    public void AssignToPayoutBatch_sets_batch_id_once()
    {
        var order = ValidDefaults();
        order.PayoutBatchId.Should().BeNull();

        order.AssignToPayoutBatch("pb-1");

        order.PayoutBatchId.Should().Be("pb-1");
    }

    [Fact]
    public void AssignToPayoutBatch_throws_when_already_claimed_same_id()
    {
        var order = ValidDefaults();
        order.AssignToPayoutBatch("pb-1");

        var act = () => order.AssignToPayoutBatch("pb-1");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AssignToPayoutBatch_throws_when_already_claimed_different_id()
    {
        var order = ValidDefaults();
        order.AssignToPayoutBatch("pb-1");

        var act = () => order.AssignToPayoutBatch("pb-2");

        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AssignToPayoutBatch_throws_on_blank_id(string batchId)
    {
        var order = ValidDefaults();

        var act = () => order.AssignToPayoutBatch(batchId);

        act.Should().Throw<ArgumentException>();
    }
}
