using Makables.Core.Domain.Products;
using Microsoft.EntityFrameworkCore;

namespace Makables.Infra.Database.Products;

/// <summary>
/// EF Core <see cref="IProductRepository"/> impl. Tracked reads on
/// <see cref="GetByIdAsync"/> because Update / AddImage / RemoveImage
/// mutate the returned aggregate.
///
/// <para>
/// Active-only filtering by the global soft-delete query filter
/// (see <c>MakablesDbContext.ApplySoftDeleteQueryFilters</c>) — no
/// explicit <c>.Where(p =&gt; p.IsActive)</c> needed.
/// </para>
///
/// <para>
/// Images are loaded eagerly via the owned-collection mapping, so a
/// single round-trip is enough for UpdateProduct / AddProductImage /
/// RemoveProductImage flows.
/// </para>
/// </summary>
public sealed class ProductRepository(MakablesDbContext db) : IProductRepository
{
    public void Add(Product product)
    {
        ArgumentNullException.ThrowIfNull(product);
        db.Set<Product>().Add(product);
    }

    public Task<Product?> GetByIdAsync(string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id)) return Task.FromResult<Product?>(null);
        // Images are eager-loaded via Navigation(p => p.Images).AutoInclude()
        // in ProductConfiguration — the cap check + RemoveImage depend on
        // a fully-populated collection, so no explicit Include here would
        // be a latent bug (T-0041 Copilot review).
        return db.Set<Product>()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public Task<Product?> GetByIdForUpdateAsync(string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id)) return Task.FromResult<Product?>(null);

        // SELECT ... FOR UPDATE row-lock held for the surrounding UoW
        // transaction — serializes concurrent rating recomputes to the
        // same product (mirrors MakerRepository.GetByIdForUpdateAsync).
        // FromSqlInterpolated bypasses the global soft-delete query
        // filter, so the is_active predicate is restated. The returned
        // instance IS tracked (the handler mutates it via
        // Product.RecomputeRating).
        return db.Set<Product>()
            .FromSqlInterpolated($@"
                SELECT * FROM products
                WHERE id = {id} AND is_active
                FOR UPDATE")
            .FirstOrDefaultAsync(cancellationToken);
    }
}
