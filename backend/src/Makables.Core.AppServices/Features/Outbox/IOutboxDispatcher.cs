using Makables.Core.AppServices.Common;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Outbox;
using Makables.Core.Domain.SeedWork;
using Microsoft.Extensions.Logging;

namespace Makables.Core.AppServices.Features.Outbox;

/// <summary>
/// Drains the outbox per ADR 0020 / T-0029. <c>ProcessOutboxFunction</c>
/// in <c>Makables.Functions</c> is a thin trigger wrapper; the
/// orchestration logic lives here so it's testable without the
/// Functions runtime.
///
/// On each call: load up to <see cref="BatchSize"/> due rows in
/// <c>created_at ASC</c> order; route each row by <c>event_type</c>
/// (today: only <c>auth.*.send</c> → <see cref="IOutboxQueuePublisher.PublishSendEmailAsync"/>);
/// unknown event types stall immediately as <see cref="OutboxErrorKind.Permanent"/>;
/// queue-publish failures stall the row as <see cref="OutboxErrorKind.Transient"/>
/// so the next sweep retries per <see cref="OutboxRetryPolicy"/>.
///
/// The row is NOT marked <c>ProcessedAt</c> here — that's the queue
/// consumer's job after the actual work succeeds. ProcessOutbox only
/// records that the row was "handed off" by advancing
/// <see cref="OutboxEvent.NextRetryAt"/> to the future so the next
/// sweep doesn't re-publish the same row before the consumer has had
/// a chance. The advance is bounded by <see cref="HandoffParkDuration"/>;
/// if the consumer crashes or the queue silently drops a message, the
/// row becomes eligible again after that window.
/// </summary>
public interface IOutboxDispatcher
{
    Task<DispatchSummary> DispatchDueAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Counters reported after a sweep. Used by the Function caller for
/// logging / metrics. Per ADR 0020 §"Observability".
/// </summary>
public sealed record DispatchSummary(
    int Loaded,
    int Routed,
    int Stalled,
    int FailedToPublish);

public sealed class OutboxDispatcher(
    IOutboxConsumerRepository outboxConsumer,
    IOutboxQueuePublisher queuePublisher,
    IUnitOfWork unitOfWork,
    IClock clock,
    ILogger<OutboxDispatcher> logger) : IOutboxDispatcher
{
    /// <summary>Per-sweep cap. ADR 0020 §"The outbox is the message hub": 50 at launch.</summary>
    public const int BatchSize = 50;

    /// <summary>
    /// How long after publishing-to-the-queue we hide the row from the
    /// next sweep. If the consumer crashes or the queue swallows the
    /// message, the row becomes eligible for re-publish after this
    /// window — bounded redelivery without losing the work.
    /// </summary>
    public static readonly TimeSpan HandoffParkDuration = TimeSpan.FromMinutes(15);

    public async Task<DispatchSummary> DispatchDueAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var due = await outboxConsumer.LoadDueAsync(BatchSize, now, cancellationToken);
        if (due.Count == 0) return new DispatchSummary(0, 0, 0, 0);

        int routed = 0, stalled = 0, failedToPublish = 0;

        foreach (var evt in due)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsEmailEventType(evt.EventType))
            {
                logger.LogError(
                    "OutboxEvent {OutboxEventId} has unknown event_type {EventType}; stalling.",
                    evt.Id, evt.EventType);
                evt.RecordFailure(
                    kind: OutboxErrorKind.Permanent,
                    errorCode: BusinessErrorMessage.EmailEventTypeUnknown,
                    nextRetryAt: null);
                stalled++;
                continue;
            }

            try
            {
                await queuePublisher.PublishSendEmailAsync(evt.Id, cancellationToken);
                // Park past the next sweep tick so the consumer owns the
                // success/failure update. If the consumer never confirms
                // (queue dropped, worker crashed), the park window expires
                // and the next sweep re-publishes. RetryCount unchanged.
                evt.ParkPendingConsumer(now + HandoffParkDuration);
                routed++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex,
                    "Failed to publish OutboxEvent {OutboxEventId} to send-email queue.", evt.Id);
                var newRetryCount = evt.RetryCount + 1;
                var nextRetry = OutboxRetryPolicy.NextAttempt(
                    OutboxErrorKind.Transient, newRetryCount, now);
                evt.RecordFailure(
                    kind: OutboxErrorKind.Transient,
                    errorCode: BusinessErrorMessage.OutboxQueuePublishFailed,
                    nextRetryAt: nextRetry);
                failedToPublish++;
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Outbox sweep complete: loaded={Loaded} routed={Routed} stalled={Stalled} failedToPublish={FailedToPublish}",
            due.Count, routed, stalled, failedToPublish);
        return new DispatchSummary(due.Count, routed, stalled, failedToPublish);
    }

    private static bool IsEmailEventType(string eventType) =>
        eventType is OutboxEventTypes.AuthMagicLinkSend
                  or OutboxEventTypes.AuthEmailConfirmationSend
                  or OutboxEventTypes.AuthPasswordResetSend;
}
