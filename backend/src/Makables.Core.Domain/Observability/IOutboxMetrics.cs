namespace Makables.Core.Domain.Observability;

/// <summary>
/// Outbox instrumentation seam per ADR 0023 §4 (T-0165, closing Q-0033).
/// Same shape as <see cref="IPayoutMetrics"/>: a pure interface here, the
/// instruments in <c>Config/Observability/OutboxMetrics.cs</c> on the
/// <c>MakablesMeters.Outbox</c> meter, so <c>Core.AppServices</c> stays
/// free of <c>System.Diagnostics.Metrics</c>.
///
/// <para>
/// The alert that matters is "the outbox stopped draining". Two signals
/// answer it from opposite directions and both are recorded once per sweep:
/// <see cref="RecordLagSeconds"/> says how old the oldest work is (rises
/// when the sweep falls behind), <see cref="RecordStalled"/> says how much
/// work has given up retrying (rises when work is stuck for good). Lag
/// alone misses a stalled row — a Permanent failure stops being "due", so
/// it never ages the lag.
/// </para>
/// </summary>
public interface IOutboxMetrics
{
    /// <summary>
    /// Age, in seconds, of the oldest event this sweep picked up — 0 when
    /// the sweep found nothing, which is the healthy steady state.
    /// </summary>
    void RecordLagSeconds(double lagSeconds);

    /// <summary>Total rows currently stalled (Permanent / retry-exhausted).</summary>
    void RecordStalled(int stalledCount);

    /// <summary>
    /// Count the sweep's per-event outcomes. <paramref name="outcome"/> ∈
    /// { routed, stalled, publish_failed } — the three terminal states of
    /// <c>DispatchSummary</c>, tagged rather than split into three
    /// instruments so a single query can show the mix.
    /// </summary>
    void RecordDispatched(string outcome, int count);
}

/// <summary>
/// Canonical <c>outcome</c> tag values for
/// <see cref="IOutboxMetrics.RecordDispatched"/>. Constants, not strings at
/// the call site: a typo would silently create a second time series.
/// </summary>
public static class OutboxDispatchOutcome
{
    public const string Routed = "routed";
    public const string Stalled = "stalled";
    public const string PublishFailed = "publish_failed";
}
