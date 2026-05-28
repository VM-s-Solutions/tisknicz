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
        // Owned collections are loaded with the principal in EF Core by
        // default (they're treated as table-split); no Include needed.
        return db.Set<Product>()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }
}
