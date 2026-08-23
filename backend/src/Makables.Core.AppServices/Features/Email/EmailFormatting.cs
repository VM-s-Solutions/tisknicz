using System.Globalization;
using Makables.Core.Domain.Common;

namespace Makables.Core.AppServices.Features.Email;

/// <summary>
/// Locale-aware formatting for the values that get substituted into
/// transactional email bodies.
///
/// <para>
/// Before this existed the auth emails rendered token expiry as
/// <c>DateTimeOffset.ToString("u")</c> — <c>"2026-08-24 18:30:00Z"</c> —
/// and the DB copy appended a literal " UTC". A Czech recipient had to
/// parse an ISO timestamp and do the +2h conversion in their head. The
/// same instant now renders as <c>"24. 8. 2026, 20:30"</c>: the country's
/// own date pattern, the country's own wall clock.
/// </para>
///
/// <para>
/// Both inputs are configuration, never a country branch (CLAUDE.md §2.7):
/// the pattern comes from <c>CountryConfiguration.DateFormat</c> and the
/// zone from <c>CountryConfiguration.TimeZoneId</c>. The culture comes
/// from the recipient's resolved language so a future locale gets correct
/// month casing and digit shaping for free.
/// </para>
/// </summary>
public static class EmailFormatting
{
    /// <summary>
    /// Fallback date pattern when <c>CountryConfiguration.DateFormat</c>
    /// is missing or unparseable. Matches the CZ seed value and
    /// <c>InvoiceFormatting.FormatDate</c>, so a document and the email
    /// announcing it never disagree about how a date looks.
    /// </summary>
    public const string DefaultDatePattern = "d. M. yyyy";

    /// <summary>24-hour clock — Czech (and every launch-scope locale) writes 20:30, not 8:30 PM.</summary>
    private const string TimePattern = "H:mm";

    /// <summary>
    /// Resolve a <see cref="CultureInfo"/> for a BCP-47 tag, falling back
    /// to the platform default language. Never throws: an unknown tag on
    /// a machine with a thin ICU set must not turn a queued email into a
    /// permanently-parked outbox row.
    /// </summary>
    public static CultureInfo CultureFor(string? languageCode)
    {
        // Gate on the domain's own tag validator rather than on whatever
        // ICU is willing to synthesize: "not-a-tag-at-all" happily becomes
        // a custom culture on some ICU builds, and a custom culture's date
        // formatting is invariant — silently wrong instead of loudly wrong.
        if (LanguageCode.IsValid(languageCode))
        {
            try
            {
                return CultureInfo.GetCultureInfo(languageCode!);
            }
            catch (CultureNotFoundException)
            {
                // Fall through to the platform default below.
            }
        }

        try
        {
            return CultureInfo.GetCultureInfo(LanguageCode.DefaultFallback);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }

    /// <summary>
    /// Resolve an IANA / Windows time-zone id, falling back to UTC. Never
    /// throws — a mis-seeded <c>CountryConfiguration.TimeZoneId</c>
    /// degrades the timestamp to UTC rather than killing the send.
    /// </summary>
    public static TimeZoneInfo TimeZoneFor(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId)) return TimeZoneInfo.Utc;
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    /// <summary>
    /// Render <paramref name="instant"/> as a wall-clock date + time in
    /// <paramref name="timeZone"/> — e.g. <c>"24. 8. 2026, 20:30"</c>.
    ///
    /// <para>
    /// The comma separator is deliberate: it reads correctly in every
    /// launch-scope language, unlike a connector word ("v" / "at") which
    /// would put translatable copy in C#. No zone abbreviation is
    /// appended — the recipient is in the country whose zone we
    /// converted to, and "SELČ" is noise to them.
    /// </para>
    /// </summary>
    public static string FormatDateTime(
        DateTimeOffset instant,
        TimeZoneInfo timeZone,
        CultureInfo culture,
        string? datePattern = null)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        ArgumentNullException.ThrowIfNull(culture);

        var local = TimeZoneInfo.ConvertTime(instant, timeZone);
        var pattern = string.IsNullOrWhiteSpace(datePattern) ? DefaultDatePattern : datePattern;

        string date;
        try
        {
            date = local.ToString(pattern, culture);
        }
        catch (FormatException)
        {
            // A hand-edited CountryConfiguration.DateFormat that isn't a
            // valid custom format string must not park the outbox row.
            date = local.ToString(DefaultDatePattern, culture);
        }

        return $"{date}, {local.ToString(TimePattern, culture)}";
    }
}
