using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using MediatR;

namespace Makables.Core.AppServices.Features.Profile;

/// <summary>
/// Returns the authenticated caller's User row. The target is resolved
/// from <see cref="IUserSessionProvider.GetUserId"/> — there is no
/// request shape field for the userId (IDOR shield, same pattern as
/// T-0034 UpdateMakerProfile).
/// </summary>
public static class GetMyProfile
{
    public sealed record Query : IQuery<Response>;

    public sealed record Response(
        string UserId,
        string Email,
        string FullName,
        string? Phone,
        string CountryCodePrimary,
        UserRole Role,
        bool EmailConfirmed,
        string? PreferredLanguage);

    public sealed class Handler(
        IUserRepository users,
        IUserSessionProvider session) : IRequestHandler<Query, BusinessResult<Response>>
    {
        public async Task<BusinessResult<Response>> Handle(Query _, CancellationToken cancellationToken)
        {
            var userId = session.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return BusinessResult.Failure<Response>(Error.Unauthorized());
            }

            var user = await users.GetByIdAsync(userId, cancellationToken);
            if (user is null)
            {
                return BusinessResult.Failure<Response>(Error.NotFound("user"));
            }

            return BusinessResult.Success(new Response(
                UserId: user.Id,
                Email: user.Email,
                FullName: user.FullName,
                Phone: user.Phone,
                CountryCodePrimary: user.CountryCodePrimary,
                Role: user.Role,
                EmailConfirmed: user.EmailConfirmedAt is not null,
                PreferredLanguage: user.PreferredLanguage));
        }
    }
}
