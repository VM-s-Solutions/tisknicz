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
}
