using System.Diagnostics.Metrics;
using Makables.Core.Domain.Observability;

namespace Makables.Config.Observability;

/// <summary>
/// Instruments on the <see cref="MakablesMeters.Outbox"/> meter per ADR 0023
/// §4 (T-0165, closing Q-0033 — the meter name was registered by T-0014 but
/// nothing ever recorded onto it, so the alert rules read empty).
/// Singleton, built once from <see cref="IMeterFactory"/>; every instrument
/// type used here is thread-safe.
/// </summary>
public sealed class OutboxMetrics : IOutboxMetrics
{
    private readonly Gauge<double> _lagSeconds;
    private readonly Gauge<long> _stalled;
    private readonly Counter<long> _dispatched;

    public OutboxMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        var meter = meterFactory.Create(MakablesMeters.Outbox);

        // Gauge, not ObservableGauge: the value is only knowable during a
        // sweep (it comes out of the same query the dispatcher already runs).
        // An observable callback would have to open its own DbContext on the
        // collector's schedule, which is both a surprise query and a worse
        // number — it would sample between sweeps rather than at one.
        _lagSeconds = meter.CreateGauge<double>(
            "makables.outbox.lag_seconds",
            unit: "s",
            description: "Age of the oldest event picked up by the last sweep; 0 when the sweep found nothing.");
        _stalled = meter.CreateGauge<long>(
            "makables.outbox.stalled",
            unit: "{event}",
            description: "Rows currently stalled (Permanent error or retries exhausted), sampled once per sweep.");
        _dispatched = meter.CreateCounter<long>(
            "makables.outbox.dispatched",
            unit: "{event}",
            description: "Events handled by a sweep, tagged by outcome (routed / stalled / publish_failed).");
    }

    public void RecordLagSeconds(double lagSeconds) =>
        _lagSeconds.Record(Math.Max(0, lagSeconds));

    public void RecordStalled(int stalledCount) =>
        _stalled.Record(Math.Max(0, stalledCount));

    public void RecordDispatched(string outcome, int count)
    {
        if (count <= 0) return;
        _dispatched.Add(count, new KeyValuePair<string, object?>("outcome", outcome));
    }
}
