using Makables.Core.Domain.Common;

namespace Makables.Core.Domain.Registry;

/// <summary>
/// Resolves the country-specific <see cref="ICompanyRegistry"/> by reading
/// <c>CountryConfiguration.DefaultRegistry</c>. Per ADR 0008 / patterns
/// §A.15 (keyed services + provider factory) — T-0124 migrates the ARES
/// adapter from direct DI onto the keyed pattern T-0065 introduced for
/// payments (CZ: "ares"; SK/PL/HU registries plug in as new keyed
/// adapters without touching the handlers).
///
/// <para>
/// Implementation caches the country-config lookup in <c>IMemoryCache</c>
/// for a short TTL — admin edits to country config are rare, maker
/// registration traffic is bursty around campaigns.
/// </para>
///
/// <para>
/// Failure modes:
/// <list type="bullet">
///   <item><description><see cref="BusinessErrorMessage.CountryConfigurationNotFound"/>
///     when the country code itself isn't seeded.</description></item>
///   <item><description><see cref="BusinessErrorMessage.CompanyRegistryNotRegistered"/>
///     when the country's <c>DefaultRegistry</c> code does not match any
///     registered keyed <see cref="ICompanyRegistry"/>.</description></item>
/// </list>
/// </para>
/// </summary>
public interface ICompanyRegistryFactory
{
    Task<BusinessResult<ICompanyRegistry>> ResolveAsync(
        string countryCode,
        CancellationToken cancellationToken);
}
