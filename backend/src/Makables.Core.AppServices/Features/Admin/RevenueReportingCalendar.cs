using Makables.Core.Domain.Orders.Queries;

namespace Makables.Core.AppServices.Features.Admin;

/// <summary>
/// Calendar arithmetic behind the admin revenue reports (T-0192). Pure and
/// static: every method is a function of its arguments, so the two handlers
/// that use it stay orchestration-only and the awkward parts — DST, month
/// lengths, ISO weeks — are unit-testable without a database or a clock.
///
/// <para>
/// Everything here works in a COUNTRY-LOCAL civil calendar and returns UTC
/// instants. That distinction is the whole reason this type exists: the
/// operator asks for "August" or "the last 30 days", which are wall-clock
/// concepts, while the orders table stores instants. August in Prague is
/// <c>2026-07-31T22:00Z … 2026-08-31T22:00Z</c>, and a "day" bucket across
/// the October switch is 25 hours long. Treating either as a fixed slice of
/// UTC silently moves money between periods.
/// </para>
///
/// <para>
/// The bucket boundaries produced here must line up EXACTLY with Postgres
/// <c>date_trunc(field, timestamptz, zone)</c>, because the chart joins the
/// grid built here onto the buckets the database returns. That is why
/// <see cref="TruncateLocal"/> mirrors <c>date_trunc</c> semantics rather
/// than inventing its own: weeks start Monday (ISO), and truncation happens
/// on the local wall clock before conversion back to an instant.
/// </para>
/// </summary>
public static class RevenueReportingCalendar
{
    /// <summary>
    /// Hard ceiling on how many buckets a grid may contain. The reporting
    /// ranges top out around 92 (a quarter of days), so this is a runaway
    /// guard for a future range, not a limit anything legitimate meets —
    /// a bad granularity/span pair should stop, not allocate forever.
    /// </summary>
    private const int MaxBuckets = 1000;

    /// <summary>
    /// Half-open <c>[from, to)</c> instants of one calendar month in
    /// <paramref name="timeZone"/>. December rolls into the next January
    /// through <see cref="DateTime.AddMonths"/>, so no year arithmetic is
    /// duplicated here.
    /// </summary>
    public static (DateTimeOffset From, DateTimeOffset To) MonthWindow(
        int year, int month, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        var start = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        return (ToInstant(start, timeZone), ToInstant(start.AddMonths(1), timeZone));
    }

    /// <summary>
    /// The calendar month <paramref name="now"/> falls in, as the operator
    /// would name it. Just past midnight on 1 September in Prague this is
    /// September, even though it is still August in UTC.
    /// </summary>
    public static (int Year, int Month) CurrentMonth(DateTimeOffset now, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        var local = ToLocal(now, timeZone);
        return (local.Year, local.Month);
    }

    /// <summary>
    /// Half-open <c>[from, to)</c> instants of the last
    /// <paramref name="bucketCount"/> buckets ending with the one in
    /// progress. <c>to</c> is <paramref name="now"/> itself, not the end of
    /// the current bucket, so the newest point on a chart is "so far today"
    /// rather than a phantom full day — the same convention a price chart
    /// uses for the current session.
    /// </summary>
    public static (DateTimeOffset From, DateTimeOffset To) TrailingWindow(
        DateTimeOffset now,
        TimeZoneInfo timeZone,
        RevenueBucketGranularity granularity,
        int bucketCount)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        ArgumentOutOfRangeException.ThrowIfLessThan(bucketCount, 1);

