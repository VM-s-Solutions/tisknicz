using System.Net;
using FluentAssertions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Shipping;
using Makables.Infra.Database;
using Makables.IntegrationTests.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Makables.IntegrationTests.Shipping;

/// <summary>
/// End-to-end coverage for the T-0070 Public-host widget-config endpoint
/// (<c>GET /api/v1/public/shipping/widget-config</c>). Real Postgres
/// (via <see cref="PostgresHarness"/>) so the
/// <see cref="ShippingCarrierFactory"/> reads the seeded
/// <c>country_configuration</c> row. Anonymous + cacheable per ADR 0017.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class WidgetConfigEndpointTests : IAsyncLifetime
{
    private const string TestKeyBase64 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
    private const string TestIssuer = "https://makables.test";

    private readonly PostgresHarness _harness;
    private WebApplicationFactory<Makables.Web.Public.Program> _factory = default!;

    public WidgetConfigEndpointTests(PostgresHarness harness)
    {
        _harness = harness;
    }

    public async Task InitializeAsync()
    {
        await _harness.ResetMutableTablesAsync();
        _factory = new WebApplicationFactory<Makables.Web.Public.Program>()
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
    }

    public Task DisposeAsync()
    {
        _factory?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task GET_widget_config_returns_200_with_cache_header()
    {
        // Public host. Anonymous (no Authorization header). The CZ
        // country_configuration row from the initial migration has
        // DefaultShippingCarrier = "packeta", so the factory resolves
        // the keyed PacketaShippingCarrier and returns its widget config.
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(
            "/api/v1/public/shipping/widget-config?country=CZ&locale=cs-CZ");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.ToString().Should().Be("public, max-age=3600");

        var body = await response.Content.ReadAsStringAsync();
        // The endpoint returns the PickupPointWidgetConfig record verbatim;
        // the contract-shape assertions just confirm the keyed Packeta
        // values came through. (PascalCase from JsonSerializer defaults;
        // camelCase from AddMakablesControllers — either lands; we match
        // both by lowercasing.)
        var lower = body.ToLowerInvariant();
        lower.Should().Contain("integration-test-packeta-public-key");
        lower.Should().Contain("widget.packeta.test");
    }

    [Fact]
    public async Task GET_widget_config_with_unknown_country_returns_error()
    {
        // No country_configuration row for "ZZ" → factory returns
        // Configuration(ShippingCarrierConfigurationError) → mapped to 500
        // by MakablesApiController. Per T-0070 locked decision A.3 — a
        // mis-routed checkout page would be a misconfig, not a user error,
        // so the 5xx classification is intentional.
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(
            "/api/v1/public/shipping/widget-config?country=ZZ&locale=cs-CZ");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var body = await response.Content.ReadAsStringAsync();
        body.ToLowerInvariant().Should().Contain(
            BusinessErrorMessage.ShippingCarrierConfigurationError.ToLowerInvariant());
    }
}
