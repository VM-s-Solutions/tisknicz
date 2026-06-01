using Makables.Core.Domain.Common;

namespace Makables.Core.Domain.Products;

/// <summary>
/// Read-side query surface for the maker dashboard (T-0049a). Separate
/// from <see cref="Catalog.ICatalogQueries"/> because the maker dashboard
/// has fundamentally different visibility rules:
///
/// <list type="bullet">
///   <item><description>It includes <b>soft-deleted</b> (inactive) products — the
///     owner wants to see and potentially restore drafts the catalog hides.
///     Implementations call <c>IgnoreQueryFilters()</c> to bypass the global
///     <see cref="Auditable"/> filter.</description></item>
///   <item><description>It does NOT apply the publicly-listable maker gate
///     (active maker + active user + confirmed email). The maker is the
///     authenticated owner; whether their public profile is gated has no
///     bearing on whether they can see their own dashboard.</description></item>
///   <item><description>Every method is <b>maker-scoped</b>: the caller resolves
///     <paramref name="makerId"/> from <see cref="IUserSessionProvider"/> +
///     <see cref="Makers.IMakerRepository"/> before calling, and implementations
///     MUST enforce maker scoping in the underlying predicate
///     (e.g. <c>p.MakerId == makerId</c>) so cross-maker probes surface as
///     <c>NotFound</c> rather than leaking another maker's data. The
///     interface stays session-free so it's trivially testable, and the
///     IDOR shield is a contract obligation of every implementation —
///     not just the caller.</description></item>
/// </list>
///
/// <para>
/// Like <see cref="Catalog.ICatalogQueries"/>, projections are
/// <c>AsNoTracking</c> and shape rows straight into DTOs — no aggregate
/// is materialised. EF/LINQ lives in
/// <c>Infra.Database/Products/MakerProductQueries.cs</c>.
/// </para>
/// </summary>
public interface IMakerProductQueries
{
    /// <summary>
    /// Paged list of all products owned by <paramref name="makerId"/>,
    /// <b>including soft-deleted ones</b>. Sorted active-first
    /// (<see cref="MakerProductListItem.IsActive"/> desc) then by
    /// creation time desc — the maker's live items lead, then drafts /
    /// recently-deactivated items behind. The implementation uses
    /// <c>Id</c> desc as a proxy for "newest first" (ULIDs are
    /// lexicographically time-ordered) so the query stays SQLite-
    /// testable — SQLite can't ORDER BY a <c>DateTimeOffset</c>, same
    /// workaround as the public catalog (T-0044 ticket doc).
    ///
    /// <para>
    /// <see cref="MakerProductListItem.PrimaryImageBlobPath"/> is the
    /// product's lowest-<c>SortOrder</c> image, or null when there are
    /// none yet (a freshly-created product has no images until upload).
    /// <see cref="MakerProductListItem.ImageCount"/> lets the dashboard
    /// show "5/10 images" without joining to the gallery.
    /// </para>
    /// </summary>
    Task<PagedData<MakerProductListItem>> GetMyProductsAsync(
        string makerId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    /// <summary>
    /// Single product detail for the dashboard edit form. Returns null
    /// when the product doesn't exist OR is owned by a different maker
    /// — the handler maps both to <c>product.notFound</c> so product ids
    /// aren't enumerable across makers (IDOR shield, same shape as
    /// <c>UpdateProduct</c> / <c>AddProductImage</c>).
    ///
    /// <para>
    /// Includes soft-deleted products (the dashboard can still edit them).
    /// Images are returned ordered by <c>SortOrder</c> ascending.
    /// </para>
    /// </summary>
    Task<MakerProductDetail?> GetMyProductByIdAsync(
        string makerId,
        string productId,
        CancellationToken cancellationToken);
}

/// <summary>
/// One row in the maker dashboard product list. Carries the fields the
/// dashboard table needs to render — title, price, weight, category, the
/// active flag (drafts are marked), the primary thumbnail + count, and
/// the creation timestamp for the default "newest first" sort within
/// each <see cref="IsActive"/> bucket.
///
/// <para>
/// Distinct from <see cref="Catalog.MakerProductItem"/> (the public
/// catalog DTO): that one omits <see cref="IsActive"/> / <see cref="ImageCount"/>
/// / <see cref="CreatedOn"/> because the public catalog only shows
/// active products and never exposes drafts.
/// </para>
/// </summary>
public sealed record MakerProductListItem(
    string ProductId,
    string Title,
    long PriceAmountMinor,
    string PriceCurrency,
    string PriceType,
    int WeightGrams,
    string CategoryId,
    bool IsActive,
    string? PrimaryImageBlobPath,
    int ImageCount,
    DateTimeOffset CreatedOn);

/// <summary>
/// Maker-scoped product-detail view for the dashboard edit form. Mirrors
/// the editable shape of the entity: every field the
/// <c>UpdateProduct</c> command accepts is on this DTO. Includes
/// <see cref="IsActive"/> so the edit UI can show a "this product is
/// deactivated" banner.
///
/// <para>
/// <see cref="Images"/> reuses <see cref="Catalog.ProductImageItem"/> —
/// the shape (Id, BlobPath, SortOrder) is identical and there's no
/// reason to fork the DTO. If the maker dashboard needs extra image
/// metadata later (e.g. dimensions), introduce a maker-scoped image
/// DTO at that point; today's duplication would be premature.
/// </para>
/// </summary>
public sealed record MakerProductDetail(
    string ProductId,
    string Title,
    string? Description,
    long PriceAmountMinor,
    string PriceCurrency,
    string PriceType,
    int WeightGrams,
    string CategoryId,
    bool IsActive,
    DateTimeOffset CreatedOn,
    IReadOnlyList<Catalog.ProductImageItem> Images);
