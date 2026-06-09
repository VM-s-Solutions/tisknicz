using FluentAssertions;
using Makables.Core.AppServices.Features.Orders;
using Makables.Core.Domain.Categories;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Money;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Outbox;
using Makables.Core.Domain.Products;
using Makables.Core.Domain.Shipping;
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
/// End-to-end test for the T-0078 SyncShipmentStatusesFunction Delivered
/// branch. Real Postgres + stubbed <see cref="IShippingCarrier"/>; sweep
/// reproduces the Function body inline so the UoW pipeline + EF schema
/// round-trip + outbox emission all line up.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SyncShipmentStatusesIntegrationTests : IAsyncLifetime
{
    private const string CountryCode = "CZ";
    private const string Currency = "CZK";
    private const string CustomerUserId = "user-customer-1";
    private const string MakerUserId = "user-maker-1";
    private const string MakerId = "maker-1";
    private const string AddressId = "addr-1";
    private const string CategoryId = "cat-1";
    private const string ProductId = "prod-1";
    private const string OrderId = "ord-1";
    private const string CarrierRef = "PKT-99";

    private static readonly DateTimeOffset Now =
        new(2026, 6, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset PacketaDeliveredAt =
        new(2026, 6, 9, 10, 0, 0, TimeSpan.Zero);

    private readonly PostgresHarness _harness;
    private readonly StubShippingCarrierFactory _carrierFactory = new();
    private WebApplicationFactory<Makables.Web.Customer.Program> _factory = default!;

    public SyncShipmentStatusesIntegrationTests(PostgresHarness harness)
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
                    var dbDescriptor = services.SingleOrDefault(
                        d => d.ServiceType == typeof(DbContextOptions<MakablesDbContext>));
                    if (dbDescriptor is not null) services.Remove(dbDescriptor);
                    services.AddDbContext<MakablesDbContext>(o =>
                        o.UseNpgsql(_harness.ConnectionString));

                    var factoryDescriptor = services.SingleOrDefault(
                        d => d.ServiceType == typeof(IShippingCarrierFactory));
                    if (factoryDescriptor is not null) services.Remove(factoryDescriptor);
                    services.AddSingleton<IShippingCarrierFactory>(_carrierFactory);
                });
            });
    }

    public Task DisposeAsync()
    {
        _factory?.Dispose();
        return Task.CompletedTask;
    }

    private async Task SeedShippedZasilkovnaOrderAsync()
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
            contactName: "Anna", contactEmail: "a@b.cz", contactPhone: "+420 723 456 789",
            productPriceAmountMinor: 50000, shippingPriceAmountMinor: 7900,
            platformFeeAmountMinor: 7500, makerPayoutAmountMinor: 50400,
            totalAmountMinor: 57900, currency: Currency, vatRateBp: 2100,
            shippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
            zasilkovnaPickupPointId: "pp-42",
            countryCode: CountryCode);
        var clock = new FixedClock(new DateTimeOffset(2026, 6, 5, 10, 0, 0, TimeSpan.Zero));
        order.MarkAsPaid(clock, "tx-1");
        order.Accept(clock);
        order.Ship(clock, CarrierRef, 7);
        order.MarkCreated(seedActor, seedAt);
        db.Set<Order>().Add(order);

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task SyncShipmentStatuses_e2e_Delivered_state_transitions_Order_and_writes_outbox()
    {
        await SeedShippedZasilkovnaOrderAsync();

        // Reproduce the SyncShipmentStatusesFunction body inline (Delivered
        // branch). Per-row Mediator dispatch through the real UoW pipeline.
        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var carrierFactory = scope.ServiceProvider.GetRequiredService<IShippingCarrierFactory>();
        var mediator = scope.ServiceProvider.GetRequiredService<ISender>();

        // Materialise the AsAsyncEnumerable stream BEFORE dispatching the
        // mutating Mediator commands. Npgsql does not support MARS, so
        // holding the streaming reader open while the per-row Send loads
        // the tracked Order on the same DbContext raises
        // NpgsqlOperationInProgressException. Same pattern as the
        // AutoDeliverOrders integration test.
        var orders = new List<Order>();
        await foreach (var order in repo.GetCarrierSyncableUnscopedReadOnlyAsync(default))
        {
            orders.Add(order);
        }

        foreach (var order in orders)
        {
            var carrierResult = await carrierFactory.ResolveAsync(order.CountryCode, default);
            carrierResult.IsSuccess.Should().BeTrue();
            var status = await carrierResult.Value!.GetStatusAsync(order.ShippingCarrierRef!, default);
            status.IsSuccess.Should().BeTrue();
            if (status.Value!.State == ShipmentState.Delivered)
            {
                await mediator.Send(
                    new MarkOrderDelivered.Command(
                        order.Id, OrderDeliverySource.Carrier, status.Value.DeliveredAt),
                    default);
            }
        }

        await using var db = _harness.CreateDbContext();
        var row = await db.Set<Order>().AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == OrderId);
        row.Should().NotBeNull();
        row!.State.Should().Be(OrderState.Delivered);
        row.DeliverySource.Should().Be(OrderDeliverySource.Carrier);
        row.DeliveredAt.Should().Be(PacketaDeliveredAt,
            "carrier's authoritative timestamp wins over clock");

        var outbox = await db.Set<OutboxEvent>().AsNoTracking()
            .Where(e => e.EventType == OutboxEventTypes.OrderDeliveredCustomerEmail)
            .ToListAsync();
        outbox.Should().HaveCount(1);
        outbox[0].AggregateId.Should().Be(OrderId);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class StubShippingCarrierFactory : IShippingCarrierFactory
    {
        public Task<BusinessResult<IShippingCarrier>> ResolveAsync(
            string countryCode, CancellationToken cancellationToken) =>
            Task.FromResult(BusinessResult.Success<IShippingCarrier>(new StubCarrier()));
    }

    private sealed class StubCarrier : IShippingCarrier
    {
        public string Code => "packeta";

        public PickupPointWidgetConfig WidgetConfig(string locale, string countryCode) =>
            new(ScriptUrl: "stub://", PublicKey: "stub", Options: new Dictionary<string, string>());

        public Task<BusinessResult<Shipment>> CreateShipmentAsync(
            Order order, CancellationToken cancellationToken) =>
            Task.FromResult(BusinessResult.Success(new Shipment(CarrierRef, "stub://tracking")));

        public Task<BusinessResult<ShipmentStatus>> GetStatusAsync(
            string carrierRef, CancellationToken cancellationToken) =>
            Task.FromResult(BusinessResult.Success(
                new ShipmentStatus(ShipmentState.Delivered, PacketaDeliveredAt)));

        public Task<BusinessResult<Stream>> GetLabelPdfAsync(
            string carrierRef, CancellationToken cancellationToken) =>
            Task.FromResult(BusinessResult.Success<Stream>(new MemoryStream(Array.Empty<byte>())));
    }
}
