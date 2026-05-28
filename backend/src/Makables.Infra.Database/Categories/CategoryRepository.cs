using Makables.Core.Domain.Categories;
using Microsoft.EntityFrameworkCore;

namespace Makables.Infra.Database.Categories;

/// <summary>
/// EF Core <see cref="ICategoryRepository"/> impl. Tracked reads on
/// <see cref="GetByIdAsync"/> because admin commands mutate.
///
/// <para>
/// Active-only filtering: <c>MakablesDbContext.ApplySoftDeleteQueryFilters</c>
/// attaches a global <c>IsActive</c> filter to every <see cref="Common.Auditable"/>
/// entity (so neither query needs an explicit <c>.Where(c =&gt; c.IsActive)</c>).
/// Same shape T-0033 documented on <c>MakerRepository</c>.
/// </para>
/// </summary>
public sealed class CategoryRepository(MakablesDbContext db) : ICategoryRepository
{
    public void Add(Category category)
    {
        ArgumentNullException.ThrowIfNull(category);
        db.Set<Category>().Add(category);
    }

    public Task<Category?> GetByIdAsync(string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id)) return Task.FromResult<Category?>(null);
        return db.Set<Category>()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(slug)) return Task.FromResult(false);
        var normalised = slug.Trim();
        return db.Set<Category>()
            .AnyAsync(c => c.Slug == normalised, cancellationToken);
    }
}
