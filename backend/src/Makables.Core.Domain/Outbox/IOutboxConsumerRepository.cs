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

    /// <summary>
    /// Count <b>stalled</b> events (T-0126 / Q-0027) — the retry ladder has
    /// exhausted and admin intervention is the only legal next state. The
    /// predicate matches T-0109's stalled set exactly (the inverse of the
    /// <c>LoadDueAsync</c> due-set, narrowed to failed-and-not-rescheduled):
    /// <c>ProcessedAt == null AND NextRetryAt == null AND LastErrorKind != None</c>.
    /// An acknowledged row has <see cref="OutboxEvent.ProcessedAt"/> set
    /// (<c>Acknowledge</c> sets both <c>ProcessedAt</c> + <c>NextRetryAt = null</c>),
    /// so <c>ProcessedAt == null</c> already excludes it; the same predicate is
    /// <see cref="OutboxEvent.ParkPendingConsumer"/>'s own "refuses to park a
    /// stalled row" guard. Read-only (<c>AsNoTracking</c>) aggregate; backs the
    /// admin overview's stalled-outbox KPI tile + the US-admin-0002 AC-2 banner.
    /// Empty set → 0.
    /// </summary>
    Task<int> CountStalledAsync(CancellationToken cancellationToken);
}
