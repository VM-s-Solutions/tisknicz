using Makables.Core.Domain.Common;
using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Registry;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Makables.Infra.Clients.Ares;

/// <summary>
/// Resolves the country-specific <see cref="ICompanyRegistry"/> per
/// ADR 0008 / patterns §A.15 (T-0124). Reads
/// <c>CountryConfiguration.DefaultRegistry</c> and returns the matching
/// keyed service. Mirrors <see cref="Comgate.PaymentProviderFactory"/>
/// line-for-line — same 5-minute code-only cache, same failure taxonomy —
/// so the four provider seams (payments, shipping, registry, email) read
/// identically.
///
/// <para>
/// Lives in <c>Infra.Clients/Ares/</c> at MVP because ARES is the only
/// registry. The factory is registry-agnostic; a second country's
/// registry adapter registers under its own key and this file moves to a
/// shared folder then.
/// </para>
/// </summary>
public sealed class CompanyRegistryFactory(
    IServiceProvider services,
    ICountryConfigurationRepository countryConfigurations,
    IMemoryCache cache,
    ILogger<CompanyRegistryFactory> logger) : ICompanyRegistryFactory
{
    /// <summary>5-minute TTL on the country → registry-code lookup.</summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    public async Task<BusinessResult<ICompanyRegistry>> ResolveAsync(
        string countryCode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(countryCode))
        {
            return BusinessResult.Failure<ICompanyRegistry>(
                Error.NotFound("countryCode", BusinessErrorMessage.CountryConfigurationNotFound));
        }

        var normalised = countryCode.Trim().ToUpperInvariant();
        var cacheKey = CacheKey(normalised);

        // Cache just the registry code (a tiny string), never the scoped
        // ICompanyRegistry instance — caching a scoped service across
        // requests would be a lifetime smell (PaymentProviderFactory
        // precedent).
        if (!cache.TryGetValue(cacheKey, out string? registryCode) || registryCode is null)
        {
            var config = await countryConfigurations.GetByCodeAsync(normalised, cancellationToken);
            if (config is null)
            {
                logger.LogWarning(
                    "CompanyRegistryFactory.Resolve: country configuration for {CountryCode} is missing.",
                    normalised);
                return BusinessResult.Failure<ICompanyRegistry>(
                    Error.NotFound("countryCode", BusinessErrorMessage.CountryConfigurationNotFound));
            }

            registryCode = config.DefaultRegistry;
            cache.Set(cacheKey, registryCode, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = CacheTtl,
            });
        }

        if (string.IsNullOrWhiteSpace(registryCode))
        {
            logger.LogError(
                "CompanyRegistryFactory.Resolve: country {CountryCode} has empty DefaultRegistry.",
                normalised);
            return BusinessResult.Failure<ICompanyRegistry>(
                Error.Configuration(BusinessErrorMessage.CompanyRegistryNotRegistered));
        }

        var registry = services.GetKeyedService<ICompanyRegistry>(registryCode);
        if (registry is null)
        {
            logger.LogError(
                "CompanyRegistryFactory.Resolve: no ICompanyRegistry registered for registry code '{RegistryCode}' (country={CountryCode}).",
                registryCode, normalised);
            return BusinessResult.Failure<ICompanyRegistry>(
                Error.Configuration(BusinessErrorMessage.CompanyRegistryNotRegistered));
        }

        return BusinessResult.Success(registry);
    }

    private static string CacheKey(string countryCode) => $"company-registry:{countryCode}";
}
