using FluentAssertions;
using Makables.Core.Domain.OrderMessages;
using Makables.Core.Domain.Orders;

namespace Makables.Tests.Domain.Orders;

/// <summary>
/// T-0079 pure-logic surface: 5-minute notification debounce predicates
/// on <see cref="Order"/>. Tested first per docs/process/tdd-policy.md
/// — no infra dependency, no DB, just predicate + pointer update.
///
/// <para>
/// Semantics (locked decision A.2):
/// <list type="number">
///   <item><description><c>ShouldEmitNotificationFor</c> returns true when
///     the recipient's pointer is null OR strictly older than
///     <see cref="Order.NotificationDebounceWindow"/> (5 min).</description></item>
///   <item><description><c>MarkNotificationEmittedFor</c> sets the pointer to
///     the supplied <c>now</c>.</description></item>
///   <item><description><c>ClearPendingNotificationFor</c> sets the pointer
///     back to null so the next post fires immediately (called from
///     <c>MarkAsRead</c> handlers).</description></item>
/// </list>
/// </para>
/// </summary>
public class OrderNotificationDebounceTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-06-09T10:00:00Z");

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
    public void NotificationDebounceWindow_is_5_minutes()
    {
        // Pin the constant so an accidental refactor changing the window
        // surfaces here, not via flaky integration timing.
        Order.NotificationDebounceWindow.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void ShouldEmit_returns_true_when_pointer_null_for_customer()
    {
        var o = ValidOrder();
        // Fresh order — pointer null.
        o.ShouldEmitNotificationFor(OrderMessageAuthorRole.Customer, Now)
            .Should().BeTrue();
    }

    [Fact]
    public void ShouldEmit_returns_true_when_pointer_null_for_maker()
    {
        var o = ValidOrder();
        o.ShouldEmitNotificationFor(OrderMessageAuthorRole.Maker, Now)
            .Should().BeTrue();
    }

    [Fact]
    public void ShouldEmit_returns_false_when_pointer_within_window()
    {
        var o = ValidOrder();
        o.MarkNotificationEmittedFor(OrderMessageAuthorRole.Maker, Now);

        // 3 min later — still inside the 5-min window. Debounce holds.
        var threeMinLater = Now.AddMinutes(3);
        o.ShouldEmitNotificationFor(OrderMessageAuthorRole.Maker, threeMinLater)
            .Should().BeFalse();
    }

    [Fact]
    public void ShouldEmit_returns_true_when_pointer_older_than_window()
    {
        var o = ValidOrder();
        o.MarkNotificationEmittedFor(OrderMessageAuthorRole.Maker, Now);

        // 6 min later — past the 5-min window. Predicate emits again.
        var sixMinLater = Now.AddMinutes(6);
        o.ShouldEmitNotificationFor(OrderMessageAuthorRole.Maker, sixMinLater)
            .Should().BeTrue();
    }

    [Fact]
    public void ShouldEmit_returns_false_at_exactly_5_minutes()
    {
        // Boundary: pointer + 5 min == now. The window is open-ended on the
        // "older than" side; equal is still within the window.
        var o = ValidOrder();
        o.MarkNotificationEmittedFor(OrderMessageAuthorRole.Maker, Now);

        var fiveMinLater = Now.AddMinutes(5);
        o.ShouldEmitNotificationFor(OrderMessageAuthorRole.Maker, fiveMinLater)
            .Should().BeFalse();
    }

    [Fact]
    public void MarkNotificationEmittedFor_Customer_sets_customer_pointer()
    {
        var o = ValidOrder();

        o.MarkNotificationEmittedFor(OrderMessageAuthorRole.Customer, Now);

        // Author=Customer → recipient=Maker → MakerPendingNotificationEmailAt.
        o.MakerPendingNotificationEmailAt.Should().Be(Now);
        o.CustomerPendingNotificationEmailAt.Should().BeNull();
    }

    [Fact]
    public void MarkNotificationEmittedFor_Maker_sets_customer_pointer()
    {
        var o = ValidOrder();

        o.MarkNotificationEmittedFor(OrderMessageAuthorRole.Maker, Now);

        // Author=Maker → recipient=Customer → CustomerPendingNotificationEmailAt.
        o.CustomerPendingNotificationEmailAt.Should().Be(Now);
        o.MakerPendingNotificationEmailAt.Should().BeNull();
    }

    [Fact]
    public void ClearPendingNotificationFor_Customer_clears_customer_pointer()
    {
        var o = ValidOrder();
        o.MarkNotificationEmittedFor(OrderMessageAuthorRole.Maker, Now);
        o.CustomerPendingNotificationEmailAt.Should().Be(Now);

        // Reader=Customer reads the thread → clear the customer pointer
        // so the next maker-authored post fires immediately, not silenced
        // by a stale window.
        o.ClearPendingNotificationFor(OrderMessageAuthorRole.Customer);

        o.CustomerPendingNotificationEmailAt.Should().BeNull();
    }
}
