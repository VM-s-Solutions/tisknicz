using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Makables.Core.Domain.Categories;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
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
/// End-to-end coverage for the T-0064 download endpoints on both the
/// customer host (<c>GET /api/v1/orders/{orderId}/attachments/{attachmentId}</c>)
/// and the maker host (same URL, different audience + scope).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OrderAttachmentDownloadTests : IAsyncLifetime
{
    private const string TestKeyBase64 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
    private const string TestIssuer = "https://makables.test";
    private const string CountryCode = "CZ";
    private const string Currency = "CZK";

    private const string CustomerUserId = "user-customer-1";
    private const string OtherCustomerUserId = "user-customer-2";
    private const string MakerUserId = "user-maker-1";
    private const string OtherMakerUserId = "user-maker-2";
    private const string MakerId = "maker-1";
    private const string OtherMakerId = "maker-2";
    private const string AddressId = "addr-1";
    private const string OtherAddressId = "addr-2";
    private const string CategoryId = "cat-1";
    private const string ProductId = "prod-1";
    private const string OrderId = "ord-1";
    private const string AttachmentId = "att-seed";
    private const string AttachmentBlobPath = "cz/orders/ord-1/seed.pdf";
    private const string AttachmentContentType = "application/pdf";
    private const string AttachmentFilename = "spec.pdf";

    private static readonly byte[] AttachmentBytes = BuildPdfBytes();

    private readonly PostgresHarness _harness;
    private readonly FakeBlobStorageClient _blobs = new();
    private WebApplicationFactory<Makables.Web.Customer.Program> _customerFactory = default!;
    private WebApplicationFactory<Makables.Web.Maker.Program> _makerFactory = default!;

    public OrderAttachmentDownloadTests(PostgresHarness harness)
    {
        _harness = harness;
    }

    public async Task InitializeAsync()
    {
        await _harness.ResetMutableTablesAsync();
        _customerFactory = BuildCustomerFactory();
        _makerFactory = BuildMakerFactory();
    }

    public Task DisposeAsync()
    {
        _customerFactory?.Dispose();
        _makerFactory?.Dispose();
        return Task.CompletedTask;
    }

    private WebApplicationFactory<Makables.Web.Customer.Program> BuildCustomerFactory() =>
        new WebApplicationFactory<Makables.Web.Customer.Program>()
            .WithWebHostBuilder(builder => ConfigureBuilder(builder));

    private WebApplicationFactory<Makables.Web.Maker.Program> BuildMakerFactory() =>
        new WebApplicationFactory<Makables.Web.Maker.Program>()
            .WithWebHostBuilder(builder => ConfigureBuilder(builder));

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

    private async Task SeedAsync(bool softDeleteOrder = false)
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
            id: MakerUserId,
            email: "maker@example.cz",
            role: UserRole.Maker,
            fullName: "Maker User",
            countryCodePrimary: CountryCode,
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        makerUser.ConfirmEmail(seedAt);
        makerUser.MarkCreated(seedActor, seedAt);
        db.Set<User>().Add(makerUser);

        var otherMakerUser = User.Create(
            id: OtherMakerUserId,
            email: "other-maker@example.cz",
            role: UserRole.Maker,
            fullName: "Other Maker User",
            countryCodePrimary: CountryCode,
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        otherMakerUser.ConfirmEmail(seedAt);
        otherMakerUser.MarkCreated(seedActor, seedAt);
        db.Set<User>().Add(otherMakerUser);

        var address = AddressEntity.Create(
            id: AddressId, street: "Pikrtova", houseNumber: "1737",
            city: "Praha", zip: "14000", countryCodeIso: CountryCode,
            auditCountryCode: CountryCode);
        address.MarkCreated(seedActor, seedAt);
        db.Set<AddressEntity>().Add(address);

        var otherAddress = AddressEntity.Create(
            id: OtherAddressId, street: "Other", houseNumber: "1",
            city: "Brno", zip: "60200", countryCodeIso: CountryCode,
            auditCountryCode: CountryCode);
        otherAddress.MarkCreated(seedActor, seedAt);
        db.Set<AddressEntity>().Add(otherAddress);

        var maker = MakerEntity.Create(
            id: MakerId, userId: MakerUserId,
            registrationNumber: "27074358", vatId: null,
            companyName: "Avast Software s.r.o.", legalForm: null,
            registeredAddressId: AddressId, incorporatedOn: null,
            isActiveInRegistry: true, sourceRegistry: "ares",
            snapshotFetchedAt: seedAt, snapshotIsStale: false,
            countryCode: CountryCode, slug: "avast");
        maker.MarkVerified();
        maker.UpdateProfile(bio: null, bankAccount: null, personalPickupEnabled: true, pickupNote: null);
        maker.MarkCreated(seedActor, seedAt);
        db.Set<MakerEntity>().Add(maker);

        var otherMaker = MakerEntity.Create(
            id: OtherMakerId, userId: OtherMakerUserId,
            registrationNumber: "27074359", vatId: null,
            companyName: "Other s.r.o.", legalForm: null,
            registeredAddressId: OtherAddressId, incorporatedOn: null,
            isActiveInRegistry: true, sourceRegistry: "ares",
            snapshotFetchedAt: seedAt, snapshotIsStale: false,
            countryCode: CountryCode, slug: "other");
        otherMaker.MarkVerified();
        otherMaker.MarkCreated(seedActor, seedAt);
        db.Set<MakerEntity>().Add(otherMaker);

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

        var order = Order.Create(
            id: OrderId, orderNumber: "M-CZ-20260042",
            customerUserId: CustomerUserId, makerId: MakerId, productId: ProductId,
            contactName: "Anna", contactEmail: "a@b.cz", contactPhone: "+420 723 456 789",
            productPriceAmountMinor: 50000, shippingPriceAmountMinor: 7900,
            platformFeeAmountMinor: 7500, makerPayoutAmountMinor: 50400,
            totalAmountMinor: 57900, currency: Currency, vatRateBp: 2100,
            shippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
            zasilkovnaPickupPointId: "pp-42", countryCode: CountryCode);
        var attachment = OrderAttachment.Create(
            id: AttachmentId,
            orderId: OrderId,
            blobPath: AttachmentBlobPath,
            originalFilename: AttachmentFilename,
            contentType: AttachmentContentType,
            sizeBytes: AttachmentBytes.LongLength,
            uploadedByUserId: CustomerUserId,
            countryCode: CountryCode);
        attachment.MarkCreated(seedActor, seedAt);
        order.AddAttachment(attachment);
        order.MarkCreated(seedActor, seedAt);
        if (softDeleteOrder)
        {
            order.MarkDeactivated("admin-1", seedAt);
        }
        db.Set<Order>().Add(order);

        await db.SaveChangesAsync();

        // Seed the blob bytes into the fake store so DownloadAsync returns OK.
        await _blobs.UploadAsync(
            BlobContainer.OrderAttachments,
            AttachmentBlobPath,
            new MemoryStream(AttachmentBytes),
            AttachmentContentType,
            CancellationToken.None);
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
    public async Task Customer_download_returns_200_with_correct_headers_and_body()
    {
        await SeedAsync();
        using var client = _customerFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", IssueCustomerToken(CustomerUserId));

        var response = await client.GetAsync(
            $"/api/v1/orders/{OrderId}/attachments/{AttachmentId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be(AttachmentContentType);
        response.Headers.CacheControl!.ToString().Should().Contain("private")
            .And.Contain("no-store");
        response.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        response.Content.Headers.ContentDisposition.FileName.Should()
            .Contain(AttachmentFilename);
        // ETag should be present (the fake assigns one).
        response.Headers.ETag.Should().NotBeNull();

        var body = await response.Content.ReadAsByteArrayAsync();
        body.Should().Equal(AttachmentBytes);
    }

    [Fact]
    public async Task Customer_download_returns_304_when_If_None_Match_matches_ETag()
    {
        await SeedAsync();
        using var client = _customerFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", IssueCustomerToken(CustomerUserId));

        // First, fetch to learn the ETag.
        var first = await client.GetAsync(
            $"/api/v1/orders/{OrderId}/attachments/{AttachmentId}");
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var etag = first.Headers.ETag!.Tag;
        etag.Should().NotBeNullOrEmpty();

        // Second request, presenting the ETag. Expect 304 + no body.
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"/api/v1/orders/{OrderId}/attachments/{AttachmentId}");
        request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        var second = await client.SendAsync(request);

        second.StatusCode.Should().Be(HttpStatusCode.NotModified);
        var body = await second.Content.ReadAsByteArrayAsync();
        body.Should().BeEmpty();
    }

    [Fact]
    public async Task Customer_download_by_other_customer_returns_404_OrderAttachmentNotFound()
    {
        await SeedAsync();
        using var client = _customerFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", IssueCustomerToken(OtherCustomerUserId));

        var response = await client.GetAsync(
            $"/api/v1/orders/{OrderId}/attachments/{AttachmentId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain($"\"code\":\"{BusinessErrorMessage.OrderAttachmentNotFound}\"");
    }

    [Fact]
    public async Task Customer_download_on_soft_deleted_order_returns_404()
    {
        await SeedAsync(softDeleteOrder: true);
        using var client = _customerFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", IssueCustomerToken(CustomerUserId));

        var response = await client.GetAsync(
            $"/api/v1/orders/{OrderId}/attachments/{AttachmentId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain($"\"code\":\"{BusinessErrorMessage.OrderAttachmentNotFound}\"");
    }

    [Fact]
    public async Task Maker_assigned_to_order_can_download_attachment()
    {
        await SeedAsync();
        using var client = _makerFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", IssueMakerToken(MakerUserId));

        var response = await client.GetAsync(
            $"/api/v1/orders/{OrderId}/attachments/{AttachmentId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be(AttachmentContentType);
        response.Headers.CacheControl!.ToString().Should().Contain("private")
            .And.Contain("no-store");
        var body = await response.Content.ReadAsByteArrayAsync();
        body.Should().Equal(AttachmentBytes);
    }

    [Fact]
    public async Task Maker_not_assigned_to_order_gets_404()
    {
        await SeedAsync();
        using var client = _makerFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", IssueMakerToken(OtherMakerUserId));

        var response = await client.GetAsync(
            $"/api/v1/orders/{OrderId}/attachments/{AttachmentId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain($"\"code\":\"{BusinessErrorMessage.OrderAttachmentNotFound}\"");
    }
}
