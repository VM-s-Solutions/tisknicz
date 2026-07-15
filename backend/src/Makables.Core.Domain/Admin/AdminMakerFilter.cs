namespace Makables.Core.Domain.Admin;

/// <summary>
/// Filter input for <see cref="IAdminQueries.GetAllMakersPagedAsync"/>
/// (T-0119b). Minimal per the T-0111 Q-E lock: a single search term
/// (company-name partial, case-insensitive, OR exact IČO match) plus
/// the verification flag. Null/blank means "no constraint".
/// </summary>
public sealed record AdminMakerFilter(
    string? Search,
    bool? IsVerified);
