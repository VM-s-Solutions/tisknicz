using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.AppServices.Features.Users;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Orders;
using MediatR;

namespace Makables.Core.AppServices.Features.Profile;

/// <summary>
/// Self-service GDPR account deletion (soft delete). Deactivates the
/// caller's <see cref="User"/> (and their <see cref="Maker"/> profile when
/// one exists) via <c>Auditable.MarkDeactivated</c> and revokes every
/// active refresh token, so the account is unreachable from that moment:
/// Login/Refresh/OAuth/magic-link all reject <c>!IsActive</c> users, and
/// the global soft-delete query filter hides the rows everywhere else.
///
/// <para>
/// Soft delete is deliberate: it satisfies the immediate "delete my
/// account" request while keeping the erasure matrix (anonymize +
/// hard-delete) an explicit, admin-adjudicated step —
/// <c>DeleteUserPermanently</c> / <see cref="Makables.Core.Domain.Privacy.IUserDataDeletionService"/>
/// reach soft-deleted users via unscoped loads (ADR 0013), so a
/// deactivated account remains fully erasable.
/// </para>
///
/// <para>
/// Two gates mirror the admin erase: <b>retype</b> (the caller retypes
/// their own email; mismatch → <c>user.deleteConfirmationMismatch</c>) and
/// the <b>in-flight interlock</b> (any order in
/// <see cref="DeleteUserPermanently.InFlightOrderStates"/> as customer or
/// maker → <c>user.cannotDeleteWithInFlightOrders</c>). Email retype (not
/// password) so OAuth-only accounts (<c>PasswordHash is null</c>) can
/// delete themselves too. No <c>SaveChangesAsync</c> — the UoW pipeline
/// commits.
/// </para>
/// </summary>
public static class DeleteMyAccount
{
    public sealed record Command(string ConfirmedEmail) : ICommand;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.ConfirmedEmail)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(200).WithErrorCode(BusinessErrorMessage.MaxLength);
        }
    }

    public sealed class Handler(
        IUserRepository users,
        IMakerRepository makers,
        IOrderRepository orders,
        IRefreshTokenRepository refreshTokens,
        IUserSessionProvider session,
        IClock clock)
        : IRequestHandler<Command, BusinessResult>
    {
        public async Task<BusinessResult> Handle(Command command, CancellationToken cancellationToken)
        {
            // 1. Fail-closed — a deletion must be attributable to the caller.
            var userId = session.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return BusinessResult.Failure(Error.Unauthorized());
            }

            // 2. Filtered load — an already-deactivated user surfaces as
            // NotFound (a stale JWT can outlive the deactivation).
            var user = await users.GetByIdAsync(userId, cancellationToken);
            if (user is null)
            {
                return BusinessResult.Failure(
                    Error.NotFound("userId", BusinessErrorMessage.UserNotFound));
            }

            // 3. Retype gate — case/NFC-insensitive (mirrors the login lookup).
            if (User.NormalizeEmail(command.ConfirmedEmail) != user.EmailNormalized)
            {
                return BusinessResult.Failure(
                    Error.Conflict("confirmedEmail", BusinessErrorMessage.UserDeleteConfirmationMismatch));
            }

            // 4. In-flight interlock — money or fulfilment still in motion
            // blocks the deletion, whether the caller is the buyer or the
            // selling maker. Same state list as the admin erase (Q-B).
            var maker = await makers.GetByUserIdAsync(user.Id, cancellationToken);
            var hasInFlight = await orders.HasInFlightOrderForUserAsync(
                user.Id, maker?.Id, DeleteUserPermanently.InFlightOrderStates, cancellationToken);
            if (hasInFlight)
            {
                return BusinessResult.Failure(
                    Error.Conflict("orders", BusinessErrorMessage.UserCannotDeleteWithInFlightOrders));
            }

            // 5. Soft-delete both aggregates. The maker's products drop out
            // of the public catalog via the soft-delete query filter, same
            // as the admin DeactivateMaker path (US-admin-0004 AC-1).
            var now = clock.UtcNow;
            maker?.MarkDeactivated(user.Id, now);
            user.MarkDeactivated(user.Id, now);

            // 6. Revoke every active session (logout-all) — the deletion
            // must end other devices' sessions too, not just this one's
            // cookie (which the controller clears).
            var activeTokens = await refreshTokens.GetActiveByUserAsync(user.Id, cancellationToken);
            foreach (var token in activeTokens)
            {
                token.Revoke(now);
            }

            return BusinessResult.Success();
        }
    }
}
