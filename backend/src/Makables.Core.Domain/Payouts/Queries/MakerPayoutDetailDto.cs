namespace Makables.Core.Domain.Payouts.Queries;

/// <summary>
/// Maker payout-batch detail (T-0112 / US-maker-0012 AC-3): the batch header
/// + this maker's per-order breakdown. <see cref="MakerTotalPaidMinor"/> is
/// the per-maker slice (NOT the cross-maker batch total). No
/// <c>BankReference</c> / CSV reference (cross-maker PII / operator-internal).
/// </summary>
public sealed record MakerPayoutDetailDto(
    string BatchId,
    string BatchNumber,
    PayoutBatchState State,
    long MakerTotalPaidMinor,
    string Currency,
    DateTimeOffset? CompletedAt,
    string? FeeInvoiceId,
    IReadOnlyList<MakerPayoutOrderLineDto> Orders);
