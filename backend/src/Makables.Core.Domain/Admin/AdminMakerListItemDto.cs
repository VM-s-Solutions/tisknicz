namespace Makables.Core.Domain.Admin;

/// <summary>
/// One row in the admin cross-tenant maker list (T-0119b /
/// US-admin-0003..0005). Privileged view — carries the account email
/// (admin sees PII per the T-0111 precedent). <c>IsActive</c> is
/// surfaced explicitly because the list ignores the soft-delete filter:
/// deactivated makers stay visible for reconciliation.
/// </summary>
public sealed record AdminMakerListItemDto(
    string MakerId,
    string CompanyName,
    string RegistrationNumber,
    string City,
    string UserEmail,
    bool IsVerified,
    bool IsActive,
    int? FeeRateOverrideBp,
    int RatingAverageBp,
    int TotalOrders,
    DateTimeOffset CreatedAt);
