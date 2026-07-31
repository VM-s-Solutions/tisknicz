using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Makers;
using MediatR;

namespace Makables.Core.AppServices.Features.Maker;

/// <summary>
/// Returns the authenticated maker's profile row. The target is resolved
/// from <see cref="IUserSessionProvider.GetUserId"/> — there is no
/// request shape field for the userId (IDOR shield).
/// </summary>
public static class GetMyMakerProfile
{
    public sealed record Query : IQuery<Response>;

    public sealed record Response(
        string MakerId,
        string RegistrationNumber,
        string? VatId,
        string CompanyName,
        string? LegalForm,
        string RegisteredAddressId,
        bool IsActiveInRegistry,
        bool IsVerified,
        bool SnapshotIsStale,
        DateTimeOffset SnapshotFetchedAt,
        string? Bio,
        string? BankAccount,
        bool PersonalPickupEnabled,
        string? PickupNote,
        string? LogoBlobPath);

    public sealed class Handler(
        IMakerRepository makers,
        IUserSessionProvider session) : IRequestHandler<Query, BusinessResult<Response>>
    {
        public async Task<BusinessResult<Response>> Handle(Query _, CancellationToken cancellationToken)
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

            return BusinessResult.Success(new Response(
                MakerId: maker.Id,
                RegistrationNumber: maker.RegistrationNumber,
                VatId: maker.VatId,
                CompanyName: maker.CompanyName,
                LegalForm: maker.LegalForm,
                RegisteredAddressId: maker.RegisteredAddressId,
                IsActiveInRegistry: maker.IsActiveInRegistry,
                IsVerified: maker.IsVerified,
                SnapshotIsStale: maker.SnapshotIsStale,
                SnapshotFetchedAt: maker.SnapshotFetchedAt,
                Bio: maker.Bio,
                BankAccount: maker.BankAccount,
                PersonalPickupEnabled: maker.PersonalPickupEnabled,
                PickupNote: maker.PickupNote,
                LogoBlobPath: maker.LogoBlobPath));
        }
    }
}
