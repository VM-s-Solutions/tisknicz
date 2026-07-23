using System.Text.Json;
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

namespace Makables.IntegrationTests.Orders;

/// <summary>
/// End-to-end pin for the T-0106 REWIRED DisputeShipment (was the
/// T-0078 stub — the old "no Order state mutation" pin is now FALSE by
/// design). Real Postgres; dispatch the Command via MediatR; assert the
/// Shipped → Disputed transition (with PreDisputeState), the
/// Source=Carrier dispute row, exactly one order.disputed.adminEmail
/// outbox row, and Silent-Success on a re-fire.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DisputeShipmentIntegrationTests : IAsyncLifetime
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

    private readonly PostgresHarness _harness;
    private WebApplicationFactory<Makables.Web.Customer.Program> _factory = default!;

    public DisputeShipmentIntegrationTests(PostgresHarness harness)
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
                        ["Resend:ApiKey"] = "re_integration_test_stub",
                        ["Resend:DefaultFromAddress"] = "no-reply@makables.test",
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

    private async Task SeedShippedOrderAsync()
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
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(new DateTimeOffset(2026, 6, 5, 10, 0, 0, TimeSpan.Zero));
        order.MarkAsPaid(clock, "tx-1");
        order.Accept(clock);
        order.Ship(clock, "PKT-99", 7);
        order.MarkCreated(seedActor, seedAt);
        db.Set<Order>().Add(order);

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task DisputeShipment_e2e_transitions_to_Disputed_with_carrier_dispute_row_and_admin_email()
    {
        await SeedShippedOrderAsync();
        using var scope = _factory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<ISender>();

        var result = await mediator.Send(
            new DisputeShipment.Command(OrderId, DisputeReason.CarrierFailed),
            default);

        result.IsSuccess.Should().BeTrue();

        await using (var db = _harness.CreateDbContext())
        {
            // T-0106: the REAL parenthesis-state transition (stub gone).
            var row = await db.Set<Order>().AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == OrderId);
            row!.State.Should().Be(OrderState.Disputed);
            row.PreDisputeState.Should().Be(OrderState.Shipped);
            row.DisputedAt.Should().NotBeNull();

            // Carrier-sourced dispute row with the §C.5 category mapping.
            var dispute = await db.Set<Dispute>().AsNoTracking()
                .SingleAsync(d => d.OrderId == OrderId);
            dispute.Source.Should().Be(DisputeSource.Carrier);
            dispute.Category.Should().Be(DisputeCategory.CarrierFailed);
            dispute.Description.Should().Contain("PKT-99",
                "the canned description carries the carrier ref");
            dispute.ResolvedAt.Should().BeNull();

            // Exactly one admin-email row; the retired carrierSourced
            // event type no longer exists anywhere.
            var outbox = await db.Set<OutboxEvent>().AsNoTracking()
                .Where(e => e.AggregateId == OrderId)
                .ToListAsync();
            outbox.Should().HaveCount(1);
            outbox[0].EventType.Should().Be(OutboxEventTypes.OrderDisputedAdminEmail);
            var payload = JsonSerializer.Deserialize<OrderDisputedAdminEmailPayload>(
                outbox[0].PayloadJson);
            payload!.OrderId.Should().Be(OrderId);
            payload.Source.Should().Be(DisputeSource.Carrier);
            payload.Category.Should().Be(DisputeCategory.CarrierFailed);
        }

        // Re-fire on the now-Disputed order: Silent-Success, no new rows,
        // no second emission (the stub's repeat-emission contract is gone).
        var refire = await mediator.Send(
            new DisputeShipment.Command(OrderId, DisputeReason.CarrierFailed),
            default);
        refire.IsSuccess.Should().BeTrue();

        await using (var db = _harness.CreateDbContext())
        {
            (await db.Set<Dispute>().AsNoTracking().CountAsync(d => d.OrderId == OrderId))
                .Should().Be(1);
            (await db.Set<OutboxEvent>().AsNoTracking().CountAsync(e => e.AggregateId == OrderId))
                .Should().Be(1);
        }
    }
}
