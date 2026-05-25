namespace Makables.Core.Domain.Outbox;

/// <summary>
/// Consumer-side read + update access to <see cref="OutboxEvent"/> rows.
/// Used by T-0029's <c>ProcessOutboxFunction</c> (sweep) and
/// <c>SendEmailFunction</c> (per-event update). Producer-side enqueue
/// stays on <see cref="IOutbox"/>.
///
/// Implementation is in <c>Makables.Infra.Database/Outbox/OutboxConsumerRepository.cs</c>.
/// </summary>
public interface IOutboxConsumerRepository
{
    /// <summary>
    /// Load up to <paramref name="batchSize"/> rows that are ready for
    /// processing: <c>processed_at IS NULL AND (next_retry_at IS NULL OR next_retry_at &lt;= now)</c>.
    /// Ordered by <c>created_at ASC</c> so oldest events drain first.
    /// Returned rows are tracked — caller must orchestrate a single
    /// <see cref="SeedWork.IUnitOfWork.SaveChangesAsync"/> per row (or
    /// per batch) to persist state changes.
    /// </summary>
    Task<IReadOnlyList<OutboxEvent>> LoadDueAsync(
        int batchSize,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>
    /// Load a single event by id. Used by queue consumers (e.g.
    /// <c>SendEmailFunction</c>) that receive only the id off the
    /// queue message and re-read the payload + status from the
    /// authoritative outbox row. Returns <c>null</c> if no row exists.
    /// </summary>
    Task<OutboxEvent?> GetByIdAsync(string id, CancellationToken cancellationToken);
}
