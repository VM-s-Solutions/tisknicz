namespace Makables.Core.Domain.Products;

/// <summary>
/// Persistence access for <see cref="Product"/>. T-0041 ships the
/// minimal surface needed by the maker CRUD commands. Catalog queries
/// (T-0043 list, T-0045 detail) project rather than load the aggregate
/// and live on their own read-side surface.
///
/// <para>
/// <b>Ownership warning.</b> Callers MUST verify the loaded product's
/// <see cref="Product.MakerId"/> matches the authenticated maker before
/// mutating — the repository does NOT check (same shape as
/// <c>IMakerRepository.GetByUserIdAsync</c>'s IDOR doc).
/// </para>
///
/// <para>
/// Implementation in <c>Makables.Infra.Database/Products/ProductRepository.cs</c>.
/// </para>
/// </summary>
public interface IProductRepository
{
    /// <summary>Mark <paramref name="product"/> as added to the change tracker.</summary>
    void Add(Product product);

    /// <summary>
    /// Load the product (including its images) by primary key. Tracked
    /// read — Update / AddImage / RemoveImage mutate the returned
    /// aggregate. Active-only via the global soft-delete query filter.
    ///
    /// <para>
    /// IDOR warning: the caller MUST check
    /// <c>product.MakerId == authenticatedMakerId</c> before mutating.
    /// </para>
    /// </summary>
    Task<Product?> GetByIdAsync(string id, CancellationToken cancellationToken);

    /// <summary>
    /// Load the product by primary key under a <c>SELECT … FOR UPDATE</c>
    /// row-lock held for the surrounding UoW transaction — serializes
    /// concurrent rating recomputes to the same product (mirrors
    /// <c>IMakerRepository.GetByIdForUpdateAsync</c>, T-0100 §A.5).
    /// Active-only; returns <c>null</c> for unknown or soft-deleted ids.
    /// </summary>
    Task<Product?> GetByIdForUpdateAsync(string id, CancellationToken cancellationToken);
}
