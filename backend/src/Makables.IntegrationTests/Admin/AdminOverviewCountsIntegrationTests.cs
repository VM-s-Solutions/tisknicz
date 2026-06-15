using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Outbox;
using Makables.Core.Domain.Payouts;
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

namespace Makables.IntegrationTests.Admin;

/// <summary>
/// T-0126 / Q-0027 admin overview count endpoints on the Admin host
/// (<c>GET /api/v1/payout-batches/count?state=Processing</c> +
/// <c>GET /api/v1/outbox-events/stalled/count</c>). Pins: the Processing-payout
/// count excludes Completed batches; the stalled-outbox count matches T-0109's
/// stalled set exactly (stalled counted; processed / acknowledged / due / fresh
/// excluded); and a customer/maker JWT cannot replay (admin-only). Real
/// Postgres. Integration fixtures MarkCreated (Auditable rows); the outbox
/// rows are bookkeeping (not Auditable) so they enqueue without MarkCreated.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AdminOverviewCountsIntegrationTests : IAsyncLifetime
{
    private const string TestKeyBase64 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
    private const string TestIssuer = "https://makables.test";
    private const string CountryCode = "CZ";
    private const string Currency = "CZK";

    private static readonly DateTimeOffset SeedAt = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgresHarness _harness;
    private WebApplicationFactory<Makables.Web.Admin.Program> _factory = default!;

    public AdminOverviewCountsIntegrationTests(PostgresHarness harness) => _harness = harness;

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

    // Distinct 2-char country codes per Processing batch — the partial unique
    // index ux_payout_batches_open_per_country allows only ONE open
    // (Processing) batch per country. CountByStateAsync counts across all
    // countries (no country filter), so spreading the open batches over
    // distinct countries is the realistic multi-country shape.
    private static readonly string[] ProcessingCountries = ["CZ", "SK", "PL", "DE", "AT"];

    private static PayoutBatch Batch(string id, string number, string countryCode, PayoutBatchState state)
    {
        var batch = PayoutBatch.Create(id, number, countryCode,
            totalAmountMinor: 10000, currency: Currency, orderCount: 1, makerCount: 1,
            excludedPartiallyRefundedOrderCount: 0, excludedNoBankAccountOrderCount: 0,
            excludedNoBankAccountMakerCount: 0);
        if (state == PayoutBatchState.Completed)
        {
            var clock = Substitute.For<Makables.Core.Domain.Common.IClock>();
            clock.UtcNow.Returns(SeedAt.AddDays(2));
            batch.Complete(clock, $"WIRE-{id}", new DateOnly(2026, 6, 3), "user-admin-1");
        }
        batch.MarkCreated("test-seed", SeedAt);
        return batch;
    }

    private async Task SeedPayoutBatchesAsync(int processing, int completed)
    {
        await using var db = _harness.CreateDbContext();
        for (var i = 0; i < processing; i++)
        {
            db.Set<PayoutBatch>().Add(Batch(
                $"pb-proc-{i}", $"VYP-{ProcessingCountries[i]}-2026-W23",
                ProcessingCountries[i], PayoutBatchState.Processing));
        }
        for (var i = 0; i < completed; i++)
        {
            // Completed rows are out of the partial index (Processing-only), so
            // they can share a country; unique numbers keep the (cc, number)
            // index legal.
            db.Set<PayoutBatch>().Add(Batch(
                $"pb-done-{i}", $"VYP-CZ-2026-W{40 + i:D2}", CountryCode, PayoutBatchState.Completed));
        }
        await db.SaveChangesAsync();
    }

    private async Task SeedOutboxAsync()
    {
        await using var db = _harness.CreateDbContext();
        var set = db.Set<OutboxEvent>();

        // 2 STALLED rows — RecordFailure with nextRetryAt: null (ladder exhausted).
        var s1 = OutboxEvent.Enqueue("ob-stalled-1", "order:1", "order.paid", "{}", SeedAt);
        s1.RecordFailure(OutboxErrorKind.Permanent, "email.invalidAddress", nextRetryAt: null);
        set.Add(s1);
        var s2 = OutboxEvent.Enqueue("ob-stalled-2", "order:2", "order.paid", "{}", SeedAt);
        s2.RecordFailure(OutboxErrorKind.Configuration, "email.misconfigured", nextRetryAt: null);
        set.Add(s2);

        // DUE/retrying row — NextRetryAt non-null (re-scheduled). Excluded.
        var due = OutboxEvent.Enqueue("ob-due-1", "order:3", "order.paid", "{}", SeedAt);
        due.RecordFailure(OutboxErrorKind.Transient, "email.providerTransient", nextRetryAt: SeedAt.AddMinutes(5));
        set.Add(due);

        // FRESH row — NextRetryAt = CreatedAt, no failure. Excluded (LastErrorKind None).
        set.Add(OutboxEvent.Enqueue("ob-fresh-1", "order:4", "order.paid", "{}", SeedAt));

        // PROCESSED row. Excluded (ProcessedAt set).
        var processed = OutboxEvent.Enqueue("ob-processed-1", "order:5", "order.paid", "{}", SeedAt);
        processed.MarkProcessed(SeedAt.AddMinutes(1));
        set.Add(processed);

        // ACKNOWLEDGED row — Acknowledge sets ProcessedAt + NextRetryAt = null
        // after a failure. Excluded by ProcessedAt != null.
        var ack = OutboxEvent.Enqueue("ob-ack-1", "order:6", "order.paid", "{}", SeedAt);
        ack.RecordFailure(OutboxErrorKind.Permanent, "email.invalidAddress", nextRetryAt: null);
        ack.Acknowledge("user-admin-1", SeedAt.AddMinutes(2));
        set.Add(ack);

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Processing_count_excludes_completed_batches()
    {
        await SeedPayoutBatchesAsync(processing: 3, completed: 2);
        using var client = ClientWith(MakablesAudiences.Admin, UserRole.Admin);

        var response = await client.GetAsync("/api/v1/payout-batches/count?state=Processing");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<CountResponse>();
        body!.Count.Should().Be(3);
    }

    [Fact]
    public async Task Stalled_outbox_count_matches_the_stalled_set_predicate()
    {
        await SeedOutboxAsync();
        using var client = ClientWith(MakablesAudiences.Admin, UserRole.Admin);

        var response = await client.GetAsync("/api/v1/outbox-events/stalled/count");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<CountResponse>();
        // Only the 2 stalled rows: due, fresh, processed, acknowledged excluded.
        body!.Count.Should().Be(2);
    }

    [Fact]
    public async Task Count_endpoints_reject_non_admin_jwt()
    {
        await SeedPayoutBatchesAsync(processing: 1, completed: 0);
        await SeedOutboxAsync();

        using var customerClient = ClientWith(MakablesAudiences.Customer, UserRole.Customer);
        (await customerClient.GetAsync("/api/v1/payout-batches/count?state=Processing"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await customerClient.GetAsync("/api/v1/outbox-events/stalled/count"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var makerClient = ClientWith(MakablesAudiences.Maker, UserRole.Maker);
        (await makerClient.GetAsync("/api/v1/payout-batches/count?state=Processing"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private sealed record CountResponse(int Count);
}
