using Makables.Core.Domain.Common;
using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Email;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Makables.Infra.Clients.SendGrid;

/// <summary>
/// Resolves the country-specific <see cref="IEmailProvider"/> per
/// ADR 0008 / patterns §A.15 (T-0124). Reads
/// <c>CountryConfiguration.DefaultEmailProvider</c> and returns the
/// matching keyed service. Mirrors
/// <see cref="Comgate.PaymentProviderFactory"/> line-for-line so the four
/// provider seams read identically.
///
/// <para>
/// The MVP send path (<c>EmailSendService</c>) still consumes the
/// unkeyed <see cref="IEmailProvider"/> alias — the outbox payloads
/// carry no recipient country yet (see
/// <see cref="IEmailProviderFactory"/> §Send-path note). This factory is
/// the seam that call sites switch to once payloads carry a country.
/// </para>
/// </summary>
public sealed class EmailProviderFactory(
    IServiceProvider services,
    ICountryConfigurationRepository countryConfigurations,
    IMemoryCache cache,
    ILogger<EmailProviderFactory> logger) : IEmailProviderFactory
{
    /// <summary>5-minute TTL on the country → provider-code lookup.</summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    public async Task<BusinessResult<IEmailProvider>> ResolveAsync(
        string countryCode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(countryCode))
        {
            return BusinessResult.Failure<IEmailProvider>(
                Error.NotFound("countryCode", BusinessErrorMessage.CountryConfigurationNotFound));
        }

        var normalised = countryCode.Trim().ToUpperInvariant();
        var cacheKey = CacheKey(normalised);

        if (!cache.TryGetValue(cacheKey, out string? providerCode) || providerCode is null)
        {
            var config = await countryConfigurations.GetByCodeAsync(normalised, cancellationToken);
            if (config is null)
            {
                logger.LogWarning(
                    "EmailProviderFactory.Resolve: country configuration for {CountryCode} is missing.",
                    normalised);
                return BusinessResult.Failure<IEmailProvider>(
                    Error.NotFound("countryCode", BusinessErrorMessage.CountryConfigurationNotFound));
            }

            providerCode = config.DefaultEmailProvider;
            cache.Set(cacheKey, providerCode, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = CacheTtl,
            });
        }

        if (string.IsNullOrWhiteSpace(providerCode))
        {
            logger.LogError(
                "EmailProviderFactory.Resolve: country {CountryCode} has empty DefaultEmailProvider.",
                normalised);
            return BusinessResult.Failure<IEmailProvider>(
                Error.Configuration(BusinessErrorMessage.EmailProviderNotRegistered));
        }

        var provider = services.GetKeyedService<IEmailProvider>(providerCode);
        if (provider is null)
        {
            logger.LogError(
                "EmailProviderFactory.Resolve: no IEmailProvider registered for provider code '{ProviderCode}' (country={CountryCode}).",
                providerCode, normalised);
            return BusinessResult.Failure<IEmailProvider>(
                Error.Configuration(BusinessErrorMessage.EmailProviderNotRegistered));
        }

        return BusinessResult.Success(provider);
    }

    private static string CacheKey(string countryCode) => $"email-provider:{countryCode}";
}
