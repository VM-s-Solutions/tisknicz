namespace Makables.Core.Domain.Registry;

/// <summary>
/// DB-cache repository per ADR 0018 §"Caching policy". The adapter
/// uses this to read fresh-or-stale rows AND to write new rows after
/// a successful registry fetch. Lookup order in the adapter: in-memory
/// cache → this DB cache → HTTP.
///
/// Implementation lives in
/// <c>Makables.Infra.Database/Registry/CompanyRegistryCacheRepository.cs</c>.
/// </summary>
public interface ICompanyRegistryCacheRepository
{
    /// <summary>
    /// Load the row by composite key, regardless of <see cref="CompanyRegistryCacheEntry.ExpiresAt"/>
    /// — the adapter decides whether to treat a stale row as "fresh
    /// enough" (within the 7-day stale-fallback window when the upstream
    /// registry is unreachable) or skip it. Returns <c>null</c> if no row.
    /// </summary>
    Task<CompanyRegistryCacheEntry?> GetAsync(
        string registryCode,
        string registrationNumber,
        CancellationToken cancellationToken);

    /// <summary>
    /// Mark a new entry as added. Caller drives <see cref="SeedWork.IUnitOfWork.SaveChangesAsync"/>;
    /// the adapter wraps the cache write + the same call's other state
    /// in one UoW commit.
    /// </summary>
    void Add(CompanyRegistryCacheEntry entry);
}
