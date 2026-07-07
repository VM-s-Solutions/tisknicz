using FluentAssertions;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.OrderMessages;
using Makables.Core.Domain.Orders;
using Makables.Infra.Database.OrderMessages;
using Makables.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;

namespace Makables.IntegrationTests.OrderMessages;

/// <summary>
/// Integration coverage for <see cref="OrderMessageRepository.HasMakerReplySinceAsync"/>
/// on real Postgres (via <see cref="PostgresHarness"/>) — the T-0145
/// 7-day-sweep "did the maker reply?" query. Runs against Postgres
/// (not the SQLite <c>TestDbHarness</c>) because the EF Core SQLite
/// provider cannot translate <c>DateTimeOffset</c> relational
/// comparisons (<c>&gt;</c>/<c>&lt;</c>) — the same reason
/// <c>GetAutoDeliverableUnscopedReadOnlyAsync</c> (T-0077) is pinned via
/// <c>AutoDeliverOrdersIntegrationTests</c> rather than a SQLite unit
/// test.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class HasMakerReplySinceIntegrationTests
{
    private const string OrderId = "ord-1";
    private const string OtherOrderId = "ord-other";
    private const string CustomerUserId = "user-customer-1";
    private const string MakerUserId = "user-maker-1";
    private const string MakerId = "maker-1";

    private static readonly DateTimeOffset DisputeCreatedAt =
        new(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);

    private readonly PostgresHarness _harness;

    public HasMakerReplySinceIntegrationTests(PostgresHarness harness)
    {
        _harness = harness;
        _harness.ResetMutableTablesAsync().GetAwaiter().GetResult();
    }

    private static Order BuildOrder(string id)
    {
        var order = Order.Create(
            id: id, orderNumber: $"M-CZ-{id}",
            customerUserId: CustomerUserId, makerId: MakerId, productId: "prod-1",
            contactName: "Jan", contactEmail: "jan@example.cz", contactPhone: "+420777",
            productPriceAmountMinor: 25000, shippingPriceAmountMinor: 7900,
            platformFeeAmountMinor: 3290, makerPayoutAmountMinor: 29610,
            totalAmountMinor: 32900, currency: "CZK", vatRateBp: 2100,
            shippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
            zasilkovnaPickupPointId: "branch-79", countryCode: "CZ");
        order.MarkCreated("seed", DisputeCreatedAt.AddDays(-10));
        return order;
    }

    private static User BuildUser(string id, string email)
    {
        var user = User.Create(
            id: id, email: email, role: UserRole.Maker,
            fullName: "Author", countryCodePrimary: "CZ",
            emailAlreadyConfirmed: true, confirmedAt: DisputeCreatedAt.AddDays(-30));
        user.MarkCreated("seed", DisputeCreatedAt.AddDays(-30));
        return user;
    }

    private static OrderMessage BuildMessage(
        string id, string orderId, OrderMessageAuthorRole role, string authorUserId, DateTimeOffset createdAt)
    {
        var message = OrderMessage.Create(id, orderId, role, authorUserId, "Zpráva.", "CZ");
        message.MarkCreated("seed", createdAt);
        return message;
    }

    [Fact]
    public async Task Maker_message_strictly_after_sinceUtc_is_detected()
    {
        await using var db = _harness.CreateDbContext();
        db.Set<Order>().Add(BuildOrder(OrderId));
        db.Set<User>().Add(BuildUser(MakerUserId, "maker@example.cz"));
        db.Set<OrderMessage>().Add(BuildMessage(
            "msg-1", OrderId, OrderMessageAuthorRole.Maker, MakerUserId,
            DisputeCreatedAt.AddDays(2)));
        await db.SaveChangesAsync();

        var sut = new OrderMessageRepository(db);
        var hasReply = await sut.HasMakerReplySinceAsync(OrderId, DisputeCreatedAt, default);

        hasReply.Should().BeTrue();
    }

    [Fact]
    public async Task Maker_message_at_or_before_sinceUtc_is_NOT_detected()
    {
        // Strictly-greater-than boundary (AC-6's anchor): a maker message
        // stamped exactly AT — or before — the dispute's CreatedAt must
        // not count as a reply; it predates the dispute.
        await using var db = _harness.CreateDbContext();
        db.Set<Order>().Add(BuildOrder(OrderId));
        db.Set<User>().Add(BuildUser(MakerUserId, "maker@example.cz"));
        db.Set<OrderMessage>().Add(BuildMessage(
            "msg-1", OrderId, OrderMessageAuthorRole.Maker, MakerUserId,
            DisputeCreatedAt)); // exactly AT, not after
        await db.SaveChangesAsync();

        var sut = new OrderMessageRepository(db);
        var hasReply = await sut.HasMakerReplySinceAsync(OrderId, DisputeCreatedAt, default);

        hasReply.Should().BeFalse();
    }

    [Fact]
    public async Task Customer_follow_up_message_does_NOT_count_as_a_maker_reply()
    {
        // AC-6 is about the MAKER's reply — a chatty customer follow-up
        // must not suppress the escalation.
        await using var db = _harness.CreateDbContext();
        db.Set<Order>().Add(BuildOrder(OrderId));
        db.Set<User>().Add(BuildUser(CustomerUserId, "cust@example.cz"));
        db.Set<OrderMessage>().Add(BuildMessage(
            "msg-1", OrderId, OrderMessageAuthorRole.Customer, CustomerUserId,
            DisputeCreatedAt.AddDays(2)));
        await db.SaveChangesAsync();

        var sut = new OrderMessageRepository(db);
        var hasReply = await sut.HasMakerReplySinceAsync(OrderId, DisputeCreatedAt, default);

        hasReply.Should().BeFalse();
    }

    [Fact]
    public async Task Maker_message_on_a_different_order_does_NOT_count()
    {
        await using var db = _harness.CreateDbContext();
        db.Set<Order>().Add(BuildOrder(OrderId));
        db.Set<Order>().Add(BuildOrder(OtherOrderId));
        db.Set<User>().Add(BuildUser(MakerUserId, "maker@example.cz"));
        db.Set<OrderMessage>().Add(BuildMessage(
            "msg-1", OtherOrderId, OrderMessageAuthorRole.Maker, MakerUserId,
            DisputeCreatedAt.AddDays(2)));
        await db.SaveChangesAsync();

        var sut = new OrderMessageRepository(db);
        var hasReply = await sut.HasMakerReplySinceAsync(OrderId, DisputeCreatedAt, default);

        hasReply.Should().BeFalse();
    }
}
