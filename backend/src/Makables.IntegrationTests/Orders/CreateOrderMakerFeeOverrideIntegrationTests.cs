using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Makables.Core.Domain.Categories;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Configuration;
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
/// T-0140 (US-admin-0018) — proves end-to-end that a priced order
/// snapshots the maker's per-maker loyalty fee-rate OVERRIDE (not the
/// country default) at order-creation time (AC-2), that changing the
/// override afterwards never touches an already-priced order's
/// <c>PlatformFeeMinor</c> snapshot (AC-8), and that a maker with no
/// override still gets the plain country default (AC-3). Runs against a
/// real Postgres via <see cref="PostgresHarness"/> — the CZ
/// <c>country_configuration</c> seed row's <c>platform_fee_rate_bp</c> is
/// 700 (7%) per the <c>UpdateCzPlatformFeeRate</c> migration.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CreateOrderMakerFeeOverrideIntegrationTests : IAsyncLifetime
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

    private readonly PostgresHarness _harness;
    private WebApplicationFactory<Makables.Web.Customer.Program> _factory = default!;

    public CreateOrderMakerFeeOverrideIntegrationTests(PostgresHarness harness)
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

    // === Helpers (trimmed copy of CreateOrderTests' seed graph) ===

    private static User BuildCustomerUser() =>
        User.Create(
            id: CustomerUserId,
            email: "anna@example.cz",
            role: UserRole.Customer,
            fullName: "Anna Nováková",
            countryCodePrimary: CountryCode,
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");

    private async Task SeedAsync()
    {
        await using var db = _harness.CreateDbContext();
        var seedActor = "test-seed";
        var seedAt = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

        var customer = BuildCustomerUser();
        customer.ConfirmEmail(seedAt);
        customer.MarkCreated(seedActor, seedAt);
        db.Set<User>().Add(customer);

        var makerUser = User.Create(
            id: MakerUserId,
            email: "maker@example.cz",
            role: UserRole.Maker,
            fullName: "Maker User",
            countryCodePrimary: CountryCode,
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        makerUser.ConfirmEmail(seedAt);
        makerUser.MarkCreated(seedActor, seedAt);
        db.Set<User>().Add(makerUser);

        var address = AddressEntity.Create(
            id: AddressId,
            street: "Pikrtova",
            houseNumber: "1737",
            city: "Praha",
            zip: "14000",
            countryCodeIso: CountryCode,
            auditCountryCode: CountryCode);
        address.MarkCreated(seedActor, seedAt);
        db.Set<AddressEntity>().Add(address);

        var maker = MakerEntity.Create(
            id: MakerId,
            userId: MakerUserId,
            registrationNumber: "27074358",
            vatId: null,
            companyName: "Avast Software s.r.o.",
            legalForm: null,
            registeredAddressId: AddressId,
            incorporatedOn: null,
            isActiveInRegistry: true,
            sourceRegistry: "ares",
            snapshotFetchedAt: seedAt,
            snapshotIsStale: false,
            countryCode: CountryCode,
            slug: "avast");
        maker.MarkVerified();
        maker.UpdateProfile(
            bio: null, bankAccount: null, personalPickupEnabled: true, pickupNote: null);
        maker.MarkCreated(seedActor, seedAt);
        db.Set<MakerEntity>().Add(maker);

        var category = Category.Create(
            id: CategoryId, name: "3D tisk", slug: "3d-tisk",
            icon: null, description: null, sortOrder: 10, countryCode: CountryCode);
        category.MarkCreated(seedActor, seedAt);
        db.Set<Category>().Add(category);

        var product = Product.Create(
            id: ProductId,
            makerId: MakerId,
            categoryId: CategoryId,
            title: "Vase",
            description: null,
            price: new Money(50000, Currency),
            priceType: PriceType.Fixed,
            weightGrams: 300,
            countryCode: CountryCode);
        product.MarkCreated(seedActor, seedAt);
        db.Set<Product>().Add(product);

        await db.SaveChangesAsync();
    }

    private string IssueCustomerToken()
    {
        var issuer = new JwtIssuer(Options.Create(new JwtOptions
        {
            Issuer = TestIssuer,
            SigningKeyBase64 = TestKeyBase64,
            AccessTokenLifetime = TimeSpan.FromMinutes(15),
        }));
        var user = BuildCustomerUser();
        return issuer.Issue(user, MakablesAudiences.Customer, DateTimeOffset.UtcNow).Token;
    }

    private sealed record CreateOrderRequest(
        string ProductId,
        int Quantity,
        string ShippingMethod,
        string? ZasilkovnaPickupPointId,
        string CustomerName,
        string CustomerEmail,
        string CustomerPhone,
        string? CustomerNotes);

    private sealed record CreateOrderResponse(
        string OrderId,
        string OrderNumber,
        long TotalPriceMinor,
        string Currency);

    private static CreateOrderRequest PersonalPickupPayload() => new(
        ProductId: ProductId,
        Quantity: 1,
        ShippingMethod: nameof(ShippingMethod.PersonalPickup),
        ZasilkovnaPickupPointId: null,
        CustomerName: "Anna Nováková",
        CustomerEmail: "anna@example.cz",
        CustomerPhone: "+420 723 456 789",
        CustomerNotes: null);

    private async Task<Order> CreateOrderAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/orders", PersonalPickupPayload());
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<CreateOrderResponse>();
        body.Should().NotBeNull();

        await using var db = _harness.CreateDbContext();
        var row = await db.Set<Order>().AsNoTracking().FirstAsync(o => o.Id == body!.OrderId);
        return row;
    }

    /// <summary>Directly flips the maker's override in the DB — the same
    /// mutation <c>SetMakerFeeOverride.Handler</c> performs; the admin
    /// command itself is pinned by
    /// <c>Makables.Tests.AppServices.Features.Maker.SetMakerFeeOverrideHandlerTests</c>.
    /// Keeping this test on the Customer host (not booting the Admin host
    /// too) isolates it to the pricing-snapshot contract this test exists
    /// to prove.</summary>
    private async Task SetMakerOverrideAsync(int? feeRateOverrideBp)
    {
        await using var db = _harness.CreateDbContext();
        var maker = await db.Set<MakerEntity>().FirstAsync(m => m.Id == MakerId);
        maker.SetFeeRateOverride(feeRateOverrideBp);
        await db.SaveChangesAsync();
    }

    // === Tests ===

    [Fact]
    public async Task CreateOrder_uses_country_default_fee_when_maker_has_no_override()
    {
        // AC-3.
        await SeedAsync();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", IssueCustomerToken());

        var order = await CreateOrderAsync(client);

        await using var db = _harness.CreateDbContext();
        var config = await db.Set<CountryConfiguration>().AsNoTracking()
            .FirstAsync(c => c.CountryId == CountryCode);

        // 700 bp of 500 CZK = 35 CZK.
        config.PlatformFeeRateBp.Should().Be(700);
        order.PlatformFeeAmountMinor.Should().Be(3500);
    }

    [Fact]
    public async Task CreateOrder_snapshots_the_overridden_rate_and_leaves_prior_orders_untouched()
    {
        // AC-2 + AC-8.
        await SeedAsync();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", IssueCustomerToken());

        // Order #1 — priced BEFORE the override exists (country default,
        // 700 bp → 35 CZK fee on a 500 CZK product).
        var firstOrder = await CreateOrderAsync(client);
        firstOrder.PlatformFeeAmountMinor.Should().Be(3500);

        // Admin grants the loyalty override: 350 bp (3,5%).
        await SetMakerOverrideAsync(350);

        // Order #2 — priced AFTER the override — must use 350 bp, not
        // 700 bp: 350 bp of 500 CZK = 17,50 CZK.
        var secondOrder = await CreateOrderAsync(client);
        secondOrder.PlatformFeeAmountMinor.Should().Be(1750);

        // AC-8: order #1's snapshot must be UNCHANGED by the override —
        // re-read it fresh from the DB.
        await using var db = _harness.CreateDbContext();
        var firstOrderReloaded = await db.Set<Order>().AsNoTracking()
            .FirstAsync(o => o.Id == firstOrder.Id);
        firstOrderReloaded.PlatformFeeAmountMinor.Should().Be(3500);

        // Clearing the override again must not touch either historical order.
        await SetMakerOverrideAsync(null);
        var thirdOrder = await CreateOrderAsync(client);
        thirdOrder.PlatformFeeAmountMinor.Should().Be(3500);

        var firstOrderStillUnchanged = await db.Set<Order>().AsNoTracking()
            .FirstAsync(o => o.Id == firstOrder.Id);
        firstOrderStillUnchanged.PlatformFeeAmountMinor.Should().Be(3500);
        var secondOrderStillUnchanged = await db.Set<Order>().AsNoTracking()
            .FirstAsync(o => o.Id == secondOrder.Id);
        secondOrderStillUnchanged.PlatformFeeAmountMinor.Should().Be(1750);
    }
}
