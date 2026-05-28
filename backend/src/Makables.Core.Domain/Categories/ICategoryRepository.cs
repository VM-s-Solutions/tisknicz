namespace Makables.Core.Domain.Categories;

/// <summary>
/// Persistence access for <see cref="Category"/>. T-0040 ships the
/// minimal surface needed by the admin CRUD commands (CreateCategory /
/// UpdateCategory / DeactivateCategory) plus a slug-uniqueness pre-check.
///
/// <para>
/// Read queries for the public catalog (list-all-active, by-slug) land
/// with T-0043 / T-0044 — those queries project rather than load the
/// aggregate, so they live on their own read-side surface.
/// </para>
///
/// <para>
/// Implementation lives in
/// <c>Makables.Infra.Database/Categories/CategoryRepository.cs</c>.
/// Caller drives the unit of work — the <c>UnitOfWorkPipelineBehavior</c>
/// commits when the surrounding command succeeds.
/// </para>
/// </summary>
public interface ICategoryRepository
{
    /// <summary>Mark <paramref name="category"/> as added to the change tracker.</summary>
    void Add(Category category);

    /// <summary>
    /// Load the category by its primary key for admin commands that mutate
    /// (<c>UpdateCategory</c>, <c>DeactivateCategory</c>). Tracked read —
    /// the returned aggregate flows through the UoW.
    ///
    /// <para>Active-only (the global soft-delete query filter applies).</para>
    /// </summary>
    Task<Category?> GetByIdAsync(string id, CancellationToken cancellationToken);

    /// <summary>
    /// True when an ACTIVE category exists with the given slug. Drives
    /// the slug-uniqueness pre-check on <c>CreateCategory</c>. The
    /// partial unique index <c>ix_categories_slug</c> backs this at the
    /// DB level so a TOCTOU race surfaces as the same
    /// <see cref="Common.BusinessErrorMessage.CategorySlugAlreadyExists"/>
    /// via the <c>UniqueConstraintTranslator</c>.
    /// </summary>
    Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken);
}
