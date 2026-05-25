using FluentAssertions;
using Makables.Core.AppServices.Common;
using Makables.Core.AppServices.Features.Outbox;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Outbox;
using Makables.Core.Domain.SeedWork;
using Makables.TestUtilities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Makables.Tests.AppServices.Features.Outbox;

/// <summary>
/// Pins the sweep contract for T-0029 <c>ProcessOutboxFunction</c>.
/// The dispatcher must:
///   - load up to BatchSize rows,
///   - publish each known event_type to the send-email queue,
///   - stall unknown event_types (Permanent),
///   - record transient failure if queue publish throws,
///   - park successfully-published rows past the next sweep tick
///     without bumping RetryCount,
///   - SaveChanges once per sweep.
/// </summary>
public class OutboxDispatcherTests
{
    private readonly IOutboxConsumerRepository _outboxConsumer = Substitute.For<IOutboxConsumerRepository>();
    private readonly IOutboxQueuePublisher _queue = Substitute.For<IOutboxQueuePublisher>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly FakeClock _clock = new();
    private readonly OutboxDispatcherOptions _opts = new() { HandoffParkMinutes = 15 };
    private readonly OutboxDispatcher _sut;

    public OutboxDispatcherTests()
    {
        _sut = new OutboxDispatcher(_outboxConsumer, _queue, _uow, _clock,
            Options.Create(_opts),
            NullLogger<OutboxDispatcher>.Instance);
    }

    private OutboxEvent EmailEvent(string id, string eventType = OutboxEventTypes.AuthMagicLinkSend) =>
        OutboxEvent.Enqueue(id, "u", eventType, "{}", _clock.UtcNow.AddMinutes(-5));

    [Fact]
    public async Task Empty_batch_returns_zero_summary_and_does_not_save()
    {
        _outboxConsumer.LoadDueAsync(Arg.Any<int>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<OutboxEvent>());

        var summary = await _sut.DispatchDueAsync(CancellationToken.None);

        summary.Should().Be(new DispatchSummary(0, 0, 0, 0));
        await _queue.DidNotReceive().PublishSendEmailAsync(default!, default);
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Routes_email_event_to_queue_and_parks_row()
    {
        var evt = EmailEvent("ob-1");
        _outboxConsumer.LoadDueAsync(Arg.Any<int>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new[] { evt });

        var summary = await _sut.DispatchDueAsync(CancellationToken.None);

        summary.Should().Be(new DispatchSummary(Loaded: 1, Routed: 1, Stalled: 0, FailedToPublish: 0));
        await _queue.Received(1).PublishSendEmailAsync("ob-1", Arg.Any<CancellationToken>());
        evt.NextRetryAt.Should().Be(_clock.UtcNow + TimeSpan.FromMinutes(_opts.HandoffParkMinutes));
        evt.RetryCount.Should().Be(0, "park is not a failure");
        evt.ProcessedAt.Should().BeNull();
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unknown_event_type_stalls_as_Permanent_without_publishing()
    {
        var evt = OutboxEvent.Enqueue("ob-1", "u", "order.shipped.send", "{}", _clock.UtcNow.AddMinutes(-5));
        _outboxConsumer.LoadDueAsync(Arg.Any<int>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new[] { evt });

        var summary = await _sut.DispatchDueAsync(CancellationToken.None);

        summary.Should().Be(new DispatchSummary(Loaded: 1, Routed: 0, Stalled: 1, FailedToPublish: 0));
        await _queue.DidNotReceive().PublishSendEmailAsync(default!, default);
        evt.LastErrorKind.Should().Be(OutboxErrorKind.Permanent);
        evt.LastErrorCode.Should().Be(BusinessErrorMessage.EmailEventTypeUnknown);
        evt.NextRetryAt.Should().BeNull();
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Queue_publish_failure_records_transient_with_next_retry_per_policy()
    {
        var evt = EmailEvent("ob-1");
        _outboxConsumer.LoadDueAsync(Arg.Any<int>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new[] { evt });
        _queue.PublishSendEmailAsync("ob-1", Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("queue down"));

        var summary = await _sut.DispatchDueAsync(CancellationToken.None);

        summary.Should().Be(new DispatchSummary(Loaded: 1, Routed: 0, Stalled: 0, FailedToPublish: 1));
        evt.RetryCount.Should().Be(1);
        evt.LastErrorKind.Should().Be(OutboxErrorKind.Transient);
        evt.LastErrorCode.Should().Be(BusinessErrorMessage.OutboxQueuePublishFailed);
        // First transient backoff = 1 minute.
        evt.NextRetryAt.Should().Be(_clock.UtcNow + TimeSpan.FromMinutes(1));
        // Two saves: one to commit the park (always), one to commit the
        // recorded failure (only on publish failure).
        await _uow.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Park_is_committed_BEFORE_publish_to_eliminate_consumer_race()
    {
        // T-0029 sec reviewer item 8: a fast consumer must see the parked
        // row, not the original NextRetryAt. The dispatcher MUST call
        // SaveChanges before PublishSendEmailAsync.
        var evt = EmailEvent("ob-race");
        _outboxConsumer.LoadDueAsync(Arg.Any<int>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new[] { evt });
        var orderLog = new List<string>();
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(callInfo => { orderLog.Add("save"); return Task.FromResult(0); });
        _queue.PublishSendEmailAsync("ob-race", Arg.Any<CancellationToken>())
            .Returns(callInfo => { orderLog.Add("publish"); return Task.CompletedTask; });

        await _sut.DispatchDueAsync(CancellationToken.None);

        orderLog.Should().Equal("save", "publish");
    }

    [Fact]
    public async Task Mixed_batch_processes_all_rows_before_saving_once()
    {
        var ok = EmailEvent("ob-ok");
        var bad = OutboxEvent.Enqueue("ob-bad", "u", "weird.event", "{}", _clock.UtcNow.AddMinutes(-5));
        _outboxConsumer.LoadDueAsync(Arg.Any<int>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new[] { ok, bad });

        var summary = await _sut.DispatchDueAsync(CancellationToken.None);

        summary.Loaded.Should().Be(2);
        summary.Routed.Should().Be(1);
        summary.Stalled.Should().Be(1);
        await _queue.Received(1).PublishSendEmailAsync("ob-ok", Arg.Any<CancellationToken>());
        await _queue.DidNotReceive().PublishSendEmailAsync("ob-bad", Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancellation_during_sweep_propagates()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        _outboxConsumer.LoadDueAsync(Arg.Any<int>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new[] { EmailEvent("ob-1") });

        var act = async () => await _sut.DispatchDueAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
