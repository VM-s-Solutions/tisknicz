using FluentAssertions;
using Makables.Core.AppServices.Features.Admin;
using Makables.Core.Domain.Orders.Queries;

namespace Makables.Tests.AppServices.Features.Admin;

/// <summary>
/// T-0192 reporting calendar. Every assertion here is about the gap between
/// what an operator MEANS ("August", "the last 30 days") and what the orders
/// table STORES (instants). Get it wrong and money moves between reporting
/// periods without anything failing, which is why this is the one part of
/// the feature with no database and no clock in the way.
///
/// <para>
/// Prague is used throughout because it is the launch country's zone and it
/// exercises both offsets (+01:00 CET, +02:00 CEST) and both DST switches.
/// The zone is passed in, never looked up inside the helper — the helper is
/// country-agnostic by construction.
/// </para>
/// </summary>
public sealed class RevenueReportingCalendarTests
{
    private static readonly TimeZoneInfo Prague = TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague");

    private static DateTimeOffset Utc(int y, int m, int d, int h = 0, int min = 0) =>
        new(y, m, d, h, min, 0, TimeSpan.Zero);

    // === MonthWindow ===

    [Fact]
    public void A_summer_month_starts_and_ends_at_22_00_UTC_the_day_before()
    {
        // CEST is +02:00, so Czech August opens at 22:00 on 31 July UTC.
        // Reporting the UTC month instead would credit those two hours of
        // sales to July.
        var (from, to) = RevenueReportingCalendar.MonthWindow(2026, 8, Prague);

        from.Should().Be(Utc(2026, 7, 31, 22));
        to.Should().Be(Utc(2026, 8, 31, 22));
    }

    [Fact]
    public void A_winter_month_starts_at_23_00_UTC_because_the_offset_is_one_hour()
    {
        var (from, to) = RevenueReportingCalendar.MonthWindow(2026, 1, Prague);

        from.Should().Be(Utc(2025, 12, 31, 23));
        to.Should().Be(Utc(2026, 1, 31, 23));
    }

    [Fact]
    public void A_month_containing_a_DST_switch_still_ends_at_its_own_local_midnight()
    {
        // March 2026 opens in CET (+01:00) and closes in CEST (+02:00): the
        // month is 743 hours, not 744. A window built by adding 31×24h to the
        // start would overrun into 1 April local time.
        var (from, to) = RevenueReportingCalendar.MonthWindow(2026, 3, Prague);

        from.Should().Be(Utc(2026, 2, 28, 23));
        to.Should().Be(Utc(2026, 3, 31, 22));
        (to - from).Should().Be(TimeSpan.FromHours(743));
    }

    [Fact]
    public void December_rolls_into_the_next_January()
    {
        var (_, to) = RevenueReportingCalendar.MonthWindow(2026, 12, Prague);

        to.Should().Be(Utc(2026, 12, 31, 23));
    }

    [Fact]
    public void February_length_follows_the_calendar_not_a_constant()
    {
        var leap = RevenueReportingCalendar.MonthWindow(2028, 2, Prague);
        var common = RevenueReportingCalendar.MonthWindow(2026, 2, Prague);

        (leap.To - leap.From).Should().Be(TimeSpan.FromDays(29));
        (common.To - common.From).Should().Be(TimeSpan.FromDays(28));
    }

    [Fact]
    public void A_month_window_in_UTC_is_the_plain_calendar_month()
    {
        // The UTC fallback path (missing country config) must stay sane.
        var (from, to) = RevenueReportingCalendar.MonthWindow(2026, 8, TimeZoneInfo.Utc);

        from.Should().Be(Utc(2026, 8, 1));
        to.Should().Be(Utc(2026, 9, 1));
    }

    // === CurrentMonth ===

    [Fact]
    public void The_current_month_is_the_operators_month_not_UTCs()
    {
        // 22:30 UTC on 31 August is already 00:30 on 1 September in Prague.
        // The panel must open on September, or the operator sees August's
        // total under a September heading for two hours a month.
        var (year, month) = RevenueReportingCalendar.CurrentMonth(Utc(2026, 8, 31, 22, 30), Prague);

        year.Should().Be(2026);
        month.Should().Be(9);
    }

