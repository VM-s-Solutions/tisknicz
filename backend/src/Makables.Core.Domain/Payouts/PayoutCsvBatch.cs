namespace Makables.Core.Domain.Payouts;

/// <summary>
/// Format-neutral input to <see cref="IPayoutCsvFormatter"/>: the batch
/// number (for the VS column) + one <see cref="PayoutCsvLine"/> per maker
/// in the order the artifact service supplies (deterministic — by company
/// name then maker id). Pure data; the formatter owns the column layout.
/// </summary>
public sealed record PayoutCsvBatch(
    string BatchNumber,
    IReadOnlyList<PayoutCsvLine> Lines);

/// <summary>
/// One bank-transfer line: the maker's verbatim bank account, the summed
/// payout in minor units, and the maker company name (for the message
/// column).
/// </summary>
public sealed record PayoutCsvLine(
    string BankAccount,
    long AmountMinor,
    string MakerCompanyName);
