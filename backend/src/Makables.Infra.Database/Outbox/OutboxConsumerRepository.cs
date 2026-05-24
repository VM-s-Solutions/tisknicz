using Makables.Core.Domain.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Makables.Infra.Database.Outbox;

/// <summary>
/// Consumer-side <see cref="IOutboxConsumerRepository"/> impl. Per ADR
/// 0020: the sweep query is the hot path; the composite index
/// <c>ix_outbox_event_due</c> over <c>(next_retry_at, processed_at)
/// WHERE processed_at IS NULL</c> covers the load. Rows are returned
/// tracked so the caller can mutate state and SaveChanges.
/// </summary>
public sealed class OutboxConsumerRepository(MakablesDbContext db) : IOutboxConsumerRepository
{
    public async Task<IReadOnlyList<OutboxEvent>> LoadDueAsync(
        int batchSize,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (batchSize <= 0) return [];
        return await db.Set<OutboxEvent>()
            .Where(e => e.ProcessedAt == null
                     && (e.NextRetryAt == null || e.NextRetryAt <= now))
            .OrderBy(e => e.CreatedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
    }

    public Task<OutboxEvent?> GetByIdAsync(string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id)) return Task.FromResult<OutboxEvent?>(null);
        return db.Set<OutboxEvent>().FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
    }
}
