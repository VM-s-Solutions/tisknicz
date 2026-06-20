using System.Linq.Expressions;
using FluentAssertions;
using Makables.Core.Domain.Outbox;

namespace Makables.Tests.Domain.Outbox;

/// <summary>
/// The load-bearing T-0127 §A.4 assertion: the SHARED
/// <see cref="IOutboxConsumerRepository.StalledPredicate"/> — used by BOTH
/// <c>CountStalledAsync</c> (the KPI tile) and <c>GetStalledPagedAsync</c>
/// (the triage list) — admits exactly the stalled set
/// (<c>ProcessedAt == null AND NextRetryAt == null AND
/// LastErrorKind != None</c>) and excludes processed / due / acknowledged /
/// freshly-enqueued rows. Pinned against the entity's public transitions so
/// the count and the list can never drift on which rows are stalled.
/// </summary>
public sealed class StalledOutboxPredicateTests
{
    private static readonly Func<OutboxEvent, bool> IsStalled =
        ((Expression<Func<OutboxEvent, bool>>)IOutboxConsumerRepository.StalledPredicate).Compile();

    private static readonly DateTimeOffset Now =
        new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

    private static OutboxEvent Enqueued() =>
        OutboxEvent.Enqueue("evt", "agg", "OrderPaidEmail", "{}", Now);

    [Fact]
    public void Freshly_enqueued_row_is_not_stalled()
    {
        // NextRetryAt = CreatedAt (due), LastErrorKind = None.
        IsStalled(Enqueued()).Should().BeFalse();
    }

    [Fact]
    public void Failed_and_not_rescheduled_row_is_stalled()
    {
        var e = Enqueued();
        // RecordFailure with nextRetryAt: null = the retry ladder exhausted.
        e.RecordFailure(OutboxErrorKind.Permanent, "email.permanent", nextRetryAt: null);

        IsStalled(e).Should().BeTrue();
    }

    [Fact]
    public void Failed_but_rescheduled_due_row_is_not_stalled()
    {
        var e = Enqueued();
        // A transient failure with a future retry — still in the ladder, not
        // stalled (NextRetryAt is non-null).
        e.RecordFailure(OutboxErrorKind.Transient, "email.transient", nextRetryAt: Now.AddMinutes(5));

        IsStalled(e).Should().BeFalse();
    }

    [Fact]
    public void Processed_row_is_not_stalled()
    {
        var e = Enqueued();
        e.MarkProcessed(Now);

        IsStalled(e).Should().BeFalse();
    }

    [Fact]
    public void Acknowledged_row_is_not_stalled()
    {
        var e = Enqueued();
        e.RecordFailure(OutboxErrorKind.Permanent, "email.permanent", nextRetryAt: null);
        // Acknowledge sets ProcessedAt + NextRetryAt = null — the
        // ProcessedAt != null already excludes it from the stalled set.
        e.Acknowledge("admin-1", Now);

        IsStalled(e).Should().BeFalse();
    }
}
