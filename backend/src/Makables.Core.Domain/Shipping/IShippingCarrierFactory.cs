using Makables.Core.Domain.Common;

namespace Makables.Core.Domain.Shipping;

/// <summary>
/// Resolves the country-specific <see cref="IShippingCarrier"/> by reading
/// <c>CountryConfiguration.DefaultShippingCarrier</c>. Per ADR 0008 /
/// patterns §A.15 (keyed services + provider factory). Mirror of
/// <see cref="Payments.IPaymentProviderFactory"/>.
///
/// <para>
/// Implementation caches the country-config lookup in <c>IMemoryCache</c>
/// for a 5-minute TTL — admin edits to country config are rare, and
/// checkout / ship traffic is hot. An admin "fix the wrong carrier for CZ"
/// change propagates within the TTL window.
/// </para>
///
/// <para>
/// Failure modes:
/// <list type="bullet">
///   <item><description><see cref="BusinessErrorMessage.CountryConfigurationNotFound"/>
///     when the country code itself isn't seeded.</description></item>
///   <item><description><see cref="BusinessErrorMessage.ShippingCarrierConfigurationError"/>
///     when the country's <c>DefaultShippingCarrier</c> code does not match
///     any registered keyed <see cref="IShippingCarrier"/>.</description></item>
/// </list>
/// </para>
/// </summary>
public interface IShippingCarrierFactory
{
    Task<BusinessResult<IShippingCarrier>> ResolveAsync(
        string countryCode,
        CancellationToken cancellationToken);
}
