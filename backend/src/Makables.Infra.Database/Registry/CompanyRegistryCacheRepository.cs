using Makables.Core.Domain.Registry;
using Microsoft.EntityFrameworkCore;

namespace Makables.Infra.Database.Registry;

/// <summary>
/// EF Core <see cref="ICompanyRegistryCacheRepository"/> impl per
/// T-0032. Tracked read because the adapter may refresh the row in
/// place after a successful registry fetch.
/// </summary>
public sealed class CompanyRegistryCacheRepository(MakablesDbContext db)
    : ICompanyRegistryCacheRepository
{
    public Task<CompanyRegistryCacheEntry?> GetAsync(
        string registryCode,
        string registrationNumber,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(registryCode) || string.IsNullOrWhiteSpace(registrationNumber))
            return Task.FromResult<CompanyRegistryCacheEntry?>(null);

        return db.Set<CompanyRegistryCacheEntry>()
            .FirstOrDefaultAsync(
                e => e.RegistryCode == registryCode && e.RegistrationNumber == registrationNumber,
                cancellationToken);
    }

    public void Add(CompanyRegistryCacheEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        db.Set<CompanyRegistryCacheEntry>().Add(entry);
    }
}
