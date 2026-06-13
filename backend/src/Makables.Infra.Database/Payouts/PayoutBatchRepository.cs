using Makables.Core.Domain.Payouts;
using Microsoft.EntityFrameworkCore;

namespace Makables.Infra.Database.Payouts;

/// <summary>
/// EF Core <see cref="IPayoutBatchRepository"/> impl per ADR 0013. All
/// reads are admin-host only (the aggregate is a privileged money record).
/// Tracked reads on <see cref="GetByIdUnscopedAsync"/> /
/// <see cref="GetOpenBatchAsync"/> / <see cref="GetByNumberAsync"/> because
/// the artifact-attach (T-0102b) and completion (T-0103) flows mutate the
/// returned aggregate and rely on the <c>UnitOfWorkPipelineBehavior</c> to
/// commit.
///
/// <para>
/// Soft-delete filtering is automatic via
/// <c>MakablesDbContext.ApplySoftDeleteQueryFilters</c>;
/// <see cref="GetByIdUnscopedAsync"/> opts out with
/// <c>.IgnoreQueryFilters()</c> for admin reconciliation. There is no
/// <c>UpdateAsync</c>/<c>DeleteAsync</c> by design (lock A.4).
/// </para>
/// </summary>
public sealed class PayoutBatchRepository(MakablesDbContext db) : IPayoutBatchRepository
{
    public Task AddAsync(PayoutBatch batch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);
        db.Set<PayoutBatch>().Add(batch);
        return Task.CompletedTask;
    }

    public Task<PayoutBatch?> GetByIdUnscopedAsync(string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
            return Task.FromResult<PayoutBatch?>(null);

        // IgnoreQueryFilters: admin + reconciliation lookup, same policy as
        // IOrderRepository.GetByIdUnscopedAsync.
        return db.Set<PayoutBatch>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
    }

    public Task<PayoutBatch?> GetOpenBatchAsync(string countryCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(countryCode))
            return Task.FromResult<PayoutBatch?>(null);

        var normalized = countryCode.ToUpperInvariant();
        // Soft-delete filter applies (no IgnoreQueryFilters): an open batch
        // is always active. The partial unique index guarantees at most one
        // Processing row per country, so FirstOrDefault is exact.
        return db.Set<PayoutBatch>()
            .FirstOrDefaultAsync(
                b => b.CountryCode == normalized && b.State == PayoutBatchState.Processing,
                cancellationToken);
    }

    public Task<PayoutBatch?> GetByNumberAsync(
        string countryCode, string batchNumber, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(countryCode) || string.IsNullOrWhiteSpace(batchNumber))
            return Task.FromResult<PayoutBatch?>(null);

        var normalizedCc = countryCode.ToUpperInvariant();
        var trimmedNumber = batchNumber.Trim();
        return db.Set<PayoutBatch>()
            .FirstOrDefaultAsync(
                b => b.CountryCode == normalizedCc && b.BatchNumber == trimmedNumber,
                cancellationToken);
    }
}
