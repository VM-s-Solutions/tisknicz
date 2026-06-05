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
/// End-to-end coverage for the T-0064 customer-host upload endpoint
/// (<c>POST /api/v1/orders/{orderId}/attachments</c>). Real Postgres
/// (via <see cref="PostgresHarness"/>) + a fake
/// <see cref="IBlobStorageClient"/> (<see cref="FakeBlobStorageClient"/>)
/// so the body and headers traverse the real pipeline.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OrderAttachmentUploadTests : IAsyncLifetime
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

    private static readonly byte[] PdfBytes = BuildPdfBytes();
    private static readonly byte[] JpegBytes = BuildJpegBytes();
    private static readonly byte[] PngBytes = BuildPngBytes();
    private static readonly byte[] WebpBytes = BuildWebpBytes();

    private readonly PostgresHarness _harness;
    private readonly FakeBlobStorageClient _blobs = new();
    private WebApplicationFactory<Makables.Web.Customer.Program> _factory = default!;

    public OrderAttachmentUploadTests(PostgresHarness harness)
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

                    // Replace IBlobStorageClient with our in-memory fake so
                    // the upload + download flows can be asserted end-to-end
                    // without Azurite.
                    var blobDescriptor = services.SingleOrDefault(
                        d => d.ServiceType == typeof(IBlobStorageClient));
                    if (blobDescriptor is not null)
                    {
                        services.Remove(blobDescriptor);
                    }
                    services.AddSingleton<IBlobStorageClient>(_blobs);
                });
            });
    }

    public Task DisposeAsync()
    {
        _factory?.Dispose();
        return Task.CompletedTask;
    }

    // === Seed helpers ===

    private async Task SeedAsync(
        bool emailConfirmed = true,
        OrderState orderState = OrderState.PendingPayment,
        int existingAttachments = 0)
    {
        await using var db = _harness.CreateDbContext();
        var seedActor = "test-seed";
        var seedAt = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

        var customer = BuildCustomerUser(CustomerUserId, "anna@example.cz", emailConfirmed);
        customer.MarkCreated(seedActor, seedAt);
        db.Set<User>().Add(customer);

        var otherCustomer = BuildCustomerUser(OtherCustomerUserId, "other@example.cz", emailConfirmed: true);
        otherCustomer.MarkCreated(seedActor, seedAt);
        db.Set<User>().Add(otherCustomer);

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
            snapshotFetchedAt: new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero),
            snapshotIsStale: false,
            countryCode: CountryCode,
            slug: "avast");
        maker.MarkVerified();
        maker.UpdateProfile(bio: null, bankAccount: null, personalPickupEnabled: true, pickupNote: null);
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

        // Order owned by the customer, optionally driven past PendingPayment.
        var order = BuildOrderInState(orderState);
        order.MarkCreated(seedActor, seedAt);
        for (var i = 0; i < existingAttachments; i++)
        {
            var att = OrderAttachment.Create(
                id: $"att-seed-{i}",
                orderId: order.Id,
                blobPath: $"cz/orders/{order.Id}/seed-{i}.pdf",
                originalFilename: $"seed-{i}.pdf",
                contentType: "application/pdf",
                sizeBytes: 1024,
                uploadedByUserId: CustomerUserId,
                countryCode: CountryCode);
            att.MarkCreated(seedActor, seedAt);
            order.AddAttachment(att);
        }
        db.Set<Order>().Add(order);

        await db.SaveChangesAsync();
    }

    private static Order BuildOrderInState(OrderState target)
    {
        var o = Order.Create(
            id: OrderId,
            orderNumber: "M-CZ-20260042",
            customerUserId: CustomerUserId,
            makerId: MakerId,
            productId: ProductId,
            contactName: "Anna",
            contactEmail: "a@b.cz",
            contactPhone: "+420 723 456 789",
            productPriceAmountMinor: 50000,
            shippingPriceAmountMinor: 7900,
            platformFeeAmountMinor: 7500,
            makerPayoutAmountMinor: 50400,
            totalAmountMinor: 57900,
            currency: Currency,
            vatRateBp: 2100,
            shippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
            zasilkovnaPickupPointId: "pp-42",
            countryCode: CountryCode);

        var fixedClock = new FixedClock(new DateTimeOffset(2026, 6, 5, 10, 0, 0, TimeSpan.Zero));
        if (target == OrderState.PendingPayment) return o;
        o.MarkAsPaid(fixedClock, "tx-1");
        if (target == OrderState.Paid) return o;
        o.Accept(fixedClock);
        if (target == OrderState.Accepted) return o;
        o.Ship(fixedClock, "PKT-1", 7);
        if (target == OrderState.Shipped) return o;
        o.MarkAsDelivered(fixedClock);
        if (target == OrderState.Delivered) return o;
        throw new ArgumentOutOfRangeException(nameof(target), target, "Unsupported");
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private static User BuildCustomerUser(string id, string email, bool emailConfirmed)
    {
        var user = User.Create(
            id: id,
            email: email,
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

    private string IssueCustomerToken(string userId = CustomerUserId, bool emailConfirmed = true)
    {
        var issuer = new JwtIssuer(Options.Create(new JwtOptions
        {
            Issuer = TestIssuer,
            SigningKeyBase64 = TestKeyBase64,
            AccessTokenLifetime = TimeSpan.FromMinutes(15),
        }));
        var user = BuildCustomerUser(userId, $"{userId}@example.cz", emailConfirmed);
        return issuer.Issue(user, MakablesAudiences.Customer, DateTimeOffset.UtcNow).Token;
    }

    private static MultipartFormDataContent BuildMultipart(
        byte[] bytes, string contentType, string filename)
    {
        var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(fileContent, name: "file", fileName: filename);
        return form;
    }

    // === Magic-byte builders (just enough to pass the validator sniff) ===

    private static byte[] BuildPdfBytes()
    {
        // "%PDF-1.7" + filler. The validator only reads the first four bytes.
        var header = "%PDF-1.7\n"u8.ToArray();
        var filler = new byte[512];
        return header.Concat(filler).ToArray();
    }

    private static byte[] BuildJpegBytes()
    {
        var bytes = new byte[256];
        bytes[0] = 0xFF; bytes[1] = 0xD8; bytes[2] = 0xFF; bytes[3] = 0xE0;
        return bytes;
    }

    private static byte[] BuildPngBytes()
    {
        var bytes = new byte[256];
        // 89 50 4E 47 0D 0A 1A 0A
        bytes[0] = 0x89; bytes[1] = 0x50; bytes[2] = 0x4E; bytes[3] = 0x47;
        bytes[4] = 0x0D; bytes[5] = 0x0A; bytes[6] = 0x1A; bytes[7] = 0x0A;
        return bytes;
    }

    private static byte[] BuildWebpBytes()
    {
        var bytes = new byte[256];
        // RIFF .... WEBP
        bytes[0] = 0x52; bytes[1] = 0x49; bytes[2] = 0x46; bytes[3] = 0x46;
        bytes[8] = 0x57; bytes[9] = 0x45; bytes[10] = 0x42; bytes[11] = 0x50;
        return bytes;
    }

    // === Tests ===

    [Fact]
    public async Task Upload_happy_path_pdf_returns_200_and_persists_blob_and_row()
    {
        await SeedAsync();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", IssueCustomerToken());

        var response = await client.PostAsync(
            $"/api/v1/orders/{OrderId}/attachments",
            BuildMultipart(PdfBytes, "application/pdf", "spec.pdf"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Row persisted with the original filename surfaced on the response.
        await using var db = _harness.CreateDbContext();
        var rows = await db.Set<OrderAttachment>()
            .AsNoTracking()
            .Where(a => a.OrderId == OrderId)
            .ToListAsync();
        rows.Should().HaveCount(1);
        var row = rows[0];
        row.ContentType.Should().Be("application/pdf");
        row.SizeBytes.Should().Be(PdfBytes.LongLength);
        row.OriginalFilename.Should().Be("spec.pdf");
        row.BlobPath.Should().StartWith("cz/orders/ord-1/").And.EndWith(".pdf");
        row.UploadedByUserId.Should().Be(CustomerUserId);
        row.CountryCode.Should().Be(CountryCode);

        // Blob stored at the row's blob_path.
        _blobs.Store.Should().ContainKey($"{BlobContainer.OrderAttachments}/{row.BlobPath}");
    }

    [Theory]
    [InlineData("image/jpeg", "jpg")]
    [InlineData("image/png", "png")]
    [InlineData("image/webp", "webp")]
    public async Task Upload_happy_path_image_types(string contentType, string expectedExt)
    {
        await SeedAsync();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", IssueCustomerToken());

        var bytes = contentType switch
        {
            "image/jpeg" => JpegBytes,
            "image/png" => PngBytes,
            "image/webp" => WebpBytes,
            _ => throw new InvalidOperationException(),
        };

        var response = await client.PostAsync(
            $"/api/v1/orders/{OrderId}/attachments",
            BuildMultipart(bytes, contentType, $"reference.{expectedExt}"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = _harness.CreateDbContext();
        var row = await db.Set<OrderAttachment>()
            .AsNoTracking()
            .SingleAsync(a => a.OrderId == OrderId);
        row.ContentType.Should().Be(contentType);
        row.BlobPath.Should().EndWith($".{expectedExt}");
    }

    [Fact]
    public async Task Upload_rejected_for_application_zip_with_FileUnsupportedType()
    {
        await SeedAsync();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", IssueCustomerToken());

        var zipBytes = new byte[256];
        zipBytes[0] = 0x50; zipBytes[1] = 0x4B; zipBytes[2] = 0x03; zipBytes[3] = 0x04;

        var response = await client.PostAsync(
            $"/api/v1/orders/{OrderId}/attachments",
            BuildMultipart(zipBytes, "application/zip", "spec.zip"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain($"\"code\":\"{BusinessErrorMessage.FileUnsupportedType}\"");
    }

    [Fact]
    public async Task Upload_rejected_when_declared_pdf_but_bytes_are_jpeg_with_FileInvalid()
    {
        await SeedAsync();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", IssueCustomerToken());

        var response = await client.PostAsync(
            $"/api/v1/orders/{OrderId}/attachments",
            BuildMultipart(JpegBytes, "application/pdf", "fake.pdf"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain($"\"code\":\"{BusinessErrorMessage.FileInvalid}\"");

        // Magic-byte mismatch rejected BEFORE the blob upload — no orphan.
        _blobs.Store.Should().BeEmpty();
    }

    [Fact]
    public async Task Upload_rejected_for_zero_byte_file_with_FileInvalid()
    {
        await SeedAsync();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", IssueCustomerToken());

        var response = await client.PostAsync(
            $"/api/v1/orders/{OrderId}/attachments",
            BuildMultipart(Array.Empty<byte>(), "application/pdf", "empty.pdf"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain($"\"code\":\"{BusinessErrorMessage.FileInvalid}\"");
    }

    [Fact]
    public async Task Upload_rejected_when_file_exceeds_request_size_limit()
    {
        await SeedAsync();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", IssueCustomerToken());

        // 11 MiB — over the 10 MiB MaxSizeBytes + 4 KiB framing slack. The
        // [RequestSizeLimit] attribute halts the request at the ASP.NET
        // layer before any controller code runs, surfacing as 413 / 400.
        var oversize = new byte[11 * 1024 * 1024];
        oversize[0] = 0x25; oversize[1] = 0x50; oversize[2] = 0x44; oversize[3] = 0x46;

        var response = await client.PostAsync(
            $"/api/v1/orders/{OrderId}/attachments",
            BuildMultipart(oversize, "application/pdf", "big.pdf"));

        // The framework can surface a [RequestSizeLimit] hit as 413 Payload
        // Too Large OR 400 Bad Request depending on which layer trips
        // first. Either is acceptable per AC-6: it must NOT be 200.
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.BadRequest,
            HttpStatusCode.RequestEntityTooLarge);

        // No row, no blob.
        await using var db = _harness.CreateDbContext();
        var attachments = await db.Set<OrderAttachment>().AsNoTracking().CountAsync();
        attachments.Should().Be(0);
    }

    [Fact]
    public async Task Upload_rejected_at_11th_attachment_with_OrderAttachmentLimitReached()
    {
        await SeedAsync(existingAttachments: Order.MaxAttachmentCount);
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", IssueCustomerToken());

        var response = await client.PostAsync(
            $"/api/v1/orders/{OrderId}/attachments",
            BuildMultipart(PdfBytes, "application/pdf", "eleventh.pdf"));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain($"\"code\":\"{BusinessErrorMessage.OrderAttachmentLimitReached}\"");
    }

    [Fact]
    public async Task Upload_rejected_to_shipped_order_with_OrderStateForbidsAttachment()
    {
        await SeedAsync(orderState: OrderState.Shipped);
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", IssueCustomerToken());

        var response = await client.PostAsync(
            $"/api/v1/orders/{OrderId}/attachments",
            BuildMultipart(PdfBytes, "application/pdf", "late.pdf"));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain($"\"code\":\"{BusinessErrorMessage.OrderStateForbidsAttachment}\"");
    }

    [Fact]
    public async Task Upload_to_other_customers_order_returns_404_OrderNotFound()
    {
        await SeedAsync();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", IssueCustomerToken(OtherCustomerUserId));

        var response = await client.PostAsync(
            $"/api/v1/orders/{OrderId}/attachments",
            BuildMultipart(PdfBytes, "application/pdf", "spec.pdf"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain($"\"code\":\"{BusinessErrorMessage.OrderNotFound}\"");
    }

    [Fact]
    public async Task Upload_without_bearer_token_returns_401()
    {
        await SeedAsync();
        using var client = _factory.CreateClient();

        var response = await client.PostAsync(
            $"/api/v1/orders/{OrderId}/attachments",
            BuildMultipart(PdfBytes, "application/pdf", "spec.pdf"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Upload_with_unconfirmed_email_returns_403_AuthEmailNotConfirmed()
    {
        await SeedAsync(emailConfirmed: false);
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", IssueCustomerToken(emailConfirmed: false));

        var response = await client.PostAsync(
            $"/api/v1/orders/{OrderId}/attachments",
            BuildMultipart(PdfBytes, "application/pdf", "spec.pdf"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain($"\"code\":\"{BusinessErrorMessage.AuthEmailNotConfirmed}\"");
    }
}
