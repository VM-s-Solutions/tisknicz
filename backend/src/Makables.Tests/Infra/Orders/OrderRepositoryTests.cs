using FluentAssertions;
using Makables.Core.Domain.Orders;
using Makables.Infra.Database.Orders;
using Makables.Tests.Infra.Database;
using Microsoft.EntityFrameworkCore;

namespace Makables.Tests.Infra.Orders;

/// <summary>
/// DB round-trip tests for <see cref="OrderRepository"/> against the
/// SQLite <see cref="TestDbHarness"/>. Covers the scoping invariants
/// from ADR 0013 (<see cref="OrderRepository.ForCustomer"/> /
/// <see cref="OrderRepository.ForMaker"/> / <see cref="OrderRepository.Unscoped"/>),
/// the IDOR-shielded lookups, the global soft-delete filter, the
/// webhook-only <see cref="OrderRepository.GetByPaymentProviderRefAsync"/>
/// path, and the partial-unique-index enforcement at the DB layer.
/// </summary>
public class OrderRepositoryTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-03T10:00:00Z");

    private static Order BuildOrder(
        string id,
        string customerUserId,
        string makerId,
        string orderNumber)
        => Order.Create(
            id: id,
            orderNumber: orderNumber,
            customerUserId: customerUserId,
            makerId: makerId,
            productId: "prod-1",
            contactName: "Jan",
            contactEmail: "jan@example.cz",
            contactPhone: "+420777",
            productPriceAmountMinor: 25000,
            shippingPriceAmountMinor: 7900,
            platformFeeAmountMinor: 3290,
            makerPayoutAmountMinor: 29610,
            totalAmountMinor: 32900,
            currency: "CZK",
            vatRateBp: 2100,
            shippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
            zasilkovnaPickupPointId: "branch-79",
            countryCode: "CZ");

    [Fact]
    public async Task ForCustomer_returns_only_caller_orders()
    {
        using var h = TestDbHarness.Create();
        h.Db.Set<Order>().Add(BuildOrder("ord-a", "user-a", "maker-1", "CZ-A"));
        h.Db.Set<Order>().Add(BuildOrder("ord-b", "user-b", "maker-1", "CZ-B"));
        await h.Db.SaveChangesAsync(default);

        var sut = new OrderRepository(h.Db);
        var mine = await sut.ForCustomer("user-a").ToListAsync();

        mine.Should().ContainSingle().Which.Id.Should().Be("ord-a");
    }

    [Fact]
    public async Task ForCustomer_with_blank_user_returns_no_rows()
    {
        // Defensive: the repository never returns "all orders" for a
        // missing scoping id. A bug in the session provider must not
        // become a data leak.
        using var h = TestDbHarness.Create();
        h.Db.Set<Order>().Add(BuildOrder("ord-a", "user-a", "maker-1", "CZ-A"));
        await h.Db.SaveChangesAsync(default);

        var sut = new OrderRepository(h.Db);
        var none = await sut.ForCustomer(string.Empty).ToListAsync();

        none.Should().BeEmpty();
    }

    [Fact]
    public async Task ForMaker_returns_only_caller_maker_orders()
    {
        using var h = TestDbHarness.Create();
        h.Db.Set<Order>().Add(BuildOrder("ord-a", "user-1", "maker-a", "CZ-A"));
        h.Db.Set<Order>().Add(BuildOrder("ord-b", "user-2", "maker-b", "CZ-B"));
        await h.Db.SaveChangesAsync(default);

        var sut = new OrderRepository(h.Db);
        var mine = await sut.ForMaker("maker-a").ToListAsync();

        mine.Should().ContainSingle().Which.Id.Should().Be("ord-a");
    }

    [Fact]
    public async Task Unscoped_returns_every_active_order()
    {
        using var h = TestDbHarness.Create();
        h.Db.Set<Order>().Add(BuildOrder("ord-a", "user-1", "maker-a", "CZ-A"));
        h.Db.Set<Order>().Add(BuildOrder("ord-b", "user-2", "maker-b", "CZ-B"));
        await h.Db.SaveChangesAsync(default);

        var sut = new OrderRepository(h.Db);
        var all = await sut.Unscoped().ToListAsync();

        all.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetByIdForCustomerAsync_returns_null_for_cross_customer_id()
    {
        // IDOR shield: another customer's order looks like "not found".
        using var h = TestDbHarness.Create();
        h.Db.Set<Order>().Add(BuildOrder("ord-mine", "user-a", "maker-1", "CZ-A"));
        h.Db.Set<Order>().Add(BuildOrder("ord-theirs", "user-b", "maker-1", "CZ-B"));
        await h.Db.SaveChangesAsync(default);

        var sut = new OrderRepository(h.Db);

        (await sut.GetByIdForCustomerAsync("ord-mine", "user-a", default))!.Id.Should().Be("ord-mine");
        (await sut.GetByIdForCustomerAsync("ord-theirs", "user-a", default)).Should().BeNull();
    }

    [Fact]
    public async Task GetByIdForMakerAsync_returns_null_for_cross_maker_id()
    {
        using var h = TestDbHarness.Create();
        h.Db.Set<Order>().Add(BuildOrder("ord-mine", "user-1", "maker-a", "CZ-A"));
        h.Db.Set<Order>().Add(BuildOrder("ord-theirs", "user-2", "maker-b", "CZ-B"));
        await h.Db.SaveChangesAsync(default);

        var sut = new OrderRepository(h.Db);

        (await sut.GetByIdForMakerAsync("ord-mine", "maker-a", default))!.Id.Should().Be("ord-mine");
        (await sut.GetByIdForMakerAsync("ord-theirs", "maker-a", default)).Should().BeNull();
    }

    [Fact]
    public async Task Soft_deleted_orders_are_excluded_by_default_scoped_queries()
    {
        // Global Auditable filter applies — neither the scoped queryables
        // nor the Get-by-id helpers should surface a deactivated row.
        using var h = TestDbHarness.Create();
        var live = BuildOrder("ord-live", "user-a", "maker-1", "CZ-LIVE");
        var dead = BuildOrder("ord-dead", "user-a", "maker-1", "CZ-DEAD");
        h.Db.Set<Order>().Add(live);
        h.Db.Set<Order>().Add(dead);
        await h.Db.SaveChangesAsync(default);
        dead.MarkDeactivated("admin", Now);
        await h.Db.SaveChangesAsync(default);

        var sut = new OrderRepository(h.Db);

        (await sut.ForCustomer("user-a").ToListAsync())
            .Should().ContainSingle().Which.Id.Should().Be("ord-live");
        (await sut.ForMaker("maker-1").ToListAsync())
            .Should().ContainSingle().Which.Id.Should().Be("ord-live");
        (await sut.GetByIdForCustomerAsync("ord-dead", "user-a", default)).Should().BeNull();
        (await sut.GetByIdForMakerAsync("ord-dead", "maker-1", default)).Should().BeNull();
        (await sut.GetByIdUnscopedAsync("ord-dead", default)).Should().BeNull();
    }

    [Fact]
    public async Task GetByPaymentProviderRefAsync_finds_paid_order()
    {
        using var h = TestDbHarness.Create();
        var o = BuildOrder("ord-paid", "user-a", "maker-1", "CZ-PAID");
        o.MarkAsPaid(h.Clock, "comgate-tx-42");
        h.Db.Set<Order>().Add(o);
        await h.Db.SaveChangesAsync(default);

        var sut = new OrderRepository(h.Db);
        var found = await sut.GetByPaymentProviderRefAsync("comgate-tx-42", default);

        found.Should().NotBeNull();
        found!.Id.Should().Be("ord-paid");
    }

    [Fact]
    public async Task GetByPaymentProviderRefAsync_returns_null_for_unknown_ref()
    {
        using var h = TestDbHarness.Create();
        h.Db.Set<Order>().Add(BuildOrder("ord-a", "user-a", "maker-1", "CZ-A"));
        await h.Db.SaveChangesAsync(default);

        var sut = new OrderRepository(h.Db);
        (await sut.GetByPaymentProviderRefAsync("nope", default)).Should().BeNull();
        (await sut.GetByPaymentProviderRefAsync(string.Empty, default)).Should().BeNull();
    }

    [Fact]
    public async Task GetByPaymentProviderRefAsync_excludes_soft_deleted_rows()
    {
        // The global soft-delete filter applies — a webhook receiving a
        // duplicate ref against an admin-purged order sees "unknown",
        // which is the right idempotent-no-op behaviour for T-0066.
        using var h = TestDbHarness.Create();
        var o = BuildOrder("ord-purged", "user-a", "maker-1", "CZ-X");
        o.MarkAsPaid(h.Clock, "comgate-tx-99");
        h.Db.Set<Order>().Add(o);
        await h.Db.SaveChangesAsync(default);
        o.MarkDeactivated("admin", Now);
        await h.Db.SaveChangesAsync(default);

        var sut = new OrderRepository(h.Db);
        (await sut.GetByPaymentProviderRefAsync("comgate-tx-99", default)).Should().BeNull();
    }

    [Fact]
    public async Task AddAsync_tracks_order_for_insert()
    {
        using var h = TestDbHarness.Create();
        var sut = new OrderRepository(h.Db);
        await sut.AddAsync(BuildOrder("ord-a", "user-a", "maker-1", "CZ-A"), default);

        await h.Db.SaveChangesAsync(default);
        (await h.Db.Set<Order>().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Unique_order_number_index_rejects_duplicates_on_active_rows()
    {
        // Belt-and-braces: even if a future caller bypasses the
        // OrderNumberGenerator's monotonic sequence, the DB blocks a
        // duplicate-number insert. The translator maps the constraint
        // name to a typed conflict (Postgres only — SQLite raises a
        // plain DbUpdateException which is enough for this AC).
        using var h = TestDbHarness.Create();
        h.Db.Set<Order>().Add(BuildOrder("ord-a", "user-a", "maker-1", "CZ-DUP"));
        h.Db.Set<Order>().Add(BuildOrder("ord-b", "user-b", "maker-1", "CZ-DUP"));

        var act = async () => await h.Db.SaveChangesAsync(default);

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Unique_payment_provider_ref_index_rejects_duplicates()
    {
        // Webhook idempotency safety net. Two orders attempting to
        // attach the same comgate tx fail at the DB.
        using var h = TestDbHarness.Create();
        var a = BuildOrder("ord-a", "user-a", "maker-1", "CZ-A");
        var b = BuildOrder("ord-b", "user-b", "maker-1", "CZ-B");
        a.MarkAsPaid(h.Clock, "comgate-tx-same");
        b.MarkAsPaid(h.Clock, "comgate-tx-same");
        h.Db.Set<Order>().Add(a);
        h.Db.Set<Order>().Add(b);

        var act = async () => await h.Db.SaveChangesAsync(default);

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Unique_payment_provider_ref_allows_multiple_nulls()
    {
        // Partial index: `WHERE payment_provider_ref IS NOT NULL ...`.
        // PendingPayment orders all carry NULL refs and must coexist.
        using var h = TestDbHarness.Create();
        h.Db.Set<Order>().Add(BuildOrder("ord-a", "user-a", "maker-1", "CZ-A"));
        h.Db.Set<Order>().Add(BuildOrder("ord-b", "user-b", "maker-1", "CZ-B"));
        h.Db.Set<Order>().Add(BuildOrder("ord-c", "user-c", "maker-1", "CZ-C"));

        await h.Db.SaveChangesAsync(default);

        (await h.Db.Set<Order>().CountAsync()).Should().Be(3);
    }
}
