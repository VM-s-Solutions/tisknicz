using Makables.Core.Domain.Common;
using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Shipping;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Makables.Infra.Clients.Packeta;

/// <summary>
/// Resolves the country-specific <see cref="IShippingCarrier"/> per
/// ADR 0008 / patterns §A.15. Reads
/// <c>CountryConfiguration.DefaultShippingCarrier</c> and returns the
/// matching keyed service. Mirror of
/// <see cref="Comgate.PaymentProviderFactory"/>.
/// </summary>
public sealed class ShippingCarrierFactory(
    IServiceProvider services,
    ICountryConfigurationRepository countryConfigurations,
    IMemoryCache cache,
    ILogger<ShippingCarrierFactory> logger) : IShippingCarrierFactory
{
    /// <summary>5-minute TTL on the country → carrier-code lookup.</summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    public async Task<BusinessResult<IShippingCarrier>> ResolveAsync(
        string countryCode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(countryCode))
        {
            return BusinessResult.Failure<IShippingCarrier>(
                Error.Configuration(BusinessErrorMessage.ShippingCarrierConfigurationError));
        }

        var normalised = countryCode.Trim().ToUpperInvariant();
        var cacheKey = CacheKey(normalised);

        if (!cache.TryGetValue(cacheKey, out string? carrierCode) || carrierCode is null)
        {
            var config = await countryConfigurations.GetByCodeAsync(normalised, cancellationToken);
            if (config is null)
            {
                logger.LogWarning(
                    "ShippingCarrierFactory.Resolve: country configuration for {CountryCode} is missing.",
                    normalised);
                return BusinessResult.Failure<IShippingCarrier>(
                    Error.Configuration(BusinessErrorMessage.ShippingCarrierConfigurationError));
            }

            carrierCode = config.DefaultShippingCarrier;
            cache.Set(cacheKey, carrierCode, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = CacheTtl,
            });
        }

        if (string.IsNullOrWhiteSpace(carrierCode))
        {
            logger.LogError(
                "ShippingCarrierFactory.Resolve: country {CountryCode} has empty DefaultShippingCarrier.",
                normalised);
            return BusinessResult.Failure<IShippingCarrier>(
                Error.Configuration(BusinessErrorMessage.ShippingCarrierConfigurationError));
        }

        var carrier = services.GetKeyedService<IShippingCarrier>(carrierCode);
        if (carrier is null)
        {
            logger.LogError(
                "ShippingCarrierFactory.Resolve: no IShippingCarrier registered for code '{CarrierCode}' (country={CountryCode}).",
                carrierCode, normalised);
            return BusinessResult.Failure<IShippingCarrier>(
                Error.Configuration(BusinessErrorMessage.ShippingCarrierConfigurationError));
        }

        return BusinessResult.Success(carrier);
    }

    private static string CacheKey(string countryCode) => $"shipping-carrier:{countryCode}";
}
