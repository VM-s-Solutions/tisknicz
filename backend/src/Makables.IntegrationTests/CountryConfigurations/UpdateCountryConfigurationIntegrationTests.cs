using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Makables.Core.Domain.Auditing;
using Makables.Core.Domain.Categories;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Money;
using Makables.Core.Domain.Orders;
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
using NSubstitute;
using AddressEntity = Makables.Core.Domain.Addresses.Address;
using MakerEntity = Makables.Core.Domain.Makers.Maker;

namespace Makables.IntegrationTests.CountryConfigurations;

/// <summary>
/// End-to-end coverage for the T-0108 admin country-configuration endpoint
/// (<c>PUT /api/v1/country-configurations/{countryCode}</c>). Pins: VAT
/// update + audit row; provider-change with a wrong retype is rejected and
/// unchanged; provider-change surfaces the in-flight advisory without
/// blocking. A fake keyed <c>dpd</c> shipping carrier is registered so the
/// real <c>ProviderRegistry</c> reports a registered alternate to change to.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class UpdateCountryConfigurationIntegrationTests : IAsyncLifetime
{
    private const string TestKeyBase64 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
    private const string TestIssuer = "https://makables.test";
    private const string CZ = "CZ";
    private const string Currency = "CZK";
    private const string AdminUserId = "user-admin-1";

    private static readonly DateTimeOffset SeedAt = new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgresHarness _harness;
    private WebApplicationFactory<Makables.Web.Admin.Program> _factory = default!;

    public UpdateCountryConfigurationIntegrationTests(PostgresHarness harness) => _harness = harness;

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
                    if (dbContextDescriptor is not null) services.Remove(dbContextDescriptor);
                    services.AddDbContext<MakablesDbContext>(o => o.UseNpgsql(_harness.ConnectionString));

                    // Register a fake keyed "dpd" shipping carrier so the real
                    // ProviderRegistry (which enumerates keyed service keys)
                    // reports a registered alternate to change to. The registry
                    // only reads the ServiceKey; the instance is never resolved.
                    services.AddKeyedSingleton<IShippingCarrier>("dpd", Substitute.For<IShippingCarrier>());
                });
            });
    }

    public Task DisposeAsync()
    {
        _factory?.Dispose();
        return Task.CompletedTask;
    }

    private async Task<CountryConfiguration> ReadConfigAsync()
    {
        await using var db = _harness.CreateDbContext();
        return await db.Set<CountryConfiguration>().AsNoTracking().FirstAsync(c => c.CountryId == CZ);
    }

    private async Task SeedAdminAsync()
    {
        await using var db = _harness.CreateDbContext();
        var admin = User.Create(AdminUserId, "admin@makables.cz", UserRole.Admin, "Admin", CZ,
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        admin.ConfirmEmail(SeedAt);
        admin.MarkCreated("test-seed", SeedAt);
        db.Set<User>().Add(admin);
        await db.SaveChangesAsync();
    }

    private async Task SeedInFlightOrdersAsync()
    {
        await using var db = _harness.CreateDbContext();
        const string actor = "test-seed";

        var custUser = User.Create("user-cust-1", "anna@example.cz", UserRole.Customer, "Anna", CZ,
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        custUser.ConfirmEmail(SeedAt); custUser.MarkCreated(actor, SeedAt);
        db.Set<User>().Add(custUser);

        var makerUser = User.Create("user-maker-1", "x@example.cz", UserRole.Maker, "Maker", CZ,
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        makerUser.ConfirmEmail(SeedAt); makerUser.MarkCreated(actor, SeedAt);
        db.Set<User>().Add(makerUser);

        var addr = AddressEntity.Create("addr-1", "Pikrtova", "1737", "Praha", "14000", CZ, CZ);
        addr.MarkCreated(actor, SeedAt);
        db.Set<AddressEntity>().Add(addr);

        var maker = MakerEntity.Create("maker-1", "user-maker-1", "27074358", null, "Avast s.r.o.", null,
            "addr-1", null, true, "ares", SeedAt, false, CZ, "avast");
        maker.MarkCreated(actor, SeedAt);
        db.Set<MakerEntity>().Add(maker);

        var category = Category.Create("cat-1", "3D tisk", "3d-tisk", null, null, 10, CZ);
        category.MarkCreated(actor, SeedAt);
        db.Set<Category>().Add(category);

        var product = Product.Create("prod-1", "maker-1", "cat-1", "Vase", null,
            new Money(50000, Currency), PriceType.Fixed, 300, CZ);
        product.MarkCreated(actor, SeedAt);
        db.Set<Product>().Add(product);

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(new DateTimeOffset(2026, 6, 5, 10, 0, 0, TimeSpan.Zero));

        Order InFlight(string id, string number, OrderState target)
        {
            var o = Order.Create(id, number, "user-cust-1", "maker-1", "prod-1",
                "Anna", "anna@example.cz", "+420 723 456 789",
                50000, 7900, 7500, 50400, 57900, Currency, 2100,
                ShippingMethod.ZasilkovnaPickupPoint, "pp-42", CZ);
            o.MarkAsPaid(clock, $"TR-{id}");
            if (target == OrderState.Accepted) o.Accept(clock);
            o.MarkCreated(actor, SeedAt);
            return o;
        }

        db.Set<Order>().Add(InFlight("ord-1", "M-CZ-20260001", OrderState.Paid));
        db.Set<Order>().Add(InFlight("ord-2", "M-CZ-20260002", OrderState.Accepted));

        await db.SaveChangesAsync();
    }

    private static string IssueAdminToken()
    {
        var issuer = new JwtIssuer(Options.Create(new JwtOptions
        {
            Issuer = TestIssuer,
            SigningKeyBase64 = TestKeyBase64,
            AccessTokenLifetime = TimeSpan.FromMinutes(15),
        }));
        var user = User.Create(AdminUserId, "admin@makables.cz", UserRole.Admin, "Admin", CZ,
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        user.ConfirmEmail(SeedAt);
        return issuer.Issue(user, MakablesAudiences.Admin, DateTimeOffset.UtcNow).Token;
    }

    private HttpClient CreateAdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", IssueAdminToken());
        return client;
    }

    private static object BodyFrom(CountryConfiguration c,
        int? standardVat = null, int? reducedVat = null, string? shippingCarrier = null, string? confirm = null) =>
        new
        {
            standardVatRateBp = standardVat ?? c.StandardVatRateBp,
            reducedVatRateBp = reducedVat ?? c.ReducedVatRateBp,
            invoicingMode = c.InvoicingMode,
            platformFeeRateBp = c.PlatformFeeRateBp,
            defaultShippingPriceMinor = c.DefaultShippingPriceMinor,
            defaultPaymentProvider = c.DefaultPaymentProvider,
            defaultShippingCarrier = shippingCarrier ?? c.DefaultShippingCarrier,
            defaultRegistry = c.DefaultRegistry,
            defaultEmailProvider = c.DefaultEmailProvider,
            confirmedProviderCode = confirm,
            reason = "Revize konfigurace.",
        };

    [Fact]
    public async Task PUT_updates_vat_and_writes_audit_row()
    {
        await SeedAdminAsync();
        var config = await ReadConfigAsync();
        var newStandard = config.StandardVatRateBp == 2100 ? 1900 : 2100;
        using var client = CreateAdminClient();

        var response = await client.PutAsJsonAsync(
            $"/api/v1/country-configurations/{CZ}", BodyFrom(config, standardVat: newStandard, reducedVat: 1200));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await ReadConfigAsync();
        updated.StandardVatRateBp.Should().Be(newStandard);
        updated.ReducedVatRateBp.Should().Be(1200);

        await using var db = _harness.CreateDbContext();
        var audit = await db.Set<AdminAuditLogEntry>().AsNoTracking()
            .Where(a => a.TargetId == CZ && a.ActionCode == "country.update").ToListAsync();
        audit.Should().HaveCount(1);
        audit[0].TargetEntity.Should().Be("countryConfiguration");
        audit[0].Notes.Should().Be("Revize konfigurace.");
    }

    [Fact]
    public async Task PUT_provider_change_with_wrong_retype_is_rejected_and_unchanged()
    {
        await SeedAdminAsync();
        var config = await ReadConfigAsync();
        using var client = CreateAdminClient();

        // Change shipping carrier to the registered "dpd" but retype a
        // mismatched confirmation.
        var response = await client.PutAsJsonAsync(
            $"/api/v1/country-configurations/{CZ}",
            BodyFrom(config, shippingCarrier: "dpd", confirm: "wrong"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("code").GetString()
            .Should().Be(BusinessErrorMessage.CountryProviderConfirmationMismatch);

        var unchanged = await ReadConfigAsync();
        unchanged.DefaultShippingCarrier.Should().Be(config.DefaultShippingCarrier);
    }

    [Fact]
    public async Task PUT_provider_change_surfaces_in_flight_advisory_without_blocking()
    {
        await SeedAdminAsync();
        await SeedInFlightOrdersAsync();
        var config = await ReadConfigAsync();
        using var client = CreateAdminClient();

        var response = await client.PutAsJsonAsync(
            $"/api/v1/country-configurations/{CZ}",
            BodyFrom(config, shippingCarrier: "dpd", confirm: "dpd"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<UpdateCountryConfigEnvelope>();
        body!.InFlightOrderCount.Should().Be(2);
        body.ProviderChanged.Should().BeTrue();

        var updated = await ReadConfigAsync();
        updated.DefaultShippingCarrier.Should().Be("dpd");

        // The in-flight orders keep their cached payment provider refs (Q-C).
        await using var db = _harness.CreateDbContext();
        var orders = await db.Set<Order>().AsNoTracking().Where(o => o.CountryCode == CZ).ToListAsync();
        orders.Should().OnlyContain(o => o.PaymentProviderRef != null);
    }

    private sealed record UpdateCountryConfigEnvelope(int InFlightOrderCount, bool ProviderChanged);
}
