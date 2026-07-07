using Makables.Core.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace Makables.Infra.Database.Orders;

/// <summary>
/// EF Core <see cref="IDisputeRepository"/> impl (T-0106). Tracked reads
/// — the resolve handler mutates the returned <see cref="Dispute"/> and
/// the <c>UnitOfWorkPipelineBehavior</c> commits. Soft-delete filtering
/// is automatic via the global query filter on <c>Auditable.IsActive</c>.
/// </summary>
public sealed class DisputeRepository(MakablesDbContext db) : IDisputeRepository
{
    public Task AddAsync(Dispute dispute, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dispute);
        return db.Set<Dispute>().AddAsync(dispute, cancellationToken).AsTask();
    }

    public Task<Dispute?> GetOpenByOrderIdAsync(string orderId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(orderId))
            return Task.FromResult<Dispute?>(null);

        // At most one row matches per the ux_disputes_order_open partial
        // unique index; FirstOrDefault keeps the query shape cheap.
        return db.Set<Dispute>()
            .FirstOrDefaultAsync(
                d => d.OrderId == orderId && d.ResolvedAt == null,
                cancellationToken);
    }

    public Task<Dispute?> GetByIdUnscopedAsync(string disputeId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(disputeId))
            return Task.FromResult<Dispute?>(null);

        return db.Set<Dispute>()
            .FirstOrDefaultAsync(d => d.Id == disputeId, cancellationToken);
    }

    public IAsyncEnumerable<string> GetAutoEscalationCandidateIdsUnscopedReadOnlyAsync(
        DateTimeOffset asOf, CancellationToken cancellationToken)
    {
        var cutoff = asOf.AddDays(-Dispute.ResponseWindowDays);
        return db.Set<Dispute>()
            .AsNoTracking()
            .Where(d => d.ResolvedAt == null
                     && d.Source == DisputeSource.Customer
                     && d.AutoEscalatedAt == null
                     && d.CreatedAt < cutoff)
            .OrderBy(d => d.CreatedAt)
            .Select(d => d.Id)
            .AsAsyncEnumerable();
    }
}
