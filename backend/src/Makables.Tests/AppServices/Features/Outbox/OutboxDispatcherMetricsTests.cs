using FluentAssertions;
using Makables.Core.AppServices.Common;
using Makables.Core.AppServices.Features.Outbox;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Observability;
using Makables.Core.Domain.Outbox;
using Makables.Core.Domain.SeedWork;
using Makables.TestUtilities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Makables.Tests.AppServices.Features.Outbox;

/// <summary>
/// T-0165 (Q-0033) — the outbox meter had a registered name and no emission,
/// so every ADR 0023 §4 alert built on it read empty.
///
/// <para>
/// The behaviour worth pinning is not "a number is recorded" but <em>when</em>:
/// an empty sweep must still record, or a gauge that only writes when there is
/// work is indistinguishable from a sweep that stopped running — which is the
/// exact outage the alert exists to catch. And the gauge sample must never be
/// able to fail the sweep: telemetry is not allowed to break dispatch.
/// </para>
/// </summary>
public class OutboxDispatcherMetricsTests
{
    private readonly IOutboxConsumerRepository _outboxConsumer = Substitute.For<IOutboxConsumerRepository>();
    private readonly IOutboxQueuePublisher _queue = Substitute.For<IOutboxQueuePublisher>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly FakeClock _clock = new();
    private readonly IOutboxMetrics _metrics = Substitute.For<IOutboxMetrics>();
    private readonly OutboxDispatcher _sut;

    public OutboxDispatcherMetricsTests()
    {
        _sut = new OutboxDispatcher(
            _outboxConsumer, _queue, _uow, _clock,
            Options.Create(new OutboxDispatcherOptions { HandoffParkMinutes = 15 }),
            _metrics,
            NullLogger<OutboxDispatcher>.Instance);
    }

    private OutboxEvent EmailEvent(string id, int ageMinutes) =>
        OutboxEvent.Enqueue(id, "u", OutboxEventTypes.AuthMagicLinkSend, "{}",
            _clock.UtcNow.AddMinutes(-ageMinutes));

    private void Due(params OutboxEvent[] events) =>
        _outboxConsumer.LoadDueAsync(Arg.Any<int>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(events);

    [Fact]
    public async Task Empty_sweep_still_records_lag_and_stalled()
    {
        Due();
        _outboxConsumer.CountStalledAsync(Arg.Any<CancellationToken>()).Returns(3);

        await _sut.DispatchDueAsync(CancellationToken.None);

        _metrics.Received(1).RecordLagSeconds(0);
        _metrics.Received(1).RecordStalled(3);
    }

    [Fact]
    public async Task Lag_is_the_age_of_the_oldest_event_in_the_batch()
    {
        Due(EmailEvent("e-young", ageMinutes: 2), EmailEvent("e-old", ageMinutes: 30));

        await _sut.DispatchDueAsync(CancellationToken.None);

        _metrics.Received(1).RecordLagSeconds(30 * 60);
    }

    [Fact]
    public async Task Sweep_records_the_stalled_gauge_from_the_repository()
    {
        Due(EmailEvent("e-1", ageMinutes: 1));
        _outboxConsumer.CountStalledAsync(Arg.Any<CancellationToken>()).Returns(7);

        await _sut.DispatchDueAsync(CancellationToken.None);

        _metrics.Received(1).RecordStalled(7);
    }

    [Fact]
    public async Task Routed_events_are_counted_under_the_routed_outcome()
    {
        Due(EmailEvent("e-1", ageMinutes: 1), EmailEvent("e-2", ageMinutes: 1));

        await _sut.DispatchDueAsync(CancellationToken.None);

        _metrics.Received(1).RecordDispatched(OutboxDispatchOutcome.Routed, 2);
    }

    [Fact]
    public async Task Unknown_event_type_is_counted_under_the_stalled_outcome()
    {
        Due(OutboxEvent.Enqueue("e-bad", "u", "totally.unknown.type", "{}", _clock.UtcNow.AddMinutes(-1)));

        await _sut.DispatchDueAsync(CancellationToken.None);

        _metrics.Received(1).RecordDispatched(OutboxDispatchOutcome.Stalled, 1);
    }

    [Fact]
    public async Task Publish_failure_is_counted_under_the_publish_failed_outcome()
    {
        Due(EmailEvent("e-1", ageMinutes: 1));
        _queue.PublishSendEmailAsync("e-1", Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("queue down"));

        await _sut.DispatchDueAsync(CancellationToken.None);

        _metrics.Received(1).RecordDispatched(OutboxDispatchOutcome.PublishFailed, 1);
        _metrics.DidNotReceive().RecordDispatched(OutboxDispatchOutcome.Routed, Arg.Is<int>(c => c > 0));
    }

    [Fact]
    public async Task A_failing_stalled_gauge_query_does_not_fail_the_sweep()
    {
        // Telemetry must never turn a working dispatch into a failed tick.
        Due(EmailEvent("e-1", ageMinutes: 1));
        _outboxConsumer.CountStalledAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("db hiccup"));

        var summary = await _sut.DispatchDueAsync(CancellationToken.None);

        summary.Routed.Should().Be(1);
        await _queue.Received(1).PublishSendEmailAsync("e-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancellation_still_propagates_from_the_gauge_query()
    {
        // The swallow above must not eat a genuine cancellation — that would
        // turn host shutdown into a silently half-finished sweep.
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        Due();
        _outboxConsumer.CountStalledAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _sut.DispatchDueAsync(cts.Token));
    }
}
