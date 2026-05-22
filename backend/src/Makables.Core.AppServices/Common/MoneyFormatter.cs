using System.Globalization;
using Makables.Core.Domain.Money;

namespace Makables.Core.AppServices.Common;

/// <summary>
/// Locale-aware money display. CZ display strips haléře per ADR 0003
/// (Czech retail rounds to whole CZK). Other locales render minor units
/// per <see cref="CultureInfo"/> defaults.
/// </summary>
public static class MoneyFormatter
{
    public static string Format(Money money, string locale = "cs-CZ")
    {
        var culture = CultureInfo.GetCultureInfo(locale);

        // Convert minor units to major as decimal for the formatter.
        var major = (decimal)money.AmountMinor / 100m;

        if (string.Equals(money.Currency, "CZK", StringComparison.OrdinalIgnoreCase))
        {
            // CZ display: whole CZK, "1 234 Kč" (space thousands separator, Kč suffix).
            var rounded = decimal.ToInt64(Math.Round(major, MidpointRounding.AwayFromZero));
            return string.Create(culture, $"{rounded:N0} Kč");
        }

        // Default: locale's currency format with the actual currency code.
        // We set NumberFormat.CurrencySymbol explicitly so the result reflects
        // the Money's currency, not the locale's default.
        var nf = (NumberFormatInfo)culture.NumberFormat.Clone();
        nf.CurrencySymbol = ResolveSymbol(money.Currency);
        return major.ToString("C", nf);
    }

    private static string ResolveSymbol(string currency) => currency.ToUpperInvariant() switch
    {
        "CZK" => "Kč",
        "EUR" => "€",
        "PLN" => "zł",
        "HUF" => "Ft",
        _ => currency,
    };
}
