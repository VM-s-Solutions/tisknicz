using Makables.Config.Extensions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Background-jobs host per ADR 0020. Functions land in Phase 2+ tickets
// (ProcessOutbox, GenerateInvoice, AutoDeliverOrders, etc.). This Program.cs
// wires the same Infrastructure + Mediator + Clients as the Web hosts.
// Auth / CORS / RateLimiting are intentionally NOT registered here —
// Functions trigger from queue/timer/HTTP-with-key, not from an authenticated
// HTTP request.

var builder = FunctionsApplication.CreateBuilder(args);

builder.Services.AddMakablesInfrastructure(builder.Configuration);
builder.Services.AddMakablesMediator();
builder.Services.AddMakablesClients(builder.Configuration);

// App Insights wiring lands in T-0014.
builder.Services.AddApplicationInsightsTelemetryWorkerService();
builder.Services.ConfigureFunctionsApplicationInsights();

builder.Build().Run();
