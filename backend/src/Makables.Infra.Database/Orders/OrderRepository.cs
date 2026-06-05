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

        // IgnoreQueryFilters: this method is the admin + GDPR-reconciliation
        // lookup (see IOrderRepository XML doc). Per ADR 0013, admin paths
        // that legitimately need to see soft-deleted rows opt out of the
        // global soft-delete filter explicitly here. Callers that must hide
        // soft-deleted rows should use the owner-scoped GetByIdForCustomerAsync
        // or GetByIdForMakerAsync. T-0060 Copilot review C-6.
        return db.Set<Order>()
            .IgnoreQueryFilters()
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

    public async Task<OrderAttachment?> GetAttachmentForCustomerAsync(
        string orderId,
        string attachmentId,
        string customerUserId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(orderId)
            || string.IsNullOrWhiteSpace(attachmentId)
            || string.IsNullOrWhiteSpace(customerUserId))
        {
            return null;
        }

        // Project to the attachment directly so we don't materialise the
        // whole order aggregate just to read one child row. AsNoTracking
        // because the customer-host download is a pure read; if the row
        // is null, the controller surfaces 404. Soft-deleted orders are
        // hidden by the global query filter — a soft-deleted parent's
        // attachments thus invisible to the customer, matching AC-12.
        return await db.Set<Order>()
            .AsNoTracking()
            .Where(o => o.Id == orderId && o.CustomerUserId == customerUserId)
            .SelectMany(o => o.Attachments)
            .FirstOrDefaultAsync(a => a.Id == attachmentId, cancellationToken);
    }

    public async Task<OrderAttachment?> GetAttachmentForMakerAsync(
        string orderId,
        string attachmentId,
        string makerId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(orderId)
            || string.IsNullOrWhiteSpace(attachmentId)
            || string.IsNullOrWhiteSpace(makerId))
        {
            return null;
        }

        return await db.Set<Order>()
            .AsNoTracking()
            .Where(o => o.Id == orderId && o.MakerId == makerId)
            .SelectMany(o => o.Attachments)
            .FirstOrDefaultAsync(a => a.Id == attachmentId, cancellationToken);
    }
}
