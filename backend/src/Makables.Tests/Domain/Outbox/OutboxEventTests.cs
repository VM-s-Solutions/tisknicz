using FluentAssertions;
using Makables.Core.Domain.Outbox;

namespace Makables.Tests.Domain.OutboxTests;

public class OutboxEventTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-05-23T10:00:00Z");

    [Fact]
    public void Enqueue_Sets_Initial_State()
    {
        var e = OutboxEvent.Enqueue("01H-1", "order:01H", "order.paid", """{"x":1}""", Now);

        e.Id.Should().Be("01H-1");
        e.AggregateId.Should().Be("order:01H");
        e.EventType.Should().Be("order.paid");
        e.PayloadJson.Should().Be("""{"x":1}""");
        e.CreatedAt.Should().Be(Now);
        e.NextRetryAt.Should().Be(Now);
        e.ProcessedAt.Should().BeNull();
        e.RetryCount.Should().Be(0);
        e.LastErrorKind.Should().Be(OutboxErrorKind.None);
    }

    [Fact]
    public void Enqueue_Rejects_Missing_Required_Fields()
    {
        ((Action)(() => OutboxEvent.Enqueue("", "a", "t", "{}", Now)))
            .Should().Throw<ArgumentException>();
        ((Action)(() => OutboxEvent.Enqueue("1", "", "t", "{}", Now)))
            .Should().Throw<ArgumentException>();
        ((Action)(() => OutboxEvent.Enqueue("1", "a", "", "{}", Now)))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void MarkProcessed_Clears_Retry_State()
    {
        var e = OutboxEvent.Enqueue("01H-1", "a", "t", "{}", Now);
        e.RecordFailure(OutboxErrorKind.Transient, "x.transient", Now.AddMinutes(1));

        e.MarkProcessed(Now.AddSeconds(30));

        e.ProcessedAt.Should().Be(Now.AddSeconds(30));
        e.NextRetryAt.Should().BeNull();
        e.LastErrorKind.Should().Be(OutboxErrorKind.None);
        e.LastErrorCode.Should().BeNull();
    }

    [Fact]
    public void RecordFailure_Increments_Retry_Count_And_Captures_Error()
    {
        var e = OutboxEvent.Enqueue("01H-1", "a", "t", "{}", Now);

        e.RecordFailure(OutboxErrorKind.Transient, "payment.gatewayUnavailable", Now.AddMinutes(1));

        e.RetryCount.Should().Be(1);
        e.LastErrorKind.Should().Be(OutboxErrorKind.Transient);
        e.LastErrorCode.Should().Be("payment.gatewayUnavailable");
        e.NextRetryAt.Should().Be(Now.AddMinutes(1));
        e.ProcessedAt.Should().BeNull();
    }

    [Fact]
    public void RecordFailure_With_None_Kind_Throws()
    {
        var e = OutboxEvent.Enqueue("01H-1", "a", "t", "{}", Now);
        var act = () => e.RecordFailure(OutboxErrorKind.None, "x", Now);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RecordFailure_With_Null_NextRetry_Stalls_The_Event()
    {
        var e = OutboxEvent.Enqueue("01H-1", "a", "t", "{}", Now);

        e.RecordFailure(OutboxErrorKind.Permanent, "x.permanent", nextRetryAt: null);

        e.NextRetryAt.Should().BeNull();
        e.ProcessedAt.Should().BeNull();
    }

    [Fact]
    public void Acknowledge_Marks_Processed_With_Admin_Identity()
    {
        var e = OutboxEvent.Enqueue("01H-1", "a", "t", "{}", Now);
        e.RecordFailure(OutboxErrorKind.Permanent, "x.permanent", null);

        e.Acknowledge("admin@example.com", Now.AddHours(1));

        e.ProcessedAt.Should().Be(Now.AddHours(1));
        e.AcknowledgedAt.Should().Be(Now.AddHours(1));
        e.AcknowledgedBy.Should().Be("admin@example.com");
        e.NextRetryAt.Should().BeNull();
    }

    [Fact]
    public void Acknowledge_Rejects_Empty_Admin_Id()
    {
        var e = OutboxEvent.Enqueue("01H-1", "a", "t", "{}", Now);
        var act = () => e.Acknowledge("  ", Now);
        act.Should().Throw<ArgumentException>();
    }

    // T-0029: ParkPendingConsumer advances NextRetryAt without touching
    // RetryCount, error fields, or ProcessedAt. Used by OutboxDispatcher
    // after publishing the row id to the send-email queue so a concurrent
    // sweep doesn't re-publish before the consumer confirms.

    [Fact]
    public void ParkPendingConsumer_advances_next_retry_without_touching_retry_count()
    {
        var e = OutboxEvent.Enqueue("01H-1", "a", "t", "{}", Now);
        var initialRetryCount = e.RetryCount;
        var parkedUntil = Now.AddMinutes(15);

        e.ParkPendingConsumer(parkedUntil);

        e.NextRetryAt.Should().Be(parkedUntil);
        e.RetryCount.Should().Be(initialRetryCount, "park is not a failure");
        e.ProcessedAt.Should().BeNull();
        e.LastErrorKind.Should().Be(OutboxErrorKind.None);
        e.LastErrorCode.Should().BeNull();
    }

    [Fact]
    public void ParkPendingConsumer_refuses_to_park_already_processed_row()
    {
        var e = OutboxEvent.Enqueue("01H-1", "a", "t", "{}", Now);
        e.MarkProcessed(Now);

        var act = () => e.ParkPendingConsumer(Now.AddMinutes(15));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ParkPendingConsumer_refuses_to_park_a_stalled_row()
    {
        // T-0029 CQ reviewer m-2 strengthening: a stalled row
        // (NextRetryAt = null after a non-transient failure) is awaiting
        // admin Acknowledge / RetryCount reset; parking it would silently
        // resurrect a row that the operator hasn't reviewed.
        var e = OutboxEvent.Enqueue("01H-1", "a", "t", "{}", Now);
        e.RecordFailure(OutboxErrorKind.Permanent, "x.permanent", nextRetryAt: null);

        var act = () => e.ParkPendingConsumer(Now.AddMinutes(15));

        act.Should().Throw<InvalidOperationException>();
    }
}
