using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Orders;
using MediatR;

namespace Makables.Core.AppServices.Features.Orders;

/// <summary>
/// T-0146 (AC-5). Maker acknowledges receiving the customer's returned
/// item — manual only, no automated carrier-status sync for the reverse
/// leg (Out of scope). Symmetric maker-session counterpart to
/// <see cref="MarkDisputeReturnReceivedByAdmin"/> ("maker OR admin on
/// their behalf" per the ticket's AC-5). Not admin-audited — mirrors
/// <see cref="HandOverOrder"/>'s plain maker-session shape.
/// </summary>
public static class MarkDisputeReturnReceivedByMaker
{
    public sealed record Command(string DisputeId) : ICommand<MarkDisputeReturnReceivedByMakerResponse>;

    public sealed record MarkDisputeReturnReceivedByMakerResponse(string DisputeId, DateTimeOffset ReturnReceivedAt);

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
        IMakerRepository makers,
        IUserSessionProvider session,
        IClock clock) : IRequestHandler<Command, BusinessResult<MarkDisputeReturnReceivedByMakerResponse>>
    {
        public async Task<BusinessResult<MarkDisputeReturnReceivedByMakerResponse>> Handle(Command command, CancellationToken cancellationToken)
        {
            // Step 1: fail-closed session check.
            var userId = session.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return BusinessResult.Failure<MarkDisputeReturnReceivedByMakerResponse>(Error.Unauthorized());
            }

            // Step 2: maker resolution.
            var maker = await makers.GetByUserIdAsync(userId, cancellationToken);
            if (maker is null)
            {
                return BusinessResult.Failure<MarkDisputeReturnReceivedByMakerResponse>(
                    Error.NotFound("disputeId", BusinessErrorMessage.OrderDisputeNotFound));
            }

            // Step 3: owner-scoped dispute load (IDOR shield, AC-7).
            var dispute = await disputes.GetByIdForMakerAsync(command.DisputeId, maker.Id, cancellationToken);
            if (dispute is null)
            {
                return BusinessResult.Failure<MarkDisputeReturnReceivedByMakerResponse>(
                    Error.NotFound("disputeId", BusinessErrorMessage.OrderDisputeNotFound));
            }

            // Step 4: record the acknowledgment.
            var result = dispute.MarkReturnReceived(clock, receivedBy: userId);
            if (!result.IsSuccess)
            {
                return BusinessResult.Failure<MarkDisputeReturnReceivedByMakerResponse>(result.Error!);
            }

            return BusinessResult.Success(new MarkDisputeReturnReceivedByMakerResponse(dispute.Id, dispute.ReturnReceivedAt!.Value));
        }
    }
}
