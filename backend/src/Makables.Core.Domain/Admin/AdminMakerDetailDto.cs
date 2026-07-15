namespace Makables.Core.Domain.Admin;

/// <summary>
/// Single privileged maker header for the admin detail page (T-0119b).
/// Full ARES snapshot + verification/erasure flags + the denormalized
/// catalog stats + the T-0140 fee override — everything the admin
/// actions on this page (verify / deactivate / refresh-ARES /
/// fee-override) need for context. No GDPR redaction — admin is
/// privileged (T-0111 precedent); the detail READ is audited via
/// <c>IAdminReadAuditWriter</c> (T-0137 — this page carries PII).
/// </summary>
public sealed record AdminMakerDetailDto(
    string MakerId,
    string UserId,
    string UserEmail,
    string CompanyName,
    string RegistrationNumber,
    string? VatId,
    string? LegalForm,
    string Slug,
    string City,
    bool IsVerified,
    bool IsActive,
    bool IsActiveInRegistry,
    bool SnapshotIsStale,
    DateTimeOffset SnapshotFetchedAt,
    int? FeeRateOverrideBp,
    int RatingAverageBp,
    int RatingCount,
    int TotalOrders,
    bool PersonalPickupEnabled,
    bool IsRetainedForLegal,
    DateTimeOffset CreatedAt);
