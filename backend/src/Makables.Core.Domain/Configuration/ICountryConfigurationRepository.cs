namespace Makables.Core.Domain.Configuration;

/// <summary>
/// Read-side access to per-country configuration. Implementation in
/// <c>Infra.Database</c> caches per-request (and across requests for
/// short windows) so the same row read many times during a request
/// is not a Postgres roundtrip per read.
/// </summary>
public interface ICountryConfigurationRepository
{
    /// <summary>
    /// Returns the configuration for the given ISO 3166-1 alpha-2 code,
    /// or <c>null</c> if the country isn't seeded.
    /// </summary>
    Task<CountryConfiguration?> GetByCodeAsync(string countryCode, CancellationToken cancellationToken);
}
