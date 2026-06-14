using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Privacy;
using MediatR;

namespace Makables.Core.AppServices.Features.Users;

/// <summary>
/// Admin GDPR hard-delete of a user (T-0110 / US-admin-0016) — the single
/// most sensitive command in the system and the ONLY genuinely irreversible
/// one. The destructive matrix lives behind the architect-owned
/// <see cref="IUserDataDeletionService"/> seam (patterns §A.23); this handler
/// is a thin orchestrator: fail-closed session → load (Unscoped) → retype
/// gate → in-flight interlock → invoke the seam → return.
///
/// <para>
/// Two gates: <b>retype</b> (the admin retypes the user's email into
/// <c>ConfirmedEmail</c>; mismatch → <c>user.deleteConfirmationMismatch</c>)
/// and <b>in-flight interlock</b> (any order in
/// <c>[PendingPayment, Paid, Accepted, Shipped]</c> → REJECT with
/// <c>user.cannotDeleteWithInFlightOrders</c>). NO Silent-Success on re-call:
/// the row is gone, so a second call returns <c>user.notFound</c>. No
/// <c>SaveChangesAsync</c> — the UoW pipeline commits; the audit row
/// (<c>IAdminAuditableCommand</c>) survives the user deletion.
/// </para>
/// </summary>
public static class DeleteUserPermanently
{
    /// <summary>
    /// The anonymization placeholder (US-admin-0016 AC-1 wording). Single
    /// source of truth shared by the entity transforms + the tests.
    /// </summary>
    public const string AnonymizationSentinel = "Anonymized";

    /// <summary>
    /// The in-flight states that block an erase (Q-B) — money or fulfilment
    /// still in motion. Settled states (Delivered/Completed/Cancelled/
    /// Refunded/Disputed) are safe to anonymize. Single source of truth
    /// shared by the interlock query and the predicate test.
    /// </summary>
    public static readonly IReadOnlyList<OrderState> InFlightOrderStates =
    [
        OrderState.PendingPayment,
        OrderState.Paid,
        OrderState.Accepted,
        OrderState.Shipped,
    ];

    public sealed record Command(string UserId, string ConfirmedEmail, string Reason)
        : ICommand<DeleteUserPermanentlyResponse>, IAdminAuditableCommand
    {
        public string ActionCode => "user.erase";
        public string TargetEntity => "user";
        public string TargetId => UserId;
        public string? Notes => Reason;
    }

    /// <summary>Globally-unique name (NSwag PR #38 convention).</summary>
    public sealed record DeleteUserPermanentlyResponse(string ErasedUserId);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.UserId)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(40).WithErrorCode(BusinessErrorMessage.MaxLength);

            RuleFor(c => c.ConfirmedEmail)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(200).WithErrorCode(BusinessErrorMessage.MaxLength);

            RuleFor(c => c.Reason)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(2000).WithErrorCode(BusinessErrorMessage.MaxLength);
        }
    }

    public sealed class Handler(
        IUserRepository users,
        IMakerRepository makers,
        IOrderRepository orders,
        IUserDataDeletionService deletion,
        IUserSessionProvider session)
        : IRequestHandler<Command, BusinessResult<DeleteUserPermanentlyResponse>>
    {
        public async Task<BusinessResult<DeleteUserPermanentlyResponse>> Handle(
            Command command, CancellationToken cancellationToken)
        {
            // 1. Fail-closed — never attribute a hard-delete to "system".
            if (string.IsNullOrEmpty(session.GetUserId()))
            {
                return BusinessResult.Failure<DeleteUserPermanentlyResponse>(Error.Unauthorized());
            }

            // 2. Load Unscoped — a soft-deleted user is still erasable
            // (a deactivation does not satisfy erasure; ADR 0013).
            var user = await users.GetByIdIgnoringFiltersAsync(command.UserId, cancellationToken);
            if (user is null)
            {
                return BusinessResult.Failure<DeleteUserPermanentlyResponse>(
                    Error.NotFound("userId", BusinessErrorMessage.UserNotFound));
            }

            // 3. Retype gate — case/NFC-insensitive (mirrors the login lookup).
            if (User.NormalizeEmail(command.ConfirmedEmail) != user.EmailNormalized)
            {
                return BusinessResult.Failure<DeleteUserPermanentlyResponse>(
                    Error.Conflict("confirmedEmail", BusinessErrorMessage.UserDeleteConfirmationMismatch));
            }

            // 4. In-flight interlock (Q-B) — existence check, short-circuits
            // BEFORE the irreversible seam. A maker-seller's orders count too.
            var maker = await makers.GetByUserIdAsync(user.Id, cancellationToken);
            var hasInFlight = await orders.HasInFlightOrderForUserAsync(
                user.Id, maker?.Id, InFlightOrderStates, cancellationToken);
            if (hasInFlight)
            {
                return BusinessResult.Failure<DeleteUserPermanentlyResponse>(
                    Error.Conflict("orders", BusinessErrorMessage.UserCannotDeleteWithInFlightOrders));
            }

            // 5. Erase — the seam stages the full matrix in tracked changes
            // (anonymize + hard-delete) inside this command's UoW, and
            // re-guards in-flight defensively (patterns §A.23 rule 1).
            var erased = await deletion.EraseAsync(user.Id, cancellationToken);
            if (!erased.IsSuccess)
            {
                return BusinessResult.Failure<DeleteUserPermanentlyResponse>(erased.Error!);
            }

            // 6. The UoW pipeline commits; the AdminAuditLogEntry survives the
            // user deletion (it records the admin action, not the user's data).
            return BusinessResult.Success(new DeleteUserPermanentlyResponse(user.Id));
        }
    }
}
