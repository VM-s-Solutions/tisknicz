namespace Makables.Core.Domain.Payouts.Queries;

/// <summary>
/// Maker-safe projection of the internal <c>OutboxEvent</c> delivery state
/// (T-0112 / US-maker-0017). Derived from <c>ProcessedAt</c> / <c>NextRetryAt</c>
/// / <c>LastErrorKind</c> down to a 3-value enum — the maker sees delivery
/// HEALTH, never the operator-internal retry mechanics.
/// </summary>
public enum OutboxDeliveryStatus
{
    /// <summary>Successfully processed (<c>ProcessedAt != null</c>).</summary>
    Processed,

    /// <summary>A retry is scheduled (<c>NextRetryAt != null</c>, not yet processed).</summary>
    Scheduled,

    /// <summary>
    /// Stalled — no retry scheduled, not processed, last error was
    /// Permanent / Configuration. Admin intervention required (the maker
    /// cannot retry — US-maker-0017 AC-2).
    /// </summary>
    Stalled,
}

/// <summary>
/// One read-only, payload-free outbox audit row for a maker's order
/// (US-maker-0017): event type + derived delivery status + timestamp ONLY.
/// NEVER carries <c>PayloadJson</c> (customer email / address / provider refs),
/// <c>LastErrorCode</c> (operator diagnostic), or <c>RetryCount</c>.
/// </summary>
public sealed record MakerOutboxEventDto(
    string EventType,
    OutboxDeliveryStatus Status,
    DateTimeOffset OccurredAt);
