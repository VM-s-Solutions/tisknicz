using Makables.Core.Domain.Addresses;
using Makables.Core.Domain.Catalog;
using Makables.Core.Domain.Categories;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Products;
using Makables.Core.Domain.Reviews;
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
        // has no domain entity, so resolve the slugs through the Category
        // set (its global soft-delete filter drops inactive rows) and do a
        // membership check on maker_categories.
        var slugs = filter.CategorySlugs?
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (slugs is { Count: > 0 })
        {
            var categoryIds = await db.Set<Category>().AsNoTracking()
                .Where(c => slugs.Contains(c.Slug))
                .Select(c => c.Id)
                .ToListAsync(cancellationToken);

            // NO requested slug resolves to an active category → empty
            // result (not an error; a stale filter chip shouldn't 500).
            // A slug that doesn't resolve alongside ones that do is simply
            // dropped — under OR semantics it can only have widened the set.
            if (categoryIds.Count == 0)
            {
                return PagedData<MakerListItem>.Empty(filter.Page, filter.PageSize);
            }

            // OR: a maker listed under ANY selected category qualifies.
            // `Any` over the join table (not a join) keeps the row count at
            // one per maker — a join would duplicate makers that match
            // several of the selected categories and corrupt both the count
            // and the paging.
            query = query.Where(x => db.Set<MakerCategory>()
                .Any(mc => categoryIds.Contains(mc.CategoryId) && mc.MakerId == x.m.Id));
        }

        if (filter.LegalType is { } legalType)
        {
            // NULL LegalType (a legal form the registry adapter could not
            // classify) matches neither bucket — `== legalType` on a
            // nullable column already excludes NULL in SQL, which is the
            // intended behaviour, not an oversight.
            query = query.Where(x => x.m.LegalType == legalType);
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
                x.m.TotalOrders,
                x.m.LogoBlobPath))
            .ToListAsync(cancellationToken);

        return new PagedData<MakerListItem>(items, filter.Page, filter.PageSize, totalCount);
    }

    public async Task<MakerProfile?> GetMakerBySlugAsync(
        string slug,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(slug)) return null;
        var normalised = slug.Trim();

        // Same publicly-listable gate as the list: active maker + active
        // user + confirmed email. The slug is unique across active makers
        // (partial index), so FirstOrDefault is the single row or null.
        var header = await (
            from m in db.Set<Maker>().AsNoTracking()
            join u in db.Set<User>().AsNoTracking() on m.UserId equals u.Id
            join a in db.Set<Address>().AsNoTracking() on m.RegisteredAddressId equals a.Id
            where m.Slug == normalised && u.EmailConfirmedAt != null
            select new
            {
                m.Id, m.Slug, m.CompanyName, m.Bio, m.LegalForm,
                a.City, m.IsVerified, m.PersonalPickupEnabled, m.PickupNote,
                m.RatingAverageBp, m.RatingCount, m.TotalOrders,
                m.LogoBlobPath,
            }).FirstOrDefaultAsync(cancellationToken);

        if (header is null) return null;

        // Active products for this maker. Owned images are auto-included
        // (T-0041), so the primary image is the lowest-sort-order one.
        var products = await db.Set<Product>().AsNoTracking()
            .Where(p => p.MakerId == header.Id)
            // Order by Id descending — product ids are ULIDs (time-
            // ordered), so this is "newest first" without an ORDER BY on
            // a DateTimeOffset (which SQLite can't translate; keeps the
            // query provider-portable for the test harness).
            .OrderByDescending(p => p.Id)
            .Select(p => new MakerProductItem(
                p.Id,
                p.Title,
                p.PriceAmountMinor,
                p.PriceCurrency,
                p.PriceType.ToString(),
                p.FulfillmentType.ToString(),
                p.RatingAverageBp,
                p.RatingCount,
                p.Images
                    .OrderBy(i => i.SortOrder)
                    .Select(i => i.BlobPath)
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);

        // Latest 5 active reviews, newest-first (T-0050 / US-customer-0008
        // AC-3). The soft-delete query filter hides deactivated reviews.
        // Ordered by Id descending — review ids are ULIDs (time-ordered),
        // same provider-portable trick as the products above. The
        // aggregate star numbers on the header stay authoritative from
        // Maker.RatingAverageBp/RatingCount (T-0100 Q5); this list is
        // display-only.
        var reviews = await db.Set<Review>().AsNoTracking()
            .Where(r => r.MakerId == header.Id)
            .OrderByDescending(r => r.Id)
            .Take(5)
            .Select(r => new MakerReviewItem(
                r.Id,
                r.Rating,
                r.Body,
                r.CreatedAt,
                r.MakerReply,
                r.MakerReplyAt,
                // Author avatar via a correlated subquery rather than a
                // join — it must behave as a LEFT JOIN. A review outlives
                // its author: GDPR erasure hard-deletes the User row and
                // deactivation hides it behind the soft-delete filter. In
                // both cases this yields null (no avatar) and the review
                // itself still renders, which an inner join would break.
                db.Set<User>()
                    .Where(u => u.Id == r.CustomerUserId)
                    .Select(u => u.AvatarBlobPath)
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);

        return new MakerProfile(
            MakerId: header.Id,
            Slug: header.Slug,
            CompanyName: header.CompanyName,
            Bio: header.Bio,
            LegalForm: header.LegalForm,
            City: header.City,
            IsVerified: header.IsVerified,
            PersonalPickupEnabled: header.PersonalPickupEnabled,
            PickupNote: header.PickupNote,
            RatingAverageBp: header.RatingAverageBp,
            RatingCount: header.RatingCount,
            TotalOrders: header.TotalOrders,
            LogoBlobPath: header.LogoBlobPath,
            Products: products,
            Reviews: reviews);
    }

    public async Task<ProductDetail?> GetProductByIdAsync(
        string productId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(productId)) return null;

        // Active product whose owning maker is publicly-listable (active
        // maker + active user + confirmed email). The global soft-delete
        // filter handles the active checks; we add the email gate. A
        // product hidden behind an unconfirmed/inactive maker returns
        // null so it isn't probeable by id.
        var detail = await (
            from p in db.Set<Product>().AsNoTracking()
            join m in db.Set<Maker>().AsNoTracking() on p.MakerId equals m.Id
            join u in db.Set<User>().AsNoTracking() on m.UserId equals u.Id
            where p.Id == productId && u.EmailConfirmedAt != null
            select new ProductDetail(
                p.Id,
                p.Title,
                p.Description,
                p.PriceAmountMinor,
                p.PriceCurrency,
                p.PriceType.ToString(),
                p.FulfillmentType.ToString(),
                p.WeightGrams,
                p.CategoryId,
                p.RatingAverageBp,
                p.RatingCount,
                m.Id,
                m.Slug,
                m.CompanyName,
                m.IsVerified,
                m.PersonalPickupEnabled,
                m.PickupNote,
                m.LogoBlobPath,
                p.Images
                    .OrderBy(i => i.SortOrder)
                    .Select(i => new ProductImageItem(i.Id, i.BlobPath, i.SortOrder))
                    .ToList()))
            .FirstOrDefaultAsync(cancellationToken);

        return detail;
    }
}