        var currentBucket = TruncateLocal(ToLocal(now, timeZone), granularity);
        var firstBucket = AddBuckets(currentBucket, granularity, -(bucketCount - 1));
        return (ToInstant(firstBucket, timeZone), now);
    }

    /// <summary>
    /// Every bucket start in <c>[from, to)</c>, ascending. The first entry
    /// is the start of the bucket CONTAINING <paramref name="from"/>, which
    /// for a window produced by <see cref="TrailingWindow"/> or
    /// <see cref="MonthWindow"/> is <paramref name="from"/> itself.
    ///
    /// <para>
    /// Stepping happens on the local wall clock (add one calendar day, one
    /// week, one month) and each step is converted back to an instant, so a
    /// DST switch shortens or lengthens the bucket instead of shifting every
    /// later boundary by an hour.
    /// </para>
    /// </summary>
    public static IReadOnlyList<DateTimeOffset> BucketStarts(
        DateTimeOffset from,
        DateTimeOffset to,
        RevenueBucketGranularity granularity,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        if (to <= from)
        {
            return [];
        }

        var starts = new List<DateTimeOffset>();
        var local = TruncateLocal(ToLocal(from, timeZone), granularity);

        for (var i = 0; i < MaxBuckets; i++)
        {
            var instant = ToInstant(local, timeZone);
            if (instant >= to)
            {
                break;
            }

            // A truncated first bucket can open BEFORE the window (asking for
            // "this month" bucketed by week starts on the preceding Monday).
            // The point still belongs on the chart — the database counts it,
            // because date_trunc puts those orders in the same bucket.
            starts.Add(instant);
            local = AddBuckets(local, granularity, 1);
        }

        return starts;
    }

    /// <summary>Wall-clock time in <paramref name="timeZone"/>, kind-unspecified.</summary>
    private static DateTime ToLocal(DateTimeOffset instant, TimeZoneInfo timeZone) =>
        DateTime.SpecifyKind(
            TimeZoneInfo.ConvertTime(instant, timeZone).DateTime, DateTimeKind.Unspecified);

    /// <summary>
    /// The instant a local wall-clock time names.
    ///
    /// <para>
    /// Deliberately built from <see cref="TimeZoneInfo.GetUtcOffset(DateTime)"/>
    /// rather than <c>ConvertTimeToUtc</c>, which THROWS on a wall-clock time
    /// that a spring-forward skipped. Bucket boundaries are usually midnight,
    /// which no European switch touches, but a timezone that shifts at
    /// midnight (or a future hourly grid) would otherwise turn a dashboard
    /// read into a 500 twice a year. <c>GetUtcOffset</c> resolves a skipped
    /// time to the offset in force before the gap and an ambiguous one to
    /// standard time — deterministic in both cases, which is all the grid
    /// needs.
    /// </para>
    /// </summary>
    private static DateTimeOffset ToInstant(DateTime localWallClock, TimeZoneInfo timeZone)
    {
        var unspecified = DateTime.SpecifyKind(localWallClock, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, timeZone.GetUtcOffset(unspecified)).ToUniversalTime();
    }

    /// <summary>
    /// Mirrors Postgres <c>date_trunc</c> on the local wall clock. Week is
    /// the ISO week (Monday), matching <c>date_trunc('week', …)</c> and the
    /// week the payout batches are numbered by.
    /// </summary>
    private static DateTime TruncateLocal(DateTime local, RevenueBucketGranularity granularity) =>
        granularity switch
        {
            RevenueBucketGranularity.Hour =>
                new DateTime(local.Year, local.Month, local.Day, local.Hour, 0, 0, DateTimeKind.Unspecified),
            RevenueBucketGranularity.Day => local.Date,
            RevenueBucketGranularity.Week =>
                local.Date.AddDays(-(((int)local.DayOfWeek + 6) % 7)),
            RevenueBucketGranularity.Month =>
                new DateTime(local.Year, local.Month, 1, 0, 0, 0, DateTimeKind.Unspecified),
            _ => throw new ArgumentOutOfRangeException(
                nameof(granularity), granularity, "Unmapped revenue bucket granularity."),
        };

    private static DateTime AddBuckets(
        DateTime local, RevenueBucketGranularity granularity, int count) =>
        granularity switch
        {
            RevenueBucketGranularity.Hour => local.AddHours(count),
            RevenueBucketGranularity.Day => local.AddDays(count),
            RevenueBucketGranularity.Week => local.AddDays(7 * count),
            RevenueBucketGranularity.Month => local.AddMonths(count),
            _ => throw new ArgumentOutOfRangeException(
                nameof(granularity), granularity, "Unmapped revenue bucket granularity."),
        };
}
