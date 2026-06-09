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

namespace Makables.IntegrationTests.Delivery;

/// <summary>
/// End-to-end test for the T-0077 auto-deliver path. The Function itself
/// lives in <c>Makables.Functions</c> (not referenced by IntegrationTests
/// to keep the dependency graph honest); this test reproduces the
/// Function's body inline — projection-only stream + per-row MediatR
/// dispatch — so the UoW pipeline + Order state transition + outbox
/// emission line up end-to-end against real Postgres.
///
/// <para>
/// Seeds three Shipped + AutoDeliverAt-expired orders + one
/// Shipped-not-expired + one Paid; sweeps them via the same code path
/// the Function executes; asserts the three expired orders transitioned
/// to Delivered with Source = Auto and exactly three outbox rows
/// landed of type <see cref="OutboxEventTypes.OrderDeliveredCustomerEmail"/>.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AutoDeliverOrdersIntegrationTests : IAsyncLifetime
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
        new(2026, 6, 9, 8, 0, 0, TimeSpan.Zero);

    private readonly PostgresHarness _harness;
    private WebApplicationFactory<Makables.Web.Customer.Program> _factory = default!;

    public AutoDeliverOrdersIntegrationTests(PostgresHarness harness)
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

    private async Task SeedThreeExpiredAndOneFutureAsync()
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

        // Build N orders. Each shipped at (Now - daysAgo). AutoDeliverAt
        // = shipped + 7 days; we choose daysAgo > 7 (expired) vs < 7 (future).
        for (var i = 1; i <= 3; i++)
        {
            db.Set<Order>().Add(BuildShipped($"expired-{i}", Now.AddDays(-(7 + i)), seedActor, seedAt));
        }
        db.Set<Order>().Add(BuildShipped("future", Now.AddDays(-3), seedActor, seedAt));  // AutoDeliverAt = now+4d

        await db.SaveChangesAsync();
    }

    private static Order BuildShipped(string id, DateTimeOffset shippedAt, string seedActor, DateTimeOffset seedAt)
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
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(shippedAt);
        o.MarkAsPaid(clock, $"tx-{id}");
        o.Accept(clock);
        o.Ship(clock, $"PKT-{id}", 7);
        // Stamp audit fields explicitly: the harness's CreateDbContext does
        // not register the AuditableSaveChangesInterceptor, so the seed path
        // must MarkCreated before SaveChangesAsync or the NOT NULL
        // created_by constraint trips. Same pattern as
        // ComgateWebhookTests.SeedOrderInStateAsync /
        // ShipOrderIntegrationTests.SeedShippedOrderAsync.
        o.MarkCreated(seedActor, seedAt);
        return o;
    }

    [Fact]
    public async Task AutoDeliverOrdersFunction_e2e_transitions_expired_orders_and_writes_outbox()
    {
        await SeedThreeExpiredAndOneFutureAsync();

        // Reproduce the AutoDeliverOrdersFunction body inline. Per-row
        // MediatR dispatch runs through the real UoW + EF Core pipeline
        // so the integration test catches schema drift the unit tests
        // can't see (DeliverySource column mapping, outbox writer, etc.).
        // The Function file itself is a thin wrapper around exactly this
        // logic — see AutoDeliverOrdersFunctionTests for the loop /
        // fail-continue / summary log coverage.
        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var mediator = scope.ServiceProvider.GetRequiredService<ISender>();

        // Materialise the projection-only stream into a list BEFORE
        // dispatching the mutating Mediator commands. Npgsql does not
        // support MARS, so holding the AsAsyncEnumerable reader open while
        // the per-row Send loads the tracked Order on the same DbContext
        // raises NpgsqlOperationInProgressException. The production
        // Function is unaffected because its IOrderRepository + ISender
        // are resolved from a scope that itself yields a fresh DbContext
        // per Mediator request through the UoW pipeline; an integration
        // test that reproduces the Function body inline must close the
        // streaming reader before each Send.
        var orderIds = new List<string>();
        await foreach (var orderId in repo.GetAutoDeliverableUnscopedReadOnlyAsync(Now, default))
        {
            orderIds.Add(orderId);
        }

        foreach (var orderId in orderIds)
        {
            await mediator.Send(
                new MarkOrderDelivered.Command(orderId, OrderDeliverySource.Auto),
                default);
        }

        await using var db = _harness.CreateDbContext();
        var transitioned = await db.Set<Order>().AsNoTracking()
            .Where(o => o.State == OrderState.Delivered)
            .OrderBy(o => o.Id)
            .ToListAsync();
        transitioned.Should().HaveCount(3,
            "three expired Shipped orders transitioned to Delivered");
        transitioned.Should().AllSatisfy(o => o.DeliverySource.Should().Be(OrderDeliverySource.Auto));
        transitioned.Should().AllSatisfy(o => o.DeliveredAt.Should().NotBeNull());

        var future = await db.Set<Order>().AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == "future");
        future!.State.Should().Be(OrderState.Shipped,
            "the not-yet-expired Shipped order is unchanged");

        var outboxRows = await db.Set<OutboxEvent>().AsNoTracking()
            .Where(e => e.EventType == OutboxEventTypes.OrderDeliveredCustomerEmail)
            .ToListAsync();
        outboxRows.Should().HaveCount(3,
            "exactly one OrderDeliveredCustomerEmail event per transition");
    }
}
