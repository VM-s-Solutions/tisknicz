using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Orders;
using MediatR;

namespace Makables.Core.AppServices.Features.Orders;

/// <summary>
/// T-0146 (AC-5). Admin acknowledges — on the maker's behalf — that the
/// customer's returned item was received, ahead of the eventual
/// <c>ResolveDispute.Command</c>. Admin-audited counterpart to
/// <see cref="MarkDisputeReturnReceivedByMaker"/>.
/// </summary>
public static class MarkDisputeReturnReceivedByAdmin
{
    public sealed record Command(string DisputeId)
        : ICommand<MarkDisputeReturnReceivedByAdminResponse>, IAdminAuditableCommand
    {
        public string ActionCode => "dispute.return.markReceived";
        public string TargetEntity => "dispute";
        public string TargetId => DisputeId;
        public string? Notes => null;
    }

    public sealed record MarkDisputeReturnReceivedByAdminResponse(string DisputeId, DateTimeOffset ReturnReceivedAt);

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
        IUserSessionProvider session,
        IClock clock) : IRequestHandler<Command, BusinessResult<MarkDisputeReturnReceivedByAdminResponse>>
    {
        public async Task<BusinessResult<MarkDisputeReturnReceivedByAdminResponse>> Handle(Command command, CancellationToken cancellationToken)
        {
            // Step 1: fail-closed session check (RefundOrder precedent).
            var userId = session.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return BusinessResult.Failure<MarkDisputeReturnReceivedByAdminResponse>(Error.Unauthorized());
            }

            // Step 2: unscoped tracked load (admin host, ADR 0013).
            var dispute = await disputes.GetByIdUnscopedAsync(command.DisputeId, cancellationToken);
            if (dispute is null)
            {
                return BusinessResult.Failure<MarkDisputeReturnReceivedByAdminResponse>(
                    Error.NotFound("disputeId", BusinessErrorMessage.OrderDisputeNotFound));
            }

            // Step 3: record the acknowledgment (admin identifier, not the
            // maker's — distinguishes the "on their behalf" path in the audit trail).
            var result = dispute.MarkReturnReceived(clock, receivedBy: $"admin:{userId}");
            if (!result.IsSuccess)
            {
                return BusinessResult.Failure<MarkDisputeReturnReceivedByAdminResponse>(result.Error!);
            }

            return BusinessResult.Success(new MarkDisputeReturnReceivedByAdminResponse(dispute.Id, dispute.ReturnReceivedAt!.Value));
        }
    }
}
