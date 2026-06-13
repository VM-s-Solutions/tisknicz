using System.Diagnostics.Metrics;
using Makables.Core.Domain.Observability;

namespace Makables.Config.Observability;

/// <summary>
/// First instruments on the <see cref="MakablesMeters.Payouts"/> meter per
/// ADR 0023 (T-0102a). Implements <see cref="IPayoutMetrics"/> so
/// <c>Core.AppServices</c> stays free of a
/// <c>System.Diagnostics.Metrics</c> dependency. Singleton — built once via
/// <see cref="IMeterFactory"/>; counters/histograms are thread-safe.
///
/// <para>
/// <b>Pre-commit emission.</b> The handler records onto these instruments
/// BEFORE the UoW pipeline commits, so a failed commit leaves a
/// counted-but-uncommitted run (e.g. a <c>created</c> tick with no batch
/// row). Acceptable telemetry noise at MVP scale — the audit row, which
/// only rides committed transactions, is the authoritative trail.
/// </para>
/// </summary>
public sealed class PayoutMetrics : IPayoutMetrics
{
    private readonly Counter<long> _batchRuns;
    private readonly Counter<long> _ordersClaimed;
    private readonly Counter<long> _ordersExcluded;
    private readonly Histogram<long> _batchAmountMinor;

    public PayoutMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        var meter = meterFactory.Create(MakablesMeters.Payouts);

        _batchRuns = meter.CreateCounter<long>(
            "makables.payouts.batch_runs",
            unit: "{run}",
            description: "Payout batch creation runs, tagged by outcome.");
        _ordersClaimed = meter.CreateCounter<long>(
            "makables.payouts.orders_claimed",
            unit: "{order}",
            description: "Orders claimed into payout batches.");
        _ordersExcluded = meter.CreateCounter<long>(
            "makables.payouts.orders_excluded",
            unit: "{order}",
            description: "Orders excluded from payout batches, tagged by reason.");
        _batchAmountMinor = meter.CreateHistogram<long>(
            "makables.payouts.batch_amount_minor",
            unit: "{minor}",
            description: "Total claimed amount per payout batch, in minor units.");
    }

    public void RecordRun(string outcome) =>
        _batchRuns.Add(1, new KeyValuePair<string, object?>("outcome", outcome));

    public void RecordClaimed(int orderCount, long totalAmountMinor)
    {
        _ordersClaimed.Add(orderCount);
        _batchAmountMinor.Record(totalAmountMinor);
    }

    public void RecordExcluded(string reason, int orderCount)
    {
        if (orderCount <= 0) return;
        _ordersExcluded.Add(orderCount, new KeyValuePair<string, object?>("reason", reason));
    }
}
