using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Makables.Core.Domain.Auditing;
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

namespace Makables.IntegrationTests.Admin;

/// <summary>
/// T-0126 / Q-0026 admin invoice-PDF download on the Admin host
/// (<c>GET /api/v1/admin-invoices/{invoiceId}/pdf</c>). Pins: admin streams ANY
/// invoice byte-equal with the faktura-{n}.pdf disposition + private/no-store
/// (Unscoped — no owner scoping); and a customer/maker JWT cannot replay
/// (cross-host 401). Real Postgres + in-memory blob.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AdminInvoiceDownloadIntegrationTests : IAsyncLifetime
{
    private const string TestKeyBase64 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
    private const string TestIssuer = "https://makables.test";
    private const string CountryCode = "CZ";
    private const string Currency = "CZK";
    private const string AdminUserId = "user-admin-1";
    private const string InvoiceId = "inv-cust-1";
    private const string InvoiceNumber = "FV-CZ-20260001";
    private const string BlobPath = "cz/orders/ord-1/FV-CZ-20260001.pdf";

    private static readonly byte[] PdfBytes = "%PDF-1.7 admin-invoice-e2e"u8.ToArray();
    private static readonly DateTimeOffset SeedAt = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgresHarness _harness;
    private readonly FakeBlobStorageClient _blobs = new();
    private WebApplicationFactory<Makables.Web.Admin.Program> _factory = default!;

    public AdminInvoiceDownloadIntegrationTests(PostgresHarness harness) => _harness = harness;

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
                    if (dbContextDescriptor is not null) services.Remove(dbContextDescriptor);
                    services.AddDbContext<MakablesDbContext>(o => o.UseNpgsql(_harness.ConnectionString));

                    var blobDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IBlobStorageClient));
                    if (blobDescriptor is not null) services.Remove(blobDescriptor);
                    services.AddSingleton<IBlobStorageClient>(_blobs);
                });
            });
    }

    public Task DisposeAsync()
    {
        _factory?.Dispose();
        return Task.CompletedTask;
    }

    private string IssueToken(string audience, UserRole role)
    {
        var issuer = new JwtIssuer(Options.Create(new JwtOptions
        {
            Issuer = TestIssuer, SigningKeyBase64 = TestKeyBase64, AccessTokenLifetime = TimeSpan.FromMinutes(15),
        }));
        var user = User.Create($"user-{role}", $"{role}@makables.cz", role, role.ToString(), CountryCode,
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        user.ConfirmEmail(SeedAt);
        return issuer.Issue(user, audience, DateTimeOffset.UtcNow).Token;
    }

    private HttpClient ClientWith(string audience, UserRole role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", IssueToken(audience, role));
        return client;
    }

    private async Task SeedAsync()
    {
        await using var db = _harness.CreateDbContext();
        const string actor = "test-seed";

        var admin = User.Create(AdminUserId, "admin@makables.cz", UserRole.Admin, "Admin", CountryCode,
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        admin.ConfirmEmail(SeedAt); admin.MarkCreated(actor, SeedAt);
        db.Set<User>().Add(admin);

        var customer = User.Create("user-customer-1", "anna@example.cz", UserRole.Customer, "Anna", CountryCode,
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        customer.ConfirmEmail(SeedAt); customer.MarkCreated(actor, SeedAt);
        db.Set<User>().Add(customer);

        var category = Category.Create("cat-1", "3D tisk", "3d-tisk", null, null, 10, CountryCode);
        category.MarkCreated(actor, SeedAt);
        db.Set<Category>().Add(category);

        var mu = User.Create("user-maker-1", "maker-1@example.cz", UserRole.Maker, "maker-1", CountryCode,
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        mu.ConfirmEmail(SeedAt); mu.MarkCreated(actor, SeedAt);
        db.Set<User>().Add(mu);

        var addr = AddressEntity.Create("addr-1", "Pikrtova", "1737", "Praha", "14000", CountryCode, CountryCode);
        addr.MarkCreated(actor, SeedAt);
        db.Set<AddressEntity>().Add(addr);

        var maker = MakerEntity.Create("maker-1", "user-maker-1", "27074358", null, "maker-1 s.r.o.", null,
            "addr-1", null, true, "ares", SeedAt, false, CountryCode, slug: "maker-1");
        maker.MarkCreated(actor, SeedAt);
        db.Set<MakerEntity>().Add(maker);

        var product = Product.Create("prod-1", "maker-1", "cat-1", "Vase", null,
            new Money(50000, Currency), PriceType.Fixed, 300, CountryCode);
        product.MarkCreated(actor, SeedAt);
        db.Set<Product>().Add(product);

        var order = Order.Create("ord-1", "M-CZ-ord-1", "user-customer-1", "maker-1", "prod-1",
            "Anna", "anna@example.cz", "+420 723 456 789",
            50000, 0, 7500, 42500, 50000, Currency, 2100,
            ShippingMethod.ZasilkovnaPickupPoint, "pp-1", CountryCode);
        order.MarkCreated(actor, SeedAt);
        db.Set<Order>().Add(order);

        var invoice = Invoice.Issue(InvoiceId, InvoiceNumber, InvoiceType.Customer,
            orderId: "ord-1", payoutBatchId: null, makerId: "maker-1",
            orderNumber: "OBJ-20260819-0001",
            issuerName: "JVM YORE s.r.o.", issuerIco: "12345678", issuerDic: null,
            issuerBankAccount: null, issuerAddress: null, recipientName: "Anna", recipientEmail: "anna@example.cz",
            recipientTaxId: null, recipientVatId: null,
            issueDate: new DateOnly(2026, 5, 6), taxableSupplyDate: new DateOnly(2026, 5, 5),
            dueDate: new DateOnly(2026, 5, 20), invoicingMode: InvoicingMode.None,
            amountWithoutVatMinor: 50000, vatRateBp: 0, vatAmountMinor: 0,
            amountWithVatMinor: 50000, currency: Currency, countryCode: CountryCode);
        invoice.AttachPdfBlobPath(BlobPath);
        invoice.MarkCreated(actor, SeedAt);
        db.Set<Invoice>().Add(invoice);

        await db.SaveChangesAsync();

        using var content = new MemoryStream(PdfBytes, writable: false);
        await _blobs.UploadAsync(BlobContainer.Invoices, BlobPath, content, "application/pdf", CancellationToken.None);
    }

    [Fact]
    public async Task GET_admin_invoice_streams_pdf_unscoped()
    {
        await SeedAsync();
        using var client = ClientWith(MakablesAudiences.Admin, UserRole.Admin);

        var response = await client.GetAsync($"/api/v1/admin-invoices/{InvoiceId}/pdf");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        (await response.Content.ReadAsByteArrayAsync()).Should().Equal(PdfBytes);
        response.Headers.CacheControl!.ToString().Should().Contain("no-store");
        response.Content.Headers.ContentDisposition!.ToString()
            .Should().Contain($"faktura-{InvoiceNumber}.pdf");
    }

    [Fact]
    public async Task GET_admin_invoice_with_customer_or_maker_jwt_is_401()
    {
        await SeedAsync();

        using var customerClient = ClientWith(MakablesAudiences.Customer, UserRole.Customer);
        var customerResp = await customerClient.GetAsync($"/api/v1/admin-invoices/{InvoiceId}/pdf");
        customerResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var makerClient = ClientWith(MakablesAudiences.Maker, UserRole.Maker);
        var makerResp = await makerClient.GetAsync($"/api/v1/admin-invoices/{InvoiceId}/pdf");
        makerResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // The admin token in this file is minted for User.Create($"user-{role}")
    // (see IssueToken), so the JWT `sub` — and therefore the audited actor —
    // is "user-Admin", NOT the seeded AdminUserId constant.
    private const string AdminTokenSub = "user-Admin";

    [Fact]
    public async Task GET_admin_invoice_writes_one_read_audit_row_on_the_200_path()
    {
        await SeedAsync();
        using var client = ClientWith(MakablesAudiences.Admin, UserRole.Admin);

        var response = await client.GetAsync($"/api/v1/admin-invoices/{InvoiceId}/pdf");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = _harness.CreateDbContext();
        var rows = await db.Set<AdminAuditLogEntry>()
            .IgnoreQueryFilters()
            .Where(e => e.ActionCode == "invoice.pdf.download")
            .ToListAsync();

        rows.Should().ContainSingle();
        var row = rows[0];
        row.TargetEntity.Should().Be("invoice");
        row.TargetId.Should().Be(InvoiceId);
        row.AdminUserId.Should().Be(AdminTokenSub);
        row.AdminUserId.Should().NotBe("system");
        row.BeforeJson.Should().BeNull();
        row.AfterJson.Should().BeNull();
    }

    [Fact]
    public async Task GET_admin_invoice_unknown_id_writes_no_read_audit_row()
    {
        await SeedAsync();
        using var client = ClientWith(MakablesAudiences.Admin, UserRole.Admin);

        var response = await client.GetAsync("/api/v1/admin-invoices/inv-does-not-exist/pdf");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var db = _harness.CreateDbContext();
        var count = await db.Set<AdminAuditLogEntry>()
            .IgnoreQueryFilters()
            .CountAsync(e => e.ActionCode == "invoice.pdf.download");

        count.Should().Be(0, "a 404 not-rendered is not a disclosure — no read audit");
    }
}
