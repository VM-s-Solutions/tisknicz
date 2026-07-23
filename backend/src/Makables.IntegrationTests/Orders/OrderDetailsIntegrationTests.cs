using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Makables.Core.AppServices.Features.Orders;
using Makables.Core.Domain.Categories;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Invoices;
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
/// End-to-end coverage for T-0082 detail endpoints
/// (<c>GET /api/v1/orders/{orderId}</c> on both Customer + Maker hosts).
/// Pins:
/// - happy path returns all lifecycle timestamps + breakdown + attachments,
/// - cross-tenant probes return 404 (no IDOR oracle),
/// - InvoicePdfUrl is null when no Invoice exists.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OrderDetailsIntegrationTests : IAsyncLifetime
{
    private const string TestKeyBase64 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
    private const string TestIssuer = "https://makables.test";
    private const string CountryCode = "CZ";
    private const string Currency = "CZK";

    private const string CustomerAUserId = "user-customer-a";
    private const string CustomerBUserId = "user-customer-b";
    private const string MakerAUserId = "user-maker-a";
    private const string MakerBUserId = "user-maker-b";
    private const string MakerAId = "maker-a";
    private const string MakerBId = "maker-b";
    private const string AddressAId = "addr-a";
    private const string AddressBId = "addr-b";
    private const string CategoryId = "cat-1";
    private const string ProductAId = "prod-a";
    private const string ProductBId = "prod-b";
    private const string OrderAId = "ord-a"; // owned by CustomerA + MakerA
    private const string OrderBId = "ord-b"; // owned by CustomerB + MakerB

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly PostgresHarness _harness;
    private WebApplicationFactory<Makables.Web.Customer.Program> _customerFactory = default!;
    private WebApplicationFactory<Makables.Web.Maker.Program> _makerFactory = default!;

    public OrderDetailsIntegrationTests(PostgresHarness harness)
    {
        _harness = harness;
    }

    public async Task InitializeAsync()
    {
        await _harness.ResetMutableTablesAsync();
        _customerFactory = BuildFactory<Makables.Web.Customer.Program>();
        _makerFactory = BuildFactory<Makables.Web.Maker.Program>();
    }

    public Task DisposeAsync()
    {
        _customerFactory?.Dispose();
        _makerFactory?.Dispose();
        return Task.CompletedTask;
    }

    private WebApplicationFactory<TProgram> BuildFactory<TProgram>() where TProgram : class =>
        new WebApplicationFactory<TProgram>().WithWebHostBuilder(builder =>
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

    private string IssueCustomerToken(string userId, string email)
    {
        var issuer = new JwtIssuer(Options.Create(new JwtOptions
        {
            Issuer = TestIssuer,
            SigningKeyBase64 = TestKeyBase64,
            AccessTokenLifetime = TimeSpan.FromMinutes(15),
        }));
        var user = User.Create(
            id: userId, email: email, role: UserRole.Customer,
            fullName: "Customer", countryCodePrimary: CountryCode,
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        user.ConfirmEmail(new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero));
        return issuer.Issue(user, MakablesAudiences.Customer, DateTimeOffset.UtcNow).Token;
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

    private async Task SeedTwoOrdersAsync(bool includeInvoiceForA = false)
    {
        var seedActor = "test-seed";
        var seedAt = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

        await using var db = _harness.CreateDbContext();

        foreach (var (userId, email, role) in new[]
        {
            (CustomerAUserId, "ca@example.cz", UserRole.Customer),
            (CustomerBUserId, "cb@example.cz", UserRole.Customer),
            (MakerAUserId, "ma@example.cz", UserRole.Maker),
            (MakerBUserId, "mb@example.cz", UserRole.Maker),
        })
        {
            var u = User.Create(
                id: userId, email: email, role: role,
                fullName: "U", countryCodePrimary: CountryCode,
                passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
            u.ConfirmEmail(seedAt);
            u.MarkCreated(seedActor, seedAt);
            db.Set<User>().Add(u);
        }

        foreach (var (addrId, makerId, userId, productId, slug, company, ico, title) in new[]
        {
            (AddressAId, MakerAId, MakerAUserId, ProductAId, "avast-a", "Avast A s.r.o.", "27074358", "Vase A"),
            (AddressBId, MakerBId, MakerBUserId, ProductBId, "avast-b", "Avast B s.r.o.", "12345670", "Vase B"),
        })
        {
            var address = AddressEntity.Create(
                id: addrId, street: "Pikrtova", houseNumber: "1737",
                city: "Praha", zip: "14000", countryCodeIso: CountryCode,
                auditCountryCode: CountryCode);
            address.MarkCreated(seedActor, seedAt);
            db.Set<AddressEntity>().Add(address);

            var m = MakerEntity.Create(
                id: makerId, userId: userId,
                registrationNumber: ico, vatId: null,
                companyName: company, legalForm: null,
                registeredAddressId: addrId,
                incorporatedOn: null, isActiveInRegistry: true,
                sourceRegistry: "ares",
                snapshotFetchedAt: seedAt, snapshotIsStale: false,
                countryCode: CountryCode, slug: slug);
            m.MarkVerified();
            m.UpdateProfile(bio: null, bankAccount: null, personalPickupEnabled: true, pickupNote: null);
            m.MarkCreated(seedActor, seedAt);
            db.Set<MakerEntity>().Add(m);

            if (db.Set<Category>().Local.All(c => c.Id != CategoryId))
            {
                var cat = Category.Create(
                    id: CategoryId, name: "3D tisk", slug: "3d-tisk",
                    icon: null, description: null, sortOrder: 10, countryCode: CountryCode);
                cat.MarkCreated(seedActor, seedAt);
                db.Set<Category>().Add(cat);
            }

            var p = Product.Create(
                id: productId, makerId: makerId, categoryId: CategoryId,
                title: title, description: null,
                price: new Money(50000, Currency),
                priceType: PriceType.Fixed, weightGrams: 300,
                countryCode: CountryCode);
            p.MarkCreated(seedActor, seedAt);
            db.Set<Product>().Add(p);
        }

        // Order A — owned by CustomerA + MakerA. Drive through Paid + Accepted + Shipped.
        var clock = new FixedClock(new DateTimeOffset(2026, 5, 5, 9, 0, 0, TimeSpan.Zero));
        var orderA = Order.Create(
            id: OrderAId, orderNumber: "M-CZ-20260001",
            customerUserId: CustomerAUserId, makerId: MakerAId, productId: ProductAId,
            contactName: "Anna", contactEmail: "ca@example.cz", contactPhone: "+420 723 456 789",
            productPriceAmountMinor: 50000, shippingPriceAmountMinor: 7900,
            platformFeeAmountMinor: 7500, makerPayoutAmountMinor: 50400,
            totalAmountMinor: 57900, currency: Currency, vatRateBp: 2100,
            shippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
            zasilkovnaPickupPointId: "pp-42",
            countryCode: CountryCode);
        orderA.MarkAsPaid(clock, "tx-A");
        orderA.Accept(clock);
        orderA.Ship(clock, "PKT-1234", 7, "https://tracking.packeta.com/ZPKT-1234");
        orderA.MarkCreated(seedActor, clock.UtcNow);
        db.Set<Order>().Add(orderA);

        // Order B — owned by CustomerB + MakerB.
        var orderB = Order.Create(
            id: OrderBId, orderNumber: "M-CZ-20260002",
            customerUserId: CustomerBUserId, makerId: MakerBId, productId: ProductBId,
            contactName: "Bob", contactEmail: "cb@example.cz", contactPhone: "+420 777 888 999",
            productPriceAmountMinor: 40000, shippingPriceAmountMinor: 7900,
            platformFeeAmountMinor: 6500, makerPayoutAmountMinor: 41400,
            totalAmountMinor: 47900, currency: Currency, vatRateBp: 2100,
            shippingMethod: ShippingMethod.PersonalPickup,
            zasilkovnaPickupPointId: null,
            countryCode: CountryCode);
        orderB.MarkCreated(seedActor, clock.UtcNow);
        db.Set<Order>().Add(orderB);

        if (includeInvoiceForA)
        {
            var invoice = Invoice.Issue(
                id: "inv-A",
                invoiceNumber: "FV-CZ-20260001",
                type: InvoiceType.Customer,
                orderId: OrderAId,
                payoutBatchId: null,
                makerId: MakerAId,
                issuerName: "JVM YORE s.r.o.",
                issuerIco: "12345678",
                issuerDic: null,
                issuerBankAccount: null,
                recipientName: "Anna",
                recipientEmail: "ca@example.cz",
                recipientTaxId: null,
                recipientVatId: null,
                issueDate: new DateOnly(2026, 5, 6),
                taxableSupplyDate: new DateOnly(2026, 5, 5),
                dueDate: new DateOnly(2026, 5, 20),
                invoicingMode: InvoicingMode.None,
                amountWithoutVatMinor: 57900,
                vatRateBp: 0,
                vatAmountMinor: 0,
                amountWithVatMinor: 57900,
                currency: Currency,
                countryCode: CountryCode);
            invoice.AttachPdfBlobPath("invoices/cz/orders/" + OrderAId + "/FV-CZ-20260001.pdf");
            invoice.MarkCreated(seedActor, clock.UtcNow);
            db.Set<Invoice>().Add(invoice);
        }

        await db.SaveChangesAsync();
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    // === Tests ===

    [Fact]
    public async Task GET_customer_detail_happy_path_returns_lifecycle_and_breakdown()
    {
        await SeedTwoOrdersAsync(includeInvoiceForA: false);

        using var client = _customerFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", IssueCustomerToken(CustomerAUserId, "ca@example.cz"));

        var response = await client.GetAsync($"/api/v1/orders/{OrderAId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<GetCustomerOrderDetails.GetCustomerOrderDetailsResponse>(JsonOpts);
        body.Should().NotBeNull();
        body!.Detail.OrderId.Should().Be(OrderAId);
        body.Detail.State.Should().Be(OrderState.Shipped);
        body.Detail.PaidAt.Should().NotBeNull();
        body.Detail.AcceptedAt.Should().NotBeNull();
        body.Detail.ShippedAt.Should().NotBeNull();
        body.Detail.DeliveredAt.Should().BeNull();
        body.Detail.CancelledAt.Should().BeNull();
        body.Detail.TotalAmountMinor.Should().Be(57900);
        body.Detail.ProductPriceMinor.Should().Be(50000);
        body.Detail.ShippingPriceMinor.Should().Be(7900);
        body.Detail.VatRateBp.Should().Be(2100);
        body.Detail.MakerName.Should().Be("Avast A s.r.o.");
        body.Detail.ProductTitle.Should().Be("Vase A");
        body.Detail.ShippingCarrierTrackingUrl.Should().Be("https://tracking.packeta.com/ZPKT-1234");
        body.Detail.InvoicePdfUrl.Should().BeNull();
    }

    [Fact]
    public async Task GET_customer_detail_cross_tenant_returns_404()
    {
        await SeedTwoOrdersAsync();

        using var client = _customerFactory.CreateClient();
        // Customer A asks for Customer B's order — must 404 (IDOR shield).
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", IssueCustomerToken(CustomerAUserId, "ca@example.cz"));

        var response = await client.GetAsync($"/api/v1/orders/{OrderBId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GET_customer_detail_InvoicePdfUrl_populated_when_invoice_exists()
    {
        await SeedTwoOrdersAsync(includeInvoiceForA: true);

        using var client = _customerFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", IssueCustomerToken(CustomerAUserId, "ca@example.cz"));

        var response = await client.GetAsync($"/api/v1/orders/{OrderAId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<GetCustomerOrderDetails.GetCustomerOrderDetailsResponse>(JsonOpts);
        body!.Detail.InvoicePdfUrl.Should().NotBeNullOrEmpty();
        body.Detail.InvoicePdfUrl.Should().Contain(OrderAId);
        // URL is a relative API path — never a raw blob path.
        body.Detail.InvoicePdfUrl.Should().StartWith("/api/");
        body.Detail.InvoicePdfUrl.Should().NotContain(".pdf");
    }

    [Fact]
    public async Task GET_maker_detail_happy_path_returns_payout_and_no_email()
    {
        await SeedTwoOrdersAsync();

        using var client = _makerFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", IssueMakerToken(MakerAUserId, "ma@example.cz"));

        var response = await client.GetAsync($"/api/v1/orders/{OrderAId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var raw = await response.Content.ReadAsStringAsync();
        // GDPR pin: no customerContactEmail / customerEmail anywhere in the response.
        raw.Should().NotContain("customerContactEmail", because: "T-0081 §A.2 GDPR lock");
        raw.Should().NotContain("customerEmail", because: "T-0081 §A.2 GDPR lock");

        var body = await response.Content.ReadFromJsonAsync<GetMakerOrderDetails.GetMakerOrderDetailsResponse>(JsonOpts);
        body!.Detail.MakerPayoutAmountMinor.Should().Be(50400);
        body.Detail.CustomerContactName.Should().Be("Anna");
        body.Detail.CustomerContactPhone.Should().Be("+420 723 456 789");
        body.Detail.ShippingCarrierRef.Should().Be("PKT-1234");
        body.Detail.ZasilkovnaPickupPointId.Should().Be("pp-42");
    }

    [Fact]
    public async Task GET_maker_detail_cross_maker_returns_404()
    {
        await SeedTwoOrdersAsync();

        using var client = _makerFactory.CreateClient();
        // Maker A asks for Maker B's order — must 404.
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", IssueMakerToken(MakerAUserId, "ma@example.cz"));

        var response = await client.GetAsync($"/api/v1/orders/{OrderBId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
