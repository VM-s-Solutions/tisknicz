using System.Text.Json;
using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.AppServices.Common;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.OrderMessages;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Outbox;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Makables.Core.AppServices.Features.Orders;

/// <summary>
/// T-0145 per-dispute escalation step, dispatched by the daily
/// <c>DisputeAutoEscalationFunction</c> sweep for a customer-sourced
/// <see cref="Dispute"/> whose <see cref="Dispute.ResponseWindowDays"/>
/// has elapsed with no maker reply. Enqueues the
/// <c>dispute.autoEscalated.adminEmail</c> outbox event <b>exactly once</b>
/// per dispute — the dispute row itself is never mutated except for the
/// <see cref="Dispute.AutoEscalatedAt"/> idempotency stamp; it stays
/// <c>Disputed</c> / unresolved, awaiting admin's own
/// <c>ResolveDispute.Command</c> (Alternatives Considered Option B — this
/// sweep never auto-resolves and never sanctions the maker).
///
/// <para>
/// <b>Re-checks the guards inside the handler</b> rather than trusting
/// the sweep's projection: the id-only candidate list can be stale by the
/// time this per-row command executes (concurrent resolve, a maker reply
/// landing seconds ago, or a previous run already escalating it). Every
/// guard failure is a silent no-op <see cref="BusinessResult.Success()"/> —
/// there is no client waiting on this command, so there is nothing to
/// surface a 4xx to.
/// </para>
/// </summary>
public static class EscalateDispute
{
    public sealed record Command(string DisputeId) : ICommand;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.DisputeId)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(40).WithErrorCode(BusinessErrorMessage.MaxLength);
        }
    }

    public sealed class Handler(
        IDisputeRepository disputes,
        IOrderRepository orders,
        IOrderMessageRepository orderMessages,
        IOutbox outbox,
        IClock clock,
        ILanguageResolver languageResolver,
        IOptions<PublicAppUrlsOptions> publicAppUrls,
        ILogger<Handler> logger)
        : IRequestHandler<Command, BusinessResult>
    {
        public async Task<BusinessResult> Handle(Command command, CancellationToken cancellationToken)
        {
            // Step 1: the sweep's projection can be stale — reload tracked.
            var dispute = await disputes.GetByIdUnscopedAsync(command.DisputeId, cancellationToken);
            if (dispute is null)
            {
                logger.LogInformation(
                    "EscalateDispute: dispute {DisputeId} no longer exists; no-op.", command.DisputeId);
                return BusinessResult.Success();
            }

            // Step 2: re-check both idempotency guards — resolved or
            // already escalated since the sweep read the candidate list.
            if (dispute.ResolvedAt is not null || dispute.AutoEscalatedAt is not null)
            {
                return BusinessResult.Success();
            }

            // Step 3: maker-reply-since check (Technical notes) — a reply
            // that landed between the sweep's read and this dispatch must
            // still suppress the escalation (AC-6).
            var makerReplied = await orderMessages.HasMakerReplySinceAsync(
                dispute.OrderId, dispute.CreatedAt, cancellationToken);
            if (makerReplied)
            {
                return BusinessResult.Success();
            }

            var order = await orders.GetByIdUnscopedReadOnlyAsync(dispute.OrderId, cancellationToken);
            if (order is null)
            {
                logger.LogCritical(
                    "EscalateDispute: dispute {DisputeId} has no backing order {OrderId}.",
                    dispute.Id, dispute.OrderId);
                return BusinessResult.Success();
            }

            // Step 4: idempotency stamp — the AutoEscalatedAt write IS the
            // claim; a concurrent second dispatch loses the race here.
            if (!dispute.TryMarkAutoEscalated(clock))
            {
                return BusinessResult.Success();
            }

            // Step 5: admin notification (recipient resolves at SEND time
            // from EmailOptions — same pattern as OrderDisputedAdminEmail).
            var language = await languageResolver.ResolveAsync(
                preferredLanguage: null, countryCode: order.CountryCode, cancellationToken);
            var payload = new DisputeAutoEscalatedAdminEmailPayload(
                OrderId: order.Id,
                OrderNumber: order.OrderNumber,
                DisputeId: dispute.Id,
                Category: dispute.Category,
                Description: dispute.Description,
                LanguageCode: language,
                ActionUrl: $"{publicAppUrls.Value.WebBaseUrl.TrimEnd('/')}/dashboard/admin/orders/{order.Id}");
            outbox.Enqueue(
                aggregateId: order.Id,
                eventType: OutboxEventTypes.DisputeAutoEscalatedAdminEmail,
                payloadJson: JsonSerializer.Serialize(payload));

            // Step 6: UoW commits the AutoEscalatedAt stamp + outbox row atomically.
            return BusinessResult.Success();
        }
    }
}
