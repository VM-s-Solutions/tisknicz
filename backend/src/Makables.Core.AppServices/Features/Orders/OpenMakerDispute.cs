using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.AppServices.Common;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Outbox;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Makables.Core.AppServices.Features.Orders;

/// <summary>
/// Maker opens a dispute on an order assigned to them (T-0106 /
/// US-admin-0011 AC-1 — mirror of <see cref="OpenCustomerDispute"/> with
/// the maker ownership scope and <see cref="DisputeSource.Maker"/>).
/// Identical state / category / idempotency semantics; the maker is
/// resolved from the JWT via <see cref="IMakerRepository.GetByUserIdAsync"/>
/// (PostMakerOrderMessage precedent) — never from the request.
/// </summary>
public static class OpenMakerDispute
{
    public sealed record Command(string OrderId, DisputeCategory Category, string Description)
        : ICommand<OpenMakerDisputeResponse>;

    /// <summary>
    /// <b>Globally-unique name</b> per the post-PR-#38 NSwag convention.
    /// </summary>
    public sealed record OpenMakerDisputeResponse(string OrderId, string DisputeId);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.OrderId)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(40).WithErrorCode(BusinessErrorMessage.MaxLength);

            RuleFor(c => c.Category)
                .Cascade(CascadeMode.Stop)
                .IsInEnum().WithErrorCode(BusinessErrorMessage.InvalidEnumValue)
                .Must(c => c is not (DisputeCategory.CarrierReturned or DisputeCategory.CarrierFailed))
                .WithErrorCode(BusinessErrorMessage.OrderDisputeCategoryNotAllowed);

            RuleFor(c => c.Description)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(Dispute.MaxDescriptionLength).WithErrorCode(BusinessErrorMessage.MaxLength);
        }
    }

    public sealed class Handler(
        IOrderRepository orders,
        IMakerRepository makers,
        IDisputeRepository disputes,
        IOutbox outbox,
        IClock clock,
        IIdGenerator ids,
        IUserSessionProvider session,
        ILanguageResolver languageResolver,
        IOptions<PublicAppUrlsOptions> publicAppUrls,
        ILogger<Handler> logger)
        : IRequestHandler<Command, BusinessResult<OpenMakerDisputeResponse>>
    {
        public async Task<BusinessResult<OpenMakerDisputeResponse>> Handle(
            Command command, CancellationToken cancellationToken)
        {
            // Step 1: Resolve maker user → maker entity (never from request).
            var userId = session.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return BusinessResult.Failure<OpenMakerDisputeResponse>(Error.Unauthorized());
            }
            var maker = await makers.GetByUserIdAsync(userId, cancellationToken);
            if (maker is null)
            {
                // A maker-audience JWT without a maker row → leak nothing
                // about whether the order exists.
                return BusinessResult.Failure<OpenMakerDisputeResponse>(
                    Error.NotFound("orderId", BusinessErrorMessage.OrderNotFound));
            }

            // Step 2: IDOR-scoped order load.
            var order = await orders.GetByIdForMakerAsync(
                command.OrderId, maker.Id, cancellationToken);
            if (order is null)
            {
                return BusinessResult.Failure<OpenMakerDisputeResponse>(
                    Error.NotFound("orderId", BusinessErrorMessage.OrderNotFound));
            }

            // Step 3: Silent-Success on re-open (§C.4) — return the
            // EXISTING open dispute's id; no mutation, no outbox.
            if (order.State == OrderState.Disputed)
            {
                var existing = await disputes.GetOpenByOrderIdAsync(order.Id, cancellationToken);
                if (existing is null)
                {
                    logger.LogCritical(
                        "OpenMakerDispute: order {OrderId} is Disputed but has NO open dispute row. " +
                        "Invariant violation — refusing the idempotent return.",
                        order.Id);
                    return BusinessResult.Failure<OpenMakerDisputeResponse>(
                        Error.Conflict("state", BusinessErrorMessage.OrderInvalidTransition));
                }
                logger.LogInformation(
                    "OpenMakerDispute: order {OrderId} already Disputed; returning open dispute {DisputeId}.",
                    order.Id, existing.Id);
                return BusinessResult.Success(new OpenMakerDisputeResponse(order.Id, existing.Id));
            }

            // Step 4: Parenthesis-state flip (stamps PreDisputeState).
            var transition = order.OpenDispute(clock);
            if (!transition.IsSuccess)
            {
                return BusinessResult.Failure<OpenMakerDisputeResponse>(transition.Error!);
            }

            // Step 5: Dispute row — Source stamped server-side.
            var dispute = Dispute.Open(
                id: ids.Next(),
                orderId: order.Id,
                category: command.Category,
                description: command.Description,
                source: DisputeSource.Maker,
                countryCode: order.CountryCode);
            await disputes.AddAsync(dispute, cancellationToken);

            // Step 6: Admin notification — shared payload builder so the
            // shape cannot drift between sources.
            await OpenCustomerDispute.Handler.EnqueueAdminEmailAsync(
                outbox, languageResolver, publicAppUrls.Value, order, dispute, cancellationToken);

            return BusinessResult.Success(new OpenMakerDisputeResponse(order.Id, dispute.Id));
        }
    }
}
