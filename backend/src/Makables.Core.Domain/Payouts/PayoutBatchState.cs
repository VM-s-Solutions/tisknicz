namespace Makables.Core.Domain.Payouts;

/// <summary>
/// Lifecycle of a <see cref="PayoutBatch"/>. Exactly two values per T-0101
/// lock A.4: the batch is born directly in <see cref="Processing"/>
/// (creation is atomic in one UoW, so a <c>Pending</c> state would be an
/// unobservable instant) and moves to <see cref="Completed"/> only when the
/// operator marks the wire transfer settled (T-0103). No other transitions.
///
/// Stored as a string via <c>HasConversion</c> (matching
/// <c>InvoiceConfiguration</c>) so admin SQL reads cleanly.
/// </summary>
public enum PayoutBatchState
{
    Processing = 1,
    Completed = 2,
}
