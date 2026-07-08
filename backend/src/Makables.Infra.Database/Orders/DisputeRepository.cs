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
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(d => d.Id == disputeId, cancellationToken);
    }

    public Task<Dispute?> GetByIdUnscopedReadOnlyAsync(string disputeId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(disputeId))
            return Task.FromResult<Dispute?>(null);

        return db.Set<Dispute>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == disputeId, cancellationToken);
    }

    public Task<Dispute?> GetByIdForCustomerReadOnlyAsync(
        string disputeId, string customerUserId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(disputeId) || string.IsNullOrWhiteSpace(customerUserId))
            return Task.FromResult<Dispute?>(null);

        // Dispute has no EF navigation to Order (lightweight aggregate per
        // ADR 0013) — join explicitly on OrderId, same pattern as
        // IOrderRepository.GetPayoutEligibleUnscopedAsync's maker join.
        var query =
            from d in db.Set<Dispute>().AsNoTracking()
            join o in db.Set<Order>().AsNoTracking() on d.OrderId equals o.Id
            where d.Id == disputeId && o.CustomerUserId == customerUserId
            select d;
        return query.FirstOrDefaultAsync(cancellationToken);
    }

    public Task<Dispute?> GetByIdForMakerAsync(
        string disputeId, string makerId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(disputeId) || string.IsNullOrWhiteSpace(makerId))
            return Task.FromResult<Dispute?>(null);

        var query =
            from d in db.Set<Dispute>()
            join o in db.Set<Order>().AsNoTracking() on d.OrderId equals o.Id
            where d.Id == disputeId && o.MakerId == makerId
            select d;
        return query.FirstOrDefaultAsync(cancellationToken);
    }
}
