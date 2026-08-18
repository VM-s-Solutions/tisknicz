namespace Makables.Core.Domain.Observability;

/// <summary>
/// Order-lifecycle instrumentation seam per ADR 0023 §4 (T-0165). Covers the
/// transitions no human triggers, which are exactly the ones that fail
/// silently: a timer Function that stops firing produces no error, no log
/// line, and no user complaint until a customer notices their order never
/// moved.
/// </summary>
public interface IOrderLifecycleMetrics
{
    /// <summary>Orders auto-delivered by the T-0077 timer in one run (0 is a valid, recorded value).</summary>
    void RecordAutoDelivered(int count);

    /// <summary>Orders auto-cancelled for expired payment by the T-0083 timer in one run.</summary>
    void RecordAutoCancelled(int count);
}
