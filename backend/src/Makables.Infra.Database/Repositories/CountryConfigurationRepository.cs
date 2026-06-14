using Makables.Core.Domain.Configuration;
using Microsoft.EntityFrameworkCore;

namespace Makables.Infra.Database.Repositories;

public sealed class CountryConfigurationRepository(MakablesDbContext db)
    : ICountryConfigurationRepository
{
    public Task<CountryConfiguration?> GetByCodeAsync(string countryCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(countryCode) || countryCode.Length != 2)
        {
            return Task.FromResult<CountryConfiguration?>(null);
        }

        var normalized = countryCode.ToUpperInvariant();

        return db.Set<CountryConfiguration>()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CountryId == normalized, cancellationToken);
    }

    public Task<CountryConfiguration?> GetByCodeForUpdateAsync(
        string countryCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(countryCode) || countryCode.Length != 2)
        {
            return Task.FromResult<CountryConfiguration?>(null);
        }

        var normalized = countryCode.ToUpperInvariant();

        // Tracked (no AsNoTracking) — the T-0108 admin command mutates the
        // returned entity and the UoW pipeline commits the change.
        return db.Set<CountryConfiguration>()
            .FirstOrDefaultAsync(c => c.CountryId == normalized, cancellationToken);
    }
}
