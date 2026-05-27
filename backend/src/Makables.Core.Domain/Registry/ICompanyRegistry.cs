using Makables.Core.Domain.Addresses;
using Makables.Core.Domain.Common;

namespace Makables.Core.Domain.Registry;

/// <summary>
/// Per ADR 0018. Look up a business by its registration number; return
/// a normalised <see cref="CompanyRecord"/>. The launch implementation
/// is <c>AresCompanyRegistry</c> in <c>Infra.Clients/Ares/</c>; SK / PL /
/// DE drop in as new keyed adapters per
/// <see cref="Configuration.CountryConfiguration.DefaultRegistry"/>.
///
/// All failures surface as <see cref="BusinessResult{T}"/> failures —
/// no exceptions cross the boundary. Specific error codes:
/// <see cref="BusinessErrorMessage.IcoFormatInvalid"/> (caller didn't
/// validate first), <see cref="BusinessErrorMessage.CompanyNotFound"/>
/// (registry replied 404), <see cref="BusinessErrorMessage.CompanyRegistryTransient"/>
/// (registry 5xx / timeout / 429 with no usable stale fallback),
/// <see cref="BusinessErrorMessage.CompanyRegistryPermanent"/>
/// (malformed registry response — alert ops).
/// </summary>
public interface ICompanyRegistry
{
    /// <summary>
    /// Keyed-service code (e.g. <c>"ares"</c>). Matches
    /// <see cref="Configuration.CountryConfiguration.DefaultRegistry"/>.
    /// </summary>
    string Code { get; }

    Task<BusinessResult<CompanyRecord>> LookupByRegistrationNumberAsync(
        string registrationNumber,
        CancellationToken cancellationToken);
}

/// <summary>
/// Normalised registry record per ADR 0018. <see cref="IsStale"/> is
/// the stale-fallback flag: <c>true</c> when ARES was unreachable AND
/// we served a DB-cache row whose <c>expires_at</c> had passed but is
/// still within the 7-day fallback window. The caller (T-0033
/// <c>RegisterMaker</c>) MAY surface a warning to the user but MUST
/// still allow registration to proceed.
/// </summary>
public sealed record CompanyRecord(
    string RegistrationNumber,
    string? VatId,
    string CompanyName,
    string? LegalForm,
    Address RegisteredAddress,
    DateOnly? IncorporatedOn,
    bool IsActiveInRegistry,
    string SourceRegistry,
    DateTimeOffset FetchedAt,
    bool IsStale = false);
