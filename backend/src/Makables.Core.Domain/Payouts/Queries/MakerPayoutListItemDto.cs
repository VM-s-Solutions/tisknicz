namespace Makables.Core.Domain.Payouts.Queries;

/// <summary>
/// One row of the maker payout-batch list (T-0112 / US-maker-0012 AC-1).
/// <see cref="MakerTotalPaidMinor"/> is the SUM of THIS maker's
/// <c>Order.MakerPayoutAmountMinor</c> in the batch — the per-maker slice,
/// NOT the cross-maker <see cref="PayoutBatch.TotalAmountMinor"/> (the
/// operator's whole-batch wire total). <see cref="FeeInvoiceId"/> backs the
/// "Stáhnout fakturu" download link (T-0112a); null until the Fee invoice
/// renders. The operator CSV + <c>BankReference</c> are deliberately ABSENT
/// (cross-maker PII / operator-internal — Q4 + Option E).
/// </summary>
public sealed record MakerPayoutListItemDto(
    string BatchId,
    string BatchNumber,
    PayoutBatchState State,
    long MakerTotalPaidMinor,
    int OrderCount,
    string Currency,
    DateTimeOffset? CompletedAt,
    string? FeeInvoiceId);
