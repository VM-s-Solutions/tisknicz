using Makables.Core.Domain.Common;
using Makables.Core.Domain.Invoices;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Outbox;
using Makables.Core.Domain.Payouts;
using Makables.Core.Domain.Payouts.Queries;
using Microsoft.EntityFrameworkCore;

namespace Makables.Infra.Database.Payouts;

/// <summary>
/// EF Core <see cref="IPayoutQueries"/> impl (T-0112). Maker-scoped,
/// projection-only reads (<c>AsNoTracking</c> + <c>IgnoreAutoIncludes</c>) for
/// the maker money surface. The IDOR predicate is baked into every
/// <c>Where</c>; cross-tenant probes surface as an empty page / null. The
/// per-maker total is a <c>GROUP BY</c>/<c>SUM</c> over the maker's claimed
/// orders — never the cross-maker <see cref="PayoutBatch.TotalAmountMinor"/>.
/// The operator CSV is admin-only and never read here. The outbox-events
/// projection NEVER references <c>OutboxEvent.PayloadJson</c> (customer PII).
/// </summary>
public sealed class PayoutQueries(MakablesDbContext db) : IPayoutQueries
{
    public async Task<PagedData<MakerPayoutListItemDto>> GetMakerPayoutsPagedAsync(
        string makerId, int page, int pageSize, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(makerId))
            return PagedData<MakerPayoutListItemDto>.Empty(page, pageSize);

        // Per-maker slice: GROUP BY the maker's claimed orders → SUM/COUNT.
        var grouped = db.Set<Order>()
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Where(o => o.MakerId == makerId && o.PayoutBatchId != null)
            .GroupBy(o => o.PayoutBatchId!)
            .Select(g => new
            {
                BatchId = g.Key,
                MakerTotalPaidMinor = g.Sum(o => o.MakerPayoutAmountMinor),
                OrderCount = g.Count(),
            });

        var totalCount = await grouped.CountAsync(ct);
        if (totalCount == 0)
            return PagedData<MakerPayoutListItemDto>.Empty(page, pageSize);

        // JOIN the grouped slice to the batch header (for BatchNumber / State /
        // Currency / CompletedAt), sort + page, materialize the anonymous
        // projection. The Fee-invoice id is resolved in a second translatable
        // pass (a correlated subquery inside the page projection does not
        // translate against the grouped set).
        var pageRows = await (
            from g in grouped
            join b in db.Set<PayoutBatch>().AsNoTracking() on g.BatchId equals b.Id
            select new
            {
                b.Id,
                b.BatchNumber,
                b.State,
                g.MakerTotalPaidMinor,
                g.OrderCount,
                b.Currency,
                b.CompletedAt,
            })
            .OrderByDescending(x => x.CompletedAt)
            .ThenByDescending(x => x.BatchNumber)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        // Resolve this maker's Fee-invoice id per batch on the page (one
        // translatable IN query, materialized into a lookup).
        var pageBatchIds = pageRows.Select(r => r.Id).ToList();
        var feeInvoiceByBatch = await db.Set<Invoice>()
            .AsNoTracking()
            .Where(i => i.MakerId == makerId && i.Type == InvoiceType.Fee
                && i.PayoutBatchId != null && pageBatchIds.Contains(i.PayoutBatchId))
            .Select(i => new { BatchId = i.PayoutBatchId!, i.Id })
            .ToDictionaryAsync(x => x.BatchId, x => x.Id, ct);

        var items = pageRows
            .Select(r => new MakerPayoutListItemDto(
                r.Id,
                r.BatchNumber,
                r.State,
                r.MakerTotalPaidMinor,
                r.OrderCount,
                r.Currency,
                r.CompletedAt,
                feeInvoiceByBatch.TryGetValue(r.Id, out var invId) ? invId : null))
            .ToList();

