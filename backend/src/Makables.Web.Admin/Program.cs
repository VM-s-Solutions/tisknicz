// Admin API host. Per ADR 0005 (per-audience route groups + hosts) and
// patterns §A.16. All registration is delegated to AddMakables* extensions
// in Makables.Config; this Program.cs stays a flat list per ADR 0008.

using Makables.Config;
using Makables.Config.Extensions;
using Makables.Core.Domain.Identity;

const string Audience = MakablesHosts.Admin;

var builder = WebApplication.CreateBuilder(args);

builder.AddMakablesObservability("makables-admin-api");
builder.Services.AddMakablesInfrastructure(builder.Configuration);
builder.Services.AddMakablesMediator();
builder.Services.AddMakablesAuth(builder.Configuration, Audience);
builder.Services.AddMakablesHostAudience(Audience);
builder.Services.AddMakablesCors(builder.Configuration, builder.Environment, Audience);
builder.Services.AddMakablesRateLimiting(Audience);
builder.Services.AddMakablesClients(builder.Configuration);
builder.Services.AddMakablesBlobStorage(builder.Configuration);
builder.Services.AddMakablesPdfRendering();
builder.Services.AddMakablesApiVersioning();

builder.Services.AddMakablesControllers();

// OpenAPI per host per version (T-0012). The host exposes /openapi/v1.json
// which NSwag in T-0013 consumes to generate the TypeScript client.
builder.Services.AddMakablesOpenApi("v1");

var app = builder.Build();

app.UseMakablesPipeline();

// Ops endpoints are excluded from the OpenAPI document (ADR 0022): they are
// not part of the client contract, and leaving them described made every ops
// change invalidate all four committed NSwag spec hashes and red-light the
// api-parity gate. Pinned by OpsEndpointsExcludedFromContractTests.
app.MapGet("/", () => "Makables Admin API — alive.").ExcludeFromDescription();
// Liveness probe for the App Service health check (healthCheckPath in
// infra/bicep/modules/app-service.bicep). Deliberately dependency-free: a
// probe that touched the DB would turn a transient outage into instance
// restarts. Any 2xx counts as healthy.
app.MapGet("/health", () => Results.Ok(new { status = "healthy" })).ExcludeFromDescription();
app.MapControllers();
app.MapOpenApi();

app.Run();

namespace Makables.Web.Admin
{
    public partial class Program { }
}
