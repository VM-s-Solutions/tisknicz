using System.Collections.Frozen;
using Makables.Core.Domain.Makers;

namespace Makables.Infra.Common.Czech;

/// <summary>
/// Maps the most common Czech <c>pravniForma</c> numeric codes ARES
/// returns to human-readable Czech labels per ADR 0018 §"Mapping", and
/// classifies each one as a company or an individual trader for the
/// catalog's "Firma / Živnostník" filter. The codes themselves are
/// defined by the Czech state administration (Číselník právních forem
/// ČSÚ).
///
/// <para>
/// Label and classification live in the SAME table entry on purpose: two
/// dictionaries keyed by the same codes would drift the moment someone
/// added a form to one and not the other. Adding a code here gives it
/// both a label and a <see cref="MakerLegalType"/> in a single edit.
/// </para>
///
/// Unknown codes pass through as <c>"&lt;code&gt;"</c> (e.g.
/// <c>"123"</c>) rather than null so a Maker profile screen can render
/// something even when ARES hands us a form we haven't catalogued yet.
/// Their <see cref="Classify"/> result is <c>null</c> — deliberately NOT
/// inferred from the code's numeric range. The ranges do not partition
/// cleanly (<c>421</c> "Zahraniční fyzická osoba" is a natural person
/// sitting in the 4xx foreign-entity block), so a range rule would
/// silently file makers into the wrong filter bucket. An unclassified
/// maker matches neither bucket and appears only in the unfiltered list.
/// Add new entries here as production traffic shows the gap.
/// </summary>
public static class CzechLegalForms
{
    private sealed record LegalForm(string Label, MakerLegalType Type);

    private static readonly FrozenDictionary<string, LegalForm> Map =
        new Dictionary<string, LegalForm>(StringComparer.Ordinal)
        {
            // OSVČ (sole-proprietor) variants — the "Živnostník" bucket.
            ["101"] = new("Fyzická osoba podnikající dle živnostenského zákona", MakerLegalType.NaturalPerson),
            ["421"] = new("Zahraniční fyzická osoba", MakerLegalType.NaturalPerson),

            // Incorporated forms — the "Firma" bucket.
            ["111"] = new("Veřejná obchodní společnost", MakerLegalType.LegalEntity),
            // s.r.o. — the launch's most common maker shape.
            ["112"] = new("Společnost s ručením omezeným", MakerLegalType.LegalEntity),
            ["117"] = new("Nadace", MakerLegalType.LegalEntity),
            ["121"] = new("Akciová společnost", MakerLegalType.LegalEntity),
            ["205"] = new("Družstvo", MakerLegalType.LegalEntity),
            ["301"] = new("Státní podnik", MakerLegalType.LegalEntity),
            ["422"] = new("Organizační složka zahraniční právnické osoby", MakerLegalType.LegalEntity),
            ["601"] = new("Vysoká škola", MakerLegalType.LegalEntity),
            ["611"] = new("Příspěvková organizace", MakerLegalType.LegalEntity),
            ["706"] = new("Spolek", MakerLegalType.LegalEntity),
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
        return Map.TryGetValue(trimmed, out var form) ? form.Label : trimmed;
    }

    /// <summary>
    /// Classifies <paramref name="code"/> as a company or an individual
    /// trader. Returns <c>null</c> for a blank input OR for a code that
    /// is not in the table — see the type-level remarks on why an
    /// uncatalogued code is left unclassified rather than guessed.
    /// </summary>
    public static MakerLegalType? Classify(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        return Map.TryGetValue(code.Trim(), out var form) ? form.Type : null;
    }
}
