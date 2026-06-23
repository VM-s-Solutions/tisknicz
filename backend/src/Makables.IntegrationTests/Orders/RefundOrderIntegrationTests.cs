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
using Makables.Core.Domain.Outbox;
using Makables.Core.Domain.Payments;
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
/// End-to-end coverage for the T-0105 admin refund endpoint
/// (<c>POST /api/v1/orders/{orderId}/refund</c> on the Admin host —
/// the FIRST admin-host endpoint). Real Postgres via
/// <see cref="PostgresHarness"/> + <see cref="FakeComgatePaymentProvider"/>
/// over the keyed registration. Pins: full refund flips state +
/// accumulates + writes outbox + admin audit row; partial accumulates
/// without state change; over-refund is blocked WITHOUT a provider call;
/// audience enforcement rejects customer JWTs (ADR 0013).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class RefundOrderIntegrationTests : IAsyncLifetime
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
    private const string TransId = "AB1C-D34E";
    private const long Total = 57900;

    private readonly PostgresHarness _harness;
    private readonly FakeComgatePaymentProvider _provider = new();
    private WebApplicationFactory<Makables.Web.Admin.Program> _factory = default!;

    public RefundOrderIntegrationTests(PostgresHarness harness)
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

                    // Replace the keyed "comgate" IPaymentProvider with the
                    // fake (CreatePaymentSessionTests precedent).
                    var keyed = services.Where(d =>
                        d.ServiceType == typeof(IPaymentProvider) &&
                        d.IsKeyedService &&
                        (d.ServiceKey as string) == "comgate").ToList();
                    foreach (var d in keyed)
                    {
                        services.Remove(d);
                    }
                    services.AddKeyedSingleton<IPaymentProvider>("comgate", _provider);
                });
            });
    }

    public Task DisposeAsync()
    {
        _factory?.Dispose();
        return Task.CompletedTask;
    }

    private async Task SeedPaidOrderAsync()
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
            totalAmountMinor: Total, currency: Currency, vatRateBp: 2100,
            shippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
            zasilkovnaPickupPointId: "pp-42",
            countryCode: CountryCode);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(new DateTimeOffset(2026, 6, 5, 10, 0, 0, TimeSpan.Zero));
        order.MarkAsPaid(clock, TransId);
        order.MarkCreated(seedActor, seedAt);
        db.Set<Order>().Add(order);

        await db.SaveChangesAsync();
    }

    private static string IssueToken(string userId, string email, UserRole role, string audience)
    {
        var issuer = new JwtIssuer(Options.Create(new JwtOptions
        {
            Issuer = TestIssuer,
            SigningKeyBase64 = TestKeyBase64,
            AccessTokenLifetime = TimeSpan.FromMinutes(15),
        }));
        var user = User.Create(
            id: userId, email: email, role: role,
            fullName: "Test", countryCodePrimary: CountryCode,
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        user.ConfirmEmail(new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero));
        return issuer.Issue(user, audience, DateTimeOffset.UtcNow).Token;
    }

    private HttpClient CreateAdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", IssueToken(AdminUserId, "admin@makables.cz", UserRole.Admin, MakablesAudiences.Admin));
        return client;
    }

    private static RefundReceipt Receipt(long amountMinor) =>
        new(TransId, amountMinor, Currency, DateTimeOffset.UtcNow);

    [Fact]
    public async Task POST_full_refund_as_admin_flips_state_writes_outbox_and_audit_row()
    {
        await SeedPaidOrderAsync();
        _provider.EnqueueRefund(BusinessResult.Success(Receipt(Total)));
        using var client = CreateAdminClient();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/orders/{OrderId}/refund",
            new { amountMinor = Total, reason = "Zboží nikdy nedorazilo.", acknowledgePostPayout = false });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _provider.RefundCallCount.Should().Be(1);
        _provider.RefundCalls.Single().Should().Be((TransId, Total, Currency));

        await using var db = _harness.CreateDbContext();
        var row = await db.Set<Order>().AsNoTracking().FirstAsync(o => o.Id == OrderId);
        row.State.Should().Be(OrderState.Refunded);
        row.RefundedAmountMinor.Should().Be(Total);
        row.RefundedAt.Should().NotBeNull();

        var outboxRows = await db.Set<OutboxEvent>().AsNoTracking()
            .Where(e => e.AggregateId == OrderId)
            .ToListAsync();
        outboxRows.Should().HaveCount(1);
        outboxRows[0].EventType.Should().Be(OutboxEventTypes.OrderRefundedCustomerEmail);

        var audit = await db.Set<AdminAuditLogEntry>().AsNoTracking()
            .Where(e => e.TargetId == OrderId)
            .ToListAsync();
        audit.Should().HaveCount(1);
        audit[0].ActionCode.Should().Be("order.refund");
        audit[0].TargetEntity.Should().Be("order");
        audit[0].AdminUserId.Should().Be(AdminUserId);
        audit[0].Notes.Should().Be("Zboží nikdy nedorazilo.");
        // jsonb storage normalizes with a space after the colon.
        audit[0].BeforeJson.Should().Contain("\"State\": 1", "before-snapshot pins the Paid state");
        audit[0].AfterJson.Should().Contain("\"State\": 7", "after-snapshot pins the Refunded state");
        audit[0].AfterJson.Should().Contain($"\"RefundedAmountMinor\": {Total}");
    }

    [Fact]
    public async Task Partial_then_over_refund_accumulates_then_blocks_without_second_provider_call()
    {
        await SeedPaidOrderAsync();
        _provider.EnqueueRefund(BusinessResult.Success(Receipt(10000)));
        using var client = CreateAdminClient();

        var first = await client.PostAsJsonAsync(
            $"/api/v1/orders/{OrderId}/refund",
            new { amountMinor = 10000L, reason = "Kompenzace poškozeného obalu.", acknowledgePostPayout = false });

        first.StatusCode.Should().Be(HttpStatusCode.OK);

        await using (var db = _harness.CreateDbContext())
        {
            var row = await db.Set<Order>().AsNoTracking().FirstAsync(o => o.Id == OrderId);
            row.State.Should().Be(OrderState.Paid, "a partial refund does not flip the state");
            row.RefundedAmountMinor.Should().Be(10000);
            row.RefundedAt.Should().BeNull();
        }

        // Second POST exceeds the remaining refundable amount — blocked
        // at the pre-flight, the provider is NEVER called again (AC-3).
        var second = await client.PostAsJsonAsync(
            $"/api/v1/orders/{OrderId}/refund",
            new { amountMinor = Total, reason = "Omylem plná částka.", acknowledgePostPayout = false });

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        // Parse the error body structurally — Error.Type serializes as a
        // string (JsonStringEnumConverter) which the default reader on
        // the record ctor refuses.
        using (var errorDoc = System.Text.Json.JsonDocument.Parse(
            await second.Content.ReadAsStringAsync()))
        {
            errorDoc.RootElement.GetProperty("code").GetString()
                .Should().Be(BusinessErrorMessage.PaymentRefundAmountExceedsRemaining);
        }
        _provider.RefundCallCount.Should().Be(1, "the blocked attempt never reached the provider");

        await using (var db = _harness.CreateDbContext())
        {
            var row = await db.Set<Order>().AsNoTracking().FirstAsync(o => o.Id == OrderId);
            row.RefundedAmountMinor.Should().Be(10000, "the blocked attempt mutated nothing");

            var audit = await db.Set<AdminAuditLogEntry>().AsNoTracking()
                .Where(e => e.TargetId == OrderId)
                .ToListAsync();
            audit.Should().HaveCount(1, "failed commands write no audit row (ADR 0014)");
        }
    }

    [Fact]
    public async Task Customer_JWT_and_anonymous_are_rejected_on_the_admin_host()
    {
        await SeedPaidOrderAsync();

        // Customer-audience JWT replayed against the admin host → 401
        // (audience enforcement per host, ADR 0013).
        using var customerClient = _factory.CreateClient();
        customerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", IssueToken(CustomerUserId, "anna@example.cz", UserRole.Customer, MakablesAudiences.Customer));
        var customerResponse = await customerClient.PostAsJsonAsync(
            $"/api/v1/orders/{OrderId}/refund",
            new { amountMinor = 1000L, reason = "Pokus o eskalaci práv.", acknowledgePostPayout = false });
        customerResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Anonymous → 401.
        using var anonymousClient = _factory.CreateClient();
        var anonymousResponse = await anonymousClient.PostAsJsonAsync(
            $"/api/v1/orders/{OrderId}/refund",
            new { amountMinor = 1000L, reason = "Anonymní pokus o refundaci.", acknowledgePostPayout = false });
        anonymousResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        _provider.RefundCallCount.Should().Be(0);
        await using var db = _harness.CreateDbContext();
        var row = await db.Set<Order>().AsNoTracking().FirstAsync(o => o.Id == OrderId);
        row.State.Should().Be(OrderState.Paid);
        row.RefundedAmountMinor.Should().Be(0);
    }
}
