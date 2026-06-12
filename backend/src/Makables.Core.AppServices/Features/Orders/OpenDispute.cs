using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.AppServices.Common;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Outbox;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Makables.Core.AppServices.Features.Orders;

/// <summary>
/// Admin opens a dispute on ANY order (T-0106) — e.g. transcribing a
/// phone-reported problem. Unscoped lookup (admin host only, ADR 0013);
/// <b>any</b> category is allowed, including the carrier-reserved ones
/// (§C.6 — an admin may record a phone-reported carrier failure).
/// Audited via <see cref="IAdminAuditableCommand"/> — the before/after
/// JSONB pins the state flip; the description rides the audit notes.
/// Same Silent-Success re-open semantics as the party variants (§C.4).
/// </summary>
public static class OpenDispute
{
    public sealed record Command(string OrderId, DisputeCategory Category, string Description)
        : ICommand<OpenDisputeResponse>, IAdminAuditableCommand
    {
        public string ActionCode => "order.dispute.open";
        public string TargetEntity => "order";
        public string TargetId => OrderId;
        public string? Notes => Description;
    }

    /// <summary>
    /// <b>Globally-unique name</b> per the post-PR-#38 NSwag convention.
    /// </summary>
    public sealed record OpenDisputeResponse(string OrderId, string DisputeId);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.OrderId)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(40).WithErrorCode(BusinessErrorMessage.MaxLength);

            // Admin accepts ALL categories — no carrier-reserved gate.
            RuleFor(c => c.Category)
                .IsInEnum().WithErrorCode(BusinessErrorMessage.InvalidEnumValue);

            RuleFor(c => c.Description)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(Dispute.MaxDescriptionLength).WithErrorCode(BusinessErrorMessage.MaxLength);
        }
    }

    public sealed class Handler(
        IOrderRepository orders,
        IDisputeRepository disputes,
        IOutbox outbox,
        IClock clock,
        IIdGenerator ids,
        IUserSessionProvider session,
        ILanguageResolver languageResolver,
        IOptions<PublicAppUrlsOptions> publicAppUrls,
        ILogger<Handler> logger)
        : IRequestHandler<Command, BusinessResult<OpenDisputeResponse>>
    {
        public async Task<BusinessResult<OpenDisputeResponse>> Handle(
            Command command, CancellationToken cancellationToken)
        {
            // Step 1: Fail-closed session check — the audit row must never
            // attribute a privileged action to "system" (VerifyMaker
            // precedent).
            if (string.IsNullOrEmpty(session.GetUserId()))
            {
                return BusinessResult.Failure<OpenDisputeResponse>(Error.Unauthorized());
            }

            // Step 2: Tracked unscoped load (admin host only, ADR 0013).
            var order = await orders.GetByIdUnscopedAsync(command.OrderId, cancellationToken);
            if (order is null)
            {
                return BusinessResult.Failure<OpenDisputeResponse>(
                    Error.NotFound("orderId", BusinessErrorMessage.OrderNotFound));
            }

            // Step 3: Silent-Success on re-open (§C.4).
            if (order.State == OrderState.Disputed)
            {
                var existing = await disputes.GetOpenByOrderIdAsync(order.Id, cancellationToken);
                if (existing is null)
                {
                    logger.LogCritical(
                        "OpenDispute (admin): order {OrderId} is Disputed but has NO open dispute row. " +
                        "Invariant violation — refusing the idempotent return.",
                        order.Id);
                    return BusinessResult.Failure<OpenDisputeResponse>(
                        Error.Conflict("state", BusinessErrorMessage.OrderInvalidTransition));
                }
                logger.LogInformation(
                    "OpenDispute (admin): order {OrderId} already Disputed; returning open dispute {DisputeId}.",
                    order.Id, existing.Id);
                return BusinessResult.Success(new OpenDisputeResponse(order.Id, existing.Id));
            }

            // Step 4: Parenthesis-state flip (stamps PreDisputeState).
            var transition = order.OpenDispute(clock);
            if (!transition.IsSuccess)
            {
                return BusinessResult.Failure<OpenDisputeResponse>(transition.Error!);
            }

            // Step 5: Dispute row — Source stamped server-side.
            var dispute = Dispute.Open(
                id: ids.Next(),
                orderId: order.Id,
                category: command.Category,
                description: command.Description,
                source: DisputeSource.Admin,
                countryCode: order.CountryCode);
            await disputes.AddAsync(dispute, cancellationToken);

            // Step 6: Admin notification — shared payload builder.
            await OpenCustomerDispute.Handler.EnqueueAdminEmailAsync(
                outbox, languageResolver, publicAppUrls.Value, order, dispute, cancellationToken);

            // Step 7: UoW commits flip + row + outbox + audit atomically.
            return BusinessResult.Success(new OpenDisputeResponse(order.Id, dispute.Id));
        }
    }
}
