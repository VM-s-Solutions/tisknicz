namespace Makables.Core.Domain.Makers;

/// <summary>
/// Whether a maker trades as a company or as an individual — the
/// "Firma / Živnostník" catalog filter (US-customer-0007).
///
/// <para>
/// Country-neutral by design. The classification itself is
/// country-specific (in CZ it is derived from the ČSÚ
/// <c>pravniForma</c> code ARES returns), so it happens inside the
/// registry adapter — <c>CzechLegalForms.Classify</c> — and only the
/// normalised result reaches the domain. A future non-CZ registry
/// adapter maps its own scheme onto these same two values; nothing
/// downstream branches on country. Same shape as ADR 0018's
/// normalise-at-the-adapter rule for <c>CompanyRecord</c>.
/// </para>
///
/// <para>
/// Deliberately has no "unknown" member: the absence of a
/// classification is modelled as a NULL
/// <see cref="Maker.LegalType"/>, not as an enum value. A registry that
/// returns a legal form we have not catalogued yields null, and a null
/// maker matches NEITHER filter bucket — it is only ever visible in the
/// unfiltered list. Encoding "unknown" as a member would make it a
/// selectable filter value and force every consumer to handle a third
/// case that carries no meaning to a customer.
/// </para>
/// </summary>
public enum MakerLegalType
{
    /// <summary>
    /// Právnická osoba — s.r.o., a.s., v.o.s., družstvo, and the other
    /// incorporated forms. Surfaced to customers as "Firma".
    /// </summary>
    LegalEntity = 1,

    /// <summary>
    /// Fyzická osoba podnikající — an OSVČ trading under their own
    /// IČO. Surfaced to customers as "Živnostník". Note that every
    /// maker on the platform holds an IČO (registration validates it
    /// against the registry), so this never means "private individual
    /// without a trade licence".
    /// </summary>
    NaturalPerson = 2,
}
