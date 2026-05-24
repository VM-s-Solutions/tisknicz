namespace Makables.Core.Domain.Common;

/// <summary>
/// BCP-47 language tag constants + a strict validator. Used everywhere the
/// domain stores or compares a language: <see cref="Makables.Core.Domain.Identity.User.PreferredLanguage"/>,
/// <see cref="Makables.Core.Domain.Configuration.CountryConfiguration.DefaultLanguageCode"/>,
/// <c>EmailTemplateTranslation.LanguageCode</c>, and the resolved language
/// carried on every outbox payload.
///
/// At launch the platform ships <see cref="CsCZ"/> and <see cref="EnUS"/>.
/// Adding a new language is a one-line edit to <see cref="Supported"/>
/// plus an <c>EmailTemplateTranslation</c> seed for every template type.
/// </summary>
public static class LanguageCode
{
    public const string CsCZ = "cs-CZ";
    public const string EnUS = "en-US";

    /// <summary>Languages we have UI + email-template coverage for at launch.</summary>
    public static readonly string[] Supported = [CsCZ, EnUS];

    /// <summary>
    /// The platform-wide fallback when neither user preference nor country
    /// default is set. Equal to <see cref="CsCZ"/> because Makables is a
    /// Czech-market launch (CLAUDE.md §"Czech-only at launch").
    /// </summary>
    public const string DefaultFallback = CsCZ;

    /// <summary>
    /// True when <paramref name="tag"/> matches the launch-scope BCP-47
    /// shape: exactly <c>{2 lowercase letters}-{2 uppercase letters}</c>.
    /// We don't accept bare language ("cs") or regionless tags at storage
    /// time — every persisted language MUST carry a region so template
    /// lookups are unambiguous.
    ///
    /// Intentionally rejects (at launch scope):
    /// <list type="bullet">
    ///   <item><description>Script subtags (<c>zh-Hans-CN</c>) — re-evaluate when a CJK language ships.</description></item>
    ///   <item><description>3-letter language tags (<c>fil-PH</c>) — re-evaluate when needed.</description></item>
    ///   <item><description>Variant or private-use extensions — never needed for transactional copy.</description></item>
    /// </list>
    /// The <c>users.preferred_language</c> column width (16) is forward-compatible
    /// for those shapes; widening the validator is a one-line change here.
    /// </summary>
    public static bool IsValid(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag) || tag.Length != 5) return false;
        if (tag[2] != '-') return false;
        for (var i = 0; i < 2; i++)
            if (tag[i] < 'a' || tag[i] > 'z') return false;
        for (var i = 3; i < 5; i++)
            if (tag[i] < 'A' || tag[i] > 'Z') return false;
        return true;
    }
}
