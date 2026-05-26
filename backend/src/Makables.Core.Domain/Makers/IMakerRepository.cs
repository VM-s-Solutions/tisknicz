namespace Makables.Core.Domain.Makers;

/// <summary>
/// Persistence access for <see cref="Maker"/>. T-0033 ships the minimal
/// surface needed by <c>RegisterMaker</c>: add + uniqueness pre-check by
/// IČO + load-by-user. Admin / catalog queries arrive with their own
/// tickets (T-0034 admin actions, T-0044 maker-profile-by-slug, etc.).
///
/// Implementation lives in
/// <c>Makables.Infra.Database/Makers/MakerRepository.cs</c>. Caller
/// drives the unit-of-work — the <c>UnitOfWorkPipelineBehavior</c>
/// commits when the surrounding command succeeds.
/// </summary>
public interface IMakerRepository
{
    /// <summary>Mark <paramref name="maker"/> as added to the change tracker.</summary>
    void Add(Maker maker);

    /// <summary>
    /// True when an ACTIVE maker row exists with the given
    /// <paramref name="registrationNumber"/>. The active-only filter is
    /// the global soft-delete filter on <see cref="Common.Auditable"/> —
    /// soft-deleted makers freeing the IČO is a deliberate property
    /// (an admin GDPR-purge of a stale registration must allow the same
    /// IČO to register again).
    /// </summary>
    Task<bool> IcoExistsAsync(string registrationNumber, CancellationToken cancellationToken);

    /// <summary>
    /// Load the Maker by its linked <see cref="Maker.UserId"/>. Null if
    /// the user has no maker row (i.e. they're a customer). Active-only.
    /// </summary>
    Task<Maker?> GetByUserIdAsync(string userId, CancellationToken cancellationToken);
}
