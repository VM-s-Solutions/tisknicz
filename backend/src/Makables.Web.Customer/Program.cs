// Customer API host — Makables.Web.Customer.
// Per ADR 0005 (per-audience route groups + hosts) and patterns §A.16.
// Real wiring (AddMakablesInfrastructure / AddMakablesAuth / AddMakablesCors /
// AddMakablesMediator / AddMakablesClients / AddMakablesRateLimiting) is filled in by T-0008/T-0009.

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.MapGet("/", () => "Makables Customer API — alive.");
app.Run();
