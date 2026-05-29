using System.Globalization;
using System.Text;

namespace Makables.Core.Domain.Common;

/// <summary>
/// Shared URL-slug generator. Used by <c>Category</c> (T-0040) and
/// <c>Maker</c> (T-0043) so there is one canonical Czech-aware
/// slugification rule rather than a copy per aggregate.
///
/// <para>
/// Steps:
/// <list type="number">
///   <item><description>Unicode NFD-decompose so combining diacritics separate from their base letters.</description></item>
///   <item><description>Drop combining marks (čďěíňřšťůýž → cdeinrstuyz).</description></item>
///   <item><description>Lowercase invariant.</description></item>
///   <item><description>Replace runs of non-<c>[a-z0-9]</c> with a single dash.</description></item>
///   <item><description>Trim leading/trailing dashes.</description></item>
/// </list>
/// Produces an empty string for whitespace / punctuation-only input —
/// callers that require a non-empty slug must check + fall back.
/// </para>
/// </summary>
public static class SlugGenerator
{
    public static string Slugify(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var normalized = value.Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        var prevDash = true;  // suppresses a leading dash

        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;

            var lower = char.ToLowerInvariant(c);
            if ((lower >= 'a' && lower <= 'z') || (lower >= '0' && lower <= '9'))
            {
                sb.Append(lower);
                prevDash = false;
            }
            else if (!prevDash)
            {
                sb.Append('-');
                prevDash = true;
            }
        }

        var result = sb.ToString();
        return result.EndsWith('-') ? result[..^1] : result;
    }

    /// <summary>
    /// True when <paramref name="slug"/> matches <c>[a-z0-9-]+</c> with
    /// no leading/trailing/double dashes. Used by aggregates that accept
    /// an admin-supplied slug override.
    /// </summary>
    public static bool IsValid(string slug)
    {
        if (string.IsNullOrEmpty(slug)) return false;
        if (slug[0] == '-' || slug[^1] == '-') return false;
        var prevDash = false;
        foreach (var c in slug)
        {
            var isAlphaNum = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');
            if (isAlphaNum)
            {
                prevDash = false;
                continue;
            }
            if (c == '-')
            {
                if (prevDash) return false;
                prevDash = true;
                continue;
            }
            return false;
        }
        return true;
    }
}
