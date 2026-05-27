using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Makables.Core.Domain.Addresses.Validators;
using Makables.Core.Domain.Configuration;

namespace Makables.Infra.Common.Addresses;

/// <summary>
/// Reads <see cref="CountryConfiguration.ZipFormat"/> per country and
/// applies it as a compiled regex. Per ADR 0010 §"Per-country format
/// validation".
///
/// Compiled regexes are cached in-process by (countryCodeIso, pattern)
/// so repeated calls during a form submission do not pay the compile
/// cost twice. The cache is keyed by the pattern string too — if an
/// admin edits <see cref="CountryConfiguration.ZipFormat"/> at runtime,
/// the new pattern simply gets its own cache slot; the old one stays
/// behind but is harmless (no eviction needed at MVP-scale country
/// counts).
///
/// A 200 ms regex timeout guards against catastrophic backtracking in
/// a pathological admin-supplied pattern.
/// </summary>
public sealed class ConfigurationDrivenAddressFormatValidator(
    ICountryConfigurationRepository countryConfig) : IAddressFormatValidator
{
    private static readonly ConcurrentDictionary<string, Regex> _regexCache = new();
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);

    public async Task<bool> IsValidZipAsync(
        string countryCodeIso, string zip, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(countryCodeIso)) return false;
        if (string.IsNullOrWhiteSpace(zip)) return false;

        var config = await countryConfig.GetByCodeAsync(countryCodeIso.ToUpperInvariant(), cancellationToken);
        // No row, or no ZIP rule on the row → accept. ADR 0010: countries
        // without a configured format are valid by default during their
        // soft-launch window before the seed lands.
        if (config is null || string.IsNullOrWhiteSpace(config.ZipFormat)) return true;

        var regex = _regexCache.GetOrAdd(
            $"{countryCodeIso.ToUpperInvariant()}|{config.ZipFormat}",
            _ => new Regex(config.ZipFormat, RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout));

        try
        {
            return regex.IsMatch(zip.Trim());
        }
        catch (RegexMatchTimeoutException)
        {
            // The pattern itself is admin-controlled; a timeout means the
            // pattern is pathological (or the input is). Treat as fail
            // closed — better to reject a single submission than to lock
            // up the request thread.
            return false;
        }
    }
}
