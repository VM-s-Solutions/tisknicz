namespace Makables.Core.Domain.Categories;

/// <summary>
/// Read-side query surface for categories (T-0119). Projection queries
/// only — <c>AsNoTracking</c>, no aggregate materialised — implemented
/// in <c>Infra.Database</c> so <c>Core.AppServices</c> stays free of
/// EF Core (same split as <c>ICatalogQueries</c> / <c>IOrderQueries</c>).
/// </summary>
public interface ICategoryQueries
{
    /// <summary>
    /// Every category for the admin dashboard, INCLUDING deactivated
    /// rows (<c>IgnoreQueryFilters</c> — admin-host only caller, ADR 0013
    /// escape hatch). Ordered by sort order, then name.
    /// </summary>
    Task<IReadOnlyList<AdminCategoryItem>> GetAllForAdminAsync(
        CancellationToken cancellationToken);

    /// <summary>
    /// Active categories for a country — the public catalog filter and
    /// the maker product-creation form. Ordered by sort order, then name.
    /// </summary>
    Task<IReadOnlyList<PublicCategoryItem>> GetActiveAsync(
        string countryCode,
        CancellationToken cancellationToken);
}

/// <summary>One row in the admin category list (includes inactive rows).</summary>
public sealed record AdminCategoryItem(
    string Id,
    string Name,
    string Slug,
    string? Icon,
    string? Description,
    int SortOrder,
    string CountryCode,
    bool IsActive);

/// <summary>One active category on the public surface.</summary>
public sealed record PublicCategoryItem(
    string Id,
    string Name,
    string Slug,
    string? Icon,
    string? Description,
    int SortOrder);
