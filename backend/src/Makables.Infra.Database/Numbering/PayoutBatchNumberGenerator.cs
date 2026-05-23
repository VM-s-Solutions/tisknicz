using System.Globalization;
using Makables.Core.Domain.Numbering;

namespace Makables.Infra.Database.Numbering;

/// <summary>
/// Derives the payout-batch number directly from the ISO 8601 week of the
/// supplied date. Format: <c>VYP-{CC}-{YYYY}-W{ww}</c>. No DB allocation.
/// Per ADR 0009. Uniqueness across batch creations is enforced by the
/// payout-batch table's unique index (added in T-0011 onwards).
/// </summary>
public sealed class PayoutBatchNumberGenerator : IPayoutBatchNumberGenerator
{
    public string For(string countryCode, DateOnly batchDate)
    {
        if (string.IsNullOrEmpty(countryCode) || countryCode.Length != 2)
        {
            throw new ArgumentException("CountryCode must be 2 chars.", nameof(countryCode));
        }

        var year = batchDate.Year;
        var week = ISOWeek.GetWeekOfYear(batchDate.ToDateTime(TimeOnly.MinValue));

        return string.Create(
            CultureInfo.InvariantCulture,
            $"VYP-{countryCode.ToUpperInvariant()}-{year:D4}-W{week:D2}");
    }
}
