using Makables.Core.Domain.Outbox;

namespace Makables.Core.AppServices.Common;

/// <summary>
/// Retry schedule for failed <see cref="OutboxEvent"/> processing per
/// T-0029 design directive (and ADR 0020 §"On failure"). Single source
/// of truth so the reviewer can audit the curve in one place.
///
/// Curve (Transient): <c>1m, 5m, 15m, 1h, 6h, 24h</c>; stall after
/// 6 attempts (~31 hours total). Stalled rows go to the admin
/// dashboard for manual retry or acknowledgement.
///
/// Permanent / Configuration / Unknown errors stall immediately — the
/// row is not retried by the policy. Admin intervention required.
/// </summary>
public static class OutboxRetryPolicy
{
    /// <summary>
    /// Backoff delays per attempt. Index 0 corresponds to the first retry
    /// (i.e. <c>RetryCount</c> just became 1 after the first failed
    /// attempt). After the list is exhausted the row stalls.
    ///
    /// Typed as <see cref="IReadOnlyList{T}"/> on the public surface so
    /// nothing in-process can mutate the schedule (T-0029 CQ reviewer m-2).
    /// </summary>
    public static readonly IReadOnlyList<TimeSpan> TransientBackoffs =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(6),
        TimeSpan.FromHours(24),
    ];

    public const int MaxTransientAttempts = 6;

    /// <summary>
    /// Compute when (or whether) to retry a failed row.
    /// </summary>
    /// <param name="kind">The classification of the latest failure.</param>
    /// <param name="newRetryCount">The <see cref="OutboxEvent.RetryCount"/> value AFTER the just-failed attempt is recorded (i.e. 1-based).</param>
    /// <param name="now">Current time used as the base for the backoff.</param>
    /// <returns>
    /// The instant the row becomes eligible for retry, or <c>null</c>
    /// if the row should stall (admin intervention required).
    /// </returns>
    public static DateTimeOffset? NextAttempt(
        OutboxErrorKind kind,
        int newRetryCount,
        DateTimeOffset now)
    {
        if (kind == OutboxErrorKind.None)
            throw new ArgumentException("OutboxErrorKind.None is not a failure.", nameof(kind));

        // Non-transient classifications stall immediately. The admin
        // dashboard surfaces these for human review.
        if (kind != OutboxErrorKind.Transient) return null;

        if (newRetryCount <= 0 || newRetryCount > MaxTransientAttempts) return null;
        return now + TransientBackoffs[newRetryCount - 1];
    }
}
