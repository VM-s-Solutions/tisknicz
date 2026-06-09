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
using Makables.Core.Domain.Orders;
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
/// End-to-end coverage for T-0081 <c>GET /api/v1/maker/orders</c>. Pins:
/// - happy path returns rows owned by the requesting maker only,
/// - cross-maker isolation (maker A's request never surfaces maker B's orders
///   even with a generous pageSize),
/// - UnreadMessageCount is always null at T-0081 (forward-compat pin for T-0079).
/// Real Postgres via <see cref="PostgresHarness"/>.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class GetMakerOrdersIntegrationTests : IAsyncLifetime
{
    private const string TestKeyBase64 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
    private const string TestIssuer = "https://makables.test";
    private const string CountryCode = "CZ";
    private const string Currency = "CZK";

    private const string CustomerUserId = "user-customer-1";
    private const string MakerAUserId = "user-maker-a";
    private const string MakerBUserId = "user-maker-b";
    private const string MakerAId = "maker-a";
    private const string MakerBId = "maker-b";
    private const string AddressAId = "addr-a";
    private const string AddressBId = "addr-b";
    private const string CategoryId = "cat-1";
    private const string ProductAId = "prod-a";
    private const string ProductBId = "prod-b";

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly PostgresHarness _harness;
    private WebApplicationFactory<Makables.Web.Maker.Program> _factory = default!;

    public GetMakerOrdersIntegrationTests(PostgresHarness harness)
    {
        _harness = harness;
    }

    public async Task InitializeAsync()
    {
        await _harness.ResetMutableTablesAsync();
        _factory = new WebApplicationFactory<Makables.Web.Maker.Program>()
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
                        ["AzureBlobStorage:ConnectionString"] = "UseDevelopmentStorage=true",
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

    private string IssueMakerToken(string userId, string email)
    {
        var issuer = new JwtIssuer(Options.Create(new JwtOptions
        {
            Issuer = TestIssuer,
            SigningKeyBase64 = TestKeyBase64,
            AccessTokenLifetime = TimeSpan.FromMinutes(15),
        }));
        var user = User.Create(
            id: userId, email: email, role: UserRole.Maker,
            fullName: "Maker", countryCodePrimary: CountryCode,
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        user.ConfirmEmail(new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero));
        return issuer.Issue(user, MakablesAudiences.Maker, DateTimeOffset.UtcNow).Token;
    }

    private async Task SeedTwoMakersWithOrdersAsync()
    {
        var seedActor = "test-seed";
        var seedAt = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

        await using var db = _harness.CreateDbContext();

        var customer = User.Create(
            id: CustomerUserId, email: "c@example.cz", role: UserRole.Customer,
            fullName: "Customer", countryCodePrimary: CountryCode,
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        customer.ConfirmEmail(seedAt);
        customer.MarkCreated(seedActor, seedAt);
        db.Set<User>().Add(customer);

        // Single shared category for both makers' products.
        var category = Category.Create(
            id: CategoryId, name: "3D tisk", slug: "3d-tisk",
            icon: null, description: null, sortOrder: 10, countryCode: CountryCode);
        category.MarkCreated(seedActor, seedAt);
        db.Set<Category>().Add(category);

        foreach (var (userId, email, addressId, makerId, productId, slug, company) in new[]
        {
            (MakerAUserId, "a@maker.cz", AddressAId, MakerAId, ProductAId, "avast-a", "Avast A s.r.o."),
            (MakerBUserId, "b@maker.cz", AddressBId, MakerBId, ProductBId, "avast-b", "Avast B s.r.o."),
        })
        {
            var u = User.Create(
                id: userId, email: email, role: UserRole.Maker,
                fullName: "Maker", countryCodePrimary: CountryCode,
                passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
            u.ConfirmEmail(seedAt);
            u.MarkCreated(seedActor, seedAt);
            db.Set<User>().Add(u);

            var address = AddressEntity.Create(
                id: addressId, street: "Pikrtova", houseNumber: "1737",
                city: "Praha", zip: "14000", countryCodeIso: CountryCode,
                auditCountryCode: CountryCode);
            address.MarkCreated(seedActor, seedAt);
            db.Set<AddressEntity>().Add(address);

            var m = MakerEntity.Create(
                id: makerId, userId: userId,
                registrationNumber: makerId == MakerAId ? "27074358" : "12345670",
                vatId: null, companyName: company, legalForm: null,
                registeredAddressId: addressId,
                incorporatedOn: null, isActiveInRegistry: true,
                sourceRegistry: "ares",
                snapshotFetchedAt: seedAt, snapshotIsStale: false,
                countryCode: CountryCode, slug: slug);
            m.MarkVerified();
            m.UpdateProfile(bio: null, bankAccount: null, personalPickupEnabled: true, pickupNote: null);
            m.MarkCreated(seedActor, seedAt);
            db.Set<MakerEntity>().Add(m);

            var product = Product.Create(
                id: productId, makerId: makerId, categoryId: CategoryId,
                title: makerId == MakerAId ? "Vase A" : "Vase B", description: null,
                price: new Money(50000, Currency),
                priceType: PriceType.Fixed, weightGrams: 300,
                countryCode: CountryCode);
            product.MarkCreated(seedActor, seedAt);
            db.Set<Product>().Add(product);
        }

        // Two orders per maker, all paid.
        var i = 0;
        foreach (var (makerId, productId) in new[]
        {
            (MakerAId, ProductAId), (MakerAId, ProductAId),
            (MakerBId, ProductBId), (MakerBId, ProductBId),
        })
        {
            i++;
            var createdAt = new DateTimeOffset(2026, 5, i, 9, 0, 0, TimeSpan.Zero);
            var o = Order.Create(
                id: $"o-{i}", orderNumber: $"M-CZ-2026-{i:D4}",
                customerUserId: CustomerUserId, makerId: makerId, productId: productId,
                contactName: $"Contact-{i}", contactEmail: "c@example.cz",
                contactPhone: "+420 723 456 789",
                productPriceAmountMinor: 30000, shippingPriceAmountMinor: 5000,
                platformFeeAmountMinor: 5000, makerPayoutAmountMinor: 30000,
                totalAmountMinor: 35000, currency: Currency, vatRateBp: 2100,
                shippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
                zasilkovnaPickupPointId: "pp-42",
                countryCode: CountryCode);
            o.MarkCreated(seedActor, createdAt);
            db.Set<Order>().Add(o);
        }

        await db.SaveChangesAsync();
    }

    // === Tests ===

    [Fact]
    public async Task GET_orders_happy_path_returns_only_requesting_makers_orders()
    {
        await SeedTwoMakersWithOrdersAsync();

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", IssueMakerToken(MakerAUserId, "a@maker.cz"));

        var response = await client.GetAsync("/api/v1/orders?page=1&pageSize=20");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<GetMakerOrders.GetMakerOrdersResponse>(JsonOpts);
        body.Should().NotBeNull();
        body!.Orders.TotalCount.Should().Be(2);
        body.Orders.Items.Should().HaveCount(2);
        body.Orders.Items.Should().OnlyContain(i =>
            i.OrderId == "o-1" || i.OrderId == "o-2");
        body.Orders.Items[0].ProductTitle.Should().Be("Vase A");
    }

    [Fact]
    public async Task GET_orders_cross_maker_isolation_makerA_cannot_see_makerB_orders()
    {
        await SeedTwoMakersWithOrdersAsync();

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", IssueMakerToken(MakerAUserId, "a@maker.cz"));

        // pageSize big enough to fit all 4 seeded orders if no scoping.
        var response = await client.GetAsync("/api/v1/orders?page=1&pageSize=50");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<GetMakerOrders.GetMakerOrdersResponse>(JsonOpts);
        body!.Orders.Items.Should().HaveCount(2);
        // Maker B's order ids MUST NOT appear.
        body.Orders.Items.Should().NotContain(i => i.OrderId == "o-3" || i.OrderId == "o-4");
    }

    [Fact]
    public async Task GET_orders_UnreadMessageCount_is_null_pin_until_T_0079()
    {
        await SeedTwoMakersWithOrdersAsync();

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", IssueMakerToken(MakerAUserId, "a@maker.cz"));

        var response = await client.GetAsync("/api/v1/orders?page=1&pageSize=20");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<GetMakerOrders.GetMakerOrdersResponse>(JsonOpts);
        body!.Orders.Items.Should().OnlyContain(i => i.UnreadMessageCount == null);
    }

    // Gate 9 test-catchup item 5 — mirror Customer-side
    // Pagination_returns_correct_window for the Maker host. Seeds 5 orders
    // for the requesting maker and asserts Page=2 / PageSize=2 returns the
    // exact 2-row middle window with TotalCount=5. Default sort is
    // CreatedAtDesc, so page 1 = day 5/4, page 2 = day 3/2, page 3 = day 1.
    [Fact]
    public async Task GET_orders_pagination_returns_correct_window()
    {
        var seedActor = "test-seed";
        var seedAt = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

        await using (var db = _harness.CreateDbContext())
        {
            var customer = User.Create(
                id: CustomerUserId, email: "c@example.cz", role: UserRole.Customer,
                fullName: "Customer", countryCodePrimary: CountryCode,
                passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
            customer.ConfirmEmail(seedAt);
            customer.MarkCreated(seedActor, seedAt);
            db.Set<User>().Add(customer);

            var category = Category.Create(
                id: CategoryId, name: "3D tisk", slug: "3d-tisk",
                icon: null, description: null, sortOrder: 10, countryCode: CountryCode);
            category.MarkCreated(seedActor, seedAt);
            db.Set<Category>().Add(category);

            var makerUser = User.Create(
                id: MakerAUserId, email: "a@maker.cz", role: UserRole.Maker,
                fullName: "Maker", countryCodePrimary: CountryCode,
                passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
            makerUser.ConfirmEmail(seedAt);
            makerUser.MarkCreated(seedActor, seedAt);
            db.Set<User>().Add(makerUser);

            var address = AddressEntity.Create(
                id: AddressAId, street: "Pikrtova", houseNumber: "1737",
                city: "Praha", zip: "14000", countryCodeIso: CountryCode,
                auditCountryCode: CountryCode);
            address.MarkCreated(seedActor, seedAt);
            db.Set<AddressEntity>().Add(address);

            var maker = MakerEntity.Create(
                id: MakerAId, userId: MakerAUserId,
                registrationNumber: "27074358", vatId: null,
                companyName: "Avast s.r.o.", legalForm: null,
                registeredAddressId: AddressAId,
                incorporatedOn: null, isActiveInRegistry: true,
                sourceRegistry: "ares",
                snapshotFetchedAt: seedAt, snapshotIsStale: false,
                countryCode: CountryCode, slug: "avast-a");
            maker.MarkVerified();
            maker.UpdateProfile(bio: null, bankAccount: null, personalPickupEnabled: true, pickupNote: null);
            maker.MarkCreated(seedActor, seedAt);
            db.Set<MakerEntity>().Add(maker);

            var product = Product.Create(
                id: ProductAId, makerId: MakerAId, categoryId: CategoryId,
                title: "Vase A", description: null,
                price: new Money(50000, Currency),
                priceType: PriceType.Fixed, weightGrams: 300,
                countryCode: CountryCode);
            product.MarkCreated(seedActor, seedAt);
            db.Set<Product>().Add(product);

            // 5 orders for Maker A, one per day May 1..May 5.
            for (var i = 1; i <= 5; i++)
            {
                var createdAt = new DateTimeOffset(2026, 5, i, 9, 0, 0, TimeSpan.Zero);
                var o = Order.Create(
                    id: $"o-{i}", orderNumber: $"M-CZ-2026-{i:D4}",
                    customerUserId: CustomerUserId, makerId: MakerAId, productId: ProductAId,
                    contactName: $"Contact-{i}", contactEmail: "c@example.cz",
                    contactPhone: "+420 723 456 789",
                    productPriceAmountMinor: 30000, shippingPriceAmountMinor: 5000,
                    platformFeeAmountMinor: 5000, makerPayoutAmountMinor: 30000,
                    totalAmountMinor: 35000, currency: Currency, vatRateBp: 2100,
                    shippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
                    zasilkovnaPickupPointId: "pp-42",
                    countryCode: CountryCode);
                o.MarkCreated(seedActor, createdAt);
                db.Set<Order>().Add(o);
            }

            await db.SaveChangesAsync();
        }

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", IssueMakerToken(MakerAUserId, "a@maker.cz"));

        var response = await client.GetAsync("/api/v1/orders?page=2&pageSize=2");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<GetMakerOrders.GetMakerOrdersResponse>(JsonOpts);
        body!.Orders.TotalCount.Should().Be(5);
        body.Orders.Page.Should().Be(2);
        body.Orders.PageSize.Should().Be(2);
        body.Orders.Items.Should().HaveCount(2);
        // Default sort = CreatedAtDesc → page 1 = May 5,4 → page 2 = May 3,2.
        body.Orders.Items[0].OrderId.Should().Be("o-3");
        body.Orders.Items[1].OrderId.Should().Be("o-2");
    }
}
