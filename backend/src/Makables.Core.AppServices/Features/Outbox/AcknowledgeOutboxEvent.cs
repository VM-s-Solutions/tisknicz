using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Outbox;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Makables.Core.AppServices.Features.Outbox;

/// <summary>
/// Admin acknowledge of a stalled outbox event (T-0109 / US-admin-0014
/// AC-2). Terminal: <see cref="OutboxEvent.Acknowledge"/> sets
/// <c>ProcessedAt</c> + <c>AcknowledgedAt</c>/<c>AcknowledgedBy</c> and the
/// row leaves the stalled set permanently (never retried). Re-acknowledging
/// an already-processed/acknowledged row is a benign Silent-Success (locked
/// A.4) — 200, no re-mutation, the EXISTING acknowledger preserved.
///
/// <para>
/// <see cref="IAdminAuditableCommand"/> → the reason rides the audit log
/// (ADR 0014). No <c>SaveChangesAsync</c> (the UoW pipeline commits).
/// </para>
/// </summary>
public static class AcknowledgeOutboxEvent
{
    public sealed record Command(string OutboxEventId, string Reason)
        : ICommand<AcknowledgeOutboxEventResponse>, IAdminAuditableCommand
    {
        public string ActionCode => "outbox.acknowledge";
        public string TargetEntity => "outbox";
        public string TargetId => OutboxEventId;
        public string? Notes => Reason;
    }

    /// <summary>Globally-unique name (NSwag PR #38 convention).</summary>
    public sealed record AcknowledgeOutboxEventResponse(
        string OutboxEventId,
        DateTimeOffset AcknowledgedAt,
        string AcknowledgedBy);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.OutboxEventId)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(40).WithErrorCode(BusinessErrorMessage.MaxLength);

            RuleFor(c => c.Reason)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(2000).WithErrorCode(BusinessErrorMessage.MaxLength);
        }
    }

    public sealed class Handler(
        IOutboxConsumerRepository outbox,
        IClock clock,
        IUserSessionProvider session,
        ILogger<Handler> logger)
        : IRequestHandler<Command, BusinessResult<AcknowledgeOutboxEventResponse>>
    {
        public async Task<BusinessResult<AcknowledgeOutboxEventResponse>> Handle(
            Command command, CancellationToken cancellationToken)
        {
            var adminUserId = session.GetUserId();
            if (string.IsNullOrEmpty(adminUserId))
            {
                return BusinessResult.Failure<AcknowledgeOutboxEventResponse>(Error.Unauthorized());
            }

            var ev = await outbox.GetByIdAsync(command.OutboxEventId, cancellationToken);
            if (ev is null)
            {
                return BusinessResult.Failure<AcknowledgeOutboxEventResponse>(
                    Error.NotFound("outboxEventId", BusinessErrorMessage.OutboxRowNotFound));
            }

            // Locked A.4: an already-processed/acknowledged row is a benign
            // no-op — 200, no re-mutation, the original acknowledger preserved.
            if (ev.ProcessedAt is not null)
            {
                logger.LogInformation(
                    "AcknowledgeOutboxEvent: outbox {OutboxEventId} already processed; idempotent skip.",
                    ev.Id);
                return BusinessResult.Success(BuildResponse(ev, adminUserId));
            }

            ev.Acknowledge(adminUserId, clock.UtcNow);

            return BusinessResult.Success(BuildResponse(ev, adminUserId));
        }

        // A swept-but-unacknowledged row has ProcessedAt set without an
        // AcknowledgedBy — fall back to the current caller / ProcessedAt so
        // the response is always coherent.
        private static AcknowledgeOutboxEventResponse BuildResponse(OutboxEvent ev, string adminUserId) =>
            new(ev.Id,
                ev.AcknowledgedAt ?? ev.ProcessedAt!.Value,
                ev.AcknowledgedBy ?? adminUserId);
    }
}
