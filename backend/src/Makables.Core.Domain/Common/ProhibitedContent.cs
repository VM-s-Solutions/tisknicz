using System.Globalization;
using System.Text;

namespace Makables.Core.Domain.Common;

/// <summary>
/// Lightweight profanity screen for operator-curated reference data
/// (category names/slugs/descriptions, US-admin-0013 hygiene). This is
/// NOT a general content-moderation system — it is a guard rail that
/// keeps obviously vulgar terms out of the public taxonomy even via an
/// admin mistake or a compromised admin session.
///
/// <para>
/// Matching: input is lowercased and diacritics-stripped, then checked
/// two ways — exact token match (words that are innocent as substrings,
/// e.g. "hovno" inside a longer word is left to the root list) and
/// substring roots (stems that virtually never occur in legitimate
/// Czech/English words, e.g. "kurv", "fuck"). Deliberately conservative
/// to avoid Scunthorpe-style false positives on a Czech corpus.
/// </para>
/// </summary>
public static class ProhibitedContent
{
    /// <summary>Whole-token matches after normalization (diacritics stripped, lowercased).</summary>
    private static readonly HashSet<string> ProhibitedTokens = new(StringComparer.Ordinal)
    {
        // Czech
        "pica", "picus", "kokot", "kokoti", "hovno", "sracka", "srac",
        "debil", "debilove", "kreten", "kreteni", "buzerant", "cigos", "negr",
        // English
        "shit", "cunt", "bitch", "asshole", "dick",
    };

    /// <summary>Substring roots after normalization — stems with no innocent Czech/English usage.</summary>
    private static readonly string[] ProhibitedRoots =
    {
        // Czech
        "kurv", "mrdk", "zmrd", "mrdat", "jebat", "jebn", "curak",
        // English + slurs
        "fuck", "nigger", "wanker",
    };

    /// <summary>
    /// True when the text contains a prohibited term. Null/blank input
    /// is allowed (emptiness is the Required validator's job).
    /// </summary>
    public static bool ContainsProhibitedTerm(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalized = Normalize(text);

        foreach (var root in ProhibitedRoots)
        {
            if (normalized.Contains(root, StringComparison.Ordinal))
            {
                return true;
            }
        }

        foreach (var token in normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (ProhibitedTokens.Contains(token))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Lowercase, strip diacritics (NFD decompose + drop combining
    /// marks — same approach as <c>Category.Slugify</c>), and collapse
    /// every non-letter/digit run to a single space so token matching
    /// sees word boundaries regardless of punctuation or separators.
    /// </summary>
    private static string Normalize(string text)
    {
        var decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        var lastWasSeparator = false;

        foreach (var ch in decomposed)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
                lastWasSeparator = false;
            }
            else if (!lastWasSeparator)
            {
                sb.Append(' ');
                lastWasSeparator = true;
            }
        }

        return sb.ToString().Trim();
    }
}
