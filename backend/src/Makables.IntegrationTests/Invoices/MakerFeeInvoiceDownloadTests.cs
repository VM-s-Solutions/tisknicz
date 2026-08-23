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
using Makables.Core.Domain.Payouts;
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
using NSubstitute;
using AddressEntity = Makables.Core.Domain.Addresses.Address;
using MakerEntity = Makables.Core.Domain.Makers.Maker;

namespace Makables.IntegrationTests.Invoices;

/// <summary>
/// T-0112a maker Fee-invoice download on the Maker host
/// (<c>GET /api/v1/maker/files/invoices/{invoiceId}</c>). Pins: owning-maker
/// happy path (byte-equal + private/no-store + faktura-{n}.pdf disposition);
/// and the oracle-free 404 family (cross-maker, Customer-invoice-via-this-route
/// type gate, unknown id, null PdfBlobPath). Real Postgres + in-memory blob.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class MakerFeeInvoiceDownloadTests : IAsyncLifetime
{
    private const string TestKeyBase64 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
    private const string TestIssuer = "https://makables.test";
    private const string CountryCode = "CZ";
    private const string Currency = "CZK";
    private const string BatchId = "pb-1";
    private const string FeeInvoiceNumber = "FV-CZ-20260042";
    private const string FeeBlobPath = "cz/payouts/pb-1/FV-CZ-20260042.pdf";

    private static readonly byte[] FeePdfBytes = "%PDF-1.7 fee-invoice-e2e"u8.ToArray();

    private readonly PostgresHarness _harness;
    private readonly FakeBlobStorageClient _blobs = new();
    private WebApplicationFactory<Makables.Web.Maker.Program> _factory = default!;

    public MakerFeeInvoiceDownloadTests(PostgresHarness harness) => _harness = harness;

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

    private string IssueMakerToken(string userId, string email)
    {
        var issuer = new JwtIssuer(Options.Create(new JwtOptions
        {
            Issuer = TestIssuer, SigningKeyBase64 = TestKeyBase64, AccessTokenLifetime = TimeSpan.FromMinutes(15),
        }));
        var user = User.Create(userId, email, UserRole.Maker, "Maker", CountryCode,
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        user.ConfirmEmail(new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero));
        return issuer.Issue(user, MakablesAudiences.Maker, DateTimeOffset.UtcNow).Token;
    }

    private HttpClient MakerClient(string userId, string email)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", IssueMakerToken(userId, email));
        return client;
    }

    /// <summary>
    /// Seed makerA (owns the Fee invoice + blob + a Customer invoice) and
    /// makerB. <c>feeBlobPath</c> null → not-yet-rendered case.
    /// </summary>
    private async Task SeedAsync(bool withFeeBlob)
    {
        await using var db = _harness.CreateDbContext();
        const string actor = "test-seed";
        var at = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(at.AddDays(10));

        var customer = User.Create("user-customer-1", "anna@example.cz", UserRole.Customer, "Anna", CountryCode,
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        customer.ConfirmEmail(at); customer.MarkCreated(actor, at);
        db.Set<User>().Add(customer);

        var category = Category.Create("cat-1", "3D tisk", "3d-tisk", null, null, 10, CountryCode);
        category.MarkCreated(actor, at);
        db.Set<Category>().Add(category);

        foreach (var (makerId, userId, addrId, ico) in new[]
        {
            ("maker-a", "user-maker-a", "addr-a", "27074358"),
            ("maker-b", "user-maker-b", "addr-b", "45317054"),
        })
        {
            var mu = User.Create(userId, $"{makerId}@example.cz", UserRole.Maker, makerId, CountryCode,
                passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
            mu.ConfirmEmail(at); mu.MarkCreated(actor, at);
            db.Set<User>().Add(mu);

            var addr = AddressEntity.Create(addrId, "Pikrtova", "1737", "Praha", "14000", CountryCode, CountryCode);
            addr.MarkCreated(actor, at);
            db.Set<AddressEntity>().Add(addr);

            var maker = MakerEntity.Create(makerId, userId, ico, null, $"{makerId} s.r.o.", null,
                addrId, null, true, "ares", at, false, CountryCode, slug: makerId);
            maker.UpdateProfile(bio: null, bankAccount: "111/0100", personalPickupEnabled: null, pickupNote: null);
            maker.MarkCreated(actor, at);
            db.Set<MakerEntity>().Add(maker);

            var product = Product.Create($"prod-{makerId}", makerId, "cat-1", "Vase", null,
                new Money(50000, Currency), PriceType.Fixed, 300, CountryCode);
            product.MarkCreated(actor, at);
            db.Set<Product>().Add(product);
        }

        var batch = PayoutBatch.Create(BatchId, "VYP-CZ-2026-W23", CountryCode,
            totalAmountMinor: 25500, currency: Currency, orderCount: 1, makerCount: 1,
            excludedPartiallyRefundedOrderCount: 0, excludedNoBankAccountOrderCount: 0,
            excludedNoBankAccountMakerCount: 0);
        batch.Complete(clock, "WIRE-SEED", new DateOnly(2026, 6, 11), "user-admin-1");
        batch.MarkCreated(actor, at);
        db.Set<PayoutBatch>().Add(batch);

        // makerA's Fee invoice (the target of this route).
        var fee = Invoice.Issue("inv-fee-a", FeeInvoiceNumber, InvoiceType.Fee,
            orderId: null, payoutBatchId: BatchId, makerId: "maker-a",
            orderNumber: "OBJ-20260819-0001",
            issuerName: "JVM YORE s.r.o.", issuerIco: "12345678", issuerDic: null,
            issuerBankAccount: null, issuerAddress: null, recipientName: "maker-a s.r.o.",
            recipientEmail: "maker-a@example.cz", recipientTaxId: "27074358", recipientVatId: null,
            issueDate: new DateOnly(2026, 6, 11), taxableSupplyDate: null,
            dueDate: new DateOnly(2026, 6, 25), invoicingMode: InvoicingMode.None,
            amountWithoutVatMinor: 4500, vatRateBp: 0, vatAmountMinor: 0,
            amountWithVatMinor: 4500, currency: Currency, countryCode: CountryCode);
        if (withFeeBlob) fee.AttachPdfBlobPath(FeeBlobPath);
        fee.MarkCreated(actor, at);
        db.Set<Invoice>().Add(fee);

        // makerA's Customer invoice (denormalised to makerA) — the type-gate case.
        var order = Order.Create("ord-9", "M-CZ-ord-9", "user-customer-1", "maker-a", "prod-maker-a",
            "Anna", "anna@example.cz", "+420 723 456 789",
            50000, 0, 7500, 42500, 50000, Currency, 2100,
            ShippingMethod.ZasilkovnaPickupPoint, "pp-1", CountryCode);
        order.MarkAsPaid(clock, "tx-9");
        order.MarkCreated(actor, at);
        db.Set<Order>().Add(order);

        var customerInv = Invoice.Issue("inv-cust-a", "FV-CZ-20260001", InvoiceType.Customer,
            orderId: "ord-9", payoutBatchId: null, makerId: "maker-a",
            orderNumber: "OBJ-20260819-0001",
            issuerName: "JVM YORE s.r.o.", issuerIco: "12345678", issuerDic: null,
            issuerBankAccount: null, issuerAddress: null, recipientName: "Anna", recipientEmail: "anna@example.cz",
            recipientTaxId: null, recipientVatId: null,
            issueDate: new DateOnly(2026, 5, 6), taxableSupplyDate: new DateOnly(2026, 5, 5),
            dueDate: new DateOnly(2026, 5, 20), invoicingMode: InvoicingMode.None,
            amountWithoutVatMinor: 50000, vatRateBp: 0, vatAmountMinor: 0,
            amountWithVatMinor: 50000, currency: Currency, countryCode: CountryCode);
        customerInv.AttachPdfBlobPath("cz/orders/ord-9/FV-CZ-20260001.pdf");
        customerInv.MarkCreated(actor, at);
        db.Set<Invoice>().Add(customerInv);

        await db.SaveChangesAsync();

        if (withFeeBlob)
        {
            using var content = new MemoryStream(FeePdfBytes, writable: false);
            await _blobs.UploadAsync(BlobContainer.Invoices, FeeBlobPath, content, "application/pdf", CancellationToken.None);
        }
    }

    [Fact]
    public async Task GET_fee_invoice_streams_pdf_for_owning_maker()
    {
        await SeedAsync(withFeeBlob: true);
        using var client = MakerClient("user-maker-a", "maker-a@example.cz");

        var response = await client.GetAsync("/api/v1/maker/files/invoices/inv-fee-a");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        (await response.Content.ReadAsByteArrayAsync()).Should().Equal(FeePdfBytes);
        response.Headers.CacheControl!.ToString().Should().Contain("no-store");
        response.Content.Headers.ContentDisposition!.ToString()
            .Should().Contain($"faktura-{FeeInvoiceNumber}.pdf");
    }

    [Fact]
    public async Task GET_fee_invoice_404_paths_are_oracle_free()
    {
        await SeedAsync(withFeeBlob: true);

        // (a) makerB requests makerA's Fee invoice → 404 order.notFound.
        using var clientB = MakerClient("user-maker-b", "maker-b@example.cz");
        var crossMaker = await clientB.GetAsync("/api/v1/maker/files/invoices/inv-fee-a");
        crossMaker.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await AssertCode(crossMaker, BusinessErrorMessage.OrderNotFound);

        using var clientA = MakerClient("user-maker-a", "maker-a@example.cz");

        // (b) makerA requests a Customer-invoice id via the Fee route → 404
        // order.notFound (type gate — no oracle the Customer invoice exists).
        var typeGate = await clientA.GetAsync("/api/v1/maker/files/invoices/inv-cust-a");
        typeGate.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await AssertCode(typeGate, BusinessErrorMessage.OrderNotFound);

        // (c) unknown invoice id → 404 order.notFound (same shape as (a)).
        var unknown = await clientA.GetAsync("/api/v1/maker/files/invoices/does-not-exist");
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await AssertCode(unknown, BusinessErrorMessage.OrderNotFound);
    }

    [Fact]
    public async Task GET_fee_invoice_with_null_blob_path_returns_not_yet_rendered()
    {
        await SeedAsync(withFeeBlob: false);
        using var client = MakerClient("user-maker-a", "maker-a@example.cz");

        var response = await client.GetAsync("/api/v1/maker/files/invoices/inv-fee-a");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await AssertCode(response, BusinessErrorMessage.InvoiceNotYetRendered);
    }

    private static async Task AssertCode(HttpResponseMessage response, string expectedCode)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("code").GetString().Should().Be(expectedCode);
    }
}
