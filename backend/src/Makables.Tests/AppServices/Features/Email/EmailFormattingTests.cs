using System.Globalization;
using FluentAssertions;
using Makables.Core.AppServices.Features.Email;

namespace Makables.Tests.AppServices.Features.Email;

/// <summary>
/// Pins the Czech-locale rendering of the timestamps that reach a
/// recipient. Before this existed, <c>{{expires_at}}</c> in every auth
/// email rendered as <c>DateTimeOffset.ToString("u")</c> —
/// <c>"2026-08-24 18:30:00Z"</c> — with a literal " UTC" appended by the
/// DB copy, which asked a Czech reader to do a summer-time conversion in
/// their head to find out how long they had to click a password-reset
/// link.
///
/// <para>
/// The DST pair is the load-bearing case: the same UTC wall time is
/// +2h in August and +1h in January, and a formatter that hard-coded an
/// offset would pass one of them and fail the other.
/// </para>
/// </summary>
public class EmailFormattingTests
{
    private static readonly TimeZoneInfo Prague = TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague");
    private static readonly CultureInfo Cs = CultureInfo.GetCultureInfo("cs-CZ");

    [Fact]
    public void Formats_summer_instant_in_Prague_wall_clock()
    {
        // CEST = UTC+2.
        var instant = new DateTimeOffset(2026, 8, 24, 18, 30, 0, TimeSpan.Zero);

        EmailFormatting.FormatDateTime(instant, Prague, Cs, "d. M. yyyy")
            .Should().Be("24. 8. 2026, 20:30");
    }

    [Fact]
    public void Formats_winter_instant_in_Prague_wall_clock()
    {
        // CET = UTC+1 — same UTC hour, one hour earlier locally.
        var instant = new DateTimeOffset(2026, 1, 24, 18, 30, 0, TimeSpan.Zero);

        EmailFormatting.FormatDateTime(instant, Prague, Cs, "d. M. yyyy")
            .Should().Be("24. 1. 2026, 19:30");
    }

    [Fact]
    public void Crossing_midnight_locally_advances_the_date_too()
    {
        // 23:10Z on the 24th is already 01:10 on the 25th in Prague. A
        // formatter that converted the time but kept the UTC date would
        // tell the recipient the link died yesterday.
        var instant = new DateTimeOffset(2026, 8, 24, 23, 10, 0, TimeSpan.Zero);

        EmailFormatting.FormatDateTime(instant, Prague, Cs, "d. M. yyyy")
            .Should().Be("25. 8. 2026, 1:10");
    }

    [Fact]
    public void Uses_a_24_hour_clock()
    {
        var instant = new DateTimeOffset(2026, 8, 24, 12, 5, 0, TimeSpan.Zero);

        EmailFormatting.FormatDateTime(instant, Prague, Cs, "d. M. yyyy")
            .Should().Be("24. 8. 2026, 14:05")
            .And.NotContain("PM");
    }

    [Fact]
    public void Honours_the_country_date_pattern_rather_than_hardcoding_one()
    {
        var instant = new DateTimeOffset(2026, 8, 24, 18, 30, 0, TimeSpan.Zero);

        // A different CountryConfiguration.DateFormat must actually change
        // the output — otherwise the "no country branching" rule is only
        // cosmetically satisfied.
        EmailFormatting.FormatDateTime(instant, Prague, Cs, "yyyy-MM-dd")
            .Should().Be("2026-08-24, 20:30");
    }

    [Fact]
    public void Falls_back_to_the_default_pattern_when_the_configured_one_is_garbage()
    {
        var instant = new DateTimeOffset(2026, 8, 24, 18, 30, 0, TimeSpan.Zero);

        // A hand-edited country row must not park a password-reset email.
        EmailFormatting.FormatDateTime(instant, Prague, Cs, "q")
            .Should().Be("24. 8. 2026, 20:30");
    }

    [Fact]
    public void Falls_back_to_the_default_pattern_when_none_is_configured()
    {
        var instant = new DateTimeOffset(2026, 8, 24, 18, 30, 0, TimeSpan.Zero);

        EmailFormatting.FormatDateTime(instant, Prague, Cs, datePattern: null)
            .Should().Be("24. 8. 2026, 20:30");
    }

    [Theory]
    [InlineData("Europe/Prague")]
    public void TimeZoneFor_resolves_a_seeded_zone(string id) =>
        EmailFormatting.TimeZoneFor(id).Should().NotBe(TimeZoneInfo.Utc);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Mars/Olympus_Mons")]
    public void TimeZoneFor_degrades_to_UTC_rather_than_throwing(string? id) =>
        EmailFormatting.TimeZoneFor(id).Should().Be(TimeZoneInfo.Utc);

    [Fact]
    public void CultureFor_resolves_the_launch_languages() =>
        EmailFormatting.CultureFor("cs-CZ").Name.Should().Be("cs-CZ");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-tag-at-all")]
    public void CultureFor_degrades_to_the_platform_default(string? tag) =>
        EmailFormatting.CultureFor(tag).Name.Should().Be("cs-CZ");
}
