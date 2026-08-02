using Makables.Core.Domain.Common;
using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Payments;
using Makables.Infra.Clients.Dev;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Makables.Infra.Clients.Comgate;

/// <summary>
/// Resolves the country-specific <see cref="IPaymentProvider"/> per
/// ADR 0008 / patterns §A.15. Reads
/// <c>CountryConfiguration.DefaultPaymentProvider</c> and returns the
/// matching keyed service.
///
/// <para>
/// <b>Caching.</b> The country-config lookup is cached in
/// <see cref="IMemoryCache"/> with a 5-minute sliding TTL — admin edits
/// to country config are rare and payment-session traffic is hot. An
/// admin "fix the wrong provider for CZ" change propagates within the
/// TTL window.
/// </para>
///
/// <para>
/// <b>Failure modes.</b> Returns
/// <see cref="BusinessErrorMessage.CountryConfigurationNotFound"/> when
/// the country code isn't seeded, and
/// <see cref="BusinessErrorMessage.PaymentProviderNotRegistered"/> when
/// the configured provider code does not match any keyed
/// <see cref="IPaymentProvider"/> registration.
/// </para>
///
/// <para>
/// Lives in <c>Infra.Clients/Comgate/</c> at MVP because Comgate is the
/// only provider. The factory is provider-agnostic and will stay here
/// until the second provider lands; T-0124 may pull it into a shared
/// <c>Infra.Clients/Payments/</c> folder then.
/// </para>
/// </summary>
public sealed class PaymentProviderFactory(
    IServiceProvider services,
    ICountryConfigurationRepository countryConfigurations,
    IMemoryCache cache,
    IOptions<DevPaymentOptions> devPayments,
    ILogger<PaymentProviderFactory> logger) : IPaymentProviderFactory
{
    /// <summary>5-minute TTL on the country → provider-code lookup.</summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    public async Task<BusinessResult<IPaymentProvider>> ResolveAsync(
        string countryCode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(countryCode))
        {
            return BusinessResult.Failure<IPaymentProvider>(
                Error.NotFound("countryCode", BusinessErrorMessage.CountryConfigurationNotFound));
        }

        // Non-production bypass. Deliberately overrides the country's
        // configured provider instead of being driven by it: dev and
        // production share the same seeded CountryConfiguration rows, so
        // encoding "dev" in the data would either fork the seed per
        // environment or risk shipping a bypassed provider code to
        // production. The switch is an app setting scoped to the dev
        // environment (see infra/bicep/main.bicep), and it fails LOUD
        // rather than silently falling back to the real gateway — an
        // enabled flag with no registration is a broken deploy, not a
        // reason to start charging cards on a test environment.
        if (devPayments.Value.Enabled)
        {
            var devProvider = services.GetKeyedService<IPaymentProvider>(DevPaymentProvider.ProviderCode);
            if (devProvider is null)
            {
                logger.LogError(
                    "PaymentProviderFactory.Resolve: Payments:Dev:Enabled is true but no '{ProviderCode}' provider is registered.",
                    DevPaymentProvider.ProviderCode);
                return BusinessResult.Failure<IPaymentProvider>(
                    Error.Configuration(BusinessErrorMessage.PaymentProviderNotRegistered));
            }

            logger.LogWarning(
                "PaymentProviderFactory.Resolve: dev payment bypass active for {CountryCode} — the real gateway is NOT being used.",
                countryCode);
            return BusinessResult.Success(devProvider);
        }

        var normalised = countryCode.Trim().ToUpperInvariant();
        var cacheKey = CacheKey(normalised);

        // Cached lookup: we cache just the provider code (a tiny string),
        // not the IPaymentProvider instance — the provider is scoped and
        // caching a scoped service across requests would be a lifetime
        // smell. Each call still resolves the keyed service from the
        // current scope's provider.
        if (!cache.TryGetValue(cacheKey, out string? providerCode) || providerCode is null)
        {
            var config = await countryConfigurations.GetByCodeAsync(normalised, cancellationToken);
            if (config is null)
            {
                logger.LogWarning(
                    "PaymentProviderFactory.Resolve: country configuration for {CountryCode} is missing.",
                    normalised);
                return BusinessResult.Failure<IPaymentProvider>(
                    Error.NotFound("countryCode", BusinessErrorMessage.CountryConfigurationNotFound));
            }

            providerCode = config.DefaultPaymentProvider;
            cache.Set(cacheKey, providerCode, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = CacheTtl,
            });
        }

        if (string.IsNullOrWhiteSpace(providerCode))
        {
            logger.LogError(
                "PaymentProviderFactory.Resolve: country {CountryCode} has empty DefaultPaymentProvider.",
                normalised);
            return BusinessResult.Failure<IPaymentProvider>(
                Error.Configuration(BusinessErrorMessage.PaymentProviderNotRegistered));
        }

        var provider = services.GetKeyedService<IPaymentProvider>(providerCode);
        if (provider is null)
        {
            logger.LogError(
                "PaymentProviderFactory.Resolve: no IPaymentProvider registered for provider code '{ProviderCode}' (country={CountryCode}).",
                providerCode, normalised);
            return BusinessResult.Failure<IPaymentProvider>(
                Error.Configuration(BusinessErrorMessage.PaymentProviderNotRegistered));
        }

        return BusinessResult.Success(provider);
    }

    private static string CacheKey(string countryCode) => $"payment-provider:{countryCode}";
}
