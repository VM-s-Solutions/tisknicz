using System.Text.Json.Serialization;
using Makables.Core.Domain.Addresses;
using Makables.Core.Domain.Registry;
using Makables.Infra.Common.Czech;

namespace Makables.Infra.Clients.Ares.Mapping;

/// <summary>
/// Pure mapping primitives that turn an ARES JSON response into a
/// <see cref="CompanyRecord"/>. Extracted from <c>AresCompanyRegistry</c>
/// per T-0032 CQ reviewer m-1 — the adapter focuses on the request /
/// cache / retry flow, the mapper focuses on the JSON shape, and the
/// mapper is independently testable without an HTTP stub.
///
/// All map failures surface as <see cref="MapFailure.IncompleteSidlo"/>
/// (essential address fields missing — ADR 0018 §"Error classification"
/// classifies "unexpected shape" as Permanent; T-0032 CQ reviewer M-1
/// closed the prior bug where missing fields silently became literal
/// "unknown" / "0" / "00000" strings).
/// </summary>
public static class AresResponseMapper
{
    /// <summary>
    /// Try to map an ARES payload to a <see cref="CompanyRecord"/>.
    /// Returns the record on success. On failure returns null and sets
    /// <paramref name="failure"/> to the structural reason — the
    /// adapter then surfaces a Permanent business error.
    /// </summary>
    public static CompanyRecord? TryMap(
        AresEkonomickySubjekt payload,
        DateTimeOffset now,
        out MapFailure failure)
    {
        if (payload is null || string.IsNullOrWhiteSpace(payload.Ico))
        {
            failure = MapFailure.MissingIco;
            return null;
        }

        var sidlo = payload.Sidlo;
        if (sidlo is null
            || string.IsNullOrWhiteSpace(sidlo.NazevObce)
            || sidlo.Psc is null
            || (string.IsNullOrWhiteSpace(sidlo.NazevUlice) && sidlo.CisloDomovni is null))
        {
            // T-0032 CQ reviewer M-1: an ARES row missing city / ZIP /
            // any street identifier is "unexpected shape" per ADR 0018
            // §"Error classification" — treat as Permanent, not silent
            // "unknown" placeholder.
            failure = MapFailure.IncompleteSidlo;
            return null;
        }

        DateOnly? incorporatedOn = null;
        if (!string.IsNullOrWhiteSpace(payload.DatumVzniku)
            && DateOnly.TryParse(payload.DatumVzniku, out var d))
        {
            incorporatedOn = d;
        }

        // Street fallback: when ARES omits `nazevUlice` (small villages,
        // OSVČ at home), use the city name. The combined "{street} {n}"
        // string is what shipping labels and invoices print, and that's
        // the format the Czech post operates on. We KEEP requiring the
        // street label to be non-empty either way.
        var street = string.IsNullOrWhiteSpace(sidlo.NazevUlice)
            ? sidlo.NazevObce!
            : sidlo.NazevUlice!;
        var houseNumber = sidlo.CisloDomovni?.ToString() ?? string.Empty;

        Address address;
        try
        {
            address = Address.Create(
                id: $"ares-snapshot-{payload.Ico}",
                street: street.Trim(),
                houseNumber: string.IsNullOrWhiteSpace(houseNumber) ? "0" : houseNumber,
                city: sidlo.NazevObce!.Trim(),
                zip: sidlo.Psc!.Value.ToString("00000"),
                countryCodeIso: "CZ",
                auditCountryCode: "CZ");
        }
        catch (ArgumentException)
        {
            failure = MapFailure.IncompleteSidlo;
            return null;
        }

        failure = MapFailure.None;
        return new CompanyRecord(
            RegistrationNumber: payload.Ico,
            VatId: string.IsNullOrWhiteSpace(payload.Dic) ? null : payload.Dic,
            CompanyName: payload.ObchodniJmeno ?? string.Empty,
            LegalForm: CzechLegalForms.Resolve(payload.PravniForma),
            // Classified here, in the CZ adapter, from the raw ČSÚ code —
            // the code is not carried further, so this is the last point
            // at which the company/individual split can be decided
            // without parsing display copy.
            LegalType: CzechLegalForms.Classify(payload.PravniForma),
            RegisteredAddress: address,
            IncorporatedOn: incorporatedOn,
            IsActiveInRegistry: string.IsNullOrWhiteSpace(payload.DatumZaniku),
            SourceRegistry: "ares",
            FetchedAt: now,
            IsStale: false);
    }
}

/// <summary>
/// Why a <see cref="AresResponseMapper.TryMap"/> call failed.
/// </summary>
public enum MapFailure
{
    None = 0,
    MissingIco = 1,
    IncompleteSidlo = 2,
}

/// <summary>
/// ARES v3 JSON shape (subset we actually map; tolerant of extra
/// fields). Lifted to <c>internal</c> so the mapper extraction tests can
/// construct instances without crossing into adapter privates.
/// </summary>
public sealed record AresEkonomickySubjekt(
    [property: JsonPropertyName("ico")]            string? Ico,
    [property: JsonPropertyName("obchodniJmeno")]  string? ObchodniJmeno,
    [property: JsonPropertyName("dic")]            string? Dic,
    [property: JsonPropertyName("pravniForma")]    string? PravniForma,
    [property: JsonPropertyName("datumVzniku")]    string? DatumVzniku,
    [property: JsonPropertyName("datumZaniku")]    string? DatumZaniku,
    [property: JsonPropertyName("sidlo")]          AresSidlo? Sidlo);

public sealed record AresSidlo(
    [property: JsonPropertyName("nazevUlice")]     string? NazevUlice,
    [property: JsonPropertyName("cisloDomovni")]   int? CisloDomovni,
    [property: JsonPropertyName("nazevObce")]      string? NazevObce,
    [property: JsonPropertyName("psc")]            int? Psc,
    [property: JsonPropertyName("nazevStatu")]     string? NazevStatu);
