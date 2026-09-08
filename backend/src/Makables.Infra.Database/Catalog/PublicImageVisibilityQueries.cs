using Makables.Core.Domain.Catalog;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Products;
using Microsoft.EntityFrameworkCore;

namespace Makables.Infra.Database.Catalog;

/// <summary>
/// EF Core implementation of <see cref="IPublicImageVisibilityQueries"/>.
///
/// <para>
/// Every predicate below is the same shape as its counterpart in
/// <see cref="CatalogQueries"/> — <c>EmailConfirmedAt != null</c> plus
/// <c>IsVerified</c>, with the global soft-delete query filter supplying the
/// active checks on product, maker and user. They are <c>AnyAsync</c> existence
/// probes rather than projections: the caller only needs the boolean, and
/// materialising a DTO to throw it away would be the kind of read this codebase
/// keeps out of hot paths.
/// </para>
/// </summary>
public sealed class PublicImageVisibilityQueries(MakablesDbContext db) : IPublicImageVisibilityQueries
{
    public async Task<bool> IsProductImageVisibleAsync(string productId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(productId)) return false;

        return await (
            from p in db.Set<Product>().AsNoTracking()
            join m in db.Set<Maker>().AsNoTracking() on p.MakerId equals m.Id
            join u in db.Set<User>().AsNoTracking() on m.UserId equals u.Id
            where p.Id == productId && u.EmailConfirmedAt != null && m.IsVerified
            select p.Id).AnyAsync(cancellationToken);
    }

    public async Task<bool> IsMakerImageVisibleAsync(string makerId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(makerId)) return false;

        return await (
            from m in db.Set<Maker>().AsNoTracking()
            join u in db.Set<User>().AsNoTracking() on m.UserId equals u.Id
            where m.Id == makerId && u.EmailConfirmedAt != null && m.IsVerified
            select m.Id).AnyAsync(cancellationToken);
    }

    public async Task<bool> IsUserAvatarVisibleAsync(string userId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId)) return false;

        return await db.Set<User>().AsNoTracking()
            .AnyAsync(u => u.Id == userId && u.EmailConfirmedAt != null, cancellationToken);
    }
}
