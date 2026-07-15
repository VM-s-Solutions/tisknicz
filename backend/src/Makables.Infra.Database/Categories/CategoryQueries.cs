using Makables.Core.Domain.Categories;
using Microsoft.EntityFrameworkCore;

namespace Makables.Infra.Database.Categories;

/// <summary>
/// EF Core read-side projection for categories (T-0119). Mirrors the
/// <c>CatalogQueries</c> conventions: <c>AsNoTracking</c>, DTO
/// projection, provider-portable ordering.
/// </summary>
public sealed class CategoryQueries(MakablesDbContext db) : ICategoryQueries
{
    public async Task<IReadOnlyList<AdminCategoryItem>> GetAllForAdminAsync(
        CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters: the admin list is the ONE surface that
        // shows deactivated categories (to reactivate/rename them and to
        // explain historical product FKs). Reached only from Web.Admin
        // behind the admin-audience JWT (ADR 0013).
        return await db.Set<Category>().AsNoTracking()
            .IgnoreQueryFilters()
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .Select(c => new AdminCategoryItem(
                c.Id, c.Name, c.Slug, c.Icon, c.Description,
                c.SortOrder, c.CountryCode, c.IsActive))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PublicCategoryItem>> GetActiveAsync(
        string countryCode,
        CancellationToken cancellationToken)
    {
        var normalized = countryCode.ToUpperInvariant();

        // Global soft-delete filter hides deactivated categories.
        return await db.Set<Category>().AsNoTracking()
            .Where(c => c.CountryCode == normalized)
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .Select(c => new PublicCategoryItem(
                c.Id, c.Name, c.Slug, c.Icon, c.Description, c.SortOrder))
            .ToListAsync(cancellationToken);
    }
}
