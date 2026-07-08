using Makables.Core.Domain.Payouts;
using Microsoft.EntityFrameworkCore;

namespace Makables.Infra.Database.Payouts;

/// <summary>
/// EF Core <see cref="IPayoutDeductionRepository"/> impl (T-0146). Admin
/// host only per ADR 0013.
/// </summary>
public sealed class PayoutDeductionRepository(MakablesDbContext db) : IPayoutDeductionRepository
{
    public Task AddAsync(PayoutDeduction deduction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(deduction);
        db.Set<PayoutDeduction>().Add(deduction);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<PayoutDeduction>> GetPendingForMakerAsync(
        string makerId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(makerId))
            return Array.Empty<PayoutDeduction>();

        return await db.Set<PayoutDeduction>()
            .Where(d => d.MakerId == makerId && d.PayoutBatchId == null)
            .ToListAsync(cancellationToken);
    }
}
