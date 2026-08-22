using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Makables.Infra.Database;
using Makables.IntegrationTests.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Makables.IntegrationTests.Auth;

/// <summary>
/// T-0167 + T-0168 (auth-recovery bundle) over the real Customer-host
/// pipeline: the Google OAuth callback must 302 the BROWSER back to the
/// frontend (never strand it on raw JSON) with no PII in the Location,
/// and the anonymous resend-confirmation endpoint must answer uniformly
/// regardless of account existence (no enumeration).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AuthRecoveryIntegrationTests : IAsyncLifetime
{
    private const string TestKeyBase64 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
    private const string TestIssuer = "https://makables.test";

    private readonly PostgresHarness _harness;
    private WebApplicationFactory<Makables.Web.Customer.Program> _factory = default!;

    public AuthRecoveryIntegrationTests(PostgresHarness harness)
    {
        _harness = harness;
    }

    public Task InitializeAsync()
    {
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
                    if (dbContextDescriptor is not null)
                    {
                        services.Remove(dbContextDescriptor);
                    }
                    services.AddDbContext<MakablesDbContext>(o =>
                        o.UseNpgsql(_harness.ConnectionString));
                });
            });
        return _harness.ResetMutableTablesAsync();
    }

    public Task DisposeAsync()
    {
        _factory?.Dispose();
        return Task.CompletedTask;
    }

    private HttpClient CreateNoRedirectClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task Google_callback_with_invalid_state_redirects_to_login_with_error_code()
    {
        using var client = CreateNoRedirectClient();

        var response = await client.GetAsync(
            "/api/v1/auth/google/callback?code=fake-code&state=garbage-state");

        response.StatusCode.Should().Be(HttpStatusCode.Found);
        var location = response.Headers.Location!.ToString();
        location.Should().StartWith("https://makables.test/login?oauth_error=");
        // Machine-readable code only — no token, email or other PII.
        location.Should().NotContain("@");
        location.Should().NotContain("code=");
        location.Should().NotContain("state=");
    }

    [Fact]
    public async Task Resend_confirmation_answers_identically_for_known_and_unknown_email()
    {
        using var client = _factory.CreateClient();

        var register = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email = "resend-known@example.cz",
            password = "Silne.heslo.123",
            fullName = "Anna Nováková",
            countryCodePrimary = "CZ",
        });
        register.StatusCode.Should().Be(HttpStatusCode.OK);

        var known = await client.PostAsJsonAsync("/api/v1/auth/resend-confirmation",
            new { email = "resend-known@example.cz" });
        var unknown = await client.PostAsJsonAsync("/api/v1/auth/resend-confirmation",
            new { email = "resend-unknown@example.cz" });

        known.StatusCode.Should().Be(HttpStatusCode.OK);
        unknown.StatusCode.Should().Be(HttpStatusCode.OK);
        (await known.Content.ReadAsStringAsync())
            .Should().Be(await unknown.Content.ReadAsStringAsync(),
                "account existence must not be observable from the response");
    }
}
