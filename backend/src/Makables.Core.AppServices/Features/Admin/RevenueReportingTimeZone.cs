using Makables.Core.Domain.Configuration;
using Microsoft.Extensions.Logging;

namespace Makables.Core.AppServices.Features.Admin;

/// <summary>
/// Resolves the civil timezone the admin revenue reports are bucketed in
/// (T-0192). Read off the country's <c>CountryConfiguration.TimeZoneId</c>
/// — never a hardcoded <c>"Europe/Prague"</c>, which CLAUDE.md forbids and
/// which would quietly report the wrong days the day a second country goes
/// live.
///
/// <para>
/// Both consumers need TWO forms of the answer: the
/// <see cref="TimeZoneInfo"/> for the C# bucket grid, and the raw IANA id
/// for the Postgres <c>date_trunc(field, timestamptz, zone)</c> call. They
/// are returned together so the two can never drift apart — a grid built
/// in one zone and buckets aggregated in another would misalign every point
/// on the chart.
/// </para>
///
/// <para>
/// A missing country row or an unusable id degrades to UTC with a warning
/// rather than failing the read. The dashboard is an operational readout:
/// showing the numbers with boundaries an hour or two off is far better
/// than a 500, and the warning is what gets the seed fixed. Nothing here
/// touches money — only which bucket it lands in.
/// </para>
/// </summary>
internal static class RevenueReportingTimeZone
{
    /// <summary>Postgres and .NET both accept this id, so the fallback needs no special-casing.</summary>
    private const string UtcId = "UTC";

    /// <param name="config">The country's configuration row, or <c>null</c> when it isn't seeded.</param>
    /// <param name="countryCode">Only for the warning — never used to branch on country.</param>
    /// <param name="logger">Caller's logger, so the warning is attributed to the handler that hit it.</param>
    public static (string Id, TimeZoneInfo Zone) Resolve(
        CountryConfiguration? config, string countryCode, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        if (config is null || string.IsNullOrWhiteSpace(config.TimeZoneId))
        {
            logger.LogWarning(
                "Revenue reporting: no usable CountryConfiguration timezone for {CountryCode}; bucketing in UTC.",
                countryCode);
            return (UtcId, TimeZoneInfo.Utc);
        }

        try
        {
            return (config.TimeZoneId, TimeZoneInfo.FindSystemTimeZoneById(config.TimeZoneId));
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            logger.LogWarning(
                ex,
                "Revenue reporting: CountryConfiguration for {CountryCode} names an unusable timezone; bucketing in UTC.",
                countryCode);
            return (UtcId, TimeZoneInfo.Utc);
        }
    }
}