    [Fact]
    public void The_current_month_rolls_the_year_at_the_local_new_year()
    {
        var (year, month) = RevenueReportingCalendar.CurrentMonth(Utc(2026, 12, 31, 23, 30), Prague);

        year.Should().Be(2027);
        month.Should().Be(1);
    }

    // === TrailingWindow ===

    [Fact]
    public void A_day_range_covers_24_hourly_buckets_ending_with_the_one_in_progress()
    {
        var now = Utc(2026, 8, 22, 14, 37);

        var (from, to) = RevenueReportingCalendar.TrailingWindow(
            now, Prague, RevenueBucketGranularity.Hour, 24);

        // 16:37 local truncates to 16:00 local (14:00 UTC); 23 hours back.
        from.Should().Be(Utc(2026, 8, 21, 15));
        to.Should().Be(now, "the newest bucket is the hour in progress, not a whole future hour");
    }

    [Fact]
    public void A_month_range_starts_at_local_midnight_29_days_back()
    {
        var now = Utc(2026, 8, 22, 14, 37);

        var (from, to) = RevenueReportingCalendar.TrailingWindow(
            now, Prague, RevenueBucketGranularity.Day, 30);

        // Local day is 22 Aug; 29 days back is 24 July, whose local midnight
        // is 22:00 UTC on the 23rd.
        from.Should().Be(Utc(2026, 7, 23, 22));
        to.Should().Be(now);
    }

    [Fact]
    public void A_week_bucketed_range_starts_on_a_Monday()
    {
        // 2026-08-22 is a Saturday; its ISO week opens Monday 2026-08-17.
        var now = Utc(2026, 8, 22, 14, 37);

        var (from, _) = RevenueReportingCalendar.TrailingWindow(
            now, Prague, RevenueBucketGranularity.Week, 26);

        // 25 weeks before 17 Aug is Monday 23 February 2026 — still CET, so
        // its local midnight is 23:00 UTC on the 22nd.
        from.Should().Be(Utc(2026, 2, 22, 23));
    }

    [Fact]
    public void A_year_range_starts_on_the_first_of_the_month_11_months_back()
    {
        var now = Utc(2026, 8, 22, 14, 37);

        var (from, _) = RevenueReportingCalendar.TrailingWindow(
            now, Prague, RevenueBucketGranularity.Month, 12);

        from.Should().Be(Utc(2025, 8, 31, 22));
    }

    [Fact]
    public void A_single_bucket_window_opens_at_the_current_bucket()
    {
        var now = Utc(2026, 8, 22, 14, 37);

        var (from, _) = RevenueReportingCalendar.TrailingWindow(
            now, Prague, RevenueBucketGranularity.Day, 1);

        from.Should().Be(Utc(2026, 8, 21, 22));
    }

