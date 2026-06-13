using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Makables.Core.Domain.Auditing;
using Makables.Core.Domain.Categories;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Invoices;
using Makables.Core.Domain.Money;
using Makables.Core.Domain.Numbering;
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
/// End-to-end coverage for the payout-core bundle on the Admin host
/// (<c>POST /api/v1/payout-batches</c> + <c>GET /{id}/csv</c>). Real
/// Postgres via <see cref="PostgresHarness"/>, the real QuestPDF renderer,
/// and an in-memory <see cref="FakeBlobStorageClient"/>. Pins: claim e2e
/// (batch row + claim links + Fee invoices + CSV blob + outbox + audit);
/// re-run idempotency; Q3/Q5 exclusions; empty run; the Q-0017 subject fix;
/// admin CSV download + audience enforcement.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CreatePayoutBatchIntegrationTests : IAsyncLifetime
{
    private const string TestKeyBase64 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
    private const string TestIssuer = "https://makables.test";
    private const string CountryCode = "CZ";
    private const string Currency = "CZK";
    private const string AdminUserId = "user-admin-1";

    private readonly PostgresHarness _harness;
    private readonly FakeBlobStorageClient _blobs = new();
    private WebApplicationFactory<Makables.Web.Admin.Program> _factory = default!;

    public CreatePayoutBatchIntegrationTests(PostgresHarness harness) => _harness = harness;

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

    private sealed record SeededMaker(string MakerId, string UserId, string AddressId, string? BankAccount, string Company, string Ico);

    private async Task SeedBaselineAsync(IReadOnlyList<SeededMaker> makers, IReadOnlyList<(string OrderId, string MakerId, OrderState State, long Product, long Fee, long Refunded)> orders)
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
            if (!string.IsNullOrEmpty(m.BankAccount))
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
            // Drive to Delivered (or leave Paid for the "not claimable" case).
            order.MarkAsPaid(clock, $"tx-{o.OrderId}");
            if (o.State != OrderState.Paid)
            {
                order.Accept(clock);
                order.Ship(clock, $"PKT-{o.OrderId}", 7);
                order.MarkAsDelivered(clock, OrderDeliverySource.Auto);
            }
            if (o.Refunded > 0) order.Refund(clock, o.Refunded, acknowledgePostPayout: false);
            order.MarkCreated(actor, at);
            db.Set<Order>().Add(order);
        }

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Seed the FV-CZ-2026 invoice <see cref="NumberingSequence"/> row so
    /// the Fee-invoice allocator locks an existing row instead of racing to
    /// create it (see the concurrent race test). LastUsedValue stays 0 so
    /// the first issued Fee invoice is FV-CZ-20260001 as in the other tests.
    /// </summary>
    private async Task SeedInvoiceSequenceAsync()
    {
        await using var db = _harness.CreateDbContext();
        var at = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        db.Set<NumberingSequence>().Add(
            NumberingSequence.StartAt(CountryCode, NumberingScope.Invoice, 2026, at));
        await db.SaveChangesAsync();
    }

    private static string IssueToken(string userId, string email, UserRole role, string audience)
    {
        var issuer = new JwtIssuer(Options.Create(new JwtOptions
        {
            Issuer = TestIssuer, SigningKeyBase64 = TestKeyBase64, AccessTokenLifetime = TimeSpan.FromMinutes(15),
        }));
        var user = User.Create(userId, email, role, "Test", CountryCode,
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        user.ConfirmEmail(new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero));
        return issuer.Issue(user, audience, DateTimeOffset.UtcNow).Token;
    }

    private HttpClient AdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", IssueToken(AdminUserId, "admin@makables.cz", UserRole.Admin, MakablesAudiences.Admin));
        return client;
    }

    [Fact]
    public async Task POST_claims_eligible_orders_writes_batch_invoices_csv_outbox_and_audit()
    {
        await SeedBaselineAsync(
            new[]
            {
                new SeededMaker("maker-a", "user-maker-a", "addr-a", "111/0100", "Alpha s.r.o.", "27074358"),
                new SeededMaker("maker-b", "user-maker-b", "addr-b", "222/0800", "Beta s.r.o.", "45317054"),
            },
            new[]
            {
                ("o1", "maker-a", OrderState.Delivered, 30000L, 4500L, 0L),
                ("o2", "maker-a", OrderState.Delivered, 15000L, 2250L, 0L),
                ("o3", "maker-b", OrderState.Delivered, 21000L, 3150L, 0L),
            });
        using var client = AdminClient();

        var response = await client.PostAsync("/api/v1/payout-batches", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<CreatePayoutBatchApiResponse>();
        body!.OrderCount.Should().Be(3);
        body.MakerCount.Should().Be(2);
        body.AlreadyExisted.Should().BeFalse();
        body.State.Should().Be(nameof(PayoutBatchState.Processing));

        await using var db = _harness.CreateDbContext();
        var batches = await db.Set<PayoutBatch>().AsNoTracking().ToListAsync();
        batches.Should().HaveCount(1);
        var batch = batches[0];
        batch.State.Should().Be(PayoutBatchState.Processing);
        batch.TotalAmountMinor.Should().Be(30000 - 4500 + 15000 - 2250 + 21000 - 3150);
        batch.CsvBlobPath.Should().NotBeNull();

        var claimed = await db.Set<Order>().AsNoTracking().Where(o => o.PayoutBatchId == batch.Id).ToListAsync();
        claimed.Should().HaveCount(3);

        var feeInvoices = await db.Set<Invoice>().AsNoTracking()
            .Where(i => i.PayoutBatchId == batch.Id).ToListAsync();
        feeInvoices.Should().HaveCount(2);
        feeInvoices.Should().AllSatisfy(i => i.Type.Should().Be(InvoiceType.Fee));
        feeInvoices.Should().AllSatisfy(i => i.PdfBlobPath.Should().NotBeNull());

        var outbox = await db.Set<OutboxEvent>().AsNoTracking()
            .Where(e => e.EventType == OutboxEventTypes.PayoutFeeInvoiceMakerEmail).ToListAsync();
        outbox.Should().HaveCount(2);

        var audit = await db.Set<AdminAuditLogEntry>().AsNoTracking()
            .Where(e => e.ActionCode == "payoutBatch.create").ToListAsync();
        audit.Should().HaveCount(1);
        audit[0].TargetEntity.Should().Be("payout_batch");
        audit[0].AdminUserId.Should().Be(AdminUserId);
    }

    [Fact]
    public async Task Re_run_returns_existing_batch_with_no_second_row()
    {
        await SeedBaselineAsync(
            new[] { new SeededMaker("maker-a", "user-maker-a", "addr-a", "111/0100", "Alpha s.r.o.", "27074358") },
            new[] { ("o1", "maker-a", OrderState.Delivered, 30000L, 4500L, 0L) });
        using var client = AdminClient();

        var first = await client.PostAsync("/api/v1/payout-batches", null);
        var firstBody = await first.Content.ReadFromJsonAsync<CreatePayoutBatchApiResponse>();

        var second = await client.PostAsync("/api/v1/payout-batches", null);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondBody = await second.Content.ReadFromJsonAsync<CreatePayoutBatchApiResponse>();
        secondBody!.AlreadyExisted.Should().BeTrue();
        secondBody.BatchId.Should().Be(firstBody!.BatchId);

        await using var db = _harness.CreateDbContext();
        (await db.Set<PayoutBatch>().AsNoTracking().CountAsync()).Should().Be(1);
        (await db.Set<Invoice>().AsNoTracking().Where(i => i.Type == InvoiceType.Fee).CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Concurrent_create_dispatches_produce_exactly_one_batch_and_never_500()
    {
        // payout-core-bundle BLOCKER-1. Two parallel CreatePayoutBatch
        // dispatches against the SAME eligible set model the Monday-02:00
        // timer racing an admin click. Both pass the GetOpenBatchAsync
        // null-check; one commits first, the loser's SaveChangesAsync
        // violates ux_payout_batches_open_per_country. The translator turns
        // that 23505 into a typed Conflict (PayoutBatchAlreadyOpen) so the
        // loser returns a deterministic 409 — NOT a raw 500 — and its whole
        // UoW (batch + claims + fee invoices + outbox) rolls back. Exactly
        // one batch row, zero split/double claim.
        await SeedBaselineAsync(
            new[] { new SeededMaker("maker-a", "user-maker-a", "addr-a", "111/0100", "Alpha s.r.o.", "27074358") },
            new[]
            {
                ("o1", "maker-a", OrderState.Delivered, 30000L, 4500L, 0L),
                ("o2", "maker-a", OrderState.Delivered, 15000L, 2250L, 0L),
            });

        // Pre-warm the FV-CZ-2026 invoice sequence row so the per-maker Fee
        // invoice allocator FOR-UPDATE-locks an EXISTING row (serialised)
        // rather than racing to INSERT it. Without this both runs would race
        // on PK_numbering_sequence (a separate, intentionally-unmapped
        // first-allocation race documented in InvoiceNumberGeneratorRace-
        // SafetyTests) and mask the open-batch index race this test targets.
        await SeedInvoiceSequenceAsync();

        // Independent clients so the two requests run on independent
        // DbContext scopes / connections — the real concurrent-commit race.
        using var clientA = AdminClient();
        using var clientB = AdminClient();

        var taskA = clientA.PostAsync("/api/v1/payout-batches", null);
        var taskB = clientB.PostAsync("/api/v1/payout-batches", null);
        var responses = await Task.WhenAll(taskA, taskB);

        // No raw 500 under the most-likely race (the BLOCKER).
        responses.Should().NotContain(r => r.StatusCode == HttpStatusCode.InternalServerError,
            because: "a money command must never 500 on its open-batch race");

        var statuses = responses.Select(r => r.StatusCode).ToList();
        // The winner returns 200. The loser returns either 200 (its read-
        // check saw the committed winner and returned AlreadyExisted) or 409
        // (it lost at the unique index and the translator mapped it).
        statuses.Should().Contain(HttpStatusCode.OK);
        statuses.Should().OnlyContain(s =>
            s == HttpStatusCode.OK || s == HttpStatusCode.Conflict);

        // If the loser surfaced as a Conflict it must be a TYPED, translated
        // payout conflict — not an untranslated raw error. Both new mappings
        // are valid: the loser's INSERT trips whichever unique index Postgres
        // evaluates first — ux_payout_batches_country_batch_number
        // (PayoutBatchWeekAlreadyProcessed) or ux_payout_batches_open_per_country
        // (PayoutBatchAlreadyOpen). Either is the same "a batch already exists"
        // semantics; what matters is it is translated, not a raw 500.
        foreach (var r in responses.Where(r => r.StatusCode == HttpStatusCode.Conflict))
        {
            using var doc = System.Text.Json.JsonDocument.Parse(await r.Content.ReadAsStringAsync());
            doc.RootElement.GetProperty("code").GetString()
                .Should().BeOneOf(
                    BusinessErrorMessage.PayoutBatchAlreadyOpen,
                    BusinessErrorMessage.PayoutBatchWeekAlreadyProcessed);
        }

        await using var db = _harness.CreateDbContext();
        // Exactly ONE batch row — the loser's batch INSERT rolled back.
        (await db.Set<PayoutBatch>().AsNoTracking().CountAsync()).Should().Be(1,
            because: "the open-per-country unique index admits exactly one Processing batch");
        var batch = await db.Set<PayoutBatch>().AsNoTracking().SingleAsync();
        // Both eligible orders are claimed by the single winning batch — no
        // split, no double claim, no orphaned claim.
        var claimed = await db.Set<Order>().AsNoTracking().Where(o => o.PayoutBatchId != null).ToListAsync();
        claimed.Should().HaveCount(2);
        claimed.Should().OnlyContain(o => o.PayoutBatchId == batch.Id);
        // Exactly one maker's fee invoice — the loser's fee invoice rolled back.
        (await db.Set<Invoice>().AsNoTracking().Where(i => i.Type == InvoiceType.Fee).CountAsync())
            .Should().Be(1);

        foreach (var r in responses) r.Dispose();
    }

    [Fact]
    public async Task Q3_and_Q5_exclusions_skip_refunded_and_no_bank_orders()
    {
        await SeedBaselineAsync(
            new[]
            {
                new SeededMaker("maker-a", "user-maker-a", "addr-a", "111/0100", "Alpha s.r.o.", "27074358"),
                new SeededMaker("maker-b", "user-maker-b", "addr-b", "222/0800", "Beta s.r.o.", "45317054"),
                new SeededMaker("maker-c", "user-maker-c", "addr-c", null, "Gamma s.r.o.", "26168685"), // no bank (Q5)
            },
            new[]
            {
                ("o1", "maker-a", OrderState.Delivered, 30000L, 4500L, 0L),     // eligible
                ("o2", "maker-b", OrderState.Delivered, 21000L, 3150L, 5000L),  // partially refunded (Q3)
                ("o3", "maker-c", OrderState.Delivered, 15000L, 2250L, 0L),     // no-bank maker (Q5)
            });
        using var client = AdminClient();

        var response = await client.PostAsync("/api/v1/payout-batches", null);
        var body = await response.Content.ReadFromJsonAsync<CreatePayoutBatchApiResponse>();
        body!.OrderCount.Should().Be(1);
        body.ExcludedPartiallyRefundedOrderCount.Should().Be(1);
        body.ExcludedNoBankAccountOrderCount.Should().Be(1);
        body.ExcludedNoBankAccountMakerCount.Should().Be(1);

        await using var db = _harness.CreateDbContext();
        (await db.Set<Order>().AsNoTracking().Where(o => o.PayoutBatchId != null).CountAsync()).Should().Be(1);
        (await db.Set<Order>().AsNoTracking().FirstAsync(o => o.Id == "o2")).PayoutBatchId.Should().BeNull();
        (await db.Set<Order>().AsNoTracking().FirstAsync(o => o.Id == "o3")).PayoutBatchId.Should().BeNull();
    }

    [Fact]
    public async Task Empty_run_returns_conflict_with_no_batch_row()
    {
        await SeedBaselineAsync(
            new[] { new SeededMaker("maker-a", "user-maker-a", "addr-a", "111/0100", "Alpha s.r.o.", "27074358") },
            new[] { ("o1", "maker-a", OrderState.Paid, 30000L, 4500L, 0L) }); // Paid, not Delivered
        using var client = AdminClient();

        var response = await client.PostAsync("/api/v1/payout-batches", null);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var errorDoc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        errorDoc.RootElement.GetProperty("code").GetString().Should().Be(BusinessErrorMessage.PayoutBatchEmpty);

        await using var db = _harness.CreateDbContext();
        (await db.Set<PayoutBatch>().AsNoTracking().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Admin_csv_download_streams_text_csv_and_rejects_anonymous()
    {
        await SeedBaselineAsync(
            new[] { new SeededMaker("maker-a", "user-maker-a", "addr-a", "111/0100", "Alpha s.r.o.", "27074358") },
            new[] { ("o1", "maker-a", OrderState.Delivered, 30000L, 4500L, 0L) });
        using var client = AdminClient();

        var created = await client.PostAsync("/api/v1/payout-batches", null);
        var body = await created.Content.ReadFromJsonAsync<CreatePayoutBatchApiResponse>();

        var download = await client.GetAsync($"/api/v1/payout-batches/{body!.BatchId}/csv");
        download.StatusCode.Should().Be(HttpStatusCode.OK);
        download.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        var csv = await download.Content.ReadAsStringAsync();
        csv.Should().Contain("account;amount;vs;message");
        csv.Should().Contain("111/0100;");

        // Anonymous → 401.
        using var anon = _factory.CreateClient();
        var anonDownload = await anon.GetAsync($"/api/v1/payout-batches/{body.BatchId}/csv");
        anonDownload.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Unknown id → 404.
        var unknown = await client.GetAsync("/api/v1/payout-batches/does-not-exist/csv");
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Q0017_subject_placeholders_are_double_brace_after_migration()
    {
        await _harness.ResetMutableTablesAsync();
        await using var db = _harness.CreateDbContext();
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();

        // Zero rows carry a single-brace {order_number} that is not part of a
        // double-brace token. Use a raw ADO command — EF's SqlQueryRaw treats
        // braces as format placeholders.
        var singleBraceCount = await ScalarCountAsync(connection,
            "SELECT count(*) FROM email_template_translations " +
            "WHERE subject LIKE '%{order_number}%' AND subject NOT LIKE '%{{order_number}}%'");
        singleBraceCount.Should().Be(0);

        // The previously-broken rows now contain the double-brace token.
        var doubleBraceCount = await ScalarCountAsync(connection,
            "SELECT count(*) FROM email_template_translations WHERE subject LIKE '%{{order_number}}%'");
        doubleBraceCount.Should().BeGreaterThanOrEqualTo(16);
    }

    private static async Task<long> ScalarCountAsync(System.Data.Common.DbConnection connection, string sql)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt64(result);
    }

    /// <summary>
    /// Mirror of CreatePayoutBatch.CreatePayoutBatchResponse for
    /// deserialization. <c>State</c> is read as a string because the API
    /// serializes the enum with <c>JsonStringEnumConverter</c>.
    /// </summary>
    private sealed record CreatePayoutBatchApiResponse(
        string BatchId,
        string BatchNumber,
        string State,
        long TotalAmountMinor,
        string Currency,
        int OrderCount,
        int MakerCount,
        int ExcludedPartiallyRefundedOrderCount,
        int ExcludedNoBankAccountOrderCount,
        int ExcludedNoBankAccountMakerCount,
        bool AlreadyExisted,
        bool ArtifactsComplete,
        int FeeInvoiceCount,
        bool CsvReady);
}
