using Makables.Core.Domain.Catalog;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Products;
using Microsoft.EntityFrameworkCore;

namespace Makables.Infra.Database.Products;

/// <summary>
/// EF Core read-side projection for the maker dashboard (T-0049a). All
/// queries are <c>AsNoTracking</c> and project straight into DTOs — no
/// aggregate is materialised.
///
/// <para>
/// <b>Soft-delete filter is intentionally bypassed.</b> Every query
/// here calls <c>IgnoreQueryFilters()</c> on <see cref="Product"/> so
/// the maker sees their inactive (soft-deleted) products too. The
/// catalog reads (<see cref="CatalogQueries"/>) leave the filter in
/// place; this surface is the deliberate exception per
/// <c>IMakerProductQueries</c> XML doc.
/// </para>
///
/// <para>
/// IDOR is enforced at <b>this layer</b> via the
/// <c>p.MakerId == makerId</c> predicate in every method. Callers MUST
/// resolve <c>makerId</c> from
/// <see cref="IUserSessionProvider"/> + <see cref="Makers.IMakerRepository"/>,
/// never from a request param — the queries trust the id they're given.
/// </para>
/// </summary>
public sealed class MakerProductQueries(MakablesDbContext db) : IMakerProductQueries
{
    public async Task<PagedData<MakerProductListItem>> GetMyProductsAsync(
        string makerId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(makerId))
        {
            return PagedData<MakerProductListItem>.Empty(page, pageSize);
        }

        // IgnoreQueryFilters bypasses the global Auditable soft-delete
        // filter so inactive products show up. This is the deliberate
        // dashboard contract per IMakerProductQueries — drafts and
        // recently-deactivated items must remain visible to the owner.
        var query = db.Set<Product>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => p.MakerId == makerId);

        var totalCount = await query.CountAsync(cancellationToken);
        if (totalCount == 0)
        {
            return PagedData<MakerProductListItem>.Empty(page, pageSize);
        }

        var items = await query
            // IsActive desc, then Id desc (= newest-first because ULIDs
            // are lexicographically time-ordered). We sort by Id rather
            // than CreatedAt because SQLite can't ORDER BY a
            // DateTimeOffset — same workaround as CatalogQueries
            // (T-0044 ticket doc explicitly calls this out). The DTO
            // still returns the real CreatedOn so the frontend can
            // render the timestamp.
            .OrderByDescending(p => p.IsActive)
            .ThenByDescending(p => p.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new MakerProductListItem(
                p.Id,
                p.Title,
                p.PriceAmountMinor,
                p.PriceCurrency,
                p.PriceType.ToString(),
                p.FulfillmentType.ToString(),
                p.WeightGrams,
                p.CategoryId,
                p.IsActive,
                p.Images
                    .OrderBy(i => i.SortOrder)
                    .Select(i => i.BlobPath)
                    .FirstOrDefault(),
                p.Images.Count(),
                p.CreatedAt))
            .ToListAsync(cancellationToken);

        return new PagedData<MakerProductListItem>(items, page, pageSize, totalCount);
    }

    public async Task<MakerProductDetail?> GetMyProductByIdAsync(
        string makerId,
        string productId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(makerId) || string.IsNullOrWhiteSpace(productId))
        {
            return null;
        }

        // IgnoreQueryFilters: same reasoning as the list — the
        // dashboard can edit / view a deactivated product. The
        // MakerId predicate is the IDOR shield: a product owned by
        // another maker is reported as "not found" rather than
        // surfacing a permission denial that would leak its existence.
        var detail = await db.Set<Product>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => p.Id == productId && p.MakerId == makerId)
            .Select(p => new MakerProductDetail(
                p.Id,
                p.Title,
                p.Description,
                p.PriceAmountMinor,
                p.PriceCurrency,
                p.PriceType.ToString(),
                p.FulfillmentType.ToString(),
                p.WeightGrams,
                p.CategoryId,
                p.IsActive,
                p.CreatedAt,
                p.Images
                    .OrderBy(i => i.SortOrder)
                    .Select(i => new ProductImageItem(i.Id, i.BlobPath, i.SortOrder))
                    .ToList()))
            .FirstOrDefaultAsync(cancellationToken);

        return detail;
    }
}
