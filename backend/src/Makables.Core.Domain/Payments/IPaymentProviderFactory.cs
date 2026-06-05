using Makables.Core.Domain.Common;

namespace Makables.Core.Domain.Payments;

/// <summary>
/// Resolves the country-specific <see cref="IPaymentProvider"/> by reading
/// <c>CountryConfiguration.DefaultPaymentProvider</c>. Per ADR 0008 / patterns
/// §A.15 (keyed services + provider factory).
///
/// <para>
/// Implementation caches the country-config lookup in <see cref="IMemoryCache"/>
/// for a short TTL — admin edits to country config are rare, payment-session
/// traffic is hot.
/// </para>
///
/// <para>
/// Failure modes:
/// <list type="bullet">
///   <item><description><see cref="BusinessErrorMessage.CountryConfigurationNotFound"/>
///     when the country code itself isn't seeded.</description></item>
///   <item><description><see cref="BusinessErrorMessage.PaymentProviderNotRegistered"/>
///     when the country's <c>DefaultPaymentProvider</c> code does not match
///     any registered keyed <see cref="IPaymentProvider"/>.</description></item>
/// </list>
/// </para>
/// </summary>
public interface IPaymentProviderFactory
{
    Task<BusinessResult<IPaymentProvider>> ResolveAsync(
        string countryCode,
        CancellationToken cancellationToken);
}
