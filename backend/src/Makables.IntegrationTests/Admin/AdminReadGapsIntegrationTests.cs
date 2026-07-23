using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Makables.Core.Domain.Admin;
using Makables.Core.Domain.Auditing;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Money;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Outbox;
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
using AddressEntity = Makables.Core.Domain.Addresses.Address;
using MakerEntity = Makables.Core.Domain.Makers.Maker;

namespace Makables.IntegrationTests.Admin;

/// <summary>
/// End-to-end coverage for the four T-0127 admin read-gap endpoints on the
/// Admin host: country-config GET round-trip + 404; admin order-detail
/// privileged header + cross-host 401; admin-orders <c>customerUserId</c>
/// in-flight filter; the stalled-outbox LIST (byte-identical to
/// <c>CountStalledAsync</c>); the payout-batch LIST (Unscoped, cross-maker).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AdminReadGapsIntegrationTests : IAsyncLifetime
{
    private const string TestKeyBase64 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
    private const string TestIssuer = "https://makables.test";
    private const string CountryCode = "CZ";
    private const string Currency = "CZK";
    private const string AdminUserId = "user-admin-1";

    private readonly PostgresHarness _harness;
    private readonly FakeBlobStorageClient _blobs = new();
    private WebApplicationFactory<Makables.Web.Admin.Program> _factory = default!;

    public AdminReadGapsIntegrationTests(PostgresHarness harness) => _harness = harness;

    private static readonly DateTimeOffset SeedAt = new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

    // Container-relative CSV path for batch-1 (cc lower-cased, per
    // PayoutArtifactService). The DownloadCsv controller strips the
    // "payouts/" prefix off CsvBlobPath, so the blob lives here.
    private const string Batch1CsvRelativePath = "cz/VYP-CZ-2026-W18.csv";
    private static readonly byte[] CsvBytes = "iban;amount\nCZ65;504\n"u8.ToArray();

    public async Task InitializeAsync()
    {
        await _harness.ResetMutableTablesAsync();
        // country_configuration is a seed table the harness does NOT truncate;
        // reset the CZ row to its seed values so the GET round-trip is exact.
        await using (var db = _harness.CreateDbContext())
        {
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE country_configuration SET standard_vat_rate_bp = 2100, " +
                "reduced_vat_rate_bp = 1200, platform_fee_rate_bp = 1500, " +
                "default_shipping_price_minor = 7900, invoicing_mode = 'None', " +
                "default_payment_provider = 'comgate', default_shipping_carrier = 'packeta', " +
                "default_registry = 'ares', default_email_provider = 'resend' WHERE id = 'CZ';");
        }
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

                    // T-0137: the payout-CSV read-audit test streams the CSV
                    // through the controller, so swap in the in-memory blob.
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

    private async Task SeedAsync()
    {
        await using var db = _harness.CreateDbContext();
        const string actor = "test-seed";

        void Add<T>(T e) where T : class => db.Set<T>().Add(e);

        AddressEntity NewAddress(string id)
        {
            var a = AddressEntity.Create(id, "Pikrtova", "1737", "Praha", "14000", CountryCode, CountryCode);
            a.MarkCreated(actor, SeedAt);
            return a;
        }

        User NewUser(string id, string email, UserRole role)
        {
            var u = User.Create(id, email, role, "Name", CountryCode,
                passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
            u.ConfirmEmail(SeedAt);
            u.MarkCreated(actor, SeedAt);
            return u;
        }

        MakerEntity NewMaker(string id, string userId, string addressId, string company, string slug, string ico)
        {
            var m = MakerEntity.Create(id, userId, ico, null, company, null, addressId,
                null, true, "ares", SeedAt, false, CountryCode, slug);
            m.MarkCreated(actor, SeedAt);
            return m;
        }

        Add(NewUser(AdminUserId, "admin@makables.cz", UserRole.Admin));
        Add(NewUser("user-cust-a", "anna@example.cz", UserRole.Customer));
        Add(NewUser("user-cust-b", "bara@example.cz", UserRole.Customer));
        Add(NewUser("user-maker-x", "x@example.cz", UserRole.Maker));
        Add(NewAddress("addr-x"));
        Add(NewMaker("maker-x", "user-maker-x", "addr-x", "Avast s.r.o.", "avast", "27074358"));

        var category = Makables.Core.Domain.Categories.Category.Create(
            "cat-1", "3D tisk", "3d-tisk", null, null, 10, CountryCode);
        category.MarkCreated(actor, SeedAt);
        Add(category);

        var product = Product.Create("prod-1", "maker-x", "cat-1", "Vase", null,
            new Money(50000, Currency), PriceType.Fixed, 300, CountryCode);
        product.MarkCreated(actor, SeedAt);
        Add(product);

        Order NewOrder(string id, string number, string custId, string custEmail, OrderState? advanceTo, DateTimeOffset created)
        {
            var o = Order.Create(id, number, custId, "maker-x", "prod-1",
                custId == "user-cust-a" ? "Anna" : "Bára", custEmail, "+420 723 456 789",
                50000, 7900, 7500, 50400, 57900, Currency, 2100,
                ShippingMethod.ZasilkovnaPickupPoint, "pp-42", CountryCode);
            // Advance state via the entity transitions so the in-flight filter
            // exercises real states.
            var clock = new FixedClock(created);
            if (advanceTo is OrderState.Paid or OrderState.Delivered or OrderState.Completed)
                o.MarkAsPaid(clock, "comgate-" + id);
            if (advanceTo is OrderState.Delivered or OrderState.Completed)
            {
                o.Accept(clock);
                o.Ship(clock, "Z" + id, 7);
                o.MarkAsDelivered(clock, OrderDeliverySource.Customer);
            }
            if (advanceTo is OrderState.Completed)
                o.Complete(clock);
            o.MarkCreated(actor, created);
            return o;
        }

        // user-cust-a: one in-flight (Paid) + one terminal (Completed).
        Add(NewOrder("ord-a-paid", "M-CZ-20260001", "user-cust-a", "anna@example.cz", OrderState.Paid, SeedAt));
        Add(NewOrder("ord-a-done", "M-CZ-20260002", "user-cust-a", "anna@example.cz", OrderState.Completed, SeedAt.AddHours(1)));
        // user-cust-b: one terminal only (Delivered — NOT in the in-flight set).
        Add(NewOrder("ord-b-deliv", "M-CZ-20260003", "user-cust-b", "bara@example.cz", OrderState.Delivered, SeedAt.AddHours(2)));

        // Payout batches: one Processing + one Completed (cross-maker browse).
        // batch-1 gets a CSV artifact so the T-0137 payout.csv.download read
        // audit can be exercised on the 200-stream path. The CsvBlobPath mirrors
        // PayoutArtifactService: "{container}/{cc}/{batchNumber}.csv".
        var b1 = PayoutBatch.Create("batch-1", "VYP-CZ-2026-W18", CountryCode, 50400, Currency, 1, 1, 0, 0, 0);
        b1.AttachCsvBlobPath($"{BlobContainer.Payouts}/{Batch1CsvRelativePath}");
        b1.MarkCreated(actor, SeedAt);
        // batch-2 stays Completed WITHOUT a CSV (CsvBlobPath null) — the 409
        // csvNotReady negative path for the read audit.
        var b2 = PayoutBatch.Create("batch-2", "VYP-CZ-2026-W17", CountryCode, 50400, Currency, 1, 1, 0, 0, 0);
        b2.Complete(new FixedClock(SeedAt.AddDays(7)), "wire-ref-1", null, AdminUserId);
        b2.MarkCreated(actor, SeedAt.AddDays(-7));
        Add(b1);
        Add(b2);

        // Outbox: 2 stalled + 2 non-stalled (1 due, 1 processed).
        OutboxEvent stalled1 = OutboxEvent.Enqueue("evt-stalled-1", "ord-a-paid", "OrderPaidEmail", "{}", SeedAt);
        stalled1.RecordFailure(OutboxErrorKind.Permanent, "email.permanent", nextRetryAt: null);
        OutboxEvent stalled2 = OutboxEvent.Enqueue("evt-stalled-2", "ord-b-deliv", "OrderDeliveredEmail", "{}", SeedAt.AddMinutes(1));
        stalled2.RecordFailure(OutboxErrorKind.Unknown, "email.unknown", nextRetryAt: null);
        OutboxEvent dueRow = OutboxEvent.Enqueue("evt-due", "ord-a-done", "OrderCompletedEmail", "{}", SeedAt);
        dueRow.RecordFailure(OutboxErrorKind.Transient, "email.transient", nextRetryAt: SeedAt.AddMinutes(5));
        OutboxEvent processed = OutboxEvent.Enqueue("evt-processed", "ord-a-paid", "OrderPaidEmail", "{}", SeedAt);
        processed.MarkProcessed(SeedAt.AddMinutes(1));
        Add(stalled1);
        Add(stalled2);
        Add(dueRow);
        Add(processed);

        await db.SaveChangesAsync();

        // Upload batch-1's CSV into the in-memory blob so the controller's
        // DownloadAsync(Payouts, "{cc}/{batchNumber}.csv") succeeds (200).
        using var content = new MemoryStream(CsvBytes, writable: false);
        await _blobs.UploadAsync(
            BlobContainer.Payouts, Batch1CsvRelativePath, content, "text/csv", CancellationToken.None);
    }

    private static string IssueToken(string userId, string email, UserRole role, string audience)
    {
        var issuer = new JwtIssuer(Options.Create(new JwtOptions
        {
            Issuer = TestIssuer,
            SigningKeyBase64 = TestKeyBase64,
            AccessTokenLifetime = TimeSpan.FromMinutes(15),
        }));
        var user = User.Create(userId, email, role, "Test", CountryCode,
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        user.ConfirmEmail(SeedAt);
        return issuer.Issue(user, audience, DateTimeOffset.UtcNow).Token;
    }

    private HttpClient CreateAdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", IssueToken(AdminUserId, "admin@makables.cz", UserRole.Admin, MakablesAudiences.Admin));
        return client;
    }

    private static readonly System.Text.Json.JsonSerializerOptions JsonOpts = new(
        System.Text.Json.JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(JsonOpts))!;

    // === (1) Country-config GET ===

    [Fact]
    public async Task GET_country_configuration_round_trips_the_seed_field_set()
    {
        await SeedAsync();
        using var client = CreateAdminClient();

        var response = await client.GetAsync("/api/v1/country-configurations/CZ");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadAsync<GetCountryConfigEnvelope>(response);
        body.CountryCode.Should().Be("CZ");
        body.StandardVatRateBp.Should().Be(2100);
        body.ReducedVatRateBp.Should().Be(1200);
        body.PlatformFeeRateBp.Should().Be(1500);
        body.DefaultShippingPriceMinor.Should().Be(7900);
        body.DefaultPaymentProvider.Should().Be("comgate");
        body.DefaultShippingCarrier.Should().Be("packeta");
        body.DefaultRegistry.Should().Be("ares");
        body.DefaultEmailProvider.Should().Be("resend");
    }

    [Fact]
    public async Task GET_country_configuration_unknown_code_is_404()
    {
        await SeedAsync();
        using var client = CreateAdminClient();

        var response = await client.GetAsync("/api/v1/country-configurations/ZZ");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // === (2) Admin order-detail ===

    [Fact]
    public async Task GET_admin_order_detail_returns_the_privileged_header()
    {
        await SeedAsync();
        using var client = CreateAdminClient();

        var response = await client.GetAsync("/api/v1/admin-orders/ord-a-paid");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadAsync<GetAdminOrderDetailEnvelope>(response);
        body.Order.OrderNumber.Should().Be("M-CZ-20260001");
        body.Order.State.Should().Be(OrderState.Paid);
        body.Order.CustomerEmail.Should().Be("anna@example.cz", "admin is privileged — no GDPR redaction");
        body.Order.MakerName.Should().Be("Avast s.r.o.");
        body.Order.TotalAmountMinor.Should().Be(57900);
    }

    [Fact]
    public async Task GET_admin_order_detail_unknown_id_is_404()
    {
        await SeedAsync();
        using var client = CreateAdminClient();

        var response = await client.GetAsync("/api/v1/admin-orders/does-not-exist");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // === (T-0137) Read-audit on the privileged single-record / file reads ===

    [Fact]
    public async Task GET_admin_order_detail_writes_one_read_audit_row_on_the_200_path()
    {
        await SeedAsync();
        using var client = CreateAdminClient();

        var response = await client.GetAsync("/api/v1/admin-orders/ord-a-paid");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = _harness.CreateDbContext();
        var rows = await db.Set<AdminAuditLogEntry>()
            .IgnoreQueryFilters()
            .Where(e => e.ActionCode == "order.detail.view")
            .ToListAsync();

        rows.Should().ContainSingle();
        var row = rows[0];
        row.TargetEntity.Should().Be("order");
        row.TargetId.Should().Be("ord-a-paid");
        row.AdminUserId.Should().Be(AdminUserId);
        row.BeforeJson.Should().BeNull();
        row.AfterJson.Should().BeNull();
    }

    [Fact]
    public async Task GET_admin_order_detail_unknown_id_writes_no_read_audit_row()
    {
        await SeedAsync();
        using var client = CreateAdminClient();

        var response = await client.GetAsync("/api/v1/admin-orders/does-not-exist");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var db = _harness.CreateDbContext();
        var count = await db.Set<AdminAuditLogEntry>()
            .IgnoreQueryFilters()
            .CountAsync(e => e.ActionCode == "order.detail.view");

        count.Should().Be(0, "a 404 is not a disclosure — no read audit");
    }

    [Fact]
    public async Task GET_payout_csv_writes_one_read_audit_row_on_the_200_path()
    {
        await SeedAsync();
        using var client = CreateAdminClient();

        var response = await client.GetAsync("/api/v1/payout-batches/batch-1/csv");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");

        await using var db = _harness.CreateDbContext();
        var rows = await db.Set<AdminAuditLogEntry>()
            .IgnoreQueryFilters()
            .Where(e => e.ActionCode == "payout.csv.download")
            .ToListAsync();

        rows.Should().ContainSingle();
        var row = rows[0];
        row.TargetEntity.Should().Be("payoutbatch");
        row.TargetId.Should().Be("batch-1");
        row.AdminUserId.Should().Be(AdminUserId);
        row.BeforeJson.Should().BeNull();
        row.AfterJson.Should().BeNull();
    }

    [Fact]
    public async Task GET_payout_csv_unknown_id_writes_no_read_audit_row()
    {
        await SeedAsync();
        using var client = CreateAdminClient();

        var response = await client.GetAsync("/api/v1/payout-batches/batch-does-not-exist/csv");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var db = _harness.CreateDbContext();
        var count = await db.Set<AdminAuditLogEntry>()
            .IgnoreQueryFilters()
            .CountAsync(e => e.ActionCode == "payout.csv.download");

        count.Should().Be(0, "a 404 unknown batch is not a disclosure — no read audit");
    }

    [Fact]
    public async Task GET_payout_csv_when_csv_not_ready_writes_no_read_audit_row()
    {
        await SeedAsync();
        using var client = CreateAdminClient();

        // batch-2 is Completed but has a null CsvBlobPath → 409 csvNotReady.
        var response = await client.GetAsync("/api/v1/payout-batches/batch-2/csv");
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        await using var db = _harness.CreateDbContext();
        var count = await db.Set<AdminAuditLogEntry>()
            .IgnoreQueryFilters()
            .CountAsync(e => e.ActionCode == "payout.csv.download");

        count.Should().Be(0, "a 409 csv-not-ready streamed nothing — no read audit");
    }

    // === (2b) Per-user in-flight signal ===

    [Fact]
    public async Task GET_admin_orders_customerUserId_in_flight_filter_returns_only_matches()
    {
        await SeedAsync();
        using var client = CreateAdminClient();

        // user-cust-a has one Paid (in-flight) + one Completed (terminal).
        var body = await ReadAsync<GetAllOrdersEnvelope>(await client.GetAsync(
            "/api/v1/admin-orders?customerUserId=user-cust-a&state=Paid"));

        body.Orders.TotalCount.Should().Be(1, "only the Paid order is in-flight for user-cust-a");
        body.Orders.Items.Single().OrderId.Should().Be("ord-a-paid");

        // user-cust-b has only a Delivered (terminal) order → no in-flight Paid.
        var bodyB = await ReadAsync<GetAllOrdersEnvelope>(await client.GetAsync(
            "/api/v1/admin-orders?customerUserId=user-cust-b&state=Paid"));
        bodyB.Orders.TotalCount.Should().Be(0, "user-cust-b has no in-flight Paid order — the no-in-flight signal");
    }

    // === (3) Stalled-outbox LIST ===

    [Fact]
    public async Task GET_outbox_stalled_returns_only_the_stalled_set()
    {
        await SeedAsync();
        using var client = CreateAdminClient();

        var body = await ReadAsync<GetStalledEnvelope>(await client.GetAsync("/api/v1/outbox-events/stalled"));

        body.Events.TotalCount.Should().Be(2, "exactly the 2 stalled rows; the due + processed rows excluded");
        body.Events.Items.Should().Contain(e => e.Id == "evt-stalled-1" && e.LastErrorCode == "email.permanent");
        body.Events.Items.Should().Contain(e => e.Id == "evt-stalled-2" && e.LastErrorCode == "email.unknown");
        body.Events.Items.Should().NotContain(e => e.Id == "evt-due" || e.Id == "evt-processed");
    }

    // === (4) Payout-batch LIST ===

    [Fact]
    public async Task GET_payout_batches_lists_processing_and_completed()
    {
        await SeedAsync();
        using var client = CreateAdminClient();

        var body = await ReadAsync<GetPayoutBatchesEnvelope>(await client.GetAsync("/api/v1/payout-batches"));

        body.Batches.TotalCount.Should().Be(2);
        body.Batches.Items.Should().Contain(b => b.BatchNumber == "VYP-CZ-2026-W18" && b.State == PayoutBatchState.Processing);
        body.Batches.Items.Should().Contain(b => b.BatchNumber == "VYP-CZ-2026-W17" && b.State == PayoutBatchState.Completed);
    }

    // === (5/6) Cross-host 401 on the unscoped reads ===

    [Fact]
    public async Task Customer_JWT_is_rejected_on_the_new_admin_reads()
    {
        await SeedAsync();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", IssueToken("user-cust-a", "anna@example.cz", UserRole.Customer, MakablesAudiences.Customer));

        (await client.GetAsync("/api/v1/admin-orders/ord-a-paid")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
        (await client.GetAsync("/api/v1/outbox-events/stalled")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
        (await client.GetAsync("/api/v1/payout-batches")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
        (await client.GetAsync("/api/v1/country-configurations/CZ")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    // Local mirror DTOs — the test reads the JSON the API emits.
    private sealed record GetCountryConfigEnvelope(
        string CountryCode, int StandardVatRateBp, int? ReducedVatRateBp,
        InvoicingMode InvoicingMode, int PlatformFeeRateBp, long DefaultShippingPriceMinor,
        string DefaultPaymentProvider, string DefaultShippingCarrier,
        string DefaultRegistry, string DefaultEmailProvider);
    private sealed record GetAdminOrderDetailEnvelope(AdminOrderDetailDto Order);
    private sealed record GetAllOrdersEnvelope(PagedData<AdminOrderListItemDto> Orders);
    private sealed record GetStalledEnvelope(PagedData<StalledOutboxEventDto> Events);
    private sealed record GetPayoutBatchesEnvelope(PagedData<AdminPayoutBatchListItemDto> Batches);
}
