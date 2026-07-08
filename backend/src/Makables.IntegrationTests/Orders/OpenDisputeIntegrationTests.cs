using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Makables.Core.Domain.Categories;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Money;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Outbox;
using Makables.Core.Domain.Products;
using Makables.Infra.Common.Auth;
using Makables.Infra.Database;
using Makables.IntegrationTests.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using AddressEntity = Makables.Core.Domain.Addresses.Address;
using MakerEntity = Makables.Core.Domain.Makers.Maker;

namespace Makables.IntegrationTests.Orders;

/// <summary>
/// End-to-end coverage for the T-0106 customer dispute-open endpoint
/// (<c>POST /api/v1/orders/{orderId}/dispute</c> on the Customer host)
/// plus the two predicate-exclusion pins: a Disputed order drops out of
/// the auto-deliver sweep BY DEFINITION (AC-11 — no predicate change
/// shipped), and the T-0079 message thread stays open in Disputed
/// (AC-12 — the evidence channel).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OpenDisputeIntegrationTests : IAsyncLifetime
{
    private const string TestKeyBase64 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
    private const string TestIssuer = "https://makables.test";
    private const string CountryCode = "CZ";
    private const string Currency = "CZK";

    private const string CustomerUserId = "user-customer-1";
    private const string MakerUserId = "user-maker-1";
    private const string MakerId = "maker-1";
    private const string AddressId = "addr-1";
    private const string CategoryId = "cat-1";
    private const string ProductId = "prod-1";
    private const string OrderId = "ord-1";

    private static readonly DateTimeOffset Now =
        new(2026, 6, 12, 10, 0, 0, TimeSpan.Zero);

    private readonly PostgresHarness _harness;
    private WebApplicationFactory<Makables.Web.Customer.Program> _factory = default!;

    public OpenDisputeIntegrationTests(PostgresHarness harness)
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
                        ["Jwt:Issuer"] = TestIssuer,
                        ["Jwt:SigningKeyBase64"] = TestKeyBase64,
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

                    // Pin the app clock to the same fixed Now the seed graph
                    // uses so T-0145's 14-day dispute-open window is evaluated
                    // deterministically. Without this the handler runs against
                    // the real SystemClock, and the seeded DeliveredAt
                    // (Now - 2d) drifts past the window as wall-clock time
                    // advances — making the OK-path tests fail with 409.
                    var clockDescriptor = services.SingleOrDefault(
                        d => d.ServiceType == typeof(IClock));
                    if (clockDescriptor is not null)
                    {
                        services.Remove(clockDescriptor);
                    }
                    var appClock = Substitute.For<IClock>();
                    appClock.UtcNow.Returns(Now);
                    services.AddSingleton(appClock);
                });
            });
    }

    public Task DisposeAsync()
    {
        _factory?.Dispose();
        return Task.CompletedTask;
    }

    private async Task SeedOrderAsync(bool delivered, DateTimeOffset? autoDeliverPastShipDate = null)
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

        var order = Order.Create(
            id: OrderId, orderNumber: "M-CZ-20260042",
            customerUserId: CustomerUserId, makerId: MakerId, productId: ProductId,
            contactName: "Anna", contactEmail: "anna@example.cz", contactPhone: "+420 723 456 789",
            productPriceAmountMinor: 50000, shippingPriceAmountMinor: 7900,
            platformFeeAmountMinor: 7500, makerPayoutAmountMinor: 50400,
            totalAmountMinor: 57900, currency: Currency, vatRateBp: 2100,
            shippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
            zasilkovnaPickupPointId: "pp-42",
            countryCode: CountryCode);
        var clock = Substitute.For<IClock>();
        // When the test wants AutoDeliverAt in the past, ship far enough
        // back that ShippedAt + 7d < Now.
        clock.UtcNow.Returns(autoDeliverPastShipDate ?? Now.AddDays(-2));
        order.MarkAsPaid(clock, "tx-1");
        order.Accept(clock);
        order.Ship(clock, "PKT-1", 7);
        if (delivered)
        {
            order.MarkAsDelivered(clock, OrderDeliverySource.Carrier);
        }
        order.MarkCreated(seedActor, seedAt);
        db.Set<Order>().Add(order);

        await db.SaveChangesAsync();
    }

    private HttpClient CreateCustomerClient()
    {
        var issuer = new JwtIssuer(Options.Create(new JwtOptions
        {
            Issuer = TestIssuer,
            SigningKeyBase64 = TestKeyBase64,
            AccessTokenLifetime = TimeSpan.FromMinutes(15),
        }));
        var user = User.Create(
            id: CustomerUserId, email: "anna@example.cz", role: UserRole.Customer,
            fullName: "Anna", countryCodePrimary: CountryCode,
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        user.ConfirmEmail(new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero));
        var token = issuer.Issue(user, MakablesAudiences.Customer, DateTimeOffset.UtcNow).Token;

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task Customer_POST_dispute_e2e_flips_state_writes_dispute_row_and_outbox()
    {
        await SeedOrderAsync(delivered: true);
        using var client = CreateCustomerClient();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/orders/{OrderId}/dispute",
            new { category = "DamagedItem", description = "Váza dorazila rozbitá." });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = _harness.CreateDbContext();
        var order = await db.Set<Order>().AsNoTracking().FirstAsync(o => o.Id == OrderId);
        order.State.Should().Be(OrderState.Disputed);
        order.PreDisputeState.Should().Be(OrderState.Delivered);
        order.DisputedAt.Should().NotBeNull();

        var dispute = await db.Set<Dispute>().AsNoTracking().SingleAsync(d => d.OrderId == OrderId);
        dispute.Source.Should().Be(DisputeSource.Customer);
        dispute.Category.Should().Be(DisputeCategory.DamagedItem);
        dispute.Description.Should().Be("Váza dorazila rozbitá.");
        dispute.ResolvedAt.Should().BeNull("a fresh dispute is OPEN");

        var outboxRows = await db.Set<OutboxEvent>().AsNoTracking()
            .Where(e => e.AggregateId == OrderId)
            .ToListAsync();
        outboxRows.Should().HaveCount(1);
        outboxRows[0].EventType.Should().Be(OutboxEventTypes.OrderDisputedAdminEmail);
    }

    [Fact]
    public async Task Disputed_order_not_claimed_by_auto_deliver_or_carrier_sweeps()
    {
        // Shipped order whose AutoDeliverAt is already in the past — the
        // sweep WOULD claim it. Opening a dispute flips State so it drops
        // out of BOTH State == Shipped predicates by definition (AC-11 —
        // pinned by test, not by dead predicate code).
        await SeedOrderAsync(delivered: false, autoDeliverPastShipDate: Now.AddDays(-30));
        using var client = CreateCustomerClient();

        using (var scope = _factory.Services.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
            var preDispute = new List<string>();
            await foreach (var id in repo.GetAutoDeliverableUnscopedReadOnlyAsync(Now, default))
            {
                preDispute.Add(id);
            }
            preDispute.Should().Contain(OrderId, "sanity: the sweep claims the order BEFORE the dispute");
        }

        var response = await client.PostAsJsonAsync(
            $"/api/v1/orders/{OrderId}/dispute",
            new { category = "NotDelivered", description = "Balík se ztratil." });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = _factory.Services.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();

            var autoDeliverable = new List<string>();
            await foreach (var id in repo.GetAutoDeliverableUnscopedReadOnlyAsync(Now, default))
            {
                autoDeliverable.Add(id);
            }
            autoDeliverable.Should().NotContain(OrderId,
                "State == Shipped naturally excludes Disputed");

            var carrierSyncable = new List<string>();
            await foreach (var o in repo.GetCarrierSyncableUnscopedReadOnlyAsync(default))
            {
                carrierSyncable.Add(o.Id);
            }
            carrierSyncable.Should().NotContain(OrderId,
                "the carrier sweep likewise stops yielding the disputed shipment");
        }
    }

    [Fact]
    public async Task Message_post_on_disputed_order_succeeds_as_the_evidence_channel()
    {
        // AC-12: PendingPayment remains the ONLY state that blocks posting
        // (T-0079 ruling) — the thread is the dispute evidence channel.
        await SeedOrderAsync(delivered: true);
        using var client = CreateCustomerClient();

        var dispute = await client.PostAsJsonAsync(
            $"/api/v1/orders/{OrderId}/dispute",
            new { category = "NotAsDescribed", description = "Jiná barva než na fotce." });
        dispute.StatusCode.Should().Be(HttpStatusCode.OK);

        var message = await client.PostAsJsonAsync(
            $"/api/v1/orders/{OrderId}/messages",
            new { body = "Posílám fotky rozdílu barev jako důkaz." });

        message.StatusCode.Should().Be(HttpStatusCode.OK,
            "the message thread stays open in Disputed (evidence channel)");
    }
}
