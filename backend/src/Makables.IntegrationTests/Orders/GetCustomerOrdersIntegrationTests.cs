using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Makables.Core.AppServices.Features.Orders;
using Makables.Core.Domain.Categories;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Money;
using Makables.Core.Domain.OrderMessages;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Orders.Queries;
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
using AddressEntity = Makables.Core.Domain.Addresses.Address;
using MakerEntity = Makables.Core.Domain.Makers.Maker;

namespace Makables.IntegrationTests.Orders;

/// <summary>
/// End-to-end coverage for T-0080 <c>GET /api/v1/customer/orders</c>.
/// Pins:
/// - paged read happy path returns rows for the requesting customer only,
/// - cross-tenant probe returns empty page (no 404 — predicate IS the IDOR shield),
/// - pagination window correctness against a seeded multi-row fixture.
/// Real Postgres via <see cref="PostgresHarness"/>.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class GetCustomerOrdersIntegrationTests : IAsyncLifetime
{
    private const string TestKeyBase64 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
    private const string TestIssuer = "https://makables.test";
    private const string CountryCode = "CZ";
    private const string Currency = "CZK";

    private const string CustomerAUserId = "user-customer-a";
    private const string CustomerBUserId = "user-customer-b";
    private const string MakerUserId = "user-maker-1";
    private const string MakerId = "maker-1";
    private const string AddressId = "addr-1";
    private const string CategoryId = "cat-1";
    private const string ProductId = "prod-1";

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly PostgresHarness _harness;
    private WebApplicationFactory<Makables.Web.Customer.Program> _factory = default!;

    public GetCustomerOrdersIntegrationTests(PostgresHarness harness)
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
                });
            });
    }

    public Task DisposeAsync()
    {
        _factory?.Dispose();
        return Task.CompletedTask;
    }

    private string IssueCustomerToken(string userId, string email)
    {
        var issuer = new JwtIssuer(Options.Create(new JwtOptions
        {
            Issuer = TestIssuer,
            SigningKeyBase64 = TestKeyBase64,
            AccessTokenLifetime = TimeSpan.FromMinutes(15),
        }));
        var user = User.Create(
            id: userId, email: email, role: UserRole.Customer,
            fullName: "Customer", countryCodePrimary: CountryCode,
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        user.ConfirmEmail(new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero));
        return issuer.Issue(user, MakablesAudiences.Customer, DateTimeOffset.UtcNow).Token;
    }

    private async Task SeedCommonAsync(MakablesDbContext db, string seedActor, DateTimeOffset seedAt)
    {
        var customerA = User.Create(
            id: CustomerAUserId, email: "a@example.cz", role: UserRole.Customer,
            fullName: "Customer A", countryCodePrimary: CountryCode,
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        customerA.ConfirmEmail(seedAt);
        customerA.MarkCreated(seedActor, seedAt);
        db.Set<User>().Add(customerA);

        var customerB = User.Create(
            id: CustomerBUserId, email: "b@example.cz", role: UserRole.Customer,
            fullName: "Customer B", countryCodePrimary: CountryCode,
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        customerB.ConfirmEmail(seedAt);
        customerB.MarkCreated(seedActor, seedAt);
        db.Set<User>().Add(customerB);

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
        maker.MarkVerified();
        maker.UpdateProfile(bio: null, bankAccount: null, personalPickupEnabled: true, pickupNote: null);
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

        await db.SaveChangesAsync();
    }

    private static Order BuildOrder(string id, string customerUserId, long total,
        DateTimeOffset createdAt, string seedActor, OrderState? targetState = null)
    {
        var o = Order.Create(
            id: id, orderNumber: $"M-CZ-2026{id}",
            customerUserId: customerUserId, makerId: MakerId, productId: ProductId,
            contactName: "Test", contactEmail: "x@y.cz", contactPhone: "+420 723 456 789",
            productPriceAmountMinor: total - 5000, shippingPriceAmountMinor: 5000,
            platformFeeAmountMinor: 7500, makerPayoutAmountMinor: total - 7500,
            totalAmountMinor: total, currency: Currency, vatRateBp: 2100,
            shippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
            zasilkovnaPickupPointId: "pp-42",
            countryCode: CountryCode);
        // Drive the state machine to the target state when requested. The
        // PostgresHarness doesn't register the AuditableSaveChangesInterceptor;
        // tests must explicitly stamp audit fields below.
        if (targetState is not null and not OrderState.PendingPayment)
        {
            var clock = new FixedClock(createdAt);
            o.MarkAsPaid(clock, $"tx-{id}");
            if (targetState != OrderState.Paid)
            {
                o.Accept(clock);
            }
        }
        o.MarkCreated(seedActor, createdAt);
        return o;
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    // === Tests ===

    [Fact]
    public async Task GET_returns_paged_orders_for_authenticated_customer()
    {
        var seedActor = "test-seed";
        var seedAt = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

        await using (var db = _harness.CreateDbContext())
        {
            await SeedCommonAsync(db, seedActor, seedAt);

            // Customer A gets 3 orders; Customer B gets 2.
            db.Set<Order>().Add(BuildOrder("o-a-1", CustomerAUserId, 30000,
                new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero), seedActor));
            db.Set<Order>().Add(BuildOrder("o-a-2", CustomerAUserId, 50000,
                new DateTimeOffset(2026, 5, 5, 9, 0, 0, TimeSpan.Zero), seedActor));
            db.Set<Order>().Add(BuildOrder("o-a-3", CustomerAUserId, 70000,
                new DateTimeOffset(2026, 5, 10, 9, 0, 0, TimeSpan.Zero), seedActor));
            db.Set<Order>().Add(BuildOrder("o-b-1", CustomerBUserId, 40000,
                new DateTimeOffset(2026, 5, 2, 9, 0, 0, TimeSpan.Zero), seedActor));
            db.Set<Order>().Add(BuildOrder("o-b-2", CustomerBUserId, 60000,
                new DateTimeOffset(2026, 5, 7, 9, 0, 0, TimeSpan.Zero), seedActor));

            await db.SaveChangesAsync();
        }

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", IssueCustomerToken(CustomerAUserId, "a@example.cz"));

        var response = await client.GetAsync("/api/v1/orders?page=1&pageSize=20");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<GetCustomerOrders.GetCustomerOrdersResponse>(JsonOpts);
        body.Should().NotBeNull();
        body!.Orders.TotalCount.Should().Be(3);
        body.Orders.Items.Should().HaveCount(3);
        body.Orders.Items.Should().OnlyContain(i =>
            i.OrderId == "o-a-1" || i.OrderId == "o-a-2" || i.OrderId == "o-a-3");
        // Default sort = CreatedAtDesc → newest first.
        body.Orders.Items[0].OrderId.Should().Be("o-a-3");
        // Denormalized MakerName populated; ProductTitle non-null when ProductId set.
        body.Orders.Items[0].MakerName.Should().Be("Avast s.r.o.");
        body.Orders.Items[0].ProductTitle.Should().Be("Vase");
    }

    [Fact]
    public async Task Cross_tenant_isolation_returns_zero_results_for_other_customers_orders()
    {
        var seedActor = "test-seed";
        var seedAt = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

        await using (var db = _harness.CreateDbContext())
        {
            await SeedCommonAsync(db, seedActor, seedAt);

            // 5 orders for Customer B, 0 for Customer A.
            for (var i = 1; i <= 5; i++)
            {
                db.Set<Order>().Add(BuildOrder($"o-b-{i}", CustomerBUserId, 10000 * i,
                    new DateTimeOffset(2026, 5, i, 9, 0, 0, TimeSpan.Zero), seedActor));
            }
            await db.SaveChangesAsync();
        }

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", IssueCustomerToken(CustomerAUserId, "a@example.cz"));

        var response = await client.GetAsync("/api/v1/orders?page=1&pageSize=20");

        // IDOR shield → empty page, NOT 404.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<GetCustomerOrders.GetCustomerOrdersResponse>(JsonOpts);
        body!.Orders.TotalCount.Should().Be(0);
        body.Orders.Items.Should().BeEmpty();
    }

    // T-0089: customer-side mirror of GetMakerOrdersIntegrationTests.
    // GET_orders_UnreadMessageCount_returns_denormalized_value — the
    // customer list projection reads the denormalized
    // orders.customer_unread_message_count column (T-0079/T-0080). A
    // bumped counter flows through to the wire DTO; orders without
    // messages surface 0 (NOT null — the customer contract is
    // non-nullable int).
    [Fact]
    public async Task GET_orders_UnreadMessageCount_returns_denormalized_value()
    {
        var seedActor = "test-seed";
        var seedAt = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

        await using (var db = _harness.CreateDbContext())
        {
            await SeedCommonAsync(db, seedActor, seedAt);

            db.Set<Order>().Add(BuildOrder("o-a-1", CustomerAUserId, 30000,
                new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero), seedActor));
            db.Set<Order>().Add(BuildOrder("o-a-2", CustomerAUserId, 50000,
                new DateTimeOffset(2026, 5, 5, 9, 0, 0, TimeSpan.Zero), seedActor));
            await db.SaveChangesAsync();
        }

        // Bump customer A's unread counter on o-a-1 (two maker-authored
        // posts) directly at the DB level; o-a-2 stays untouched.
        await using (var db = _harness.CreateDbContext())
        {
            var order = await db.Set<Order>().FirstAsync(o => o.Id == "o-a-1");
            order.IncrementUnreadFor(OrderMessageAuthorRole.Maker);
            order.IncrementUnreadFor(OrderMessageAuthorRole.Maker);
            await db.SaveChangesAsync();
        }

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", IssueCustomerToken(CustomerAUserId, "a@example.cz"));

        var response = await client.GetAsync("/api/v1/orders?page=1&pageSize=20");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<GetCustomerOrders.GetCustomerOrdersResponse>(JsonOpts);
        body!.Orders.Items.Should().HaveCount(2);
        body.Orders.Items.Single(i => i.OrderId == "o-a-1")
            .UnreadMessageCount.Should().Be(2, "the denormalized counter flows through the projection");
        body.Orders.Items.Single(i => i.OrderId == "o-a-2")
            .UnreadMessageCount.Should().Be(0, "no-message orders surface 0, not null");
    }

    [Fact]
    public async Task Pagination_returns_correct_window()
    {
        var seedActor = "test-seed";
        var seedAt = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

        await using (var db = _harness.CreateDbContext())
        {
            await SeedCommonAsync(db, seedActor, seedAt);

            // 25 orders for Customer A, one per day Jan 1 .. Jan 25.
            for (var i = 1; i <= 25; i++)
            {
                db.Set<Order>().Add(BuildOrder($"o-a-{i:D2}", CustomerAUserId, 10000 + i * 100,
                    new DateTimeOffset(2026, 1, i, 9, 0, 0, TimeSpan.Zero), seedActor));
            }
            await db.SaveChangesAsync();
        }

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", IssueCustomerToken(CustomerAUserId, "a@example.cz"));

        var response = await client.GetAsync("/api/v1/orders?page=2&pageSize=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<GetCustomerOrders.GetCustomerOrdersResponse>(JsonOpts);
        body!.Orders.TotalCount.Should().Be(25);
        body.Orders.Page.Should().Be(2);
        body.Orders.PageSize.Should().Be(10);
        body.Orders.Items.Should().HaveCount(10);
        // Default sort = CreatedAtDesc → page 1 is Jan 25..16; page 2 is Jan 15..6.
        body.Orders.Items[0].OrderId.Should().Be("o-a-15");
        body.Orders.Items[9].OrderId.Should().Be("o-a-06");
    }
}
