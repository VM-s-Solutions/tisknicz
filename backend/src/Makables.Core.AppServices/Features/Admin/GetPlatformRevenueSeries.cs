using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.AppServices.Features.Auth;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Orders.Queries;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Makables.Core.AppServices.Features.Admin;

/// <summary>
/// Platform revenue as a time series (T-0192) — the admin overview's
/// "how are sales moving" chart. Same recognition rule, same unscoped
/// admin-host-only boundary and same no-audit read as
/// <see cref="GetPlatformRevenue"/>; the only difference is that the money
/// arrives split into buckets instead of one total.
///
/// <para>
/// A series summed over its buckets equals the single-number read for the
/// same span. That is a property of sharing <c>PaidAt</c> and the earned
/// states, not a coincidence, and it is what lets the chart sit next to the
/// tiles without the operator having to reconcile two different answers.
/// </para>
///
/// <para>
/// <b>Every range returns a comparable number of points</b> (12–92), because
/// the bucket width scales with the span — a day is read hour by hour, a
/// year month by month. That keeps one chart component honest across the
/// whole ladder and keeps the payload small: the widest range on offer is 92
/// rows, so the read is bounded no matter how large the orders table grows.
/// </para>
///
/// <para>
/// Empty buckets are filled in HERE rather than by the database, which only
/// knows about buckets that contain orders. A line chart that skips its
/// empty days draws a straight run between two distant points and reads as
/// steady trade during a week with no sales at all — the exact opposite of
/// the truth.
/// </para>
/// </summary>
public static class GetPlatformRevenueSeries
{
    /// <summary>
    /// How far back the chart looks. Spans a single day to a full year, the
    /// range a price chart offers, because the operator's two questions —
    /// "what is happening right now" and "is the business growing" — sit at
    /// opposite ends of that scale.
    ///
    /// <para>
    /// Every member is TRAILING from now, not calendar-aligned:
    /// <see cref="Month"/> is the last 30 days, not August. The
    /// calendar-aligned question is what <see cref="GetPlatformRevenue"/>
    /// answers, and the two surfaces are deliberately different — a trend
    /// line that reset to one point on the first of each month would be
    /// useless.
    /// </para>
    /// </summary>
    public enum RevenueRange
    {
        /// <summary>Last 24 hours, hour by hour.</summary>
        Day = 0,

        /// <summary>Last 7 days, day by day.</summary>
        Week = 1,

        /// <summary>Last 30 days, day by day.</summary>
        Month = 2,

        /// <summary>Last 90 days, day by day.</summary>
        Quarter = 3,

        /// <summary>Last 26 weeks, week by week.</summary>
        HalfYear = 4,

        /// <summary>Last 12 months, month by month.</summary>
        Year = 5,
    }

    public sealed record Query(RevenueRange Range) : IQuery<GetPlatformRevenueSeriesResponse>;

    /// <summary>
    /// One point on the series. Carries every money line rather than just the
    /// one being plotted, so switching which measure the chart shows is a
    /// client-side re-render instead of another round-trip — and so the four
    /// lines can never come from four differently-timed reads.
    /// </summary>
    /// <param name="BucketStart">Instant the bucket opens (inclusive).</param>
    /// <param name="PaidOrderCount">Orders whose payment cleared in the bucket and was not reversed.</param>
    /// <param name="GrossVolumeMinor">What customers were charged, minor units.</param>
    /// <param name="PlatformFeeMinor">What the platform earned, minor units.</param>
    /// <param name="MakerPayoutMinor">What the makers are owed, minor units.</param>
    /// <param name="RefundedMinor">Gross refunded on orders paid in the bucket, minor units.</param>
    public sealed record PlatformRevenuePointDto(
        DateTimeOffset BucketStart,
        int PaidOrderCount,
        long GrossVolumeMinor,
        long PlatformFeeMinor,
        long MakerPayoutMinor,
        long RefundedMinor);

