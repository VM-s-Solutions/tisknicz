using Makables.Core.Domain.Addresses;
using Makables.Core.Domain.Catalog;
using Makables.Core.Domain.Categories;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Makers;
using Microsoft.EntityFrameworkCore;

namespace Makables.Infra.Database.Catalog;

/// <summary>
/// EF Core read-side projection for the catalog (T-0043). All queries
/// are <c>AsNoTracking</c> and project straight into DTOs — no aggregate
/// is materialised. The global soft-delete query filter already excludes
/// inactive makers / users / addresses, so the publicly-listable gate
/// only needs the extra <c>EmailConfirmedAt is not null</c> check.
/// </summary>
public sealed class CatalogQueries(MakablesDbContext db) : ICatalogQueries
{
    // Rating is stored in basis points of a star (0..50000). A
    // MinRatingStars filter of 4 means rating_average_bp >= 40000.
    private const int BpPerStar = 10_000;

    public async Task<PagedData<MakerListItem>> GetPagedMakersAsync(
        CatalogFilter filter,
        CancellationToken cancellationToken)
    {
        var countryCode = filter.CountryCode.ToUpperInvariant();

        // Base set: active makers (global filter) in the requested
        // country whose owning user is active (global filter) AND has a
        // confirmed email. Join to User for the email gate and to
        // Address for the city.
        var query =
            from m in db.Set<Maker>().AsNoTracking()
            join u in db.Set<User>().AsNoTracking() on m.UserId equals u.Id
            join a in db.Set<Address>().AsNoTracking() on m.RegisteredAddressId equals a.Id
            where m.CountryCode == countryCode
                && u.EmailConfirmedAt != null
            select new { m, a };

        // Category filter via the maker_categories join table. The table
        // has no domain entity, so query it by its category slug through
        // the Category set + a raw membership check on maker_categories.
        if (!string.IsNullOrWhiteSpace(filter.CategorySlug))
        {
            var slug = filter.CategorySlug.Trim();
            var categoryId = await db.Set<Category>().AsNoTracking()
                .Where(c => c.Slug == slug)
                .Select(c => c.Id)
                .FirstOrDefaultAsync(cancellationToken);

            // Unknown / inactive category → empty result (not an error;
            // a stale filter chip shouldn't 500).
            if (categoryId is null)
            {
                return PagedData<MakerListItem>.Empty(filter.Page, filter.PageSize);
            }

            query = query.Where(x => db.Set<MakerCategory>()
                .Any(mc => mc.CategoryId == categoryId && mc.MakerId == x.m.Id));
        }

        if (!string.IsNullOrWhiteSpace(filter.City))
        {
            // Partial, case-insensitive match ("Praha" matches "Praha 2").
            // ToLower().Contains rather than EF.Functions.ILike so the
            // query translates on both Postgres (prod) AND SQLite (tests).
            // Postgres lowercases per the DB collation, handling Czech.
            var city = filter.City.Trim().ToLowerInvariant();
            query = query.Where(x => x.a.City.ToLower().Contains(city));
        }

        if (filter.MinRatingStars is int stars && stars > 0)
        {
            var minBp = stars * BpPerStar;
            query = query.Where(x => x.m.RatingAverageBp >= minBp);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        if (totalCount == 0)
        {
            return PagedData<MakerListItem>.Empty(filter.Page, filter.PageSize);
        }

        var items = await query
            // Default sort per US-customer-0007 AC-1.
            .OrderByDescending(x => x.m.RatingAverageBp)
            .ThenByDescending(x => x.m.TotalOrders)
            .ThenBy(x => x.m.Id)  // stable tiebreaker for deterministic paging
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(x => new MakerListItem(
                x.m.Id,
                x.m.Slug,
                x.m.CompanyName,
                x.m.Bio,
                x.a.City,
                x.m.IsVerified,
                x.m.RatingAverageBp,
                x.m.RatingCount,
                x.m.TotalOrders))
            .ToListAsync(cancellationToken);

        return new PagedData<MakerListItem>(items, filter.Page, filter.PageSize, totalCount);
    }
}
