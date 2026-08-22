namespace Makables.Core.Domain.Admin;

/// <summary>
/// Server-resolved identity behind the GDPR erase screen (T-0178, audit
/// ADM-H1). The erase flow used to run entirely on identifiers the admin
/// pasted in from outside the app: the "lookup" phase verified nothing,
/// and the type-the-email interlock matched against the email the admin
/// had typed moments earlier — so the single irreversible operation in
/// the system was gated on the operator's own clipboard.
///
/// <para>
/// This is a privileged PII read (email + account state), so the
/// controller writes a <c>user.lookup</c> row through
/// <c>IAdminReadAuditWriter</c> per the T-0137 policy.
/// </para>
///
/// <para>
/// <see cref="InFlightOrderCount"/> lets the UI pre-disable the erase
/// before the admin types a confirmation the backend would reject with
/// <c>user.cannotDeleteWithInFlightOrders</c>.
/// </para>
/// </summary>
public sealed record AdminUserLookupDto(
    string UserId,
    string Email,
    string FullName,
    string Role,
    string CountryCodePrimary,
    bool IsActive,
    bool EmailConfirmed,
    /// <summary>Non-null once the account has been GDPR-erased or soft-deleted.</summary>
    DateTimeOffset? DeactivatedAt,
    DateTimeOffset CreatedAt,
    /// <summary>Maker id when this account owns a maker profile; null otherwise.</summary>
    string? MakerId,
    int InFlightOrderCount);
