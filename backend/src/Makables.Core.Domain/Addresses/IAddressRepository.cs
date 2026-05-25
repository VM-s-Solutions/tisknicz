namespace Makables.Core.Domain.Addresses;

/// <summary>
/// Persistence access to <see cref="Address"/> rows. Minimal surface per
/// T-0030 size:S scope: add a new address; read one by id (used by
/// T-0031's geocoder fill, T-0033 maker-registration command). Later
/// query methods (list-by-owner, soft-delete, replace) land when a
/// concrete feature actually needs them.
///
/// Implementation: <c>Makables.Infra.Database/Addresses/AddressRepository.cs</c>.
/// Caller drives the unit-of-work — the <c>UnitOfWorkPipelineBehavior</c>
/// commits when the surrounding command succeeds, exactly like every
/// other repository in the codebase.
/// </summary>
public interface IAddressRepository
{
    /// <summary>
    /// Mark <paramref name="address"/> as added to the change tracker.
    /// Use <see cref="Address.Create"/> to instantiate first.
    /// </summary>
    void Add(Address address);

    /// <summary>
    /// Returns the address by id, or <c>null</c> if no active row exists.
    /// Active-only by virtue of the soft-delete query filter on
    /// <see cref="Common.Auditable"/>.
    /// </summary>
    Task<Address?> GetByIdAsync(string id, CancellationToken cancellationToken);
}
