namespace Makables.Core.Domain.Observability;

/// <summary>
/// Payout-batch instrumentation seam per ADR 0023. Pure interface (no
/// packages in Core.Domain); the implementation
/// (<c>Config/Observability/PayoutMetrics.cs</c>) records onto the
/// <c>MakablesMeters.Payouts</c> meter. The handler depends on this rather
/// than on <c>System.Diagnostics.Metrics</c> directly so
/// <c>Core.AppServices</c> stays package-free.
///
/// <para>
/// Instruments are recorded BEFORE the UoW pipeline commits — a failed
/// commit leaves a counted-but-uncommitted run. Acceptable telemetry noise
/// at MVP scale; documented on the implementation.
/// </para>
/// </summary>
public interface IPayoutMetrics
{
    /// <summary>
    /// Increment the batch-run counter tagged by outcome ∈ { created,
    /// already_open, empty, week_already_processed, currency_mismatch }.
    /// </summary>
    void RecordRun(string outcome);

    /// <summary>Record a successful claim: order count + the batch total (minor units).</summary>
    void RecordClaimed(int orderCount, long totalAmountMinor);

    /// <summary>
    /// Record an exclusion bucket: reason ∈ { partially_refunded,
    /// no_bank_account } + the excluded order count.
    /// </summary>
    void RecordExcluded(string reason, int orderCount);
}
