using FluentAssertions;
using Makables.Core.AppServices.Features.Outbox;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Outbox;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Outbox;

/// <summary>
/// Unit tests for the T-0109 <see cref="RetryOutboxEvent"/> handler.
/// </summary>
public sealed class RetryOutboxEventHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 14, 10, 0, 0, TimeSpan.Zero);

    private readonly IOutboxConsumerRepository _outbox = Substitute.For<IOutboxConsumerRepository>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly IUserSessionProvider _session = Substitute.For<IUserSessionProvider>();
    private readonly RetryOutboxEvent.Handler _sut;

    public RetryOutboxEventHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _session.GetUserId().Returns("admin-1");
        _sut = new RetryOutboxEvent.Handler(_outbox, _clock, _session, NullLogger<RetryOutboxEvent.Handler>.Instance);
    }

    private static OutboxEvent StalledEvent()
    {
        var e = OutboxEvent.Enqueue("ob-1", "order:1", "order.paid", "{}", Now.AddHours(-2));
        e.RecordFailure(OutboxErrorKind.Permanent, "x.permanent", null);
        return e;
    }

    [Fact]
    public async Task Stalled_event_is_requeued_for_retry()
    {
        var ev = StalledEvent();
        _outbox.GetByIdAsync("ob-1", Arg.Any<CancellationToken>()).Returns(ev);

        var result = await _sut.Handle(new RetryOutboxEvent.Command("ob-1"), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.RetryCount.Should().Be(2);
        result.Value.NextRetryAt.Should().Be(Now);
        ev.NextRetryAt.Should().Be(Now, "RequeueForRetry ran");
    }

    [Fact]
    public async Task Missing_event_returns_outbox_rowNotFound()
    {
        _outbox.GetByIdAsync("nope", Arg.Any<CancellationToken>()).Returns((OutboxEvent?)null);

        var result = await _sut.Handle(new RetryOutboxEvent.Command("nope"), default);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OutboxRowNotFound);
    }

    [Fact]
    public async Task Already_processed_event_returns_outbox_alreadyProcessed_409()
    {
        var ev = OutboxEvent.Enqueue("ob-1", "a", "t", "{}", Now);
        ev.MarkProcessed(Now);
        _outbox.GetByIdAsync("ob-1", Arg.Any<CancellationToken>()).Returns(ev);

        var result = await _sut.Handle(new RetryOutboxEvent.Command("ob-1"), default);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OutboxAlreadyProcessed);
        result.Error.Type.Should().Be(ErrorType.Conflict);
        ev.RetryCount.Should().Be(0, "RequeueForRetry was never reached");
    }

    [Fact]
    public async Task Fail_closed_when_session_has_no_user()
    {
        _session.GetUserId().Returns((string?)null);

        var result = await _sut.Handle(new RetryOutboxEvent.Command("ob-1"), default);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);
        await _outbox.DidNotReceive().GetByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
