namespace Makables.Core.Domain.Outbox;

/// <summary>
/// One unit of work waiting to be processed off the request path. Per
/// ADR 0014 / patterns §A.20 — committed in the same transaction as the
/// state change that produced it, so we never lose an event to a
/// commit-publish-failure window.
///
/// Not <see cref="Common.Auditable"/>: this is bookkeeping infrastructure,
/// not a transactional aggregate.
/// </summary>
public sealed class OutboxEvent
{
    public string Id { get; private set; } = default!;
    public string AggregateId { get; private set; } = default!;
    public string EventType { get; private set; } = default!;
    public string PayloadJson { get; private set; } = default!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public int RetryCount { get; private set; }
    public DateTimeOffset? NextRetryAt { get; private set; }
    public OutboxErrorKind LastErrorKind { get; private set; } = OutboxErrorKind.None;
    public string? LastErrorCode { get; private set; }
    public DateTimeOffset? AcknowledgedAt { get; private set; }
    public string? AcknowledgedBy { get; private set; }

    private OutboxEvent() { }

    public static OutboxEvent Enqueue(
        string id,
        string aggregateId,
        string eventType,
        string payloadJson,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Id is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(aggregateId)) throw new ArgumentException("AggregateId is required.", nameof(aggregateId));
        if (string.IsNullOrWhiteSpace(eventType)) throw new ArgumentException("EventType is required.", nameof(eventType));

        return new OutboxEvent
        {
            Id = id,
            AggregateId = aggregateId,
            EventType = eventType,
            PayloadJson = payloadJson,
            CreatedAt = now,
            NextRetryAt = now,
        };
    }

    /// <summary>Mark the event as successfully processed.</summary>
    public void MarkProcessed(DateTimeOffset now)
    {
        ProcessedAt = now;
        NextRetryAt = null;
        LastErrorKind = OutboxErrorKind.None;
        LastErrorCode = null;
    }

    /// <summary>
    /// Record a failed processing attempt. <paramref name="nextRetryAt"/>
    /// null means the policy has decided not to retry (admin intervention
    /// required); ProcessedAt stays null.
    /// </summary>
    public void RecordFailure(
        OutboxErrorKind kind,
        string errorCode,
        DateTimeOffset? nextRetryAt)
    {
        if (kind == OutboxErrorKind.None)
        {
            throw new ArgumentException("Use MarkProcessed for successful processing.", nameof(kind));
        }

        RetryCount = checked(RetryCount + 1);
        LastErrorKind = kind;
        LastErrorCode = errorCode;
        NextRetryAt = nextRetryAt;
    }

    /// <summary>
    /// Park the row until <paramref name="parkedUntil"/> so a concurrent
    /// sweep doesn't re-publish before the queue consumer has had a chance
    /// to mark it processed (T-0029 ProcessOutboxFunction handoff). Does
    /// NOT increment <see cref="RetryCount"/> and does NOT set any error
    /// fields — the row is still in flight, not failed. If the consumer
    /// never confirms (queue dropped / worker crashed), the park expires
    /// and the next sweep re-publishes.
    ///
    /// Refuses to park (1) an already-processed row, and (2) a stalled
    /// row (<see cref="NextRetryAt"/> is null with a non-None
    /// <see cref="LastErrorKind"/> — admin intervention is the only
    /// legal next state). T-0029 CQ reviewer m-2 strengthening of the
    /// state-machine contract.
    /// </summary>
    public void ParkPendingConsumer(DateTimeOffset parkedUntil)
    {
        if (ProcessedAt is not null)
            throw new InvalidOperationException("Cannot park an already-processed event.");
        if (NextRetryAt is null && LastErrorKind != OutboxErrorKind.None)
            throw new InvalidOperationException(
                "Cannot park a stalled event; admin must Acknowledge or reset RetryCount first.");
        NextRetryAt = parkedUntil;
    }

    /// <summary>Admin marks a stalled event as acknowledged (won't be retried).</summary>
    public void Acknowledge(string adminUserId, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(adminUserId))
        {
            throw new ArgumentException("AdminUserId required.", nameof(adminUserId));
        }
        AcknowledgedAt = now;
        AcknowledgedBy = adminUserId;
        ProcessedAt = now;
        NextRetryAt = null;
    }
}
