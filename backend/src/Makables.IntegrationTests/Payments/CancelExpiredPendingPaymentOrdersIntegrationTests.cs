using FluentAssertions;
using Makables.Core.AppServices.Features.Orders;
using Makables.Core.Domain.Categories;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Money;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Outbox;
using Makables.Core.Domain.Products;
using Makables.Infra.Database;
using Makables.IntegrationTests.Common;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using AddressEntity = Makables.Core.Domain.Addresses.Address;
using MakerEntity = Makables.Core.Domain.Makers.Maker;

namespace Makables.IntegrationTests.Payments;

/// <summary>
/// End-to-end test for the T-0083 cancel-expired-pending-payment path.
/// Mirrors <c>AutoDeliverOrdersIntegrationTests</c> verbatim — the
/// Function lives in <c>Makables.Functions</c> (not referenced here);
/// this test reproduces the Function body inline so the UoW pipeline +
/// state transition + outbox emission line up end-to-end against real
/// Postgres.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CancelExpiredPendingPaymentOrdersIntegrationTests : IAsyncLifetime
{
    private const string CountryCode = "CZ";
    private const string Currency = "CZK";
    private const string CustomerUserId = "user-customer-1";
    private const string MakerUserId = "user-maker-1";
    private const string MakerId = "maker-1";
    private const string AddressId = "addr-1";
    private const string CategoryId = "cat-1";
    private const string ProductId = "prod-1";

    private static readonly DateTimeOffset Now =
        new(2026, 6, 10, 2, 0, 0, TimeSpan.Zero);

    private readonly PostgresHarness _harness;
    private WebApplicationFactory<Makables.Web.Customer.Program> _factory = default!;

    public CancelExpiredPendingPaymentOrdersIntegrationTests(PostgresHarness harness)
    {
        _harness = harness;
    }

    public async Task InitializeAsync()
    {
        await _harness.ResetMutableTablesAsync();
        _factory = new WebApplicationFactory<Makables.Web.Customer.Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("IntegrationTest");
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:Postgres"] = _harness.ConnectionString,
                        ["Jwt:Issuer"] = "https://makables.test",
                        ["Jwt:SigningKeyBase64"] = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
                        ["SendGrid:ApiKey"] = "SG.integration-test-stub",
                        ["SendGrid:DefaultFromAddress"] = "no-reply@makables.test",
                        ["PublicAppUrls:WebBaseUrl"] = "https://makables.test",
                        ["Mapbox:AccessToken"] = "pk.integration-test-stub",
                        ["Ares:BaseUrl"] = "https://ares.integration-test.local",
                        ["Comgate:MerchantId"] = "12345",
                        ["Comgate:Secret"] = "integration-test-secret",
                        ["Comgate:BaseUrl"] = "https://payments.comgate.test",
                        ["Packeta:ApiKey"] = "integration-test-packeta-key",
                        ["Packeta:PublicWidgetKey"] = "integration-test-packeta-public-key",
                        ["Packeta:BaseUrl"] = "https://api.packeta.test",
                        ["Packeta:WidgetScriptUrl"] = "https://widget.packeta.test/v6/library.js",
                        ["Packeta:SenderLabel"] = "makables-test",
                        ["BlobStorage:ConnectionString"] = "UseDevelopmentStorage=true",
                        ["Cors:AllowedOrigins:customer:0"] = "https://customer.makables.test",
                        ["Cors:AllowedOrigins:maker:0"] = "https://maker.makables.test",
                        ["Cors:AllowedOrigins:admin:0"] = "https://admin.makables.test",
                        ["Cors:AllowedOrigins:public:0"] = "https://makables.test",
                    });
                });
                builder.ConfigureServices(services =>
                {
                    var dbContextDescriptor = services.SingleOrDefault(
                        d => d.ServiceType == typeof(DbContextOptions<MakablesDbContext>));
                    if (dbContextDescriptor is not null)
                    {
                        services.Remove(dbContextDescriptor);
                    }
                    services.AddDbContext<MakablesDbContext>(o =>
                        o.UseNpgsql(_harness.ConnectionString));
                });
            });
    }

    public Task DisposeAsync()
    {
        _factory?.Dispose();
        return Task.CompletedTask;
    }

    private async Task SeedExpiredPendingAndOneRecentAsync()
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

        var category = Category.Create(
            id: CategoryId, name: "3D tisk", slug: "3d-tisk",
            icon: null, description: null, sortOrder: 10, countryCode: CountryCode);
        category.MarkCreated(seedActor, seedAt);
        db.Set<Category>().Add(category);

        var product = Product.Create(
            id: ProductId, makerId: MakerId, categoryId: CategoryId,
            title: "Vase", description: null,
            price: new Money(50000, Currency),
            priceType: PriceType.Fixed, weightGrams: 300,
            countryCode: CountryCode);
        product.MarkCreated(seedActor, seedAt);
        db.Set<Product>().Add(product);

        // Two expired PendingPayment orders + one recent PendingPayment +
        // one Paid (excluded). Need to override CreatedAt directly because
        // Order.Create stamps it via the interceptor (which isn't wired in
        // the harness DbContext); use MarkCreated with the desired timestamp.
        db.Set<Order>().Add(BuildOrder("expired-1", Now.AddHours(-30), seedActor));
        db.Set<Order>().Add(BuildOrder("expired-2", Now.AddHours(-26), seedActor));
        db.Set<Order>().Add(BuildOrder("recent", Now.AddHours(-12), seedActor));

        // Paid order — past the 24h window but state != PendingPayment.
        var paid = BuildOrder("paid", Now.AddHours(-30), seedActor);
        var paidClock = Substitute.For<IClock>();
        paidClock.UtcNow.Returns(Now.AddHours(-20));
        paid.MarkAsPaid(paidClock, "tx-paid");
        db.Set<Order>().Add(paid);

        await db.SaveChangesAsync();
    }

    private static Order BuildOrder(string id, DateTimeOffset createdAt, string seedActor)
    {
        var o = Order.Create(
            id: id, orderNumber: $"M-CZ-{id}",
            customerUserId: CustomerUserId, makerId: MakerId, productId: ProductId,
            contactName: "Anna", contactEmail: "anna@example.cz", contactPhone: "+420 723 456 789",
            productPriceAmountMinor: 50000, shippingPriceAmountMinor: 7900,
            platformFeeAmountMinor: 7500, makerPayoutAmountMinor: 50400,
            totalAmountMinor: 57900, currency: Currency, vatRateBp: 2100,
            shippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
            zasilkovnaPickupPointId: "pp-42",
            countryCode: CountryCode);
        o.MarkCreated(seedActor, createdAt);
        return o;
    }

    [Fact]
    public async Task CancelExpiredPendingPaymentOrders_e2e_transitions_expired_orders_and_writes_outbox()
    {
        await SeedExpiredPendingAndOneRecentAsync();

        // Reproduce the CancelExpiredPendingPaymentOrdersFunction body inline.
        // Materialize the projection-only stream BEFORE the per-row mediator.Send
        // loop (Q-0008 MARS workaround per ADR 0020 + AutoDeliverOrders precedent).
        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var mediator = scope.ServiceProvider.GetRequiredService<ISender>();

        var orderIds = new List<string>();
        await foreach (var orderId in repo.GetExpiredPendingPaymentUnscopedReadOnlyAsync(Now, default))
        {
            orderIds.Add(orderId);
        }

        orderIds.Should().BeEquivalentTo(new[] { "expired-1", "expired-2" },
            "only PendingPayment orders older than 24h are in the projection");

        foreach (var orderId in orderIds)
        {
            await mediator.Send(new CancelExpiredOrder.Command(orderId), default);
        }

        await using var db = _harness.CreateDbContext();
        var cancelled = await db.Set<Order>().AsNoTracking()
            .Where(o => o.State == OrderState.Cancelled)
            .OrderBy(o => o.Id)
            .ToListAsync();
        cancelled.Should().HaveCount(2,
            "both expired PendingPayment orders transitioned to Cancelled");
        cancelled.Should().AllSatisfy(o =>
            o.CancellationSource.Should().Be(OrderCancellationSource.AutoExpiry));
        cancelled.Should().AllSatisfy(o => o.CancelledAt.Should().NotBeNull());

        var recent = await db.Set<Order>().AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == "recent");
        recent!.State.Should().Be(OrderState.PendingPayment,
            "the recent PendingPayment order is still inside the TTL window");

        var paid = await db.Set<Order>().AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == "paid");
        paid!.State.Should().Be(OrderState.Paid,
            "the Paid order is excluded from the projection regardless of age");

        var outboxRows = await db.Set<OutboxEvent>().AsNoTracking()
            .Where(e => e.EventType == OutboxEventTypes.OrderCancelledCustomerEmail)
            .ToListAsync();
        outboxRows.Should().HaveCount(2,
            "one OrderCancelledCustomerEmail event per transition");
    }
}
