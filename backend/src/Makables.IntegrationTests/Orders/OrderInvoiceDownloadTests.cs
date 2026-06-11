using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Makables.Core.Domain.Categories;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Invoices;
using Makables.Core.Domain.Money;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Products;
using Makables.Core.Domain.Storage;
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
/// End-to-end coverage for the T-0088 invoice download endpoints on both
/// the customer host (<c>GET /api/v1/orders/{orderId}/invoice</c>) and
/// the maker host (same URL, different audience + ownership scope).
/// Harness mirrors <see cref="OrderAttachmentDownloadTests"/> (both
/// factories + <see cref="FakeBlobStorageClient"/>).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OrderInvoiceDownloadTests : IAsyncLifetime
{
    private const string TestKeyBase64 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
    private const string TestIssuer = "https://makables.test";
    private const string CountryCode = "CZ";
    private const string Currency = "CZK";

    private const string CustomerUserId = "user-customer-1";
    private const string OtherCustomerUserId = "user-customer-2";
    private const string MakerUserId = "user-maker-1";
    private const string MakerId = "maker-1";
    private const string AddressId = "addr-1";
    private const string CategoryId = "cat-1";
    private const string ProductId = "prod-1";
    private const string OrderId = "ord-1";
    private const string OrderWithoutInvoiceId = "ord-2";
    private const string InvoiceId = "inv-1";
    private const string InvoiceNumber = "FV-CZ-20260001";
    private const string InvoiceBlobPath = "cz/orders/ord-1/FV-CZ-20260001.pdf";

    private static readonly byte[] InvoiceBytes = BuildPdfBytes();

    private readonly PostgresHarness _harness;
    private readonly FakeBlobStorageClient _blobs = new();
    private WebApplicationFactory<Makables.Web.Customer.Program> _customerFactory = default!;
    private WebApplicationFactory<Makables.Web.Maker.Program> _makerFactory = default!;

    public OrderInvoiceDownloadTests(PostgresHarness harness)
    {
        _harness = harness;
    }

    public async Task InitializeAsync()
    {
        await _harness.ResetMutableTablesAsync();
        _customerFactory = new WebApplicationFactory<Makables.Web.Customer.Program>()
            .WithWebHostBuilder(builder => ConfigureBuilder(builder));
        _makerFactory = new WebApplicationFactory<Makables.Web.Maker.Program>()
            .WithWebHostBuilder(builder => ConfigureBuilder(builder));
    }

    public Task DisposeAsync()
    {
        _customerFactory?.Dispose();
        _makerFactory?.Dispose();
        return Task.CompletedTask;
    }

    private void ConfigureBuilder(IWebHostBuilder builder)
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

            var blobDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(IBlobStorageClient));
            if (blobDescriptor is not null)
            {
                services.Remove(blobDescriptor);
            }
            services.AddSingleton<IBlobStorageClient>(_blobs);
        });
    }

    // === Seed helpers ===

    private async Task SeedAsync()
    {
        await using var db = _harness.CreateDbContext();
        var seedActor = "test-seed";
        var seedAt = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

        var customer = BuildCustomerUser(CustomerUserId, "anna@example.cz");
        customer.MarkCreated(seedActor, seedAt);
        db.Set<User>().Add(customer);

        var otherCustomer = BuildCustomerUser(OtherCustomerUserId, "other@example.cz");
        otherCustomer.MarkCreated(seedActor, seedAt);
        db.Set<User>().Add(otherCustomer);

        var makerUser = User.Create(
            id: MakerUserId, email: "maker@example.cz", role: UserRole.Maker,
            fullName: "Maker User", countryCodePrimary: CountryCode,
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
            companyName: "Avast Software s.r.o.", legalForm: null,
            registeredAddressId: AddressId, incorporatedOn: null,
            isActiveInRegistry: true, sourceRegistry: "ares",
            snapshotFetchedAt: seedAt, snapshotIsStale: false,
            countryCode: CountryCode, slug: "avast");
        maker.MarkVerified();
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
            price: new Money(50000, Currency), priceType: PriceType.Fixed,
            weightGrams: 300, countryCode: CountryCode);
        product.MarkCreated(seedActor, seedAt);
        db.Set<Product>().Add(product);

        db.Set<Order>().Add(BuildOrder(OrderId, "M-CZ-20260042", seedActor, seedAt));
        db.Set<Order>().Add(BuildOrder(OrderWithoutInvoiceId, "M-CZ-20260043", seedActor, seedAt));

        // Invoice for ord-1 only; ord-2 stays invoice-less so the
        // not-yet-rendered 404 path has a real fixture.
        var invoice = Invoice.Issue(
            id: InvoiceId, invoiceNumber: InvoiceNumber,
            type: InvoiceType.Customer, orderId: OrderId, payoutBatchId: null,
            makerId: MakerId,
            issuerName: "JVM YORE s.r.o.", issuerIco: "12345678",
            issuerDic: null, issuerBankAccount: null,
            recipientName: "Anna", recipientEmail: "anna@example.cz",
            recipientTaxId: null, recipientVatId: null,
            issueDate: new DateOnly(2026, 5, 6),
            taxableSupplyDate: new DateOnly(2026, 5, 5),
            dueDate: new DateOnly(2026, 5, 20),
            invoicingMode: InvoicingMode.None,
            amountWithoutVatMinor: 57900, vatRateBp: 0,
            vatAmountMinor: 0, amountWithVatMinor: 57900,
            currency: Currency, countryCode: CountryCode);
        invoice.AttachPdfBlobPath(InvoiceBlobPath);
        invoice.MarkCreated(seedActor, seedAt);
        db.Set<Invoice>().Add(invoice);

        await db.SaveChangesAsync();

        // Seed the rendered-PDF bytes into the fake store so DownloadAsync
        // returns OK for ord-1's invoice.
        await _blobs.UploadAsync(
            BlobContainer.Invoices,
            InvoiceBlobPath,
            new MemoryStream(InvoiceBytes),
            "application/pdf",
            CancellationToken.None);
    }

    private static Order BuildOrder(
        string id, string orderNumber, string seedActor, DateTimeOffset seedAt)
    {
        var order = Order.Create(
            id: id, orderNumber: orderNumber,
            customerUserId: CustomerUserId, makerId: MakerId, productId: ProductId,
            contactName: "Anna", contactEmail: "a@b.cz", contactPhone: "+420 723 456 789",
            productPriceAmountMinor: 50000, shippingPriceAmountMinor: 7900,
            platformFeeAmountMinor: 7500, makerPayoutAmountMinor: 50400,
            totalAmountMinor: 57900, currency: Currency, vatRateBp: 2100,
            shippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
            zasilkovnaPickupPointId: "pp-42", countryCode: CountryCode);
        order.MarkCreated(seedActor, seedAt);
        return order;
    }

    private static User BuildCustomerUser(string id, string email)
    {
        var user = User.Create(
            id: id, email: email, role: UserRole.Customer,
            fullName: "Customer", countryCodePrimary: CountryCode,
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        user.ConfirmEmail(new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero));
        return user;
    }

    private string IssueCustomerToken(string userId)
    {
        var issuer = new JwtIssuer(Options.Create(new JwtOptions
        {
            Issuer = TestIssuer,
            SigningKeyBase64 = TestKeyBase64,
            AccessTokenLifetime = TimeSpan.FromMinutes(15),
        }));
        var user = BuildCustomerUser(userId, $"{userId}@example.cz");
        return issuer.Issue(user, MakablesAudiences.Customer, DateTimeOffset.UtcNow).Token;
    }

    private string IssueMakerToken(string userId)
    {
        var issuer = new JwtIssuer(Options.Create(new JwtOptions
        {
            Issuer = TestIssuer,
            SigningKeyBase64 = TestKeyBase64,
            AccessTokenLifetime = TimeSpan.FromMinutes(15),
        }));
        var user = User.Create(
            id: userId, email: $"{userId}@example.cz", role: UserRole.Maker,
            fullName: "Maker", countryCodePrimary: CountryCode,
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        user.ConfirmEmail(new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero));
        return issuer.Issue(user, MakablesAudiences.Maker, DateTimeOffset.UtcNow).Token;
    }

    private static byte[] BuildPdfBytes()
    {
        var header = "%PDF-1.7\n"u8.ToArray();
        var filler = new byte[512];
        return header.Concat(filler).ToArray();
    }

    // === Tests ===

    [Fact]
    public async Task GET_invoice_streams_pdf_for_owning_customer_and_maker()
    {
        await SeedAsync();

        // Customer host as the owning customer.
        using (var client = _customerFactory.CreateClient())
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", IssueCustomerToken(CustomerUserId));

            var response = await client.GetAsync($"/api/v1/orders/{OrderId}/invoice");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
            response.Headers.CacheControl!.ToString().Should().Contain("private")
                .And.Contain("no-store");
            response.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
            response.Content.Headers.ContentDisposition.FileName.Should()
                .Contain($"faktura-{InvoiceNumber}.pdf");

            var body = await response.Content.ReadAsByteArrayAsync();
            body.Should().Equal(InvoiceBytes);
        }

        // Maker host as the assigned maker — identical shape (AC-2).
        using (var client = _makerFactory.CreateClient())
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", IssueMakerToken(MakerUserId));

            var response = await client.GetAsync($"/api/v1/orders/{OrderId}/invoice");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
            response.Headers.CacheControl!.ToString().Should().Contain("private")
                .And.Contain("no-store");
            response.Content.Headers.ContentDisposition!.FileName.Should()
                .Contain($"faktura-{InvoiceNumber}.pdf");

            var body = await response.Content.ReadAsByteArrayAsync();
            body.Should().Equal(InvoiceBytes);
        }
    }

    [Fact]
    public async Task GET_invoice_404_paths_are_oracle_free()
    {
        await SeedAsync();
        using var client = _customerFactory.CreateClient();

        // (a) Customer B probes customer A's order → 404 order.notFound.
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", IssueCustomerToken(OtherCustomerUserId));
        var crossTenant = await client.GetAsync($"/api/v1/orders/{OrderId}/invoice");
        crossTenant.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await crossTenant.Content.ReadAsStringAsync())
            .Should().Contain($"\"code\":\"{BusinessErrorMessage.OrderNotFound}\"");

        // (b) Owner probes an owned order with NO invoice row →
        //     404 invoice.notYetRendered.
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", IssueCustomerToken(CustomerUserId));
        var noInvoice = await client.GetAsync($"/api/v1/orders/{OrderWithoutInvoiceId}/invoice");
        noInvoice.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await noInvoice.Content.ReadAsStringAsync())
            .Should().Contain($"\"code\":\"{BusinessErrorMessage.InvoiceNotYetRendered}\"");

        // (c) Unknown orderId → 404 order.notFound — same shape as (a),
        //     no existence oracle.
        var unknown = await client.GetAsync("/api/v1/orders/ord-unknown/invoice");
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await unknown.Content.ReadAsStringAsync())
            .Should().Contain($"\"code\":\"{BusinessErrorMessage.OrderNotFound}\"");
    }
}
