using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using MediatR;

namespace Makables.Core.AppServices.Features.Profile;

/// <summary>
/// Customer / Maker self-service profile patch — updates fields on the
/// User entity (full name, phone). The target is resolved from
/// <see cref="IUserSessionProvider.GetUserId"/> (IDOR shield, same
/// pattern as T-0034 UpdateMakerProfile).
///
/// <para>
/// Email cannot be changed here — that's a separate flow (T-future
/// "Change email" — needs re-confirmation and refresh-family
/// invalidation).
/// </para>
/// </summary>
public static class UpdateUserProfile
{
    public sealed record Command(string FullName, string? Phone) : ICommand;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.FullName)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(200).WithErrorCode(BusinessErrorMessage.MaxLength);

            When(c => c.Phone is not null, () =>
            {
                RuleFor(c => c.Phone!)
                    .MaximumLength(40).WithErrorCode(BusinessErrorMessage.MaxLength);
            });
        }
    }

    public sealed class Handler(
        IUserRepository users,
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

            user.UpdateProfile(command.FullName, command.Phone);
            return BusinessResult.Success();
        }
    }
}
