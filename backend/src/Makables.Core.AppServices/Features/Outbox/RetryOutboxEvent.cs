using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Outbox;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Makables.Core.AppServices.Features.Outbox;

/// <summary>
/// Admin force-retry of a stalled outbox event (T-0109 / US-admin-0014
/// AC-1). One-shot "try now": <see cref="OutboxEvent.RequeueForRetry"/>
/// flips the row back into the due set (<c>NextRetryAt = now</c>) and bumps
/// <see cref="OutboxEvent.RetryCount"/> WITHOUT resetting the backoff ladder
/// (locked A.1) — the next failure resumes the ladder from the bumped count.
/// The <c>ProcessOutbox</c> sweep re-publishes on its next pass.
///
/// <para>
/// <see cref="IAdminAuditableCommand"/> → before/after JSONB + the admin id
/// ride the audit pipeline (ADR 0014). Retry on an already-processed row is
/// a clean 409 (<c>outbox.alreadyProcessed</c>, locked A.3) — operator error
/// surfaced loudly, NOT a Silent-Success. No <c>SaveChangesAsync</c>.
/// </para>
/// </summary>
public static class RetryOutboxEvent
{
    public sealed record Command(string OutboxEventId)
        : ICommand<RetryOutboxEventResponse>, IAdminAuditableCommand
    {
        public string ActionCode => "outbox.retry";
        public string TargetEntity => "outbox";
        public string TargetId => OutboxEventId;
        public string? Notes => null; // retry is a one-shot nudge, no reason
    }

    /// <summary>Globally-unique name (NSwag PR #38 convention).</summary>
    public sealed record RetryOutboxEventResponse(
        string OutboxEventId,
        int RetryCount,
        DateTimeOffset NextRetryAt);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.OutboxEventId)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(40).WithErrorCode(BusinessErrorMessage.MaxLength);
        }
    }

    public sealed class Handler(
        IOutboxConsumerRepository outbox,
        IClock clock,
        IUserSessionProvider session,
        ILogger<Handler> logger)
        : IRequestHandler<Command, BusinessResult<RetryOutboxEventResponse>>
    {
        public async Task<BusinessResult<RetryOutboxEventResponse>> Handle(
            Command command, CancellationToken cancellationToken)
        {
            // Fail-closed: never attribute a delivery-infra mutation to
            // "system" (RefundOrder precedent).
            if (string.IsNullOrEmpty(session.GetUserId()))
            {
                return BusinessResult.Failure<RetryOutboxEventResponse>(Error.Unauthorized());
            }

            var ev = await outbox.GetByIdAsync(command.OutboxEventId, cancellationToken);
            if (ev is null)
            {
                return BusinessResult.Failure<RetryOutboxEventResponse>(
                    Error.NotFound("outboxEventId", BusinessErrorMessage.OutboxRowNotFound));
            }

            // Locked A.3: a drained row has nothing to retry — clean 409, not
            // a Silent-Success. The domain method's throw is the backstop.
            if (ev.ProcessedAt is not null)
            {
                return BusinessResult.Failure<RetryOutboxEventResponse>(
                    Error.Conflict("outboxEventId", BusinessErrorMessage.OutboxAlreadyProcessed));
            }

            ev.RequeueForRetry(clock.UtcNow);
            logger.LogInformation(
                "RetryOutboxEvent: admin requeued outbox {OutboxEventId} (retryCount now {RetryCount}).",
                ev.Id, ev.RetryCount);

            return BusinessResult.Success(
                new RetryOutboxEventResponse(ev.Id, ev.RetryCount, ev.NextRetryAt!.Value));
        }
    }
}
