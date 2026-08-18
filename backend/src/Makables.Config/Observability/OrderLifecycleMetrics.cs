using System.Diagnostics.Metrics;
using Makables.Core.Domain.Observability;

namespace Makables.Config.Observability;

/// <summary>
/// Instruments on the <see cref="MakablesMeters.Orders"/> meter per ADR 0023
/// §4 (T-0165). Singleton; counters are thread-safe.
///
/// <para>
/// A zero-count run still records, because zero is the signal: these
/// counters exist to prove the timer Functions are alive, and a counter that
/// only writes when it has work looks identical to one whose Function has
/// stopped firing.
/// </para>
/// </summary>
public sealed class OrderLifecycleMetrics : IOrderLifecycleMetrics
{
    private readonly Counter<long> _autoDelivered;
    private readonly Counter<long> _autoCancelled;

    public OrderLifecycleMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        var meter = meterFactory.Create(MakablesMeters.Orders);

        _autoDelivered = meter.CreateCounter<long>(
            "makables.orders.auto_delivered",
            unit: "{order}",
            description: "Orders moved Shipped → Delivered by the auto-deliver timer.");
        _autoCancelled = meter.CreateCounter<long>(
            "makables.orders.auto_cancelled",
            unit: "{order}",
            description: "Orders cancelled for expired payment by the cleanup timer.");
    }

    public void RecordAutoDelivered(int count) => _autoDelivered.Add(Math.Max(0, count));

    public void RecordAutoCancelled(int count) => _autoCancelled.Add(Math.Max(0, count));
}
