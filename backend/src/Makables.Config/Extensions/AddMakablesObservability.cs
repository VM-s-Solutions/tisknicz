using Azure.Monitor.OpenTelemetry.AspNetCore;
using Makables.Config.Observability;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Formatting.Compact;

namespace Makables.Config.Extensions;

/// <summary>
/// Wires the observability stack per ADR 0023 §4:
///   - Serilog as the logger provider; <see cref="SensitivePropertyMasker"/>
///     redacts password/token/secret/signing-key properties at the sink so
///     they never reach App Insights.
///   - OpenTelemetry traces with the ADR's sampling policy (100% webhooks,
///     10% other successes; see <see cref="MakablesTraceSampler"/>) and
///     ASP.NET Core / HttpClient / EF Core instrumentation.
///   - OpenTelemetry metrics with the ASP.NET / HttpClient instrumentations
///     plus every meter listed in <see cref="MakablesMeters.All"/> — the
///     six required custom signals (outbox_lag, outbox_stalled,
///     payment_create_failures, webhook_received, auto_deliver, payout_batch)
///     are added to these meters in their owning modules in later tickets.
///   - Azure Monitor exporter for both, gated on a non-empty App Insights
///     connection string. Without the connection string the host still
///     emits structured logs to console — useful in tests and local runs.
/// </summary>
public static class AddMakablesObservabilityExtensions
{
    private const double SuccessTraceSamplingRatio = 0.1;

    public static WebApplicationBuilder AddMakablesObservability(
        this WebApplicationBuilder builder,
        string serviceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        builder.Host.UseSerilog((context, services, configuration) =>
        {
            configuration
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("service", serviceName)
                .Enrich.With<SensitivePropertyMasker>();

            if (context.HostingEnvironment.IsDevelopment())
            {
                configuration.WriteTo.Console();
            }
            else
            {
                configuration.WriteTo.Console(new RenderedCompactJsonFormatter());
            }
        });

        var otel = builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName))
            .WithTracing(t =>
            {
                t.SetSampler(new MakablesTraceSampler(SuccessTraceSamplingRatio));
                t.AddAspNetCoreInstrumentation(o => o.RecordException = true);
                t.AddHttpClientInstrumentation(o => o.RecordException = true);
                t.AddEntityFrameworkCoreInstrumentation();
            })
            .WithMetrics(m =>
            {
                m.AddAspNetCoreInstrumentation();
                m.AddHttpClientInstrumentation();
                foreach (var meter in MakablesMeters.All)
                {
                    m.AddMeter(meter);
                }
            });

        var appInsights = builder.Configuration["AzureMonitor:ConnectionString"]
            ?? builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
        if (!string.IsNullOrWhiteSpace(appInsights))
        {
            otel.UseAzureMonitor(o => o.ConnectionString = appInsights);
        }

        return builder;
    }
}
