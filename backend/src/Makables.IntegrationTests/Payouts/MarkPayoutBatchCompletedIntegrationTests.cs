using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Makables.Core.Domain.Auditing;
using Makables.Core.Domain.Categories;
using Makables.Core.Domain.Common;
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
using NSubstitute;
using AddressEntity = Makables.Core.Domain.Addresses.Address;
using MakerEntity = Makables.Core.Domain.Makers.Maker;

namespace Makables.IntegrationTests.Payouts;

/// <summary>
/// T-0103 settle e2e on the Admin host
/// (<c>POST /api/v1/payout-batches/{id}/complete</c>). Pins: the live
/// transition (batch Completed + bank ref + completed_at = paymentDate midnight
/// UTC + orders Completed + N payout-sent outbox rows + one audit row);
/// Silent-Success idempotent re-call; multi-maker grouping. Real Postgres +
/// real QuestPDF + in-memory blob.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class MarkPayoutBatchCompletedIntegrationTests : IAsyncLifetime
{
    private const string TestKeyBase64 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
    private const string TestIssuer = "https://makables.test";
    private const string CountryCode = "CZ";
    private const string Currency = "CZK";
    private const string AdminUserId = "user-admin-1";

    private readonly PostgresHarness _harness;
    private readonly FakeBlobStorageClient _blobs = new();
    private WebApplicationFactory<Makables.Web.Admin.Program> _factory = default!;

    public MarkPayoutBatchCompletedIntegrationTests(PostgresHarness harness) => _harness = harness;

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

    private sealed record SeededMaker(string MakerId, string UserId, string AddressId, string BankAccount, string Company, string Ico);

    private async Task SeedDeliveredAsync(
        IReadOnlyList<SeededMaker> makers,
        IReadOnlyList<(string OrderId, string MakerId, long Product, long Fee)> orders)
    {
        await using var db = _harness.CreateDbContext();
        const string actor = "test-seed";
        var at = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

        var admin = User.Create(AdminUserId, "admin@makables.cz", UserRole.Admin, "Admin", CountryCode,
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        admin.ConfirmEmail(at); admin.MarkCreated(actor, at);
        db.Set<User>().Add(admin);

        var customer = User.Create("user-customer-1", "anna@example.cz", UserRole.Customer, "Anna", CountryCode,
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        customer.ConfirmEmail(at); customer.MarkCreated(actor, at);
        db.Set<User>().Add(customer);

        var category = Category.Create("cat-1", "3D tisk", "3d-tisk", null, null, 10, CountryCode);
        category.MarkCreated(actor, at);
        db.Set<Category>().Add(category);

        foreach (var m in makers)
        {
            var mu = User.Create(m.UserId, $"{m.MakerId}@example.cz", UserRole.Maker, m.Company, CountryCode,
                passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
            mu.ConfirmEmail(at); mu.MarkCreated(actor, at);
            db.Set<User>().Add(mu);

            var addr = AddressEntity.Create(m.AddressId, "Pikrtova", "1737", "Praha", "14000", CountryCode, CountryCode);
            addr.MarkCreated(actor, at);
            db.Set<AddressEntity>().Add(addr);

            var maker = MakerEntity.Create(m.MakerId, m.UserId, m.Ico, null, m.Company, null,
                m.AddressId, null, true, "ares", at, false, CountryCode, slug: m.MakerId);
            maker.UpdateProfile(bio: null, bankAccount: m.BankAccount, personalPickupEnabled: null, pickupNote: null);
            maker.MarkCreated(actor, at);
            db.Set<MakerEntity>().Add(maker);

            var product = Product.Create($"prod-{m.MakerId}", m.MakerId, "cat-1", "Vase", null,
                new Money(50000, Currency), PriceType.Fixed, 300, CountryCode);
            product.MarkCreated(actor, at);
            db.Set<Product>().Add(product);
        }

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(at.AddDays(5));
        foreach (var o in orders)
        {
            var payout = o.Product - o.Fee;
            var order = Order.Create(o.OrderId, $"M-CZ-{o.OrderId}", "user-customer-1", o.MakerId, $"prod-{o.MakerId}",
                "Anna", "anna@example.cz", "+420 723 456 789",
                o.Product, 0, o.Fee, payout, o.Product, Currency, 2100,
                ShippingMethod.ZasilkovnaPickupPoint, "pp-1", CountryCode);
            order.MarkAsPaid(clock, $"tx-{o.OrderId}");
            order.Accept(clock);
            order.Ship(clock, $"PKT-{o.OrderId}", 7);
            order.MarkAsDelivered(clock, OrderDeliverySource.Auto);
            order.MarkCreated(actor, at);
            db.Set<Order>().Add(order);
        }

        await db.SaveChangesAsync();
    }

    private static string IssueAdminToken()
    {
        var issuer = new JwtIssuer(Options.Create(new JwtOptions
        {
            Issuer = TestIssuer, SigningKeyBase64 = TestKeyBase64, AccessTokenLifetime = TimeSpan.FromMinutes(15),
        }));
        var user = User.Create(AdminUserId, "admin@makables.cz", UserRole.Admin, "Admin", CountryCode,
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        user.ConfirmEmail(new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero));
        return issuer.Issue(user, MakablesAudiences.Admin, DateTimeOffset.UtcNow).Token;
    }

    private HttpClient AdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", IssueAdminToken());
        return client;
    }

    /// <summary>Create a Processing batch via the claim endpoint and return its id.</summary>
    private static async Task<string> CreateProcessingBatchAsync(HttpClient client)
    {
        var created = await client.PostAsync("/api/v1/payout-batches", null);
        created.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("batchId").GetString()!;
    }

    [Fact]
    public async Task Settle_e2e_completes_batch_and_orders_with_outbox_and_audit()
    {
        await SeedDeliveredAsync(
            new[]
            {
                new SeededMaker("maker-a", "user-maker-a", "addr-a", "111/0100", "Alpha s.r.o.", "27074358"),
                new SeededMaker("maker-b", "user-maker-b", "addr-b", "222/0800", "Beta s.r.o.", "45317054"),
            },
            new[]
            {
                ("o1", "maker-a", 30000L, 4500L),
                ("o2", "maker-a", 15000L, 2250L),
                ("o3", "maker-b", 21000L, 3150L),
            });
        using var client = AdminClient();
        var batchId = await CreateProcessingBatchAsync(client);

        var body = new { bankReference = "WIRE-2026-06-13-001", paymentDate = "2026-06-10" };
        var response = await client.PostAsJsonAsync($"/api/v1/payout-batches/{batchId}/complete", body);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var resDoc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        resDoc.RootElement.GetProperty("alreadyCompleted").GetBoolean().Should().BeFalse();
        resDoc.RootElement.GetProperty("bankReference").GetString().Should().Be("WIRE-2026-06-13-001");

        await using var db = _harness.CreateDbContext();
        var batch = await db.Set<PayoutBatch>().AsNoTracking().SingleAsync(b => b.Id == batchId);
        batch.State.Should().Be(PayoutBatchState.Completed);
        batch.BankReference.Should().Be("WIRE-2026-06-13-001");
        batch.CompletedAt.Should().Be(new DateTimeOffset(2026, 6, 10, 0, 0, 0, TimeSpan.Zero));
        batch.CompletedBy.Should().Be(AdminUserId);

        var claimed = await db.Set<Order>().AsNoTracking().Where(o => o.PayoutBatchId == batchId).ToListAsync();
        claimed.Should().HaveCount(3);
        claimed.Should().AllSatisfy(o => o.State.Should().Be(OrderState.Completed));

        var sentOutbox = await db.Set<OutboxEvent>().AsNoTracking()
            .Where(e => e.EventType == OutboxEventTypes.PayoutBatchPayoutSentMakerEmail).ToListAsync();
        sentOutbox.Should().HaveCount(2, "one payout-sent email per distinct maker");

        var audit = await db.Set<AdminAuditLogEntry>().AsNoTracking()
            .Where(e => e.ActionCode == "payoutBatch.complete").ToListAsync();
        audit.Should().HaveCount(1);
        audit[0].TargetEntity.Should().Be("payout_batch");
        audit[0].TargetId.Should().Be(batchId);
        audit[0].Notes.Should().Be("WIRE-2026-06-13-001");
    }

    [Fact]
    public async Task Idempotent_re_call_is_silent_success_with_no_new_rows()
    {
        await SeedDeliveredAsync(
            new[] { new SeededMaker("maker-a", "user-maker-a", "addr-a", "111/0100", "Alpha s.r.o.", "27074358") },
            new[] { ("o1", "maker-a", 30000L, 4500L) });
        using var client = AdminClient();
        var batchId = await CreateProcessingBatchAsync(client);

        var first = await client.PostAsJsonAsync($"/api/v1/payout-batches/{batchId}/complete",
            new { bankReference = "WIRE-FIRST", paymentDate = "2026-06-10" });
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        var second = await client.PostAsJsonAsync($"/api/v1/payout-batches/{batchId}/complete",
            new { bankReference = "WIRE-SECOND", paymentDate = "2026-06-12" });
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        using var resDoc = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        resDoc.RootElement.GetProperty("alreadyCompleted").GetBoolean().Should().BeTrue();
        resDoc.RootElement.GetProperty("bankReference").GetString().Should().Be("WIRE-FIRST");

        await using var db = _harness.CreateDbContext();
        var batch = await db.Set<PayoutBatch>().AsNoTracking().SingleAsync(b => b.Id == batchId);
        batch.BankReference.Should().Be("WIRE-FIRST", "the first settlement is authoritative");
        batch.CompletedAt.Should().Be(new DateTimeOffset(2026, 6, 10, 0, 0, 0, TimeSpan.Zero));

        // The core idempotency guarantee: the Silent-Success re-call performs
        // NO order re-completion and enqueues NO second payout-sent email.
        (await db.Set<OutboxEvent>().AsNoTracking()
            .CountAsync(e => e.EventType == OutboxEventTypes.PayoutBatchPayoutSentMakerEmail))
            .Should().Be(1, "no second payout-sent email on the idempotent re-call");
        // The order is still Completed (not re-touched).
        var order = await db.Set<Order>().AsNoTracking().SingleAsync(o => o.PayoutBatchId == batchId);
        order.State.Should().Be(OrderState.Completed);
    }

    [Fact]
    public async Task Multi_maker_grouping_enqueues_one_email_per_maker_with_summed_total()
    {
        await SeedDeliveredAsync(
            new[]
            {
                new SeededMaker("maker-a", "user-maker-a", "addr-a", "111/0100", "Alpha s.r.o.", "27074358"),
                new SeededMaker("maker-b", "user-maker-b", "addr-b", "222/0800", "Beta s.r.o.", "45317054"),
            },
            new[]
            {
                ("o1", "maker-a", 30000L, 4500L),
                ("o2", "maker-a", 15000L, 2250L),
                ("o3", "maker-a", 9000L, 1350L),
                ("o4", "maker-b", 21000L, 3150L),
            });
        using var client = AdminClient();
        var batchId = await CreateProcessingBatchAsync(client);

        var response = await client.PostAsJsonAsync($"/api/v1/payout-batches/{batchId}/complete",
            new { bankReference = "WIRE-MULTI", paymentDate = (string?)null });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = _harness.CreateDbContext();
        var rows = await db.Set<OutboxEvent>().AsNoTracking()
            .Where(e => e.EventType == OutboxEventTypes.PayoutBatchPayoutSentMakerEmail).ToListAsync();
        rows.Should().HaveCount(2);

        var payloads = rows
            .Select(r => JsonSerializer.Deserialize<PayoutBatchPayoutSentMakerEmailPayload>(r.PayloadJson)!)
            .ToList();
        var a = payloads.Single(p => p.MakerId == "maker-a");
        a.OrderCount.Should().Be(3);
        a.MakerTotalPaidMinor.Should().Be((30000 - 4500) + (15000 - 2250) + (9000 - 1350));
        var b = payloads.Single(p => p.MakerId == "maker-b");
        b.OrderCount.Should().Be(1);
        b.MakerTotalPaidMinor.Should().Be(21000 - 3150);
    }
}
