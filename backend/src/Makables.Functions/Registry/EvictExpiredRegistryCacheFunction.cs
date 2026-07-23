using Makables.Core.Domain.Common;
using Makables.Core.Domain.Registry;
using Makables.Infra.Clients.Ares;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Makables.Functions.Registry;

/// <summary>
/// Timer-triggered Function that evicts expired ARES cache rows so the
/// <c>company_registry_cache</c> table does not grow unbounded (T-0113,
/// ADR 0018 §"Caching policy" — the eviction job the
/// <see cref="CompanyRegistryCacheEntry"/> XML-doc explicitly defers to
/// this Function).
///
/// <para>
/// A row is usable as a stale fallback only while
/// <c>FetchedAt &gt; now - StaleFallbackDays</c> (see
/// <see cref="AresCompanyRegistry"/>). Past that window it can never be
/// served, so this job deletes it. The eviction cutoff is derived from
/// the SAME <see cref="AresOptions.StaleFallbackDays"/> the read path
/// uses, so the two can never drift.
/// </para>
///
/// <para>
/// The Function stays thin: it computes the cutoff and calls
/// <see cref="ICompanyRegistryCacheStore.EvictFetchedBeforeAsync"/> —
/// the set-based delete and its own-DbContext isolation live in the
/// store (no request UoW, no <c>SaveChangesAsync</c> in a handler).
/// </para>
///
/// <para>
/// <b>Schedule:</b> daily 02:30 UTC — off-peak and offset from T-0083's
/// <c>CancelExpiredPendingPaymentOrders</c> (02:00) so the two nightly
/// cleanup jobs do not fire simultaneously (the codebase's load-spreading
/// convention). Configured via the <c>EvictExpiredRegistryCache:Schedule</c>
/// app setting.
/// </para>
/// </summary>
public sealed class EvictExpiredRegistryCacheFunction(
    ICompanyRegistryCacheStore cacheStore,
    IOptions<AresOptions> aresOptions,
    IClock clock,
    ILogger<EvictExpiredRegistryCacheFunction> logger)
{
    public const string FunctionName = "EvictExpiredRegistryCache";

    [Function(FunctionName)]
    public async Task RunAsync(
        [TimerTrigger("%EvictExpiredRegistryCache:Schedule%")] TimerInfo timer,
        CancellationToken cancellationToken)
    {
        // Mirror the read path's clamp (AresCompanyRegistry uses
        // Math.Max(1, StaleFallbackDays)) so a misconfigured 0/negative
        // value can never evict still-usable rows.
        var staleFallbackDays = Math.Max(1, aresOptions.Value.StaleFallbackDays);
        var fetchedBefore = clock.UtcNow - TimeSpan.FromDays(staleFallbackDays);

        var evicted = await cacheStore.EvictFetchedBeforeAsync(fetchedBefore, cancellationToken);

        logger.LogInformation(
            "EvictExpiredRegistryCache completed: removed {Evicted} row(s) fetched before {FetchedBefore:o} ({StaleFallbackDays}-day stale-fallback window).",
            evicted, fetchedBefore, staleFallbackDays);
    }
}