    [Fact]
    public void A_bucket_count_below_one_is_a_programmer_error()
    {
        var act = () => RevenueReportingCalendar.TrailingWindow(
            Utc(2026, 8, 22), Prague, RevenueBucketGranularity.Day, 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // === BucketStarts ===

    [Fact]
    public void The_grid_has_one_entry_per_bucket_and_starts_at_the_window()
    {
        var now = Utc(2026, 8, 22, 14, 37);
        var (from, to) = RevenueReportingCalendar.TrailingWindow(
            now, Prague, RevenueBucketGranularity.Day, 30);

        var grid = RevenueReportingCalendar.BucketStarts(from, to, RevenueBucketGranularity.Day, Prague);

        grid.Should().HaveCount(30);
        grid[0].Should().Be(from);
        grid.Should().BeInAscendingOrder();
    }

    [Fact]
    public void The_grid_never_reaches_past_the_window_end()
    {
        var now = Utc(2026, 8, 22, 14, 37);
        var (from, to) = RevenueReportingCalendar.TrailingWindow(
            now, Prague, RevenueBucketGranularity.Hour, 24);

        var grid = RevenueReportingCalendar.BucketStarts(from, to, RevenueBucketGranularity.Hour, Prague);

        grid.Should().HaveCount(24);
        grid[^1].Should().BeBefore(to);
    }

    [Fact]
    public void Daily_buckets_across_the_spring_switch_stay_on_local_midnight()
    {
        // Clocks go forward 02:00 → 03:00 on Sunday 2026-03-29. The 29th is
        // therefore a 23-hour day. Stepping in absolute 24h units would push
        // every bucket after it to 01:00 local and no longer line up with
        // date_trunc, so every point past the switch would read as empty.
        var from = Utc(2026, 3, 26, 23);
        var to = Utc(2026, 4, 2, 22);

        var grid = RevenueReportingCalendar.BucketStarts(from, to, RevenueBucketGranularity.Day, Prague);

        grid.Should().HaveCount(7);
        grid[1].Should().Be(Utc(2026, 3, 27, 23), "28 March opens at 00:00 CET");
        grid[2].Should().Be(Utc(2026, 3, 28, 23), "the 29th opens at 00:00, still CET");
        grid[3].Should().Be(Utc(2026, 3, 29, 22), "the 30th opens at 00:00 CEST — one hour earlier in UTC");
        (grid[3] - grid[2]).Should().Be(TimeSpan.FromHours(23), "29 March is a 23-hour day");
        (grid[4] - grid[3]).Should().Be(TimeSpan.FromHours(24), "and the days after it are 24 again");
    }

    [Fact]
    public void Daily_buckets_across_the_autumn_switch_stay_on_local_midnight()
    {
        // Clocks go back 03:00 → 02:00 on Sunday 2026-10-25: a 25-hour day.
        var from = Utc(2026, 10, 22, 22);
        var to = Utc(2026, 10, 28, 23);

        var grid = RevenueReportingCalendar.BucketStarts(from, to, RevenueBucketGranularity.Day, Prague);

        grid.Should().Contain(Utc(2026, 10, 24, 22));
        grid.Should().Contain(Utc(2026, 10, 25, 23));
        (Utc(2026, 10, 25, 23) - Utc(2026, 10, 24, 22)).Should().Be(TimeSpan.FromHours(25));
    }

    [Fact]
    public void Monthly_buckets_over_a_year_are_twelve_local_first_of_months()
    {
        var now = Utc(2026, 8, 22, 14, 37);
        var (from, to) = RevenueReportingCalendar.TrailingWindow(
            now, Prague, RevenueBucketGranularity.Month, 12);

        var grid = RevenueReportingCalendar.BucketStarts(from, to, RevenueBucketGranularity.Month, Prague);

        grid.Should().HaveCount(12);
        grid[0].Should().Be(Utc(2025, 8, 31, 22), "September 2025 local");
        grid[^1].Should().Be(Utc(2026, 7, 31, 22), "August 2026 local — the month in progress");
    }

    [Fact]
    public void Weekly_buckets_are_all_local_Mondays()
    {
        var now = Utc(2026, 8, 22, 14, 37);
        var (from, to) = RevenueReportingCalendar.TrailingWindow(
            now, Prague, RevenueBucketGranularity.Week, 26);

        var grid = RevenueReportingCalendar.BucketStarts(from, to, RevenueBucketGranularity.Week, Prague);

        grid.Should().HaveCount(26);
        grid.Should().OnlyContain(
            start => TimeZoneInfo.ConvertTime(start, Prague).DayOfWeek == DayOfWeek.Monday);
        grid.Should().OnlyContain(start => TimeZoneInfo.ConvertTime(start, Prague).Hour == 0);
    }

    [Fact]
    public void A_grid_over_a_month_window_bucketed_by_week_may_open_before_the_month()
    {
        // 1 August 2026 is a Saturday, so its ISO week opened on 27 July. The
        // first bucket is that Monday — which is what date_trunc('week', …)
        // returns for those orders too, so dropping it would drop real money.
        var (from, to) = RevenueReportingCalendar.MonthWindow(2026, 8, Prague);

        var grid = RevenueReportingCalendar.BucketStarts(from, to, RevenueBucketGranularity.Week, Prague);

        grid[0].Should().Be(Utc(2026, 7, 26, 22), "Monday 27 July local");
        grid[0].Should().BeBefore(from);
    }

    [Fact]
    public void An_inverted_or_empty_window_yields_no_buckets()
    {
        var instant = Utc(2026, 8, 22);

        RevenueReportingCalendar
            .BucketStarts(instant, instant, RevenueBucketGranularity.Day, Prague)
            .Should().BeEmpty();

        RevenueReportingCalendar
            .BucketStarts(instant, instant.AddDays(-1), RevenueBucketGranularity.Day, Prague)
            .Should().BeEmpty();
    }
}
