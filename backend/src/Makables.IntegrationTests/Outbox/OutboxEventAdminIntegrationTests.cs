using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Makables.Core.Domain.Auditing;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Outbox;
using Makables.Infra.Common.Auth;
using Makables.Infra.Database;
using Makables.IntegrationTests.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Makables.IntegrationTests.Outbox;

/// <summary>
/// End-to-end coverage for the T-0109 admin outbox triage endpoints
/// (<c>POST /api/v1/outbox-events/{id}/retry | acknowledge</c>). Pins:
/// retry makes a stalled row due again (and writes an audit row); acknowledge
/// drains the row from the stalled set (and writes an audit row with the
/// reason).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OutboxEventAdminIntegrationTests : IAsyncLifetime
{
    private const string TestKeyBase64 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
    private const string TestIssuer = "https://makables.test";
    private const string AdminUserId = "user-admin-1";
    private const string OutboxId = "ob-stalled-1";

    private static readonly DateTimeOffset SeedAt = new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgresHarness _harness;
    private WebApplicationFactory<Makables.Web.Admin.Program> _factory = default!;

    public OutboxEventAdminIntegrationTests(PostgresHarness harness) => _harness = harness;

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

    private async Task SeedStalledRowAsync()
    {
        await using var db = _harness.CreateDbContext();

        var admin = User.Create(AdminUserId, "admin@makables.cz", UserRole.Admin, "Admin", "CZ",
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        admin.ConfirmEmail(SeedAt);
        admin.MarkCreated("test-seed", SeedAt);
        db.Set<User>().Add(admin);

        // A stalled row: failed enough that the policy stopped retrying
        // (NextRetryAt = null after a Permanent failure), retry_count > 0.
        var ev = OutboxEvent.Enqueue(OutboxId, "order:1", "order.paid", "{}", SeedAt);
        ev.RecordFailure(OutboxErrorKind.Permanent, "email.invalidAddress", nextRetryAt: null);
        db.Set<OutboxEvent>().Add(ev);

        await db.SaveChangesAsync();
    }

    private static string IssueAdminToken()
    {
        var issuer = new JwtIssuer(Options.Create(new JwtOptions
        {
            Issuer = TestIssuer,
            SigningKeyBase64 = TestKeyBase64,
            AccessTokenLifetime = TimeSpan.FromMinutes(15),
        }));
        var user = User.Create(AdminUserId, "admin@makables.cz", UserRole.Admin, "Admin", "CZ",
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        user.ConfirmEmail(SeedAt);
        return issuer.Issue(user, MakablesAudiences.Admin, DateTimeOffset.UtcNow).Token;
    }

    private HttpClient CreateAdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", IssueAdminToken());
        return client;
    }

    [Fact]
    public async Task Retry_makes_a_stalled_row_due_again_for_the_sweep()
    {
        await SeedStalledRowAsync();
        using var client = CreateAdminClient();

        var response = await client.PostAsync($"/api/v1/outbox-events/{OutboxId}/retry", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = _harness.CreateDbContext();
        var row = await db.Set<OutboxEvent>().AsNoTracking().FirstAsync(e => e.Id == OutboxId);
        row.NextRetryAt.Should().NotBeNull("the row is due again so the sweep re-picks it");
        row.RetryCount.Should().Be(2);
        row.ProcessedAt.Should().BeNull();

        var audit = await db.Set<AdminAuditLogEntry>().AsNoTracking()
            .Where(a => a.TargetId == OutboxId).ToListAsync();
        audit.Should().HaveCount(1);
        audit[0].ActionCode.Should().Be("outbox.retry");
    }

    [Fact]
    public async Task Acknowledge_removes_a_row_from_the_stalled_set()
    {
        await SeedStalledRowAsync();
        using var client = CreateAdminClient();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/outbox-events/{OutboxId}/acknowledge",
            new { reason = "Trvale neplatná e-mailová adresa." });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = _harness.CreateDbContext();
        var row = await db.Set<OutboxEvent>().AsNoTracking().FirstAsync(e => e.Id == OutboxId);
        row.ProcessedAt.Should().NotBeNull();
        row.AcknowledgedAt.Should().NotBeNull();
        row.AcknowledgedBy.Should().Be(AdminUserId);

        var audit = await db.Set<AdminAuditLogEntry>().AsNoTracking()
            .Where(a => a.TargetId == OutboxId).ToListAsync();
        audit.Should().HaveCount(1);
        audit[0].ActionCode.Should().Be("outbox.acknowledge");
        audit[0].Notes.Should().Be("Trvale neplatná e-mailová adresa.");
    }

    [Fact]
    public async Task Retry_missing_row_is_404_outbox_rowNotFound()
    {
        await SeedStalledRowAsync();
        using var client = CreateAdminClient();

        var response = await client.PostAsync("/api/v1/outbox-events/does-not-exist/retry", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
