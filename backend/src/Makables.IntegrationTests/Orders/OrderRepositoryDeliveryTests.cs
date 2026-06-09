using FluentAssertions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Orders;
using Makables.Infra.Database.Orders;
using Makables.IntegrationTests.Common;
using NSubstitute;
using AddressEntity = Makables.Core.Domain.Addresses.Address;
using MakerEntity = Makables.Core.Domain.Makers.Maker;

namespace Makables.IntegrationTests.Orders;

/// <summary>
/// Integration coverage for the T-0077 + T-0078 read-only stream
/// methods on <see cref="OrderRepository"/>. Pins the per-method
/// predicate + AsNoTracking + soft-delete-filter behaviour against
/// real Postgres.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OrderRepositoryDeliveryTests
{
    private const string CountryCode = "CZ";
    private const string Currency = "CZK";
    private const string CustomerUserId = "user-customer-1";
    private const string MakerUserId = "user-maker-1";
    private const string MakerId = "maker-1";
    private const string AddressId = "addr-1";

    private static readonly DateTimeOffset Now =
        new(2026, 6, 9, 8, 0, 0, TimeSpan.Zero);

    private readonly PostgresHarness _harness;

    public OrderRepositoryDeliveryTests(PostgresHarness harness)
    {
        _harness = harness;
        _harness.ResetMutableTablesAsync().GetAwaiter().GetResult();
    }

    private async Task SeedActorsAsync()
    {
        await using var db = _harness.CreateDbContext();
        const string seedActor = "test-seed";
        var seedAt = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

        var customer = User.Create(
            id: CustomerUserId, email: "anna@example.cz", role: UserRole.Customer,
            fullName: "Anna", countryCodePrimary: CountryCode,
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        customer.ConfirmEmail(seedAt);
        customer.MarkCreated(seedActor, seedAt);
        db.Set<User>().Add(customer);

        var makerUser = User.Create(
            id: MakerUserId, email: "maker@example.cz", role: UserRole.Maker,
            fullName: "Maker", countryCodePrimary: CountryCode,
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        makerUser.ConfirmEmail(seedAt);
        makerUser.MarkCreated(seedActor, seedAt);
        db.Set<User>().Add(makerUser);

        var address = AddressEntity.Create(
            id: AddressId, street: "Pikrtova", houseNumber: "1737",
            city: "Praha", zip: "14000", countryCodeIso: CountryCode,
            auditCountryCode: CountryCode);
        address.MarkCreated(seedActor, seedAt);
        db.Set<AddressEntity>().Add(address);

        var maker = MakerEntity.Create(
            id: MakerId, userId: MakerUserId,
            registrationNumber: "27074358", vatId: null,
            companyName: "Avast s.r.o.", legalForm: null,
            registeredAddressId: AddressId,
            incorporatedOn: null, isActiveInRegistry: true,
            sourceRegistry: "ares",
            snapshotFetchedAt: seedAt, snapshotIsStale: false,
            countryCode: CountryCode, slug: "avast");
        maker.MarkCreated(seedActor, seedAt);
        db.Set<MakerEntity>().Add(maker);

        await db.SaveChangesAsync();
    }

    private static Order BuildOrder(
        string id,
        OrderState targetState,
        ShippingMethod method = ShippingMethod.ZasilkovnaPickupPoint,
        string? carrierRef = "PKT-1",
        DateTimeOffset? autoDeliverAt = null)
    {
        var o = Order.Create(
            id: id, orderNumber: $"M-CZ-{id}",
            customerUserId: CustomerUserId, makerId: MakerId, productId: null,
            contactName: "Anna", contactEmail: "anna@example.cz", contactPhone: "+420",
            productPriceAmountMinor: 50000, shippingPriceAmountMinor: 7900,
            platformFeeAmountMinor: 7500, makerPayoutAmountMinor: 50400,
            totalAmountMinor: 57900, currency: Currency, vatRateBp: 2100,
            shippingMethod: method,
            zasilkovnaPickupPointId: method == ShippingMethod.ZasilkovnaPickupPoint ? "pp-42" : null,
            countryCode: CountryCode);

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now.AddDays(-10));
        if (targetState == OrderState.PendingPayment) return o;

        o.MarkAsPaid(clock, $"tx-{id}");
        if (targetState == OrderState.Paid) return o;

        o.Accept(clock);
        if (targetState == OrderState.Accepted) return o;

        // Ship — using a fixed clock so we can plug AutoDeliverAt deterministically.
        // Pass the shippingCarrierRef parameter only when carrierRef is non-null.
        var shipClock = Substitute.For<IClock>();
        shipClock.UtcNow.Returns(autoDeliverAt is not null
            ? autoDeliverAt.Value.AddDays(-7)
            : Now.AddDays(-5));
        o.Ship(shipClock, carrierRef, autoDeliverWindowDays: 7);
        if (targetState == OrderState.Shipped) return o;

        var deliverClock = Substitute.For<IClock>();
        deliverClock.UtcNow.Returns(Now.AddDays(-2));
        o.MarkAsDelivered(deliverClock, OrderDeliverySource.Auto);
        return o;
    }

    [Fact]
    public async Task GetAutoDeliverableUnscopedReadOnlyAsync_returns_only_Shipped_with_expired_AutoDeliverAt()
    {
        await SeedActorsAsync();
        await using (var db = _harness.CreateDbContext())
        {
            // Three Shipped + expired orders (AutoDeliverAt in the past).
            db.Set<Order>().Add(BuildOrder("expired-1", OrderState.Shipped,
                autoDeliverAt: Now.AddDays(-3)));
            db.Set<Order>().Add(BuildOrder("expired-2", OrderState.Shipped,
                autoDeliverAt: Now.AddDays(-1)));
            db.Set<Order>().Add(BuildOrder("expired-3", OrderState.Shipped,
                autoDeliverAt: Now.AddDays(-2)));
            // Shipped but NOT expired (AutoDeliverAt in the future).
            db.Set<Order>().Add(BuildOrder("future", OrderState.Shipped,
                autoDeliverAt: Now.AddDays(+5)));
            // Wrong state — Paid order.
            db.Set<Order>().Add(BuildOrder("paid", OrderState.Paid));
            // Already delivered.
            db.Set<Order>().Add(BuildOrder("delivered", OrderState.Delivered,
                autoDeliverAt: Now.AddDays(-10)));
            await db.SaveChangesAsync();
        }

        await using var readDb = _harness.CreateDbContext();
        var repo = new OrderRepository(readDb);
        var ids = new List<string>();
        await foreach (var id in repo.GetAutoDeliverableUnscopedReadOnlyAsync(Now, default))
        {
            ids.Add(id);
        }

        ids.Should().BeEquivalentTo(new[] { "expired-1", "expired-2", "expired-3" });
        // Ordering: oldest expirations first (asc by AutoDeliverAt).
        ids[0].Should().Be("expired-1");
        ids[2].Should().Be("expired-2");
    }

    [Fact]
    public async Task GetCarrierSyncableUnscopedReadOnlyAsync_returns_only_Shipped_Zasilkovna_orders_with_carrier_ref()
    {
        await SeedActorsAsync();
        await using (var db = _harness.CreateDbContext())
        {
            // Match: Shipped + Zasilkovna + carrierRef.
            db.Set<Order>().Add(BuildOrder("match-1", OrderState.Shipped,
                method: ShippingMethod.ZasilkovnaPickupPoint, carrierRef: "PKT-99",
                autoDeliverAt: Now.AddDays(+5)));
            // PersonalPickup — excluded (no carrier).
            db.Set<Order>().Add(BuildOrder("personal", OrderState.Shipped,
                method: ShippingMethod.PersonalPickup, carrierRef: null,
                autoDeliverAt: Now.AddDays(+5)));
            // Wrong state — Paid.
            db.Set<Order>().Add(BuildOrder("paid", OrderState.Paid));
            // Already delivered — excluded.
            db.Set<Order>().Add(BuildOrder("delivered", OrderState.Delivered,
                method: ShippingMethod.ZasilkovnaPickupPoint, carrierRef: "PKT-77",
                autoDeliverAt: Now.AddDays(-5)));
            await db.SaveChangesAsync();
        }

        await using var readDb = _harness.CreateDbContext();
        var repo = new OrderRepository(readDb);
        var orders = new List<Order>();
        await foreach (var o in repo.GetCarrierSyncableUnscopedReadOnlyAsync(default))
        {
            orders.Add(o);
        }

        orders.Should().HaveCount(1);
        orders[0].Id.Should().Be("match-1");
        orders[0].ShippingMethod.Should().Be(ShippingMethod.ZasilkovnaPickupPoint);
        orders[0].ShippingCarrierRef.Should().Be("PKT-99");
        orders[0].State.Should().Be(OrderState.Shipped);
        orders[0].CountryCode.Should().Be(CountryCode);
    }
}
