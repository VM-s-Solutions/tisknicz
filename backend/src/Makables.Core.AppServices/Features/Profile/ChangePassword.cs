using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using MediatR;

namespace Makables.Core.AppServices.Features.Profile;

/// <summary>
/// Authenticated password change — verifies the current password, then
/// stores a new Argon2id hash via <see cref="User.SetPasswordHash"/>.
///
/// <para>
/// Target user is resolved from <see cref="IUserSessionProvider.GetUserId"/>
/// (IDOR shield). Current-password failure returns
/// <see cref="BusinessErrorMessage.AuthCurrentPasswordWrong"/> with
/// <see cref="ErrorType.Unauthorized"/> — refresh-family invalidation
/// is a separate concern (see T-future "force-logout-other-sessions").
/// </para>
/// </summary>
public static class ChangePassword
{
    public sealed record Command(string CurrentPassword, string NewPassword) : ICommand;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.CurrentPassword)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required);

            RuleFor(c => c.NewPassword)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MinimumLength(10).WithErrorCode(BusinessErrorMessage.MinLength)
                .MaximumLength(200).WithErrorCode(BusinessErrorMessage.MaxLength);
        }
    }

    public sealed class Handler(
        IUserRepository users,
        IPasswordHasher hasher,
        IUserSessionProvider session) : IRequestHandler<Command, BusinessResult>
    {
        public async Task<BusinessResult> Handle(Command command, CancellationToken cancellationToken)
        {
            var userId = session.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return BusinessResult.Failure(Error.Unauthorized());
            }

            var user = await users.GetByIdAsync(userId, cancellationToken);
            if (user is null)
            {
                return BusinessResult.Failure(Error.NotFound("user"));
            }
            if (string.IsNullOrEmpty(user.PasswordHash))
            {
                // OAuth-only accounts have no password to verify.
                return BusinessResult.Failure(
                    new Error("currentPassword", BusinessErrorMessage.AuthCurrentPasswordWrong, ErrorType.Unauthorized));
            }
            if (!hasher.Verify(command.CurrentPassword, user.PasswordHash))
            {
                return BusinessResult.Failure(
                    new Error("currentPassword", BusinessErrorMessage.AuthCurrentPasswordWrong, ErrorType.Unauthorized));
            }

            user.SetPasswordHash(hasher.Hash(command.NewPassword));
            return BusinessResult.Success();
        }
    }
}
