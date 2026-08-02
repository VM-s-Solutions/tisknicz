using Makables.Core.Domain.Addresses;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Registry;

namespace Makables.Infra.Clients.Ares.Caching;

/// <summary>
/// Flat DTO for cache persistence of an ARES <see cref="CompanyRecord"/>.
/// <see cref="CompanyRecord"/> embeds a live <see cref="Address"/>
/// aggregate whose <see cref="Address.Create"/> factory is the only
/// construction path — <c>System.Text.Json</c> can't round-trip through
/// it. Mapping to/from this DTO keeps cache rows readable in SQL and
/// lets a future <c>EvictExpiredRegistryCache</c> Function / admin
/// inspector deserialise the same shape.
///
/// <c>internal</c> on purpose (T-0032 CQ reviewer m-2): public is
/// overreach since this is a private persistence contract; private
/// nested blocked the mapper-extraction.
/// </summary>
internal sealed record CachedCompanyRecord(
    string RegistrationNumber,
    string? VatId,
    string CompanyName,
    string? LegalForm,
    MakerLegalType? LegalType,
    string Street,
    string HouseNumber,
    string City,
    string Zip,
    string? State,
    string CountryCodeIso,
    string AuditCountryCode,
    DateOnly? IncorporatedOn,
    bool IsActiveInRegistry,
    string SourceRegistry,
    DateTimeOffset FetchedAt)
{
    public static CachedCompanyRecord From(CompanyRecord r) => new(
        r.RegistrationNumber,
        r.VatId,
        r.CompanyName,
        r.LegalForm,
        r.LegalType,
        r.RegisteredAddress.Street,
        r.RegisteredAddress.HouseNumber,
        r.RegisteredAddress.City,
        r.RegisteredAddress.Zip,
        r.RegisteredAddress.State,
        r.RegisteredAddress.CountryCodeIso,
        r.RegisteredAddress.CountryCode,
        r.IncorporatedOn,
        r.IsActiveInRegistry,
        r.SourceRegistry,
        r.FetchedAt);

    public CompanyRecord ToRecord() => new(
        RegistrationNumber: RegistrationNumber,
        VatId: VatId,
        CompanyName: CompanyName,
        LegalForm: LegalForm,
        // Pre-T-0163 cache rows have no `legalType` key; System.Text.Json
        // leaves the nullable enum null, which is the correct
        // "unclassified" value. The row re-fetches on expiry and picks up
        // a real classification then — no cache flush needed.
        LegalType: LegalType,
        RegisteredAddress: Address.Create(
            id: $"ares-snapshot-{RegistrationNumber}",
            street: Street,
            houseNumber: HouseNumber,
            city: City,
            zip: Zip,
            countryCodeIso: CountryCodeIso,
            auditCountryCode: AuditCountryCode,
            state: State),
        IncorporatedOn: IncorporatedOn,
        IsActiveInRegistry: IsActiveInRegistry,
        SourceRegistry: SourceRegistry,
        FetchedAt: FetchedAt,
        IsStale: false);
}
