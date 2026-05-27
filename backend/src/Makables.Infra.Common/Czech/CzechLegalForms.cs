using System.Collections.Frozen;

namespace Makables.Infra.Common.Czech;

/// <summary>
/// Maps the most common Czech <c>pravniForma</c> numeric codes ARES
/// returns to human-readable Czech labels per ADR 0018 §"Mapping". The
/// codes themselves are defined by the Czech state administration
/// (Číselník právních forem ČSÚ).
///
/// Unknown codes pass through as <c>"&lt;code&gt;"</c> (e.g.
/// <c>"123"</c>) rather than null so a Maker profile screen can render
/// something even when ARES hands us a form we haven't catalogued yet.
/// Add new entries here as production traffic shows the gap.
/// </summary>
public static class CzechLegalForms
{
    private static readonly FrozenDictionary<string, string> Map =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // OSVČ (sole-proprietor) variants.
            ["101"] = "Fyzická osoba podnikající dle živnostenského zákona",
            ["111"] = "Veřejná obchodní společnost",
            // s.r.o. — the launch's most common maker shape.
            ["112"] = "Společnost s ručením omezeným",
            ["117"] = "Nadace",
            ["121"] = "Akciová společnost",
            ["205"] = "Družstvo",
            ["301"] = "Státní podnik",
            ["421"] = "Zahraniční fyzická osoba",
            ["422"] = "Organizační složka zahraniční právnické osoby",
            ["601"] = "Vysoká škola",
            ["611"] = "Příspěvková organizace",
            ["706"] = "Spolek",
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    /// Returns the Czech human-readable label for <paramref name="code"/>
    /// or the trimmed code itself if unknown. Returns <c>null</c> only
    /// when the input is null/blank.
    /// </summary>
    public static string? Resolve(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var trimmed = code.Trim();
        return Map.TryGetValue(trimmed, out var label) ? label : trimmed;
    }
}
