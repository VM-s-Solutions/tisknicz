using System.Text.Json;
using FluentAssertions;
using Makables.Infra.Database;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Makables.IntegrationTests.HostStartup;

/// <summary>
/// Pins the OpenAPI document shape that
/// <see cref="Makables.Config.Extensions.MakablesOpenApiExtensions.AddMakablesOpenApi"/>
/// produces for the Maker host's multipart upload endpoint (T-0049c).
/// Boots <see cref="Makables.Web.Maker.Program"/> via
/// <see cref="WebApplicationFactory{TEntryPoint}"/> with the same stub
/// configuration the host-startup tests use, GETs
/// <c>/openapi/v1.json</c>, and asserts:
///
/// <list type="bullet">
/// <item>The multipart upload endpoint's request body schema is rewritten
/// to the canonical <c>{ type: "string", format: "binary" }</c> +
/// <c>required: ["file"]</c> shape (AC-1).</item>
/// <item>The transformer leaves non-multipart bodies untouched — the
/// JSON Create endpoint's request body still resolves to a
/// <c>CreateProductRequest</c> shape (AC-4).</item>
/// </list>
///
/// AC-5 (wrapper-DTO branch with non-file properties surviving the
/// rewrite) is design-level only today — T-0064 will land the first
/// production wrapper DTO and exercise it. The transformer source code
/// is the contract for that branch until then.
/// </summary>
public class MultipartSchemaTransformerTests
{
    private static WebApplicationFactory<Makables.Web.Maker.Program> BuildFactory()
    {
        return new WebApplicationFactory<Makables.Web.Maker.Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("IntegrationTest");

            // Mirror HostStartupTestBase's stub config — every Options
            // block that's ValidateOnStart needs a value or the host
            // refuses to boot. Production hosts read all of these from
            // Azure App Config / Key Vault.
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Postgres"] = "Host=placeholder;Database=ignored",
                    ["Jwt:Issuer"] = "https://makables.test",
                    ["Jwt:SigningKeyBase64"] = Convert.ToBase64String(new byte[32]),
                    ["SendGrid:ApiKey"] = "SG.integration-test-stub",
                    ["SendGrid:DefaultFromAddress"] = "no-reply@makables.test",
                    ["PublicAppUrls:WebBaseUrl"] = "https://makables.test",
                    ["Mapbox:AccessToken"] = "pk.integration-test-stub",
                    ["Ares:BaseUrl"] = "https://ares.integration-test.local",
                    ["AzureBlobStorage:ConnectionString"] = "UseDevelopmentStorage=true",
                    ["Cors:AllowedOrigins:maker:0"] = "https://maker.makables.test",
                });
            });

            builder.ConfigureServices(services =>
            {
                // Swap the Postgres registration for in-memory SQLite so
                // the host actually starts. The OpenAPI spec is generated
                // from the type model and route metadata — no DB roundtrip
                // — so SQLite is enough to serve /openapi/v1.json.
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

    [Fact]
    public async Task Multipart_Upload_Endpoint_Has_Canonical_Binary_Schema()
    {
        using var factory = BuildFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");
        response.IsSuccessStatusCode.Should().BeTrue("/openapi/v1.json must be served");

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);

        var schema = document.RootElement
            .GetProperty("paths")
            .GetProperty("/api/v1/products/{productId}/images")
            .GetProperty("post")
            .GetProperty("requestBody")
            .GetProperty("content")
            .GetProperty("multipart/form-data")
            .GetProperty("schema");

        schema.GetProperty("type").GetString().Should().Be("object",
            "the multipart request body schema must be an object wrapper around the file properties");

        var fileProperty = schema.GetProperty("properties").GetProperty("file");
        fileProperty.GetProperty("type").GetString().Should().Be("string",
            "the canonical OpenAPI 3.0 multipart-file property is type: string");
        fileProperty.GetProperty("format").GetString().Should().Be("binary",
            "format: binary is the marker NSwag's Fetch template reads to emit FileParameter rather than string");

        var required = schema.GetProperty("required").EnumerateArray()
            .Select(e => e.GetString()).ToArray();
        required.Should().Contain("file",
            "marking file required is what removes the `| undefined` union from the generated client signature");
    }

    [Fact]
    public async Task Json_Request_Bodies_Are_Not_Touched_By_The_Multipart_Transformer()
    {
        using var factory = BuildFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");
        response.IsSuccessStatusCode.Should().BeTrue();

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);

        // The Create endpoint takes [FromBody] CreateProductRequest as
        // application/json — the multipart transformer must leave its
        // schema entirely alone. A regression here would mean the
        // transformer's content-type filter is missing and JSON DTOs
        // are being clobbered.
        var schema = document.RootElement
            .GetProperty("paths")
            .GetProperty("/api/v1/products")
            .GetProperty("post")
            .GetProperty("requestBody")
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("schema");

        // The emitter resolves request bodies to a $ref against the
        // component schemas dictionary. The exact reference name is
        // CreateProductRequest (the controller-level record). We accept
        // either a $ref form OR an inline `type: object` form so the
        // assertion is resilient to a future change that inlines the
        // schema (e.g. if the emitter ever stops emitting components for
        // controller-nested types); the load-bearing part is that the
        // schema is NOT a string + binary stub.
        var isRef = schema.TryGetProperty("$ref", out var refElement);
        if (isRef)
        {
            refElement.GetString().Should().EndWith("CreateProductRequest",
                "the JSON Create body should still resolve to the CreateProductRequest component schema");
        }
        else
        {
            schema.GetProperty("type").GetString().Should().Be("object",
                "if the emitter ever inlines the schema, it must still be an object — not the multipart binary stub");
            schema.TryGetProperty("format", out _).Should().BeFalse(
                "the JSON body schema must not carry the multipart binary format marker");
        }
    }
}
