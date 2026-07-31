using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Makers;
using MediatR;

namespace Makables.Core.AppServices.Features.Maker;

/// <summary>
/// Attaches an already-uploaded logo blob to the authenticated maker, or
/// clears the current one when <see cref="Command.BlobPath"/> is null.
///
/// <para>
/// Split from <see cref="UpdateMakerProfile"/> because the lifecycle is
/// different: the blob is written by the upload controller BEFORE this
/// command runs (ADR 0011 — blob I/O stays outside the unit-of-work
/// transaction), so the command is the second half of a two-phase
/// operation rather than a plain field patch. Folding it into the
/// profile PUT would make every bio edit capable of orphaning a blob.
/// </para>
///
/// <para>
/// <see cref="Response.PreviousBlobPath"/> hands the superseded blob back
/// to the controller, which deletes it after the command succeeds.
/// Without that, replacing a logo ten times would leave ten unreachable
/// blobs billed to us forever.
/// </para>
///
/// <para>
/// The target maker is resolved from
/// <see cref="IUserSessionProvider.GetUserId"/> — never from a request
/// param (IDOR shield).
/// </para>
/// </summary>
public static class SetMakerLogo
{
    /// <param name="BlobPath">Blob path in the <c>profile-images</c> container, or null to clear.</param>
    public sealed record Command(string? BlobPath) : ICommand<Response>;

    /// <param name="PreviousBlobPath">The replaced blob, for the caller to delete. Null if there was none.</param>
    public sealed record Response(string? PreviousBlobPath);

    public sealed class Handler(
        IMakerRepository makers,
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

            var maker = await makers.GetByUserIdAsync(userId, cancellationToken);
            if (maker is null)
            {
                return BusinessResult.Failure<Response>(Error.NotFound("maker"));
            }

            var previous = maker.SetLogo(command.BlobPath);

            // Only report a previous blob worth deleting when it actually
            // changed — a no-op re-set of the same path must not schedule
            // a delete of the blob we just kept.
            return BusinessResult.Success(new Response(
                previous == maker.LogoBlobPath ? null : previous));
        }
    }
}
