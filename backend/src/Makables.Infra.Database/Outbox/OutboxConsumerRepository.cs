using Makables.Core.Domain.Common;
using Makables.Core.Domain.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Makables.Infra.Database.Outbox;

/// <summary>
/// Consumer-side <see cref="IOutboxConsumerRepository"/> impl. Per ADR
/// 0020: the sweep query is the hot path; the composite index
/// <c>ix_outbox_event_due</c> over <c>(next_retry_at, processed_at)
/// WHERE processed_at IS NULL</c> covers the load.
///
/// All reads here are intentionally TRACKED (no <c>.AsNoTracking()</c>):
/// callers mutate state on the entity and SaveChanges. T-0029 CQ
/// reviewer m-4 — flagged so a future contributor doesn't reflexively
/// add a tracking-disabling call that would break the orchestrators.
/// </summary>
public sealed class OutboxConsumerRepository(MakablesDbContext db) : IOutboxConsumerRepository
{
    public async Task<IReadOnlyList<OutboxEvent>> LoadDueAsync(
        int batchSize,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (batchSize <= 0) return [];
        // NextRetryAt == NULL means "stalled — admin intervention required"
        // (RecordFailure with nextRetryAt: null), so it must NOT be loaded
        // by the sweep. Newly-enqueued rows have NextRetryAt = CreatedAt,
        // so they're picked up on the first sweep after enqueue.
        return await db.Set<OutboxEvent>()
            .Where(e => e.ProcessedAt == null
                     && e.NextRetryAt != null
                     && e.NextRetryAt <= now)
            .OrderBy(e => e.CreatedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
    }

    public Task<OutboxEvent?> GetByIdAsync(string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id)) return Task.FromResult<OutboxEvent?>(null);
        return db.Set<OutboxEvent>().FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
    }

    public Task<int> CountStalledAsync(CancellationToken cancellationToken)
    {
        // Stalled set (T-0126 / Q-0027 / T-0127) — exactly T-0109's
        // definition. The predicate is the SHARED
        // IOutboxConsumerRepository.StalledPredicate so the count and the
        // T-0127 list (GetStalledPagedAsync) can never drift. AsNoTracking —
        // this is a pure count, never an entity.
        return db.Set<OutboxEvent>()
            .AsNoTracking()
            .CountAsync(IOutboxConsumerRepository.StalledPredicate, cancellationToken);
    }

    public async Task<PagedData<StalledOutboxEventDto>> GetStalledPagedAsync(
        int page, int pageSize, CancellationToken cancellationToken)
    {
        // Same SHARED StalledPredicate as CountStalledAsync (T-0127 §A.4 —
        // byte-identical, so the triage list and the KPI tile agree on the
        // stalled set). AsNoTracking — projection-only, never an entity. Two
        // round-trips (count + page) per the T-0080 precedent.
        var baseQuery = db.Set<OutboxEvent>()
            .AsNoTracking()
            .Where(IOutboxConsumerRepository.StalledPredicate);

        var totalCount = await baseQuery.CountAsync(cancellationToken);
        if (totalCount == 0)
            return PagedData<StalledOutboxEventDto>.Empty(page, pageSize);

        var items = await baseQuery
            .OrderByDescending(e => e.CreatedAt)
            .ThenByDescending(e => e.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new StalledOutboxEventDto(
                e.Id,
                e.EventType,
                e.AggregateId,
                e.LastErrorCode,
                e.RetryCount,
                e.CreatedAt))
            .ToListAsync(cancellationToken);

        return new PagedData<StalledOutboxEventDto>(items, page, pageSize, totalCount);
    }
}