        return new PagedData<MakerPayoutListItemDto>(items, page, pageSize, totalCount);
    }

    public async Task<MakerPayoutDetailDto?> GetMakerPayoutDetailAsync(
        string makerId, string batchId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(makerId) || string.IsNullOrWhiteSpace(batchId))
            return null;

        // IDOR + existence in one check: the batch must contain ≥1 order of
        // this maker. Unknown id and cross-maker id both yield null (no oracle).
        var anyForMaker = await db.Set<Order>()
            .AsNoTracking()
            .AnyAsync(o => o.PayoutBatchId == batchId && o.MakerId == makerId, ct);
        if (!anyForMaker)
            return null;

        var header = await db.Set<PayoutBatch>()
            .AsNoTracking()
            .Where(b => b.Id == batchId)
            .Select(b => new { b.BatchNumber, b.State, b.Currency, b.CompletedAt })
            .FirstOrDefaultAsync(ct);
        if (header is null)
            return null;

        var lines = await db.Set<Order>()
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Where(o => o.PayoutBatchId == batchId && o.MakerId == makerId)
            .OrderByDescending(o => o.OrderNumber)
            .Select(o => new MakerPayoutOrderLineDto(
                o.Id,
                o.OrderNumber,
                o.ProductPriceAmountMinor,
                o.ShippingPriceAmountMinor,
                o.PlatformFeeAmountMinor,
                o.MakerPayoutAmountMinor,
                o.Currency))
            .ToListAsync(ct);

        var feeInvoiceId = await db.Set<Invoice>()
            .AsNoTracking()
            .Where(i => i.PayoutBatchId == batchId && i.MakerId == makerId && i.Type == InvoiceType.Fee)
            .Select(i => i.Id)
            .FirstOrDefaultAsync(ct);

        return new MakerPayoutDetailDto(
            BatchId: batchId,
            BatchNumber: header.BatchNumber,
            State: header.State,
            MakerTotalPaidMinor: lines.Sum(l => l.MakerPayoutAmountMinor),
            Currency: header.Currency,
            CompletedAt: header.CompletedAt,
            FeeInvoiceId: feeInvoiceId,
            Orders: lines);
    }

    public async Task<PagedData<MakerOutboxEventDto>> GetMakerOutboxEventsForOrderAsync(
        string makerId, string orderId, int page, int pageSize, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(makerId) || string.IsNullOrWhiteSpace(orderId))
            return PagedData<MakerOutboxEventDto>.Empty(page, pageSize);

        // IDOR guard: the order must belong to this maker. A cross-maker /
        // unknown id yields an empty page (not an oracle).
        var ownsOrder = await db.Set<Order>()
            .AsNoTracking()
            .AnyAsync(o => o.Id == orderId && o.MakerId == makerId, ct);
        if (!ownsOrder)
            return PagedData<MakerOutboxEventDto>.Empty(page, pageSize);

        var baseQuery = db.Set<OutboxEvent>()
            .AsNoTracking()
            .Where(e => e.AggregateId == orderId);

        var totalCount = await baseQuery.CountAsync(ct);
        if (totalCount == 0)
            return PagedData<MakerOutboxEventDto>.Empty(page, pageSize);

        // Derived status — payload-free projection. PayloadJson is NEVER
        // referenced (it embeds customer PII). DESC (latest first).
        var items = await baseQuery
            .OrderByDescending(e => e.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new MakerOutboxEventDto(
                e.EventType,
                e.ProcessedAt != null
                    ? OutboxDeliveryStatus.Processed
                    : e.NextRetryAt == null
                        && (e.LastErrorKind == OutboxErrorKind.Permanent
                            || e.LastErrorKind == OutboxErrorKind.Configuration)
                        ? OutboxDeliveryStatus.Stalled
                        : OutboxDeliveryStatus.Scheduled,
                e.CreatedAt))
            .ToListAsync(ct);

        return new PagedData<MakerOutboxEventDto>(items, page, pageSize, totalCount);
    }
}
