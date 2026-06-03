using Makables.Core.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace Makables.Infra.Database.Orders;

/// <summary>
/// EF Core <see cref="IOrderRepository"/> impl. Tracked reads on every
/// <c>GetByIdFor*</c> because the state-machine command handlers (T-0067
/// mark paid, T-0071 accept, T-0072 ship, T-0076 deliver, T-0083 cancel,
/// T-0105 refund, T-0106 dispute, T-0107 admin manual) all mutate the
/// returned aggregate and rely on the
/// <c>UnitOfWorkPipelineBehavior</c> to commit.
///
/// <para>
/// Soft-delete filtering is automatic via
/// <c>MakablesDbContext.ApplySoftDeleteQueryFilters</c> — no explicit
/// <c>.Where(o =&gt; o.IsActive)</c> in any method below. Admin paths
/// needing soft-deleted rows must add <c>.IgnoreQueryFilters()</c>
/// explicitly with a comment.
/// </para>
/// </summary>
public sealed class OrderRepository(MakablesDbContext db) : IOrderRepository
{
    public IQueryable<Order> ForCustomer(string customerUserId)
    {
        if (string.IsNullOrWhiteSpace(customerUserId))
        {
            // Defensive: an empty session id should yield no rows, not
            // every row. The filter still composes correctly downstream.
            return db.Set<Order>().Where(o => false);
        }
        return db.Set<Order>().Where(o => o.CustomerUserId == customerUserId);
    }

    public IQueryable<Order> ForMaker(string makerId)
    {
        if (string.IsNullOrWhiteSpace(makerId))
        {
            return db.Set<Order>().Where(o => false);
        }
        return db.Set<Order>().Where(o => o.MakerId == makerId);
    }

    public IQueryable<Order> Unscoped() => db.Set<Order>();

    public Task<Order?> GetByIdForCustomerAsync(
        string orderId,
        string customerUserId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(orderId) || string.IsNullOrWhiteSpace(customerUserId))
            return Task.FromResult<Order?>(null);

        return db.Set<Order>()
            .FirstOrDefaultAsync(
                o => o.Id == orderId && o.CustomerUserId == customerUserId,
                cancellationToken);
    }

    public Task<Order?> GetByIdForMakerAsync(
        string orderId,
        string makerId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(orderId) || string.IsNullOrWhiteSpace(makerId))
            return Task.FromResult<Order?>(null);

        return db.Set<Order>()
            .FirstOrDefaultAsync(
                o => o.Id == orderId && o.MakerId == makerId,
                cancellationToken);
    }

    public Task<Order?> GetByIdUnscopedAsync(string orderId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(orderId))
            return Task.FromResult<Order?>(null);

        return db.Set<Order>()
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);
    }

    public Task<Order?> GetByPaymentProviderRefAsync(
        string paymentProviderRef,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(paymentProviderRef))
            return Task.FromResult<Order?>(null);

        var trimmed = paymentProviderRef.Trim();
        return db.Set<Order>()
            .FirstOrDefaultAsync(o => o.PaymentProviderRef == trimmed, cancellationToken);
    }

    public Task AddAsync(Order order, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);
        db.Set<Order>().Add(order);
        return Task.CompletedTask;
    }
}
