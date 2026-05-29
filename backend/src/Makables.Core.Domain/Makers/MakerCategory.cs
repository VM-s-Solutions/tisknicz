namespace Makables.Core.Domain.Makers;

/// <summary>
/// Join row linking a <see cref="Maker"/> to a category it offers
/// (many-to-many). The table was created in T-0040 without a domain
/// type; T-0043 adds this lightweight mapping so the catalog
/// category-filter can query membership through LINQ.
///
/// <para>
/// Not an aggregate and not <c>Auditable</c> — it carries only the
/// composite key, the owner's tenant country, and a created stamp
/// (matching the columns the T-0040 migration created). Membership
/// management (a maker picking categories) arrives with the maker-
/// profile categories ticket; this type only needs to be queryable
/// for now.
/// </para>
/// </summary>
public sealed class MakerCategory
{
    public string MakerId { get; private set; } = default!;
    public string CategoryId { get; private set; } = default!;
    public string CountryCode { get; private set; } = default!;
    public DateTimeOffset CreatedAt { get; private set; }

    private MakerCategory() { }

    public static MakerCategory Link(string makerId, string categoryId, string countryCode, DateTimeOffset createdAt)
    {
        if (string.IsNullOrWhiteSpace(makerId))
            throw new ArgumentException("MakerId is required.", nameof(makerId));
        if (string.IsNullOrWhiteSpace(categoryId))
            throw new ArgumentException("CategoryId is required.", nameof(categoryId));
        if (string.IsNullOrWhiteSpace(countryCode) || countryCode.Length != 2)
            throw new ArgumentException("CountryCode must be 2 chars.", nameof(countryCode));

        return new MakerCategory
        {
            MakerId = makerId,
            CategoryId = categoryId,
            CountryCode = countryCode.ToUpperInvariant(),
            CreatedAt = createdAt,
        };
    }
}
