using FluentAssertions;
using Makables.Core.AppServices.Common;
using Makables.Core.AppServices.Features.Email;
using Makables.Core.AppServices.Features.Outbox;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Email;
using Makables.Core.Domain.Outbox;
using Makables.Core.Domain.SeedWork;
using Makables.TestUtilities;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Outbox;

/// <summary>
/// Pins the per-row outbox consumer contract for T-0029
/// <c>SendEmailFunction</c>. The handler must:
///   - idempotent no-op on an already-processed row (queue at-least-once),
///   - call <see cref="IEmailSendService"/> on a due row,
///   - MarkProcessed + SaveChanges on success,
///   - RecordFailure with the right retry instant on failure,
///   - never throw on application-level failure (Azure-queue retry would double-bill the budget).
/// </summary>
public class SendEmailHandlerTests
{
    private readonly IOutboxConsumerRepository _outboxConsumer = Substitute.For<IOutboxConsumerRepository>();
    private readonly IEmailSendService _emailSend = Substitute.For<IEmailSendService>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly FakeClock _clock = new();
    private readonly SendEmailHandler _sut;

    public SendEmailHandlerTests()
    {
        _sut = new SendEmailHandler(_outboxConsumer, _emailSend, _uow, _clock,
            NullLogger<SendEmailHandler>.Instance);
    }

    private static OutboxEvent EnqueuedEvent(string id = "ob-1",
        string eventType = OutboxEventTypes.AuthMagicLinkSend,
        string payload = """{"UserId":"u","Email":"a@b","RawToken":"t","ExpiresAt":"2026-05-25T13:00:00Z","LanguageCode":"cs-CZ"}""") =>
        OutboxEvent.Enqueue(id, "u", eventType, payload, DateTimeOffset.Parse("2026-05-25T12:00:00Z"));

