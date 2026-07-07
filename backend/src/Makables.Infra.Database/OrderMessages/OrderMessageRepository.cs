using Makables.Core.Domain.OrderMessages;
using Makables.Core.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace Makables.Infra.Database.OrderMessages;

/// <summary>
/// EF Core <see cref="IOrderMessageRepository"/> impl. Write-side surface
/// per ADR 0013 — every method bakes the audience scope predicate into
/// the SQL so cross-tenant probes touch zero rows.
///
/// <para>
/// Soft-delete filtering is automatic via the global query filter on
/// <see cref="Core.Domain.Common.Auditable"/>; no explicit
/// <c>.Where(o =&gt; o.IsActive)</c> below. The MarkAsRead bulk-UPDATE
/// uses <c>ExecuteUpdateAsync</c> for a single SQL round-trip
/// independent of thread length.
/// </para>
/// </summary>
public sealed class OrderMessageRepository(MakablesDbContext db) : IOrderMessageRepository
{
    public Task AddAsync(OrderMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        db.Set<OrderMessage>().Add(message);
        return Task.CompletedTask;
    }

    public async Task<int> MarkAsReadForCustomerAsync(
        string orderId,
        string customerUserId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(orderId)
            || string.IsNullOrWhiteSpace(customerUserId))
        {
            return 0;
        }

        // Bulk-UPDATE: WHERE Order.CustomerUserId == customerUserId AND
        // order_messages.OrderId == orderId AND author_role == Maker AND
        // read_by_counterparty_at IS NULL. Customer-scope predicate IS the
        // IDOR shield — a cross-tenant probe touches no rows (0 returned).
        // ExecuteUpdateAsync is a single round-trip; AsAsyncEnumerable-style
        // per-row iteration would re-read every row and be O(N) in client
        // SQL roundtrips. Per ADR 0023 paging NFRs.
        return await db.Set<OrderMessage>()
            .Where(m => m.OrderId == orderId
                     && m.AuthorRole == OrderMessageAuthorRole.Maker
                     && m.ReadByCounterpartyAt == null
                     && db.Set<Order>()
                         .Any(o => o.Id == orderId && o.CustomerUserId == customerUserId))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(m => m.ReadByCounterpartyAt, asOf),
                cancellationToken);
    }

    public async Task<int> MarkAsReadForMakerAsync(
        string orderId,
        string makerId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(orderId)
            || string.IsNullOrWhiteSpace(makerId))
        {
            return 0;
        }

        return await db.Set<OrderMessage>()
            .Where(m => m.OrderId == orderId
                     && m.AuthorRole == OrderMessageAuthorRole.Customer
                     && m.ReadByCounterpartyAt == null
                     && db.Set<Order>()
                         .Any(o => o.Id == orderId && o.MakerId == makerId))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(m => m.ReadByCounterpartyAt, asOf),
                cancellationToken);
    }

    public Task<bool> HasMakerReplySinceAsync(
        string orderId, DateTimeOffset sinceUtc, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(orderId))
            return Task.FromResult(false);

        // Targeted EXISTS — the T-0145 sweep needs a boolean, not the
        // thread contents.
        return db.Set<OrderMessage>()
            .AnyAsync(
                m => m.OrderId == orderId
                  && m.AuthorRole == OrderMessageAuthorRole.Maker
                  && m.CreatedAt > sinceUtc,
                cancellationToken);
    }
}
