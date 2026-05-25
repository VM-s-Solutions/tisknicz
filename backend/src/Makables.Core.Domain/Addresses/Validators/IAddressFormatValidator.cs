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
    /// configured for that country. False only when a regex IS configured
    /// and the zip fails to match.
    /// </summary>
    /// <param name="countryCodeIso">ISO 3166-1 alpha-2.</param>
    /// <param name="zip">User-supplied ZIP value (trimmed).</param>
    Task<bool> IsValidZipAsync(string countryCodeIso, string zip, CancellationToken cancellationToken);
}
