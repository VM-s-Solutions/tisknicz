using Makables.Core.Domain.Addresses;
using Microsoft.EntityFrameworkCore;

namespace Makables.Infra.Database.Addresses;

/// <summary>
/// EF Core <see cref="IAddressRepository"/> impl. Tracked reads on
/// <see cref="GetByIdAsync"/> because the caller mutates and
/// SaveChanges through the UoW pipeline (or directly from T-0031's
/// geocoder fill). The soft-delete query filter on
/// <see cref="Makables.Core.Domain.Common.Auditable"/> hides deactivated
/// rows by default (T-0030 Copilot review: cref fully-qualified so the
/// XML doc resolves from this assembly).
/// </summary>
public sealed class AddressRepository(MakablesDbContext db) : IAddressRepository
{
    public void Add(Address address)
    {
        ArgumentNullException.ThrowIfNull(address);
        db.Set<Address>().Add(address);
    }

    public Task<Address?> GetByIdAsync(string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id)) return Task.FromResult<Address?>(null);
        return db.Set<Address>().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
    }
}
