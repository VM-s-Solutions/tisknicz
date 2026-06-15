using System.Linq.Expressions;
using Makables.Core.Domain.Common;

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
    /// The SINGLE source of truth for the <b>stalled</b> predicate (T-0126 /
    /// T-0127 / Q-0027): <c>ProcessedAt == null AND NextRetryAt == null AND
    /// LastErrorKind != None</c> — the retry ladder exhausted and admin
    /// intervention is the only legal next state. Both
    /// <see cref="CountStalledAsync"/> (the KPI tile) and
    /// <see cref="GetStalledPagedAsync"/> (the triage list) reference THIS
    /// expression, so the count and the list can never drift on which rows
    /// are stalled (T-0127 §A.4 — byte-identical). Matches T-0109's stalled
    /// set + <see cref="OutboxEvent.ParkPendingConsumer"/>'s stall guard.
    /// </summary>
    static readonly Expression<Func<OutboxEvent, bool>> StalledPredicate =
        e => e.ProcessedAt == null
          && e.NextRetryAt == null
          && e.LastErrorKind != OutboxErrorKind.None;

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

    /// <summary>
    /// Paged <b>stalled</b>-event list (T-0127 / Q-0029 / US-admin-0014) for
    /// the admin triage page — browse + retry / acknowledge by VISIBLE id,
    /// replacing the T-0118c count + blind by-id surface. Uses the EXACT
    /// <see cref="StalledPredicate"/> <see cref="CountStalledAsync"/> uses, so
    /// the list and the KPI tile agree byte-for-byte on the stalled set.
    /// Read-only (<c>AsNoTracking</c>); projects to
    /// <see cref="StalledOutboxEventDto"/> (no <c>PayloadJson</c>). Sorted
    /// <c>CreatedAt DESC</c> (newest stalls first, tie-broken on
    /// <see cref="OutboxEvent.Id"/>). Two round-trips (count + page) per the
    /// T-0080 precedent. Empty set → <c>PagedData</c> with
    /// <c>TotalCount = 0</c>, never 404. <b>Admin host only</b> (ADR 0013).
    /// </summary>
    Task<PagedData<StalledOutboxEventDto>> GetStalledPagedAsync(
        int page, int pageSize, CancellationToken cancellationToken);
}
