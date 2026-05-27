namespace Makables.Core.Domain.Addresses.Validators;

/// <summary>
/// Per-country address format checks per ADR 0010 §"Per-country format
/// validation". Today only ZIP is country-variant; other format rules
/// (phone number shape, IČO check digit) live on dedicated validators
/// per their own ADRs. The implementation reads <c>CountryConfiguration.ZipFormat</c>
/// regex and applies it; null/empty regex means "no rule, accept" — so
/// countries without a configured format pass through (good for future
/// soft-launch in a new market before the seed lands).
///
/// Implementation: <c>Makables.Infra.Common/Addresses/ConfigurationDrivenAddressFormatValidator.cs</c>.
/// </summary>
public interface IAddressFormatValidator
{
    /// <summary>
    /// True if <paramref name="zip"/> matches the ZIP format regex
    /// configured for <paramref name="countryCodeIso"/>, OR no regex is
    /// configured for that country.
    ///
    /// <para>Returns false in three cases:</para>
    /// <list type="bullet">
    ///   <item><description><paramref name="countryCodeIso"/> is null, empty, or whitespace.</description></item>
    ///   <item><description><paramref name="zip"/> is null, empty, or whitespace.</description></item>
    ///   <item><description>A regex IS configured for the country and the zip fails to match.</description></item>
    /// </list>
    ///
    /// <para>
    /// In practice the FluentValidation <c>NotEmpty</c> rule on the
    /// surrounding command catches blank inputs first, so this method
    /// only sees pre-validated values at runtime. The blank-input
    /// short-circuit is defence in depth (T-0030 Copilot review).
    /// </para>
    /// </summary>
    /// <param name="countryCodeIso">ISO 3166-1 alpha-2.</param>
    /// <param name="zip">User-supplied ZIP value (trimmed).</param>
    Task<bool> IsValidZipAsync(string countryCodeIso, string zip, CancellationToken cancellationToken);
}