    /// <summary>Globally-unique name (NSwag PR #38 convention).</summary>
    /// <param name="Range">Echoed back so the caller can confirm which span it is reading.</param>
    /// <param name="Granularity">Bucket width the range resolved to — the caller labels its axis from this, it does not guess.</param>
    /// <param name="FromInclusive">Start of the first bucket (inclusive).</param>
    /// <param name="ToExclusive">End of the span (exclusive) — "now" at read time, so the last bucket is partial.</param>
    /// <param name="Currency">ISO 4217 code every amount is denominated in. CZK at launch.</param>
    /// <param name="TimeZoneId">
    /// IANA id of the civil calendar the buckets were truncated in. Sent so
    /// the caller can LABEL a bucket in the same calendar it was computed in:
    /// a bucket start is an instant, and a browser in another timezone that
    /// formatted it locally would draw a chart whose axis disagreed with its
    /// own data by an hour or two.
    /// </param>
    /// <param name="Points">Ascending by <c>BucketStart</c>, gap-free.</param>
    public sealed record GetPlatformRevenueSeriesResponse(
        RevenueRange Range,
        RevenueBucketGranularity Granularity,
        DateTimeOffset FromInclusive,
        DateTimeOffset ToExclusive,
        string Currency,
        string TimeZoneId,
        IReadOnlyList<PlatformRevenuePointDto> Points);

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            RuleFor(q => q.Range)
                .Cascade(CascadeMode.Stop)
                .IsInEnum()
                .WithErrorCode(BusinessErrorMessage.InvalidEnumValue);
        }
    }

    /// <summary>
    /// The single place a range becomes a bucket width and a point count.
    /// Both halves belong together — changing one without the other silently
    /// changes the span the chart covers — so they are returned as a pair
    /// and never derived separately.
    /// </summary>
    private static (RevenueBucketGranularity Granularity, int BucketCount) LadderFor(RevenueRange range) =>
        range switch
        {
            RevenueRange.Day => (RevenueBucketGranularity.Hour, 24),
            RevenueRange.Week => (RevenueBucketGranularity.Day, 7),
            RevenueRange.Month => (RevenueBucketGranularity.Day, 30),
            RevenueRange.Quarter => (RevenueBucketGranularity.Day, 90),
            RevenueRange.HalfYear => (RevenueBucketGranularity.Week, 26),
            RevenueRange.Year => (RevenueBucketGranularity.Month, 12),
            // Unreachable past the Validator; falling back to the narrowest
            // range keeps a future enum member cheap rather than expensive.
            _ => (RevenueBucketGranularity.Hour, 24),
        };

    /// <summary>ISO 4217 fallback when the country row is missing — matches the read side's launch currency.</summary>
    private const string FallbackCurrency = "CZK";

    public sealed class Handler(
        IOrderQueries orders,
        IClock clock,
        ICountryConfigurationRepository countries,
        IOptions<AuthDefaultCountryOptions> defaultCountry,
        ILogger<Handler> logger)
        : IRequestHandler<Query, BusinessResult<GetPlatformRevenueSeriesResponse>>
    {
        public async Task<BusinessResult<GetPlatformRevenueSeriesResponse>> Handle(
            Query query, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(query);

            var countryCode = defaultCountry.Value.CountryCodePrimary;
            var config = await countries.GetByCodeAsync(countryCode, cancellationToken);
            var (timeZoneId, zone) = RevenueReportingTimeZone.Resolve(config, countryCode, logger);

            var (granularity, bucketCount) = LadderFor(query.Range);
            var (from, to) = RevenueReportingCalendar.TrailingWindow(
                clock.UtcNow, zone, granularity, bucketCount);

            var buckets = await orders.GetPlatformRevenueSeriesAsync(
                from, to, granularity, timeZoneId, cancellationToken);

            var points = FillGaps(
                buckets,
                RevenueReportingCalendar.BucketStarts(from, to, granularity, zone));

            return BusinessResult.Success(new GetPlatformRevenueSeriesResponse(
                query.Range,
                granularity,
                from,
                to,
                config?.DefaultCurrencyCode ?? FallbackCurrency,
                timeZoneId,
                points));
        }
    }

    /// <summary>
    /// Projects the sparse database buckets onto the expected grid, zeroing
    /// the ones with no sales.
    ///
    /// <para>
    /// Any database bucket that is NOT on the grid is appended rather than
    /// dropped. It should never happen — the grid mirrors <c>date_trunc</c>
    /// — but the two are computed by different engines from different
    /// timezone databases, and the failure mode of a silent drop is money
    /// disappearing from a chart with nothing to show for it. Extra points
    /// are visible and diagnosable; missing ones are not.
    /// </para>
    /// </summary>
    internal static IReadOnlyList<PlatformRevenuePointDto> FillGaps(
        IReadOnlyList<PlatformRevenueBucketDto> buckets,
        IReadOnlyList<DateTimeOffset> grid)
    {
        ArgumentNullException.ThrowIfNull(buckets);
        ArgumentNullException.ThrowIfNull(grid);

        // Keyed on UtcTicks, not the DateTimeOffset itself: two offsets can
        // name the same instant, and only the instant matters here.
        var byInstant = new Dictionary<long, PlatformRevenueBucketDto>(buckets.Count);
        foreach (var bucket in buckets)
        {
            byInstant[bucket.BucketStart.UtcTicks] = bucket;
        }

        var points = new List<PlatformRevenuePointDto>(grid.Count);
        var placed = new HashSet<long>(grid.Count);

        foreach (var start in grid)
        {
            placed.Add(start.UtcTicks);
            points.Add(byInstant.TryGetValue(start.UtcTicks, out var bucket)
                ? ToPoint(bucket)
                : new PlatformRevenuePointDto(start, 0, 0, 0, 0, 0));
        }

        foreach (var bucket in buckets)
        {
            if (!placed.Contains(bucket.BucketStart.UtcTicks))
            {
                points.Add(ToPoint(bucket));
            }
        }

        points.Sort(static (a, b) => a.BucketStart.CompareTo(b.BucketStart));
        return points;
    }

    private static PlatformRevenuePointDto ToPoint(PlatformRevenueBucketDto bucket) =>
        new(bucket.BucketStart,
            bucket.PaidOrderCount,
            bucket.GrossVolumeMinor,
            bucket.PlatformFeeMinor,
            bucket.MakerPayoutMinor,
            bucket.RefundedMinor);
}
