using Makables.Core.Domain.Common;

namespace Makables.Core.Domain.Catalog;

/// <summary>
/// Read-side query surface for the public catalog (T-0043+). These are
/// projection queries — they do NOT load aggregates; they shape rows
/// straight into DTOs with <c>AsNoTracking</c> for catalog performance.
/// Kept as an interface in <c>Core.Domain</c> (implemented in
/// <c>Infra.Database</c>) so the LINQ/EF lives in the infra layer and
/// <c>Core.AppServices</c> handlers stay free of
/// <c>Microsoft.EntityFrameworkCore</c> (CLAUDE.md).
/// </summary>
public interface ICatalogQueries
{
    /// <summary>
    /// Paged maker list for the catalog (US-customer-0007). Only
    /// publicly-listable makers (active + user active + email confirmed)
    /// in a serviced country appear. Filters: category (via
    /// maker_categories), city (partial, case-insensitive), minimum
    /// rating. Sorted by rating average desc, then total orders desc.
    /// </summary>
    Task<PagedData<MakerListItem>> GetPagedMakersAsync(
        CatalogFilter filter,
        CancellationToken cancellationToken);
}

/// <summary>
/// Filter + paging input for <see cref="ICatalogQueries.GetPagedMakersAsync"/>.
/// All filter fields are optional; null/blank means "no constraint".
/// <see cref="Page"/> is 1-based.
/// </summary>
public sealed record CatalogFilter(
    string CountryCode,
    string? CategorySlug,
    string? City,
    int? MinRatingStars,
    int Page,
    int PageSize);

/// <summary>
/// One row in the catalog maker list. Denormalized for the card UI —
/// the city comes from the maker's registered address, the rating from
/// the maker's denormalized stats.
/// </summary>
public sealed record MakerListItem(
    string MakerId,
    string Slug,
    string CompanyName,
    string? Bio,
    string City,
    bool IsVerified,
    int RatingAverageBp,
    int RatingCount,
    int TotalOrders);
