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
    /// or <c>null</c> if the country isn't seeded. Read-side: the impl uses
    /// <c>AsNoTracking</c> (the hot provider-resolution path reads this many
    /// times per request) — do NOT mutate the returned entity expecting a
    /// commit; use <see cref="GetByCodeForUpdateAsync"/> for admin writes.
    /// </summary>
    Task<CountryConfiguration?> GetByCodeAsync(string countryCode, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the TRACKED configuration row for the given code (admin write
    /// path, T-0108). EF change-tracking on the returned entity carries the
    /// mutation and the <c>UnitOfWorkPipelineBehavior</c> commits — the
    /// command handler never calls <c>SaveChangesAsync</c>. Distinct from the
    /// <c>AsNoTracking</c> <see cref="GetByCodeAsync"/> read path so the hot
    /// provider-resolution reads stay tracking-free.
    /// </summary>
    Task<CountryConfiguration?> GetByCodeForUpdateAsync(string countryCode, CancellationToken cancellationToken);
}
