using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

namespace Makables.Config.Extensions;

/// <summary>
/// Wires the observability stack per ADR 0023 (NFRs):
///   - Serilog as the logger provider (structured JSON in non-Development).
///   - OpenTelemetry traces + metrics with ASP.NET Core / HttpClient / EF Core
///     instrumentation.
///   - Azure Monitor exporter for both, gated on a non-empty App Insights
///     connection string. Without the connection string the host still emits
///     structured logs to console — useful in tests and local runs.
/// </summary>
public static class AddMakablesObservabilityExtensions
{
    public static WebApplicationBuilder AddMakablesObservability(
        this WebApplicationBuilder builder,
        string serviceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        // Serilog reads configuration from appsettings + env, with sensible defaults.
        builder.Host.UseSerilog((context, services, configuration) =>
        {
            configuration
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("service", serviceName);

            // Default sink: console. Structured JSON outside Development so the
            // log shipper can parse it; pretty text in Development for humans.
            if (context.HostingEnvironment.IsDevelopment())
            {
                configuration.WriteTo.Console();
            }
            else
            {
                configuration.WriteTo.Console(new Serilog.Formatting.Compact.RenderedCompactJsonFormatter());
            }
        });

        var otel = builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName))
            .WithTracing(t => t
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddEntityFrameworkCoreInstrumentation())
            .WithMetrics(m => m
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation());

        // App Insights export only when a connection string is provided. Tests and
        // local development run without it and rely on console output instead.
        var appInsights = builder.Configuration["AzureMonitor:ConnectionString"]
            ?? builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
        if (!string.IsNullOrWhiteSpace(appInsights))
        {
            otel.UseAzureMonitor(o => o.ConnectionString = appInsights);
        }

        return builder;
    }
}
