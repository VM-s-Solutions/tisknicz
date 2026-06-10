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
/// AuthorName resolution (Gate 8 M-1 fold):
/// <list type="bullet">
///   <item><description>AuthorRole == Customer → Order.ContactName snapshot
///     (the customer's own name as they entered it at checkout).</description></item>
///   <item><description>AuthorRole == Maker → Maker.CompanyName (the
///     ARES-verified business label).</description></item>
/// </list>
/// Both names are constant across the single-order thread, so they are
/// fetched ONCE in an owner-scoped pre-query and projected into the page
/// SELECT as parameterised locals — no per-row correlated subqueries.
/// The pre-query doubles as the cross-tenant short-circuit (null →
/// empty page); the ownership EXISTS predicate STAYS in the page query
/// as defence-in-depth.
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

        // Gate 8 M-1 fold: AuthorName sources (Order.ContactName +
        // Maker.CompanyName) are constant across the single-order thread.
        // Fetch them ONCE via an owner-scoped projection instead of two
        // correlated subqueries per page row. Null result == unknown
        // order OR cross-tenant probe → empty page per T-0080 contract.
        var names = await db.Set<Order>()
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Where(o => o.Id == orderId && o.CustomerUserId == customerUserId)
            .Select(o => new
            {
                o.ContactName,
                MakerCompanyName = db.Set<Maker>()
                    .Where(mk => mk.Id == o.MakerId)
                    .Select(mk => mk.CompanyName)
                    .FirstOrDefault(),
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (names is null)
        {
            return PagedData<OrderMessageDto>.Empty(page, pageSize);
        }
        var customerName = names.ContactName;
        var makerName = names.MakerCompanyName ?? string.Empty;

        // IDOR shield: the message-row's parent Order must be owned by
        // the customer. Subquery on Orders sweeps the predicate into
        // the SQL plan (defence-in-depth alongside the pre-query above).
        // Soft-deleted rows hidden by the global query filter on both
        // Order + OrderMessage.
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
                // Captured locals — EF parameterises them; the per-row
                // subqueries from the pre-fold shape vanish.
                m.AuthorRole == OrderMessageAuthorRole.Customer
                    ? customerName
                    : makerName,
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

        // Gate 8 M-1 fold — same single-roundtrip name pre-fetch as the
        // customer variant, scoped to the requesting maker.
        var names = await db.Set<Order>()
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Where(o => o.Id == orderId && o.MakerId == makerId)
            .Select(o => new
            {
                o.ContactName,
                MakerCompanyName = db.Set<Maker>()
                    .Where(mk => mk.Id == o.MakerId)
                    .Select(mk => mk.CompanyName)
                    .FirstOrDefault(),
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (names is null)
        {
            return PagedData<OrderMessageDto>.Empty(page, pageSize);
        }
        var customerName = names.ContactName;
        var makerName = names.MakerCompanyName ?? string.Empty;

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
                    ? customerName
                    : makerName,
                m.Body,
                m.CreatedAt,
                // Maker host: IsMine when the message is maker-authored.
                m.AuthorRole == OrderMessageAuthorRole.Maker))
            .ToListAsync(cancellationToken);

        return new PagedData<OrderMessageDto>(items, page, pageSize, totalCount);
    }
}
