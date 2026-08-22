using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Makables.Core.Domain.Auditing;
using Makables.Core.Domain.Identity;
using Makables.Infra.Database;
using Makables.IntegrationTests.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Makables.IntegrationTests.Admin;

/// <summary>
/// T-0177 + T-0178 over the real Admin host: the audit log must scope to
/// one entity server-side (audit ADM-H2 — the order detail used to
/// client-filter the global slice and could render an EMPTY evidence
/// trail), and the user lookup behind the GDPR erase must resolve
/// server-side, stay audience-gated, distinguish "not found" from
/// "already erased" (ADM-H1/M9), and leave a <c>user.lookup</c> PII-read
/// audit row on success only (T-0137 policy).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AdminReadIntegrityIntegrationTests : IAsyncLifetime
{
    private const string TestKeyBase64 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
    private const string TestIssuer = "https://makables.test";

    private readonly PostgresHarness _harness;
    private WebApplicationFactory<Makables.Web.Admin.Program> _factory = default!;

    public AdminReadIntegrityIntegrationTests(PostgresHarness harness)
    {
        _harness = harness;
    }

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
                });
            });
    }

    public Task DisposeAsync()
    {
        _factory?.Dispose();
        return Task.CompletedTask;
    }

    private async Task<User> SeedUserAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MakablesDbContext>();
        var user = User.Create(
            Guid.NewGuid().ToString("N")[..24], email, UserRole.Customer, "Anna Nováková", "CZ",
            "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        db.Set<User>().Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task Lookup_requires_an_admin_audience()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/admin-users/lookup?email=anna@example.cz");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "the GDPR-erase lookup exposes account PII and must never answer an anonymous caller");
    }

    [Fact]
    public async Task Lookup_rejects_both_selectors_at_once()
    {
        using var client = AdminClient();

        var response = await client.GetAsync(
            "/api/v1/admin-users/lookup?id=user-1&email=anna@example.cz");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "which selector wins would be invisible to the caller");
    }

    [Fact]
    public async Task Lookup_of_an_unknown_email_is_404_and_writes_no_audit_row()
    {
        using var client = AdminClient();

        var response = await client.GetAsync("/api/v1/admin-users/lookup?email=nobody@example.cz");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CountReadAuditRowsAsync()).Should().Be(0, "a 404 discloses nothing");
    }

    [Fact]
    public async Task Lookup_resolves_the_user_and_writes_one_read_audit_row()
    {
        var user = await SeedUserAsync("lookup-target@example.cz");
        using var client = AdminClient();

        var response = await client.GetAsync(
            "/api/v1/admin-users/lookup?email=lookup-target@example.cz");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain(user.Id);
        body.Should().Contain("lookup-target@example.cz");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MakablesDbContext>();
        var rows = await db.Set<AdminAuditLogEntry>()
            .Where(a => a.ActionCode == "user.lookup")
            .ToListAsync();
        rows.Should().HaveCount(1);
        rows[0].TargetId.Should().Be(user.Id, "the row targets the resolved id, never the typed email");
        rows[0].TargetEntity.Should().Be("user");
    }

    [Fact]
    public async Task Audit_log_targetId_filter_narrows_to_one_entity()
    {
        using var client = AdminClient();

        // The filter is accepted and applied server-side; with an empty log
        // both shapes return zero rows, which is what the order detail now
        // paginates over (instead of the global slice it used to narrow
        // client-side).
        var response = await client.GetAsync(
            "/api/v1/audit-log?targetEntity=order&targetId=order-42");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("\"totalCount\":0");
    }

    private HttpClient AdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", IssueAdminToken());
        return client;
    }

    private static string IssueAdminToken()
    {
        var issuer = new Makables.Infra.Common.Auth.JwtIssuer(
            Microsoft.Extensions.Options.Options.Create(new Makables.Infra.Common.Auth.JwtOptions
            {
                Issuer = TestIssuer,
                SigningKeyBase64 = TestKeyBase64,
                AccessTokenLifetime = TimeSpan.FromMinutes(15),
            }));
        var admin = User.Create("admin-1", "ops@makables.test", UserRole.Admin, "Ops", "CZ",
            "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        return issuer.Issue(admin, "admin", DateTimeOffset.UtcNow).Token;
    }

    private async Task<int> CountReadAuditRowsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MakablesDbContext>();
        return await db.Set<AdminAuditLogEntry>().CountAsync(a => a.ActionCode == "user.lookup");
    }
}
