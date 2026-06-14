namespace Makables.Core.Domain.Configuration;

/// <summary>
/// Write-time validation seam for <see cref="CountryConfiguration"/>
/// provider codes (T-0108). The entity does not know whether a code is
/// registered (role doc: "that's the DI container's concern"); this
/// abstraction surfaces the registered keys without leaking
/// <c>IServiceProvider</c> into <c>Core.AppServices</c>.
///
/// <para>
/// The Infra implementation probes the keyed container for payment +
/// shipping and falls back to a static known-codes set for email +
/// registry until they are keyed (T-0124).
/// </para>
/// </summary>
public interface IProviderRegistry
{
    /// <summary>
    /// The set of provider codes registered for <paramref name="kind"/>.
    /// Comparison is case-insensitive (provider codes are lowercase
    /// constants; admin input matches leniently).
    /// </summary>
    IReadOnlySet<string> GetRegisteredCodes(ProviderKind kind);
}
