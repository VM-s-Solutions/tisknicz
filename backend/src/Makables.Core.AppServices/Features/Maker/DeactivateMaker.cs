using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Makers;
using MediatR;

namespace Makables.Core.AppServices.Features.Maker;

/// <summary>
/// Admin soft-deletes a Maker (US-admin-0004). The Maker's products
/// disappear from the public catalog via the global soft-delete query
/// filter; in-flight orders are unaffected (US-admin-0004 AC-1).
///
/// <para>
/// Audited via <c>AdminAuditPipelineBehavior</c>. The admin's notes are
/// captured on the audit row (NOT on the Maker — Auditable carries
/// <c>DeactivatedBy/At</c>, not a reason field).
/// </para>
///
/// <para>
/// <c>DeactivatedBy/At</c> are stamped by <c>Auditable.MarkDeactivated</c>;
/// the audit interceptor does NOT auto-fill these on EF Remove (which
/// would be a hard delete anyway). The handler passes the admin's user
/// id + the clock's <c>UtcNow</c> explicitly.
/// </para>
///
/// <para>
/// <b>Authorization.</b> The handler does NOT verify the caller is an
/// admin. The host that wires this controller MUST gate the endpoint
/// with <c>[Authorize(Roles = "Admin")]</c>. T-0034 security reviewer M-1.
/// </para>
/// </summary>
public static class DeactivateMaker
{
    public sealed record Command(string MakerId, string? Notes)
        : ICommand, IAdminAuditableCommand
    {
        public string ActionCode => "maker.deactivate";
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

            // Cap Notes at the audit-log column width. T-0034 sec
            // reviewer m-3.
            When(c => c.Notes is not null, () =>
            {
                RuleFor(c => c.Notes!)
                    .MaximumLength(2000).WithErrorCode(BusinessErrorMessage.MaxLength);
            });
        }
    }

    public sealed class Handler(
        IMakerRepository makers,
        IUserSessionProvider session,
        IClock clock)
        : IRequestHandler<Command, BusinessResult>
    {
        public async Task<BusinessResult> Handle(Command command, CancellationToken cancellationToken)
        {
            // Fail-closed if the caller has no session. The host-level
            // [Authorize] gate should make this unreachable, but
            // attributing destructive actions to a "system" pseudo-user
            // would mask a misconfigured endpoint. T-0034 sec reviewer m-1.
            var adminUserId = session.GetUserId();
            if (string.IsNullOrEmpty(adminUserId))
            {
                return BusinessResult.Failure(Error.Unauthorized());
            }

            // IMakerRepository.GetByIdAsync is filtered by the global
            // soft-delete query filter, so an already-deactivated maker
            // surfaces as NotFound. T-0034 sec reviewer n-1: no
            // additional IsActive check needed here.
            var maker = await makers.GetByIdAsync(command.MakerId, cancellationToken);
            if (maker is null)
            {
                return BusinessResult.Failure(Error.NotFound("maker"));
            }

            maker.MarkDeactivated(adminUserId, clock.UtcNow);

            return BusinessResult.Success();
        }
    }
}
