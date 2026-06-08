using Makables.Core.AppServices.Common;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Outbox;
using Makables.Core.Domain.SeedWork;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Makables.Core.AppServices.Features.Outbox;

/// <summary>
/// Drains the outbox per ADR 0020 / T-0029. <c>ProcessOutboxFunction</c>
/// in <c>Makables.Functions</c> is a thin trigger wrapper; the
/// orchestration logic lives here so it's testable without the
/// Functions runtime.
///
/// On each call: load up to <see cref="BatchSize"/> due rows in
/// <c>created_at ASC</c> order; classify each row by <c>event_type</c>
/// (today: only <c>auth.*.send</c> via <see cref="OutboxEventTypes.IsEmailSend"/>);
/// unknown event types stall immediately as <see cref="OutboxErrorKind.Permanent"/>.
///
/// <b>Commit then publish</b> (T-0029 sec reviewer item 8): the rows
/// that classified as email-sends are first parked via
/// <see cref="OutboxEvent.ParkPendingConsumer"/> AND committed via
/// <see cref="IUnitOfWork.SaveChangesAsync"/>, then the queue messages
/// are published. This eliminates the race where a fast consumer
/// dequeues + writes <c>ProcessedAt</c> before the dispatcher's own
/// transaction commits (which would otherwise overwrite the consumer's
/// committed state with a stale tracker). Queue-publish failures after
/// the commit are recorded in a second SaveChanges with retry-per-policy.
///
/// The row is NOT marked <c>ProcessedAt</c> here — that's the queue
/// consumer's job after the actual work succeeds. ProcessOutbox only
/// records that the row was "handed off" by advancing
/// <see cref="OutboxEvent.NextRetryAt"/> to the future so the next
/// sweep doesn't re-publish the same row before the consumer has had
/// a chance. The advance is bounded by <c>OutboxQueueOptions.HandoffParkMinutes</c>;
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
    IOptions<OutboxDispatcherOptions> dispatcherOptions,
    ILogger<OutboxDispatcher> logger) : IOutboxDispatcher
{
    /// <summary>Per-sweep cap. ADR 0020 §"The outbox is the message hub": 50 at launch.</summary>
    public const int BatchSize = 50;

    public async Task<DispatchSummary> DispatchDueAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var due = await outboxConsumer.LoadDueAsync(BatchSize, now, cancellationToken);
        if (due.Count == 0) return new DispatchSummary(0, 0, 0, 0);

        var parkDuration = TimeSpan.FromMinutes(Math.Max(1, dispatcherOptions.Value.HandoffParkMinutes));
        // T-0069: classify each event once at Phase 1 + remember the verdict
        // so Phase 3 can publish to the right per-event-type queue without
        // re-classifying (single source of truth for the routing table).
        var toPublish = new List<(OutboxEvent Event, RouteTarget Target)>(due.Count);
        var stalled = 0;

        // Phase 1: classify + park-or-stall in the entity tracker.
        foreach (var evt in due)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var target = ClassifyRoute(evt.EventType);
            if (target == RouteTarget.Unknown)
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

            evt.ParkPendingConsumer(now + parkDuration);
            toPublish.Add((evt, target));
        }

        // Phase 2: commit the park + stall mutations BEFORE publishing.
        // A fast consumer that dequeues and tries to MarkProcessed must
        // see the row already-parked, not the original NextRetryAt.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Phase 3: publish each parked row to its destination queue per
        // the Phase 1 verdict. Failures here happen AFTER the park is
        // committed; we record them as Transient with policy-driven
        // nextRetryAt in a second SaveChanges below.
        var routed = 0;
        var failedToPublish = 0;
        foreach (var (evt, target) in toPublish)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await PublishToTargetAsync(target, evt.Id, cancellationToken);
                routed++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Failed to publish OutboxEvent {OutboxEventId} ({EventType}) to {Target} queue.",
                    evt.Id, evt.EventType, target);
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

        // Phase 4: persist any publish-failure state. Skip the roundtrip
        // when nothing changed.
        if (failedToPublish > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        logger.LogInformation(
            "Outbox sweep complete: loaded={Loaded} routed={Routed} stalled={Stalled} failedToPublish={FailedToPublish}",
            due.Count, routed, stalled, failedToPublish);
        return new DispatchSummary(due.Count, routed, stalled, failedToPublish);
    }

    private Task PublishToTargetAsync(RouteTarget target, string outboxEventId, CancellationToken cancellationToken) =>
        target switch
        {
            RouteTarget.SendEmail => queuePublisher.PublishSendEmailAsync(outboxEventId, cancellationToken),
            RouteTarget.GenerateInvoice => queuePublisher.PublishGenerateInvoiceAsync(outboxEventId, cancellationToken),
            RouteTarget.GenerateLabel => queuePublisher.PublishGenerateLabelAsync(outboxEventId, cancellationToken),
            _ => throw new InvalidOperationException(
                $"Internal error: ClassifyRoute returned {target} which should have stalled before reaching Phase 3."),
        };

    /// <summary>
    /// Classify <paramref name="eventType"/> into its destination queue per
    /// T-0069 locked decision 2 + T-0072 (label) split. Disjoint by
    /// construction — anything matching no classifier is
    /// <see cref="RouteTarget.Unknown"/> and stalls.
    /// </summary>
    private static RouteTarget ClassifyRoute(string eventType)
    {
        if (OutboxEventTypes.IsEmailSend(eventType)) return RouteTarget.SendEmail;
        if (OutboxEventTypes.IsInvoiceGenerate(eventType)) return RouteTarget.GenerateInvoice;
        if (OutboxEventTypes.IsGenerateLabel(eventType)) return RouteTarget.GenerateLabel;
        return RouteTarget.Unknown;
    }

    private enum RouteTarget
    {
        Unknown = 0,
        SendEmail,
        GenerateInvoice,
        GenerateLabel,
    }
}
