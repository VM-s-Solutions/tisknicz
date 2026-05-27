namespace Makables.Core.Domain.Registry;

/// <summary>
/// Owns DB-cache reads + writes per ADR 0018 §"Caching policy", isolated
/// from the request-scoped <see cref="SeedWork.IUnitOfWork"/>.
///
/// <para>
/// <b>Why a dedicated store, not the standard repository pattern</b>
/// (T-0032 sec reviewer M-1 / CQ reviewer m-4): the registry adapter is
/// called mid-command (e.g. inside T-0033's <c>RegisterMaker</c>
/// handler). If it shared the caller's scoped DbContext + called
/// <c>SaveChangesAsync</c>, it would flush ANY tracked-but-uncommitted
/// changes the handler had made — silently committing half-built
/// aggregates regardless of whether the outer command later succeeded.
/// </para>
/// <para>
/// The store implementation uses its own <c>IDbContextFactory</c>-built
/// DbContext per call so the cache write commits in isolation. The
/// caller's request-scoped context is untouched.
/// </para>
/// </summary>
public interface ICompanyRegistryCacheStore
{
    /// <summary>
    /// Read the row by composite key regardless of expiry — the adapter
    /// decides whether to treat a stale row as "fresh enough" or skip.
    /// Returns <c>null</c> if no row.
    /// </summary>
    Task<CompanyRegistryCacheEntry?> GetAsync(
        string registryCode,
        string registrationNumber,
        CancellationToken cancellationToken);

    /// <summary>
    /// Insert-or-update the row for (<paramref name="registryCode"/>,
    /// <paramref name="registrationNumber"/>) with the given payload +
    /// timestamps. Commits in its own DbContext scope so the caller's
    /// tracked changes are NEVER flushed by this call.
    /// </summary>
    Task UpsertAsync(
        string registryCode,
        string registrationNumber,
        string payloadJson,
        DateTimeOffset fetchedAt,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken);
}
