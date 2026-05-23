// Maker API host. Per ADR 0005 (per-audience route groups + hosts) and
// patterns §A.16. All registration is delegated to AddMakables* extensions
// in Makables.Config; this Program.cs stays a flat list per ADR 0008.

using Makables.Config;
using Makables.Config.Extensions;

const string Audience = "maker";

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMakablesInfrastructure(builder.Configuration);
builder.Services.AddMakablesMediator();
builder.Services.AddMakablesAuth(builder.Configuration, Audience);
builder.Services.AddMakablesCors(builder.Configuration, Audience);
builder.Services.AddMakablesRateLimiting(Audience);
builder.Services.AddMakablesClients(builder.Configuration);

builder.Services.AddControllers();

var app = builder.Build();

app.UseMakablesPipeline();

app.MapGet("/", () => "Makables Maker API — alive.");
app.MapControllers();

app.Run();

// Test hook: make Program partial+public so WebApplicationFactory<Program>
// can target it from the integration-tests project.
namespace Makables.Web.Maker
{
    public partial class Program { }
}
