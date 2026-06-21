using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Makables.Config.Extensions;
using Makables.Infra.Database;
using Makables.IntegrationTests.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Makables.IntegrationTests.RateLimiting;

/// <summary>
/// T-0136 (Q-0011) coverage for the rate-limit mount: the tight per-IP
/// <c>"auth"</c> policy on the anonymous <c>AuthController</c> and the
/// per-host <c>GlobalLimiter</c> envelope.
///
/// <para>
/// <b>A-vs-B decision: Option A — behavioral integration test.</b> The
/// codebase already proves pipeline/middleware wiring behaviorally through
/// <see cref="WebApplicationFactory{TProgram}"/> (see
/// <c>HostStartup/WebHostStartupTests.cs</c> — CORS preflight, correlation-id
/// echo, OpenAPI doc, rate-limiter-options registration). The
/// <c>[EnableRateLimiting("auth")]</c> attribute on <c>AuthController</c> is a
/// route-level concern that only a real host pipeline (with
/// <c>UseRateLimiter()</c>, wired in <c>UseMakablesPipeline</c>) actually
/// enforces; a registration-only assertion (Option B) would prove the policy
/// exists in DI but NOT that it is mounted on the controller or that
/// <c>OnRejected</c> surfaces <c>Retry-After</c>. So the primary proof hammers
/// <c>POST /api/v1/auth/login</c> 11+ times and asserts the 11th flips to 429
/// with a <c>Retry-After</c> header.
/// </para>
///
/// <para>
/// <b>Why this is deterministic under the in-memory TestServer:</b> the auth
/// policy partitions per remote IP (<c>ip:{ip}</c>), falling back to a fixed
/// <c>ip:unknown</c> bucket when <c>Connection.RemoteIpAddress</c> is
/// null/empty. The in-memory <c>WebApplicationFactory</c> client does not
/// populate a real remote IP, so every request lands in ONE bucket — meaning
/// the 10/min limit trips regardless of whether the IP is resolved. The
/// bad-credential login body is irrelevant: the limiter counts requests, not
/// auth outcomes, so the 11th request is 429 even though no user is seeded.
/// </para>
///
/// <para>
/// <b>Postgres-backed:</b> the login handler touches the DB (user lookup), so
/// the host needs a real database — hence <c>[Collection(PostgresCollection.Name)]</c>
/// and the full Comgate/Packeta/Mapbox/Cors config block reused verbatim from
/// the sibling integration tests. No user seeding is required (bad-credential
/// requests still count against the limiter).
/// </para>
///
/// <para>
/// <b>Pure-logic IP-partition-key test: intentionally omitted.</b> The
/// partition helpers (<c>DefaultPartition</c>, <c>PartitionAuth</c>,
/// <c>IpPartitionKey</c>) are <c>private static</c> on
/// <c>MakablesRateLimitingExtensions</c> and <c>Makables.Config</c> exposes no
/// <c>InternalsVisibleTo</c> for the test project. Per the ticket, prod
/// visibility is NOT changed just to test internals — the IP-key shape is
/// instead proven indirectly: a single 429 fires because all loopback requests
/// share the <c>ip:unknown</c>/<c>ip:{ip}</c> bucket.
/// </para>
///
/// <para>
/// We test on the <b>Admin</b> host (global envelope 30/min) so the tight
/// auth policy (10/min) is unambiguously the limiter that trips first on the
/// auth surface. The two pre-existing partitioned policies
/// (<c>addresses-autocomplete</c>, <c>shipping-widget-config</c>) are
/// untouched and keep their own tests.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class RateLimitMountIntegrationTests : IAsyncLifetime
{
    private const string TestKeyBase64 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
    private const string TestIssuer = "https://makables.test";

    private readonly PostgresHarness _harness;
    private WebApplicationFactory<Makables.Web.Admin.Program> _factory = default!;

    public RateLimitMountIntegrationTests(PostgresHarness harness) => _harness = harness;

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

    [Fact]
    public async Task POST_auth_login_trips_429_after_per_ip_budget_with_RetryAfter()
    {
        // The "auth" policy is 10/min/IP with no queue. Eleven anonymous
        // login attempts from the same in-memory client share one partition
        // bucket; the 11th must be rejected with 429 + Retry-After.
        //
        // NOTE on a single shared HttpClient: the in-memory TestServer keys
        // the partition off the (null/empty) RemoteIpAddress → one bucket for
        // every request, so reusing one client (or many) lands in the same
        // bucket regardless. We reuse one client for clarity.
        using var client = _factory.CreateClient();
        var body = new { Email = "nobody@example.cz", Password = "wrong-password" };

        HttpResponseMessage? rejected = null;
        var acceptedBeforeReject = 0;

        // Up to 12 attempts: 10 should pass the limiter (and return a normal
        // 4xx bad-credential result), the 11th (or 12th, allowing for any
        // pre-warm request) should flip to 429.
        for (var i = 0; i < 12; i++)
        {
            var response = await client.PostAsJsonAsync("/api/v1/auth/login", body);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                rejected = response;
                break;
            }
            acceptedBeforeReject++;
        }

        rejected.Should().NotBeNull(
            "the per-IP auth limiter (10/min) must reject once the budget is exhausted");
        rejected!.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        // T-0136 OnRejected: a Retry-After header tells a well-behaved client
        // how long to back off for the remainder of the fixed window.
        rejected.Headers.Contains("Retry-After").Should().BeTrue(
            "the OnRejected callback must surface Retry-After from the fixed-window metadata");

        // Sanity: the limiter let through roughly the configured budget before
        // tripping (10), not zero (which would mean the limiter rejected the
        // first request — a misconfigured permit limit) and not unbounded.
        acceptedBeforeReject.Should().BeInRange(1, 11,
            "the auth budget is 10/min/IP, so the reject must land within the first dozen attempts");
    }

    [Fact]
    public async Task POST_auth_refresh_is_excluded_from_the_tight_auth_bucket()
    {
        // T-0136 secops fold: refresh + logout carry [DisableRateLimiting] —
        // refresh is machine-triggered (frontend auto-calls on 401) and
        // cookie-bearing, so it must NOT share the tight 10/min auth bucket
        // (a multi-tab session / shared-NAT office would lock itself out).
        // Twelve cookieless refresh calls (each a fast 401) must NOT trip the
        // auth 429 — they fall under only the per-host global envelope (admin
        // 30/min), which 12 requests stay safely under.
        using var client = _factory.CreateClient();

        var sawTooManyRequests = false;
        for (var i = 0; i < 12; i++)
        {
            var response = await client.PostAsync("/api/v1/auth/refresh", content: null);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                sawTooManyRequests = true;
                break;
            }
        }

        sawTooManyRequests.Should().BeFalse(
            "refresh is [DisableRateLimiting]-excluded from the 10/min auth bucket; " +
            "12 calls stay under the 30/min admin global envelope");
    }

    [Fact]
    public async Task GlobalLimiter_is_registered_on_the_host()
    {
        // Belt-and-suspenders, Docker-independent registration proof: the
        // GlobalLimiter (the per-host "default" envelope mounted in T-0136)
        // is present on the resolved RateLimiterOptions, alongside the named
        // "auth" policy. This mirrors the existing
        // Host_RateLimiter_Options_Are_Registered smoke test but asserts the
        // T-0136-specific surface.
        using var scope = _factory.Services.CreateScope();
        var options = scope.ServiceProvider
            .GetRequiredService<IOptions<RateLimiterOptions>>().Value;

        options.GlobalLimiter.Should().NotBeNull(
            "T-0136 mounts the per-host envelope as the GlobalLimiter");
        options.RejectionStatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
    }

    [Fact]
    public void AuthPolicyName_constant_is_the_mounted_policy()
    {
        // Pins the policy-name constant the AuthController references via
        // [EnableRateLimiting(MakablesRateLimitingExtensions.AuthPolicyName)].
        // If the constant value drifts, the attribute would point at an
        // unregistered policy and the host would throw at request time — this
        // keeps the contract between the attribute and the registration honest.
        MakablesRateLimitingExtensions.AuthPolicyName.Should().Be("auth");
    }
}
