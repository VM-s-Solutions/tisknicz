using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
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
/// End-to-end CreateOrder coverage on the Customer host backed by a real
/// Postgres (via <see cref="PostgresHarness"/>). Pins the T-0063 contract:
/// the POST round-trip persists an Order row in
/// <see cref="OrderState.PendingPayment"/> with an
/// <c>M-CZ-{YYYY}{NNNN}</c> number, a snapshotted pricing breakdown, the
/// IDOR-safe <c>CustomerUserId</c> from the JWT, and the email-confirmed
/// gate + audience-binding work as advertised.
///
/// <para>
/// Seed graph: a CZ <c>CountryConfiguration</c> (already in the
/// initial-schema migration), an active+verified Maker (with personal
/// pickup enabled), a Fixed-price Product, and an
/// email-confirmed Customer user. The Postgres TRUNCATE in the harness
/// constructor clears every row between tests.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CreateOrderTests : IAsyncLifetime
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

    public CreateOrderTests(PostgresHarness harness)
    {
        _harness = harness;
    }

    public async Task InitializeAsync()
    {
        await _harness.ResetMutableTablesAsync();

        // Build a customer-host factory bound to the harness Postgres
        // (overrides the Program.cs SQLite or in-memory wiring, in case
        // a future host start config diverges).
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
                        // T-0065 ComgateOptions ValidateOnStart stubs.
                        ["Comgate:MerchantId"] = "12345",
                        ["Comgate:Secret"] = "integration-test-secret",
                        ["Comgate:BaseUrl"] = "https://payments.comgate.test",
                        // T-0070 PacketaOptions ValidateOnStart stubs.
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
                    // Replace the registered DbContext with one pointing at
                    // the harness container. Same approach as
                    // JwtAuthMiddlewareTests but pointing at Postgres
                    // instead of in-memory SQLite — CreateOrder hits the
                    // numbering sequence, which only honours FOR UPDATE on
                    // Postgres.
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

    // === Helpers ===

    private static User BuildCustomerUser(bool emailConfirmed)
    {
        var user = User.Create(
            id: CustomerUserId,
            email: "anna@example.cz",
            role: UserRole.Customer,
            fullName: "Anna Nováková",
            countryCodePrimary: CountryCode,
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        if (emailConfirmed)
        {
            user.ConfirmEmail(new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero));
        }
        return user;
    }

    private async Task SeedAsync(bool emailConfirmed = true)
    {
        await using var db = _harness.CreateDbContext();
        var seedActor = "test-seed";
        var seedAt = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

        // Customer user
        var customer = BuildCustomerUser(emailConfirmed);
        customer.MarkCreated(seedActor, seedAt);
        db.Set<User>().Add(customer);

        // Maker user
        var makerUser = User.Create(
            id: MakerUserId,
            email: "maker@example.cz",
            role: UserRole.Maker,
            fullName: "Maker User",
            countryCodePrimary: CountryCode,
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        makerUser.ConfirmEmail(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        makerUser.MarkCreated(seedActor, seedAt);
        db.Set<User>().Add(makerUser);

        // Address (legal seat for the maker)
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

        // Maker (verified + personal pickup enabled)
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
            snapshotFetchedAt: new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero),
            snapshotIsStale: false,
            countryCode: CountryCode,
            slug: "avast");
        maker.MarkVerified();
        maker.UpdateProfile(
            bio: null,
            bankAccount: null,
            personalPickupEnabled: true,
            pickupNote: null);
        maker.MarkCreated(seedActor, seedAt);
        db.Set<MakerEntity>().Add(maker);

        // Category
        var category = Category.Create(
            id: CategoryId, name: "3D tisk", slug: "3d-tisk",
            icon: null, description: null, sortOrder: 10, countryCode: CountryCode);
        category.MarkCreated(seedActor, seedAt);
        db.Set<Category>().Add(category);

        // Product (Fixed price, 500 Kč)
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

    private string IssueCustomerToken(bool emailConfirmed = true)
    {
        var issuer = new JwtIssuer(Options.Create(new JwtOptions
        {
            Issuer = TestIssuer,
            SigningKeyBase64 = TestKeyBase64,
            AccessTokenLifetime = TimeSpan.FromMinutes(15),
        }));
        var user = BuildCustomerUser(emailConfirmed);
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

    private static CreateOrderRequest ZasilkovnaPayload() => new(
        ProductId: ProductId,
        Quantity: 1,
        ShippingMethod: nameof(ShippingMethod.ZasilkovnaPickupPoint),
        ZasilkovnaPickupPointId: "pp-42",
        CustomerName: "Anna Nováková",
        CustomerEmail: "anna@example.cz",
        CustomerPhone: "+420 723 456 789",
        CustomerNotes: null);

    private static CreateOrderRequest PersonalPickupPayload() => new(
        ProductId: ProductId,
        Quantity: 1,
        ShippingMethod: nameof(ShippingMethod.PersonalPickup),
        ZasilkovnaPickupPointId: null,
        CustomerName: "Anna Nováková",
        CustomerEmail: "anna@example.cz",
        CustomerPhone: "+420 723 456 789",
        CustomerNotes: null);

    // === Tests ===

    [Fact]
    public async Task CreateOrder_happy_path_zasilkovna_persists_order_in_PendingPayment_with_M_CZ_YYYY_NNNN_number()
    {
        await SeedAsync();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", IssueCustomerToken());

        var response = await client.PostAsJsonAsync(
            "/api/v1/orders", ZasilkovnaPayload());

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<CreateOrderResponse>();
        body.Should().NotBeNull();
        body!.OrderNumber.Should().MatchRegex(@"^M-CZ-\d{8}$");
        body.Currency.Should().Be(Currency);
        body.TotalPriceMinor.Should().BeGreaterThan(0);

        await using var db = _harness.CreateDbContext();
        var row = await db.Set<Order>()
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == body.OrderId);

        row.Should().NotBeNull();
        row!.State.Should().Be(OrderState.PendingPayment);
        row.CustomerUserId.Should().Be(CustomerUserId);
        row.MakerId.Should().Be(MakerId);
        row.ProductId.Should().Be(ProductId);
        row.ShippingMethod.Should().Be(ShippingMethod.ZasilkovnaPickupPoint);
        row.ZasilkovnaPickupPointId.Should().Be("pp-42");
        row.ProductPriceAmountMinor.Should().Be(50000);
        // ShippingPrice / PlatformFee / VAT come from the seeded CZ
        // CountryConfiguration — we don't pin the absolute value (it can
        // legitimately change with config tweaks) but the snapshot must
        // exist and reconcile.
        row.TotalAmountMinor.Should().Be(row.ProductPriceAmountMinor + row.ShippingPriceAmountMinor);
        row.PlatformFeeAmountMinor.Should().BeGreaterThan(0);
        row.MakerPayoutAmountMinor.Should().Be(
            row.ProductPriceAmountMinor - row.PlatformFeeAmountMinor + row.ShippingPriceAmountMinor);
        row.Currency.Should().Be(Currency);
        row.CountryCode.Should().Be(CountryCode);
        row.ContactName.Should().Be("Anna Nováková");
        row.OrderNumber.Should().Be(body.OrderNumber);
    }

    [Fact]
    public async Task CreateOrder_happy_path_personal_pickup_persists_order_with_zero_shipping_price()
    {
        await SeedAsync();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", IssueCustomerToken());

        var response = await client.PostAsJsonAsync(
            "/api/v1/orders", PersonalPickupPayload());

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<CreateOrderResponse>();
        body.Should().NotBeNull();

        await using var db = _harness.CreateDbContext();
        var row = await db.Set<Order>()
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == body!.OrderId);

        row.Should().NotBeNull();
        row!.ShippingMethod.Should().Be(ShippingMethod.PersonalPickup);
        row.ZasilkovnaPickupPointId.Should().BeNull();
        // Personal pickup is free per role/order-pricing.md.
        row.ShippingPriceAmountMinor.Should().Be(0);
        row.TotalAmountMinor.Should().Be(row.ProductPriceAmountMinor);
    }

    [Fact]
    public async Task CreateOrder_returns_403_AuthEmailNotConfirmed_when_user_email_unconfirmed()
    {
        await SeedAsync(emailConfirmed: false);
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", IssueCustomerToken(emailConfirmed: false));

        var response = await client.PostAsJsonAsync(
            "/api/v1/orders", ZasilkovnaPayload());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Wire-shape regression for T-0063 Copilot review H-1. The
        // middleware MUST emit camelCase property names + string-named
        // enums (matching AddMakablesControllers + the NSwag-generated
        // TS client) so the frontend's api-fetch.ts reader resolves
        // `payload.code` to the typed i18n key instead of falling
        // through to a generic 403 message.
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain($"\"code\":\"{BusinessErrorMessage.AuthEmailNotConfirmed}\"",
            because: "the 403 body uses camelCase 'code' per AddMakablesControllers / NSwag contract");
        body.Should().Contain("\"type\":\"Forbidden\"",
            because: "ErrorType serialises as a string-named enum, not the numeric ordinal");
        body.Should().NotContain("\"Code\":",
            because: "PascalCase 'Code' would mean the middleware used JsonSerializer defaults instead of Web defaults");
        body.Should().NotContain("\"Type\":",
            because: "PascalCase 'Type' would mean the middleware used JsonSerializer defaults instead of Web defaults");
    }

    [Fact]
    public async Task CreateOrder_returns_401_AuthRequired_when_no_bearer_token()
    {
        await SeedAsync();
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/orders", ZasilkovnaPayload());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RequireEmailConfirmedMiddleware_bypasses_auth_subpath_for_authenticated_unconfirmed_user()
    {
        // Regression net for T-0063 reviewer M-1: the original
        // IsAuthSubpath used PathString.StartsWithSegments("/v", ...) which
        // is whole-segment-anchored and never matched "/v1/...". An
        // authenticated-unconfirmed customer hitting any /api/v1/auth/*
        // endpoint would have been 403'd by the middleware before the
        // [AllowAnonymous] auth controller could respond. The defect is
        // masked today because every auth endpoint is [AllowAnonymous] AND
        // the middleware early-returns on !IsAuthenticated — but as soon
        // as an authenticated user (e.g. for "resend confirmation",
        // "logout from elsewhere") hits one, the broken bypass surfaces.
        // This test pins the bypass via a real HTTP call with an
        // authenticated-unconfirmed JWT.
        await SeedAsync(emailConfirmed: false);
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", IssueCustomerToken(emailConfirmed: false));

        // POST /api/v1/auth/logout is [AllowAnonymous]; if the middleware
        // misses the bypass it returns 403 with AuthEmailNotConfirmed
        // *before* the controller runs. We assert the response is NOT a
        // 403 from the middleware. Logout returning anything else
        // (200, 204, even 400 on a missing body) proves the bypass fired
        // and the request reached the auth controller.
        var response = await client.PostAsync("/api/v1/auth/logout", content: null);

        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden,
            "the /api/v1/auth/* bypass must let authenticated-unconfirmed " +
            "callers reach the auth controller; a 403 here means the " +
            "middleware's segment-aware bypass logic regressed");
    }
}