    [Fact]
    public async Task RowMissing_when_id_not_in_outbox()
    {
        _outboxConsumer.GetByIdAsync("missing", Arg.Any<CancellationToken>())
            .Returns((OutboxEvent?)null);

        var outcome = await _sut.HandleAsync("missing", CancellationToken.None);

        outcome.Should().Be(HandleOutcome.RowMissing);
        await _emailSend.DidNotReceive().SendAsync(default!, default!, default);
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Already_processed_row_is_idempotent_no_op_at_least_once_delivery()
    {
        var evt = EnqueuedEvent();
        evt.MarkProcessed(_clock.UtcNow);
        _outboxConsumer.GetByIdAsync("ob-1", Arg.Any<CancellationToken>()).Returns(evt);

        var outcome = await _sut.HandleAsync("ob-1", CancellationToken.None);

        outcome.Should().Be(HandleOutcome.Sent);
        await _emailSend.DidNotReceive().SendAsync(default!, default!, default);
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Happy_path_calls_email_send_service_then_marks_processed()
    {
        var evt = EnqueuedEvent();
        _outboxConsumer.GetByIdAsync("ob-1", Arg.Any<CancellationToken>()).Returns(evt);
        _emailSend.SendAsync(OutboxEventTypes.AuthMagicLinkSend, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(new EmailSentReceipt("sg-1", _clock.UtcNow)));

        var outcome = await _sut.HandleAsync("ob-1", CancellationToken.None);

        outcome.Should().Be(HandleOutcome.Sent);
        evt.ProcessedAt.Should().Be(_clock.UtcNow);
        evt.LastErrorKind.Should().Be(OutboxErrorKind.None);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Transient_failure_records_retry_per_policy()
    {
        var evt = EnqueuedEvent();
        _outboxConsumer.GetByIdAsync("ob-1", Arg.Any<CancellationToken>()).Returns(evt);
        _emailSend.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Failure<EmailSentReceipt>(
                Error.Transient(BusinessErrorMessage.EmailProviderTransientFailure)));

        var outcome = await _sut.HandleAsync("ob-1", CancellationToken.None);

        outcome.Should().Be(HandleOutcome.TransientFailureRecorded);
        evt.ProcessedAt.Should().BeNull();
        evt.RetryCount.Should().Be(1);
        evt.LastErrorKind.Should().Be(OutboxErrorKind.Transient);
        evt.LastErrorCode.Should().Be(BusinessErrorMessage.EmailProviderTransientFailure);
        // First transient backoff is 1 minute per OutboxRetryPolicy.
        evt.NextRetryAt.Should().Be(_clock.UtcNow + TimeSpan.FromMinutes(1));
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Permanent_failure_stalls_immediately()
    {
        var evt = EnqueuedEvent();
        _outboxConsumer.GetByIdAsync("ob-1", Arg.Any<CancellationToken>()).Returns(evt);
        _emailSend.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Failure<EmailSentReceipt>(
                Error.Permanent(BusinessErrorMessage.EmailTemplateNotFound)));

        var outcome = await _sut.HandleAsync("ob-1", CancellationToken.None);

        outcome.Should().Be(HandleOutcome.Stalled);
        evt.NextRetryAt.Should().BeNull();
        evt.LastErrorKind.Should().Be(OutboxErrorKind.Permanent);
        evt.LastErrorCode.Should().Be(BusinessErrorMessage.EmailTemplateNotFound);
        evt.RetryCount.Should().Be(1);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(ErrorType.Validation)]
    [InlineData(ErrorType.Unauthorized)]
    [InlineData(ErrorType.Forbidden)]
    [InlineData(ErrorType.NotFound)]
    [InlineData(ErrorType.Conflict)]
    public async Task Request_level_error_types_from_IEmailSendService_stall_as_Permanent_with_LogError(ErrorType requestLevel)
    {
        // T-0029 CQ reviewer M-1: these shouldn't reach the outbox boundary
        // (they're HTTP-handler shapes), but if they do it's a contract
        // violation by IEmailSendService — stall the row as Permanent
        // (not Unknown) and log an error so a future contributor sees the
        // breadcrumb.
        var evt = EnqueuedEvent();
        _outboxConsumer.GetByIdAsync("ob-1", Arg.Any<CancellationToken>()).Returns(evt);
        _emailSend.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Failure<EmailSentReceipt>(new Error("", "x.bogus", requestLevel)));

        var outcome = await _sut.HandleAsync("ob-1", CancellationToken.None);

        outcome.Should().Be(HandleOutcome.Stalled);
        evt.LastErrorKind.Should().Be(OutboxErrorKind.Permanent);
        evt.NextRetryAt.Should().BeNull();
    }

    [Fact]
    public async Task Transient_failures_keep_recording_until_max_attempts_then_stall()
    {
        // Pre-bump retry count to MaxTransientAttempts-1 by recording 5 failures
        // so the next failure pushes it past the curve.
        var evt = EnqueuedEvent();
        for (var i = 1; i < OutboxRetryPolicy.MaxTransientAttempts; i++)
        {
            evt.RecordFailure(OutboxErrorKind.Transient, "x", _clock.UtcNow);
        }
        _outboxConsumer.GetByIdAsync("ob-1", Arg.Any<CancellationToken>()).Returns(evt);
        _emailSend.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Failure<EmailSentReceipt>(
                Error.Transient(BusinessErrorMessage.EmailProviderTransientFailure)));

        var outcome = await _sut.HandleAsync("ob-1", CancellationToken.None);

        outcome.Should().Be(HandleOutcome.TransientFailureRecorded);
        evt.RetryCount.Should().Be(OutboxRetryPolicy.MaxTransientAttempts);
        // Last slot: 24 h.
        evt.NextRetryAt.Should().Be(_clock.UtcNow + TimeSpan.FromHours(24));

        // Next failure exceeds the curve → stall.
        var outcome2 = await _sut.HandleAsync("ob-1", CancellationToken.None);
        outcome2.Should().Be(HandleOutcome.Stalled);
        evt.NextRetryAt.Should().BeNull();
    }
}
