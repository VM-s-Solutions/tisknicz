using System.Diagnostics.Metrics;
using Makables.Core.Domain.Observability;

namespace Makables.Config.Observability;

/// <summary>
/// Instruments on the <see cref="MakablesMeters.Payments"/> meter per ADR
/// 0023 §4 (T-0165). Singleton; counters are thread-safe.
/// </summary>
public sealed class PaymentMetrics : IPaymentMetrics
{
    private readonly Counter<long> _sessions;

    public PaymentMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        var meter = meterFactory.Create(MakablesMeters.Payments);

        _sessions = meter.CreateCounter<long>(
            "makables.payments.sessions_created",
            unit: "{session}",
            description: "Payment-session creation attempts, tagged by provider and outcome.");
    }

    public void RecordSessionCreated(string provider, string outcome) =>
        _sessions.Add(1,
            new KeyValuePair<string, object?>("provider", provider),
            new KeyValuePair<string, object?>("outcome", outcome));
}
