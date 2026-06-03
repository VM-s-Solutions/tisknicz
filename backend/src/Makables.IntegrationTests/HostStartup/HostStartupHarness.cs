using Makables.Infra.Database;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Makables.IntegrationTests.HostStartup;

/// <summary>
/// Shared <see cref="WebApplicationFactory{TProgram}"/> builder for every
/// host-startup integration test. Encapsulates the stub configuration
/// each <c>ValidateOnStart</c> Options block requires (Jwt, SendGrid,
/// PublicAppUrls, Mapbox, Ares, AzureBlobStorage, Cors) and the
/// Postgres → in-memory-SQLite swap that lets the host actually start
/// without external infrastructure.
///
/// <para>
/// Lives as a static helper rather than an abstract base class so a
/// test that only needs to boot one host doesn't accidentally inherit
/// every <c>[Fact]</c> from <see cref="HostStartupTestBase{TProgram}"/>
/// — that's how T-0049c's <c>MultipartSchemaTransformerTests</c> stays
/// scoped to its two assertions instead of re-running the full 9-test
/// startup smoke battery. T-0049c Copilot review L.
/// </para>
///
/// <para>
/// New <c>ValidateOnStart</c> Options blocks land here. Drift is
/// dramatic: any test that boots a host without the new stub value will
/// fail with <c>OptionsValidationException</c> at startup, so the
/// catalogue stays honest.
/// </para>
/// </summary>
internal static class HostStartupHarness
{
    public static WebApplicationFactory<TProgram> Build<TProgram>() where TProgram : class
    {
        return new WebApplicationFactory<TProgram>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("IntegrationTest");

            // AddMakablesInfrastructure throws on empty Postgres connection
            // string (reviewer T-0009 MAJOR #1 fix). Supply a placeholder so
            // the host starts; ConfigureServices below swaps the registration
            // for SQLite before any DbContext is actually constructed.
            // T-0021 added eager validation of the JWT signing key — supply
            // a deterministic 32-byte key so JwtIssuer can be resolved if a
            // test asks for it; tests never produce real tokens with it.
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Postgres"] = "Host=placeholder;Database=ignored",
                    ["Jwt:Issuer"] = "https://makables.test",
                    ["Jwt:SigningKeyBase64"] = Convert.ToBase64String(new byte[32]),
                    // T-0028: SendGrid + PublicAppUrls Options are now
                    // ValidateOnStart per sec reviewer M-3 / B-2.
                    ["SendGrid:ApiKey"] = "SG.integration-test-stub",
                    ["SendGrid:DefaultFromAddress"] = "no-reply@makables.test",
                    ["PublicAppUrls:WebBaseUrl"] = "https://makables.test",
                    ["Mapbox:AccessToken"] = "pk.integration-test-stub",
                    ["Ares:BaseUrl"] = "https://ares.integration-test.local",
                    // T-0042: AddMakablesBlobStorage requires either
                    // ConnectionString (dev/CI) or ServiceUri (Managed
                    // Identity). Stub connection string for tests.
                    ["AzureBlobStorage:ConnectionString"] = "UseDevelopmentStorage=true",
                    // T-0035 sec reviewer B1: AddMakablesCors now fails-closed
                    // outside Development when allowed-origins is unconfigured.
                    // Supply explicit origins per audience for the test host.
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

                var connection = new SqliteConnection("DataSource=:memory:");
                connection.Open();

                services.AddSingleton(connection);
                services.AddDbContext<MakablesDbContext>(options =>
                {
                    options.UseSqlite(connection);
                });
            });
        });
    }
}
