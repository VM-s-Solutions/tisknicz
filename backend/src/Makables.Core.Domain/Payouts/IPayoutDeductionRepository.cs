namespace Makables.Core.Domain.Payouts;

/// <summary>
/// Persistence access for <see cref="PayoutDeduction"/> (T-0146). Admin
/// host only per ADR 0013 — money-adjacent record, same visibility class
/// as <see cref="IPayoutBatchRepository"/>.
/// </summary>
public interface IPayoutDeductionRepository
{
    /// <summary>Track <paramref name="deduction"/> as a pending insert.</summary>
    Task AddAsync(PayoutDeduction deduction, CancellationToken cancellationToken);

    /// <summary>
    /// Every unclaimed (<c>PayoutBatchId == null</c>) deduction for
    /// <paramref name="makerId"/>, tracked so
    /// <see cref="PayoutDeduction.ApplyToPayoutBatch"/> can be called
    /// inside the claim's UoW. Backs <c>CreatePayoutBatch.Handler</c>'s
    /// per-maker total reduction.
    /// </summary>
    Task<IReadOnlyList<PayoutDeduction>> GetPendingForMakerAsync(
        string makerId, CancellationToken cancellationToken);
}
