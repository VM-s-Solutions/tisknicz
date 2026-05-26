using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Makers;
using MediatR;

namespace Makables.Core.AppServices.Features.Maker;

/// <summary>
/// Admin sets a Maker as verified (US-admin-0003). Audited via the
/// <c>AdminAuditPipelineBehavior</c> — the before/after JSONB snapshot
/// pins the <c>IsVerified</c> flip per ADR 0014.
///
/// <para>
/// AC-2: a re-verify attempt returns
/// <see cref="BusinessErrorMessage.MakerAlreadyVerified"/> as a
/// <see cref="ErrorType.Conflict"/> — the handler checks before calling
/// the entity's mutator (which would throw on double-verify).
/// </para>
/// </summary>
public static class VerifyMaker
{
    public sealed record Command(string MakerId, string? Notes)
        : ICommand, IAdminAuditableCommand
    {
        public string ActionCode => "maker.verify";
        public string TargetEntity => "maker";
        public string TargetId => MakerId;
    }

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.MakerId)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(40).WithErrorCode(BusinessErrorMessage.MaxLength);
        }
    }

    public sealed class Handler(IMakerRepository makers)
        : IRequestHandler<Command, BusinessResult>
    {
        public async Task<BusinessResult> Handle(Command command, CancellationToken cancellationToken)
        {
            var maker = await makers.GetByIdAsync(command.MakerId, cancellationToken);
            if (maker is null)
            {
                return BusinessResult.Failure(Error.NotFound("maker"));
            }

            if (maker.IsVerified)
            {
                return BusinessResult.Failure(
                    Error.Conflict("maker", BusinessErrorMessage.MakerAlreadyVerified));
            }

            maker.MarkVerified();
            return BusinessResult.Success();
        }
    }
}
