using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Minimal entry point. Functions are added in subsequent tickets
// (T-0020+ outbox, T-0077 auto-deliver, etc.).
// OpenTelemetry + App Insights wiring lands in T-0014.
var builder = FunctionsApplication.CreateBuilder(args);

builder.Services.AddApplicationInsightsTelemetryWorkerService();
builder.Services.ConfigureFunctionsApplicationInsights();

builder.Build().Run();
