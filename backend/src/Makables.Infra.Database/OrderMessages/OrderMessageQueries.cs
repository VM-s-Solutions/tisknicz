using Makables.Core.Domain.Common;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.OrderMessages;
using Makables.Core.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace Makables.Infra.Database.OrderMessages;

/// <summary>
/// EF Core <see cref="IOrderMessageQueries"/> impl. Every method is
/// <c>AsNoTracking</c> + <c>IgnoreAutoIncludes</c> and projects straight
/// into <see cref="OrderMessageDto"/>. Per ADR 0023.
///
/// <para>
/// The audience-scope predicate is baked into the SQL <c>WHERE</c>
/// clause via the Order subquery — a cross-tenant probe selects no
/// rows because the message's parent Order is not owned by the
/// requesting party.
/// </para>
///
/// <para>
/// AuthorName resolution at projection time:
/// <list type="bullet">
///   <item><description>AuthorRole == Customer → Order.ContactName snapshot
///     (the customer's own name as they entered it at checkout).</description></item>
///   <item><description>AuthorRole == Maker → Maker.CompanyName (the
///     ARES-verified business label).</description></item>
/// </list>
/// Single SQL roundtrip — no per-row follow-up query.
/// </para>
/// </summary>
public sealed class OrderMessageQueries(MakablesDbContext db) : IOrderMessageQueries
{
    public async Task<PagedData<OrderMessageDto>> GetByOrderForCustomerAsync(
        string orderId,
        string customerUserId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(orderId)
            || string.IsNullOrWhiteSpace(customerUserId))
        {
            return PagedData<OrderMessageDto>.Empty(page, pageSize);
        }

        // IDOR shield: the message-row's parent Order must be owned by
        // the customer. Subquery on Orders sweeps the predicate into
        // the SQL plan. Soft-deleted rows hidden by the global query
        // filter on both Order + OrderMessage.
        var baseQuery = db.Set<OrderMessage>()
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Where(m => m.OrderId == orderId
                     && db.Set<Order>()
                         .Any(o => o.Id == orderId && o.CustomerUserId == customerUserId));

        var totalCount = await baseQuery.CountAsync(cancellationToken);
        if (totalCount == 0)
        {
            return PagedData<OrderMessageDto>.Empty(page, pageSize);
        }

        var items = await baseQuery
            // Newest first; tiebreak on lexicographically-ordered ulid id.
            .OrderByDescending(m => m.CreatedAt)
            .ThenByDescending(m => m.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new OrderMessageDto(
                m.Id,
                m.OrderId,
                m.AuthorRole,
                m.AuthorRole == OrderMessageAuthorRole.Customer
                    ? db.Set<Order>()
                        .Where(o => o.Id == m.OrderId)
                        .Select(o => o.ContactName)
                        .FirstOrDefault() ?? string.Empty
                    : db.Set<Maker>()
                        .Where(mk => db.Set<Order>()
                            .Any(o => o.Id == m.OrderId && o.MakerId == mk.Id))
                        .Select(mk => mk.CompanyName)
                        .FirstOrDefault() ?? string.Empty,
                m.Body,
                m.CreatedAt,
                m.AuthorRole == OrderMessageAuthorRole.Customer))
            .ToListAsync(cancellationToken);

        return new PagedData<OrderMessageDto>(items, page, pageSize, totalCount);
    }

    public async Task<PagedData<OrderMessageDto>> GetByOrderForMakerAsync(
        string orderId,
        string makerId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(orderId)
            || string.IsNullOrWhiteSpace(makerId))
        {
            return PagedData<OrderMessageDto>.Empty(page, pageSize);
        }

        var baseQuery = db.Set<OrderMessage>()
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Where(m => m.OrderId == orderId
                     && db.Set<Order>()
                         .Any(o => o.Id == orderId && o.MakerId == makerId));

        var totalCount = await baseQuery.CountAsync(cancellationToken);
        if (totalCount == 0)
        {
            return PagedData<OrderMessageDto>.Empty(page, pageSize);
        }

        var items = await baseQuery
            .OrderByDescending(m => m.CreatedAt)
            .ThenByDescending(m => m.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new OrderMessageDto(
                m.Id,
                m.OrderId,
                m.AuthorRole,
                m.AuthorRole == OrderMessageAuthorRole.Customer
                    ? db.Set<Order>()
                        .Where(o => o.Id == m.OrderId)
                        .Select(o => o.ContactName)
                        .FirstOrDefault() ?? string.Empty
                    : db.Set<Maker>()
                        .Where(mk => db.Set<Order>()
                            .Any(o => o.Id == m.OrderId && o.MakerId == mk.Id))
                        .Select(mk => mk.CompanyName)
                        .FirstOrDefault() ?? string.Empty,
                m.Body,
                m.CreatedAt,
                // Maker host: IsMine when the message is maker-authored.
                m.AuthorRole == OrderMessageAuthorRole.Maker))
            .ToListAsync(cancellationToken);

        return new PagedData<OrderMessageDto>(items, page, pageSize, totalCount);
    }
}
