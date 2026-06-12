using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Makables.Core.Domain.Auditing;
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
using NSubstitute;
using AddressEntity = Makables.Core.Domain.Addresses.Address;
using MakerEntity = Makables.Core.Domain.Makers.Maker;

namespace Makables.IntegrationTests.Orders;

/// <summary>
/// End-to-end coverage for the T-0107 admin manual state change
/// (<c>POST /api/v1/orders/{orderId}/state</c> on the Admin host). Pins
/// the Shipped → Delivered happy path (AdminManual source + audit row)
/// and the allow-list interlock: a Paid → Refunded request is a 409
/// naming <c>RefundOrder</c>, with no mutation and no audit row.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ChangeOrderStateManuallyIntegrationTests : IAsyncLifetime
{
    private const string TestKeyBase64 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
    private const string TestIssuer = "https://makables.test";
    private const string CountryCode = "CZ";
    private const string Currency = "CZK";

    private const string CustomerUserId = "user-customer-1";
    private const string MakerUserId = "user-maker-1";
    private const string AdminUserId = "user-admin-1";
    private const string MakerId = "maker-1";
    private const string AddressId = "addr-1";
    private const string CategoryId = "cat-1";
    private const string ProductId = "prod-1";
    private const string OrderId = "ord-1";

    private readonly PostgresHarness _harness;
    private WebApplicationFactory<Makables.Web.Admin.Program> _factory = default!;

    public ChangeOrderStateManuallyIntegrationTests(PostgresHarness harness)
    {
        _harness = harness;
    }

    public async Task InitializeAsync()
    {
        await _harness.ResetMutableTablesAsync();
        _factory = new WebApplicationFactory<Makables.Web.Admin.Program>()
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

    private async Task SeedOrderAsync(OrderState state)
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

        var adminUser = User.Create(
            id: AdminUserId, email: "admin@makables.cz", role: UserRole.Admin,
            fullName: "Admin", countryCodePrimary: CountryCode,
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        adminUser.ConfirmEmail(seedAt);
        adminUser.MarkCreated(seedActor, seedAt);
        db.Set<User>().Add(adminUser);

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
        clock.UtcNow.Returns(new DateTimeOffset(2026, 6, 5, 10, 0, 0, TimeSpan.Zero));
        order.MarkAsPaid(clock, "tx-1");
        if (state is OrderState.Accepted or OrderState.Shipped)
            order.Accept(clock);
        if (state is OrderState.Shipped)
            order.Ship(clock, "PKT-1", 7);
        order.MarkCreated(seedActor, seedAt);
        db.Set<Order>().Add(order);

        await db.SaveChangesAsync();
    }

    private HttpClient CreateAdminClient()
    {
        var issuer = new JwtIssuer(Options.Create(new JwtOptions
        {
            Issuer = TestIssuer,
            SigningKeyBase64 = TestKeyBase64,
            AccessTokenLifetime = TimeSpan.FromMinutes(15),
        }));
        var user = User.Create(
            id: AdminUserId, email: "admin@makables.cz", role: UserRole.Admin,
            fullName: "Admin", countryCodePrimary: CountryCode,
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        user.ConfirmEmail(new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero));
        var token = issuer.Issue(user, MakablesAudiences.Admin, DateTimeOffset.UtcNow).Token;

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task POST_shipped_to_delivered_succeeds_and_audits()
    {
        await SeedOrderAsync(OrderState.Shipped);
        using var client = CreateAdminClient();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/orders/{OrderId}/state",
            new { targetState = "Delivered", reason = "Zákazník potvrdil převzetí telefonicky." });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = _harness.CreateDbContext();
        var order = await db.Set<Order>().AsNoTracking().FirstAsync(o => o.Id == OrderId);
        order.State.Should().Be(OrderState.Delivered);
        order.DeliverySource.Should().Be(OrderDeliverySource.AdminManual);
        order.DeliveredAt.Should().NotBeNull();

        var audit = await db.Set<AdminAuditLogEntry>().AsNoTracking()
            .Where(e => e.TargetId == OrderId)
            .ToListAsync();
        audit.Should().HaveCount(1);
        audit[0].ActionCode.Should().Be("order.manualStateChange");
        audit[0].TargetEntity.Should().Be("order");
        audit[0].AdminUserId.Should().Be(AdminUserId);
        audit[0].Notes.Should().Be("Zákazník potvrdil převzetí telefonicky.");
        // jsonb normalizes with a space after the colon; Shipped = 3,
        // Delivered = 4.
        audit[0].BeforeJson.Should().Contain("\"State\": 3");
        audit[0].AfterJson.Should().Contain("\"State\": 4");
    }

    [Fact]
    public async Task POST_paid_to_refunded_blocked_409_names_RefundOrder()
    {
        await SeedOrderAsync(OrderState.Paid);
        using var client = CreateAdminClient();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/orders/{OrderId}/state",
            new { targetState = "Refunded", reason = "Pokus o ruční označení refundace." });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var errorDoc = System.Text.Json.JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        errorDoc.RootElement.GetProperty("code").GetString()
            .Should().Be(BusinessErrorMessage.OrderManualTransitionUseRefundOrder);

        await using var db = _harness.CreateDbContext();
        var order = await db.Set<Order>().AsNoTracking().FirstAsync(o => o.Id == OrderId);
        order.State.Should().Be(OrderState.Paid, "the blocked attempt mutated nothing");

        var audit = await db.Set<AdminAuditLogEntry>().AsNoTracking()
            .Where(e => e.TargetId == OrderId)
            .ToListAsync();
        audit.Should().BeEmpty("blocked commands write no audit row (ADR 0014)");
    }
}
