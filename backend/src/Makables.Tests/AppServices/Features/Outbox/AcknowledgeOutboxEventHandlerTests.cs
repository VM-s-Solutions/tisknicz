using FluentAssertions;
using Makables.Core.AppServices.Features.Outbox;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Outbox;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Outbox;

/// <summary>
/// Unit tests for the T-0109 <see cref="AcknowledgeOutboxEvent"/> handler.
/// </summary>
public sealed class AcknowledgeOutboxEventHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 14, 10, 0, 0, TimeSpan.Zero);

    private readonly IOutboxConsumerRepository _outbox = Substitute.For<IOutboxConsumerRepository>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly IUserSessionProvider _session = Substitute.For<IUserSessionProvider>();
    private readonly AcknowledgeOutboxEvent.Handler _sut;

    public AcknowledgeOutboxEventHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _session.GetUserId().Returns("admin-1");
        _sut = new AcknowledgeOutboxEvent.Handler(
            _outbox, _clock, _session, NullLogger<AcknowledgeOutboxEvent.Handler>.Instance);
    }

    private static OutboxEvent StalledEvent()
    {
        var e = OutboxEvent.Enqueue("ob-1", "order:1", "order.paid", "{}", Now.AddHours(-2));
        e.RecordFailure(OutboxErrorKind.Permanent, "x.permanent", null);
        return e;
    }

    [Fact]
    public async Task Stalled_event_is_acknowledged_with_admin_identity()
    {
        var ev = StalledEvent();
        _outbox.GetByIdAsync("ob-1", Arg.Any<CancellationToken>()).Returns(ev);

        var result = await _sut.Handle(new AcknowledgeOutboxEvent.Command("ob-1", "Neplatná adresa."), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AcknowledgedBy.Should().Be("admin-1");
        result.Value.AcknowledgedAt.Should().Be(Now);
        ev.ProcessedAt.Should().Be(Now);
    }

    [Fact]
    public async Task Already_acknowledged_event_is_silent_success_preserving_original_acknowledger()
    {
        var ev = StalledEvent();
        ev.Acknowledge("admin-2", Now.AddHours(-1)); // an earlier admin already ack'd
        _outbox.GetByIdAsync("ob-1", Arg.Any<CancellationToken>()).Returns(ev);

        var result = await _sut.Handle(new AcknowledgeOutboxEvent.Command("ob-1", "Druhý pokus."), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AcknowledgedBy.Should().Be("admin-2", "the original acknowledger is preserved");
        result.Value.AcknowledgedAt.Should().Be(Now.AddHours(-1));
    }

    [Fact]
    public async Task Missing_event_returns_outbox_rowNotFound()
    {
        _outbox.GetByIdAsync("nope", Arg.Any<CancellationToken>()).Returns((OutboxEvent?)null);

        var result = await _sut.Handle(new AcknowledgeOutboxEvent.Command("nope", "x"), default);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OutboxRowNotFound);
    }

    [Fact]
    public void Reason_required_and_capped()
    {
        var validator = new AcknowledgeOutboxEvent.Validator();

        validator.Validate(new AcknowledgeOutboxEvent.Command("ob-1", "")).IsValid.Should().BeFalse();
        validator.Validate(new AcknowledgeOutboxEvent.Command("ob-1", new string('x', 2001)))
            .IsValid.Should().BeFalse();
        validator.Validate(new AcknowledgeOutboxEvent.Command("ob-1", "ok")).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Fail_closed_when_session_has_no_user()
    {
        _session.GetUserId().Returns((string?)null);

        var result = await _sut.Handle(new AcknowledgeOutboxEvent.Command("ob-1", "x"), default);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);
        await _outbox.DidNotReceive().GetByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
