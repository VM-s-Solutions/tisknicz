// Public API host. Per ADR 0005 (per-audience route groups + hosts) and
// patterns §A.16. All registration is delegated to AddMakables* extensions
// in Makables.Config; this Program.cs stays a flat list per ADR 0008.

using Makables.Config;
using Makables.Config.Extensions;
using Makables.Core.Domain.Identity;

const string Audience = MakablesHosts.Public;

var builder = WebApplication.CreateBuilder(args);

builder.AddMakablesObservability("makables-public-api");
builder.Services.AddMakablesInfrastructure(builder.Configuration);
builder.Services.AddMakablesMediator();
builder.Services.AddMakablesAuth(builder.Configuration, Audience);
builder.Services.AddMakablesHostAudience(Audience);
builder.Services.AddMakablesCors(builder.Configuration, builder.Environment, Audience);
builder.Services.AddMakablesRateLimiting(Audience);
builder.Services.AddMakablesClients(builder.Configuration);
builder.Services.AddMakablesBlobStorage(builder.Configuration);
builder.Services.AddMakablesApiVersioning();

builder.Services.AddMakablesControllers();

// OpenAPI per host per version (T-0012). The host exposes /openapi/v1.json
// which NSwag in T-0013 consumes to generate the TypeScript client.
builder.Services.AddMakablesOpenApi("v1");

var app = builder.Build();

app.UseMakablesPipeline();

app.MapGet("/", () => "Makables Public API — alive.");
app.MapControllers();
app.MapOpenApi();

app.Run();

namespace Makables.Web.Public
{
    public partial class Program { }
}
