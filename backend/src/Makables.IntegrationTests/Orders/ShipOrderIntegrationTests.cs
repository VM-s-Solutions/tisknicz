using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentAssertions;
using Makables.Core.Domain.Categories;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Money;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Outbox;
using Makables.Core.Domain.Products;
using Makables.Core.Domain.Shipping;
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
/// End-to-end coverage for the T-0072 maker-host ShipOrder endpoint
/// (<c>POST /api/v1/orders/{orderId}/ship</c>) on a Zásilkovna order.
/// Pins the atomic outbox emission contract: a single transaction
/// commits the Accepted → Shipped transition together with EXACTLY
/// two outbox rows
/// (<see cref="OutboxEventTypes.OrderShippedCustomerEmail"/> +
/// <see cref="OutboxEventTypes.ShippingGenerateLabel"/>) per
/// ADR 0014 + T-0072 locked decision A.1.
///
/// <para>
/// The real Packeta HTTP adapter is replaced with a fake
/// <see cref="IShippingCarrierFactory"/> that returns a stubbed
/// <see cref="IShippingCarrier"/> so the handler exercises the real
/// Mediator pipeline + UoW commit without hitting a third-party API.
/// Same fake-over-keyed-service pattern as
/// <see cref="FakeComgatePaymentProvider"/> in the T-0066 webhook tests.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ShipOrderIntegrationTests : IAsyncLifetime
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

    private const string CarrierRef = "9876543210";
    private const string TrackingUrl = "https://tracking.packeta.com/Z9876543210";

    private readonly PostgresHarness _harness;
    private readonly StubShippingCarrierFactory _carrierFactory = new();
    private WebApplicationFactory<Makables.Web.Maker.Program> _factory = default!;

    public ShipOrderIntegrationTests(PostgresHarness harness)
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

                    // Replace the real IShippingCarrierFactory with the
                    // stub so the handler doesn't hit the Packeta HTTP
                    // endpoint. The fake returns a successful Shipment
                    // every time.
                    var factoryDescriptor = services.SingleOrDefault(
                        d => d.ServiceType == typeof(IShippingCarrierFactory));
                    if (factoryDescriptor is not null)
                    {
                        services.Remove(factoryDescriptor);
                    }
                    services.AddSingleton<IShippingCarrierFactory>(_carrierFactory);
                });
            });
    }

    public Task DisposeAsync()
    {
        _factory?.Dispose();
        return Task.CompletedTask;
    }

    private async Task SeedAcceptedZasilkovnaOrderAsync()
    {
        await using var db = _harness.CreateDbContext();
        var seedActor = "test-seed";
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
        order.MarkAsPaid(clock, "preset-tx");
        order.Accept(clock);
        order.MarkCreated(seedActor, seedAt);
        db.Set<Order>().Add(order);

        await db.SaveChangesAsync();
    }

    private string IssueMakerToken()
    {
        var issuer = new JwtIssuer(Options.Create(new JwtOptions
        {
            Issuer = TestIssuer,
            SigningKeyBase64 = TestKeyBase64,
            AccessTokenLifetime = TimeSpan.FromMinutes(15),
        }));
        var user = User.Create(
            id: MakerUserId, email: "maker@example.cz", role: UserRole.Maker,
            fullName: "Maker", countryCodePrimary: CountryCode,
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        user.ConfirmEmail(new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero));
        return issuer.Issue(user, MakablesAudiences.Maker, DateTimeOffset.UtcNow).Token;
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    // === Tests ===

    [Fact]
    public async Task ShipOrder_Zasilkovna_end_to_end_emits_2_outbox_events()
    {
        await SeedAcceptedZasilkovnaOrderAsync();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", IssueMakerToken());

        var response = await client.PostAsync(
            $"/api/v1/orders/{OrderId}/ship", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = _harness.CreateDbContext();
        var row = await db.Set<Order>().AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == OrderId);
        row.Should().NotBeNull();
        row!.State.Should().Be(OrderState.Shipped);
        row.ShippingCarrierRef.Should().Be(CarrierRef);
        row.ShippingCarrierTrackingUrl.Should().Be(TrackingUrl);

        var outboxRows = await db.Set<OutboxEvent>().AsNoTracking()
            .Where(e => e.AggregateId == OrderId)
            .OrderBy(e => e.EventType)
            .ToListAsync();
        outboxRows.Should().HaveCount(2,
            "ShipOrder emits exactly one customer-email + one generate-label event per T-0072");
        outboxRows.Select(e => e.EventType).Should().BeEquivalentTo(new[]
        {
            OutboxEventTypes.OrderShippedCustomerEmail,
            OutboxEventTypes.ShippingGenerateLabel,
        });

        // The customer-email payload carries the tracking URL the fake
        // returned. Pins the Order.Ship + payload contract end-to-end.
        var customerRow = outboxRows.Single(e =>
            e.EventType == OutboxEventTypes.OrderShippedCustomerEmail);
        var customerPayload = JsonSerializer.Deserialize<OrderShippedCustomerEmailPayload>(
            customerRow.PayloadJson);
        customerPayload.Should().NotBeNull();
        customerPayload!.TrackingUrl.Should().Be(TrackingUrl);
        customerPayload.OrderId.Should().Be(OrderId);
    }

    /// <summary>
    /// In-memory <see cref="IShippingCarrierFactory"/> that returns a
    /// stub carrier whose <c>CreateShipmentAsync</c> always succeeds with
    /// the documented MVP ref + tracking URL. Mirrors the
    /// <see cref="FakeComgatePaymentProvider"/> shape — scripted-but-stable.
    /// </summary>
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
            Task.FromResult(BusinessResult.Success(new Shipment(CarrierRef, TrackingUrl)));

        public Task<BusinessResult<ShipmentStatus>> GetStatusAsync(
            string carrierRef, CancellationToken cancellationToken) =>
            Task.FromResult(BusinessResult.Success(new ShipmentStatus(ShipmentState.Created, null)));

        public Task<BusinessResult<Stream>> GetLabelPdfAsync(
            string carrierRef, CancellationToken cancellationToken) =>
            Task.FromResult(BusinessResult.Success<Stream>(new MemoryStream(Array.Empty<byte>())));
    }
}
