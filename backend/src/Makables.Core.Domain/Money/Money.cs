using System.Globalization;

namespace Makables.Core.Domain.Money;

/// <summary>
/// A monetary amount in a specific currency, stored as integer minor units
/// (haléře for CZK, cents for EUR, whole forint for HUF where the minor
/// unit is defunct). Per ADR 0003 and patterns §A.18.
///
/// Currency-aware arithmetic: <see cref="Add"/>, <see cref="Subtract"/>,
/// and <see cref="PercentOfBp"/> throw <see cref="InvalidOperationException"/>
/// on currency mismatch. Mixing currencies is a programmer error, not an
/// expected failure.
/// </summary>
public readonly record struct Money(long AmountMinor, string Currency)
{
    public static Money Of(long amountMinor, string currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new ArgumentException("Currency must be a non-empty ISO 4217 code.", nameof(currency));
        }

        if (currency.Length != 3)
        {
            throw new ArgumentException(
                $"Currency must be 3 characters; got '{currency}'.",
                nameof(currency));
        }

        return new Money(amountMinor, currency.ToUpperInvariant());
    }

    public static Money CZK(long minor) => new(minor, "CZK");

    public static Money Zero(string currency) => Of(0L, currency);

    public Money Add(Money other)
    {
        AssertSameCurrency(other);
        return new Money(checked(AmountMinor + other.AmountMinor), Currency);
    }

    public Money Subtract(Money other)
    {
        AssertSameCurrency(other);
        return new Money(checked(AmountMinor - other.AmountMinor), Currency);
    }

    /// <summary>
    /// Multiply by a percentage expressed in basis points (1500 = 15%).
    /// Half-up rounding to the nearest whole minor unit.
    /// </summary>
    public Money PercentOfBp(int basisPoints)
    {
        // decimal for the intermediate to avoid floating-point drift.
        var rounded = decimal.ToInt64(
            Math.Round(
                (decimal)AmountMinor * basisPoints / 10_000m,
                MidpointRounding.AwayFromZero));
        return new Money(rounded, Currency);
    }

    public int CompareTo(Money other)
    {
        AssertSameCurrency(other);
        return AmountMinor.CompareTo(other.AmountMinor);
    }

    private void AssertSameCurrency(Money other)
    {
        if (!string.Equals(Currency, other.Currency, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Currency mismatch: {Currency} vs {other.Currency}. " +
                "Cross-currency arithmetic is not allowed.");
        }
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{AmountMinor} {Currency} (minor)");
}
