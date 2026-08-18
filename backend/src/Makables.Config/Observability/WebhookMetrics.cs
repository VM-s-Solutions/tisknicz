using System.Diagnostics.Metrics;
using Makables.Core.Domain.Observability;

namespace Makables.Config.Observability;

/// <summary>
/// Instruments on the <see cref="MakablesMeters.Webhooks"/> meter per ADR
/// 0023 §4 (T-0165). Singleton; counters are thread-safe.
/// </summary>
public sealed class WebhookMetrics : IWebhookMetrics
{
    private readonly Counter<long> _received;

    public WebhookMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        var meter = meterFactory.Create(MakablesMeters.Webhooks);

        _received = meter.CreateCounter<long>(
            "makables.webhooks.received",
            unit: "{delivery}",
            description: "Inbound webhook deliveries, tagged by provider and outcome.");
    }

    public void RecordReceived(string provider, string outcome) =>
        _received.Add(1,
            new KeyValuePair<string, object?>("provider", provider),
            new KeyValuePair<string, object?>("outcome", outcome));
}
