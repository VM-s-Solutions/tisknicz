using Makables.Core.AppServices.Common;
using Makables.Core.AppServices.Features.Email;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Outbox;
using Makables.Core.Domain.SeedWork;
using Microsoft.Extensions.Logging;

namespace Makables.Core.AppServices.Features.Outbox;

/// <summary>
/// Per-row outbox processor for the <c>send-email</c> queue per ADR
/// 0020 + T-0029. Called once per queue message by
/// <c>SendEmailFunction</c> with the outbox event id; loads the row
/// from the database, dispatches via <see cref="IEmailSendService"/>,
/// and updates the row's status atomically (success → MarkProcessed;
/// failure → RecordFailure with retry instant from
/// <see cref="OutboxRetryPolicy"/>).
///
/// Idempotency: if the row is already <c>ProcessedAt</c>-set, the
/// handler returns success without re-sending. Azure Storage Queues
/// guarantee at-least-once delivery, so a duplicate message can arrive
/// after a successful send — the idempotency guard makes the duplicate
/// a no-op rather than a second email.
/// </summary>
public interface ISendEmailHandler
{
    Task<HandleOutcome> HandleAsync(string outboxEventId, CancellationToken cancellationToken);
}

/// <summary>
/// Reports what the handler did. The Function caller logs / metrics off
/// this; the queue trigger NEVER throws on a recorded failure (per
/// T-0029 design: outbox owns the retry budget, not Azure-queue retry).
/// </summary>
public enum HandleOutcome
{
    /// <summary>Row sent successfully (or already-processed no-op).</summary>
    Sent = 0,

    /// <summary>Row failed transiently; recorded for retry by ProcessOutboxFunction.</summary>
    TransientFailureRecorded = 1,

    /// <summary>Row stalled (permanent / configuration / unknown error); admin intervention.</summary>
    Stalled = 2,

    /// <summary>Row not found by id — queue message references a deleted row. No-op.</summary>
    RowMissing = 3,
}

public sealed class SendEmailHandler(
    IOutboxConsumerRepository outboxConsumer,
    IEmailSendService emailSendService,
    IUnitOfWork unitOfWork,
    IClock clock,
    ILogger<SendEmailHandler> logger) : ISendEmailHandler
{
    public async Task<HandleOutcome> HandleAsync(string outboxEventId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outboxEventId);

        var evt = await outboxConsumer.GetByIdAsync(outboxEventId, cancellationToken);
        if (evt is null)
        {
            logger.LogWarning("SendEmail received unknown OutboxEvent id {OutboxEventId}; no-op.", outboxEventId);
            return HandleOutcome.RowMissing;
        }

        if (evt.ProcessedAt is not null)
        {
            // At-least-once delivery: a duplicate queue message after a
            // successful send. Idempotency wins — the row is settled, the
            // email already went out.
            logger.LogDebug("OutboxEvent {OutboxEventId} already processed; idempotent no-op.", outboxEventId);
            return HandleOutcome.Sent;
        }

        var result = await emailSendService.SendAsync(
            evt.EventType, evt.PayloadJson, cancellationToken);

        var now = clock.UtcNow;
        if (result.IsSuccess)
        {
            evt.MarkProcessed(now);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return HandleOutcome.Sent;
        }

        var (kind, code) = ClassifyFailure(result.Error!);
        var newRetryCount = evt.RetryCount + 1;
        var nextRetry = OutboxRetryPolicy.NextAttempt(kind, newRetryCount, now);

        evt.RecordFailure(kind, code, nextRetry);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (nextRetry is null)
        {
            logger.LogError(
                "OutboxEvent {OutboxEventId} stalled after attempt {Attempt}: {Code} ({Kind}).",
                outboxEventId, newRetryCount, code, kind);
            return HandleOutcome.Stalled;
        }

        logger.LogWarning(
            "OutboxEvent {OutboxEventId} transient failure attempt {Attempt}: {Code}; next retry at {NextRetryAt:o}.",
            outboxEventId, newRetryCount, code, nextRetry.Value);
        return HandleOutcome.TransientFailureRecorded;
    }

    /// <summary>
    /// Maps <see cref="IEmailSendService"/> failure shapes onto the outbox
    /// retry classification. <see cref="ErrorType.Transient"/> drives the
    /// backoff curve; everything else stalls per
    /// <see cref="OutboxRetryPolicy"/>.
    /// </summary>
    private static (OutboxErrorKind Kind, string Code) ClassifyFailure(Error error)
    {
        var kind = error.Type switch
        {
            ErrorType.Transient => OutboxErrorKind.Transient,
            ErrorType.Permanent => OutboxErrorKind.Permanent,
            ErrorType.Configuration => OutboxErrorKind.Configuration,
            _ => OutboxErrorKind.Unknown,
        };
        return (kind, error.Code);
    }
}
