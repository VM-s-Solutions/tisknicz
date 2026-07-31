using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using MediatR;

namespace Makables.Core.AppServices.Features.Profile;

/// <summary>
/// Attaches an already-uploaded avatar blob to the authenticated user, or
/// clears the current one when <see cref="Command.BlobPath"/> is null.
/// Mirror of <c>Maker.SetMakerLogo</c> for the User aggregate — see that
/// file for why the attach is its own command rather than a field on the
/// profile PUT.
///
/// <para>
/// The target user is resolved from
/// <see cref="IUserSessionProvider.GetUserId"/> — never from a request
/// param (IDOR shield).
/// </para>
/// </summary>
public static class SetUserAvatar
{
    /// <param name="BlobPath">Blob path in the <c>profile-images</c> container, or null to clear.</param>
    public sealed record Command(string? BlobPath) : ICommand<Response>;

    /// <param name="PreviousBlobPath">The replaced blob, for the caller to delete. Null if there was none.</param>
    public sealed record Response(string? PreviousBlobPath);

    public sealed class Handler(
        IUserRepository users,
        IUserSessionProvider session)
        : IRequestHandler<Command, BusinessResult<Response>>
    {
        public async Task<BusinessResult<Response>> Handle(Command command, CancellationToken cancellationToken)
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

            var previous = user.SetAvatar(command.BlobPath);

            // Only report a previous blob worth deleting when it actually
            // changed — a no-op re-set of the same path must not schedule
            // a delete of the blob we just kept.
            return BusinessResult.Success(new Response(
                previous == user.AvatarBlobPath ? null : previous));
        }
    }
}
