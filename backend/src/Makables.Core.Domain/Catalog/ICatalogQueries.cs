using Makables.Core.Domain.Common;

namespace Makables.Core.Domain.Catalog;

/// <summary>
/// Read-side query surface for the public catalog (T-0043+). These are
/// projection queries — they do NOT load aggregates; they shape rows
/// straight into DTOs with <c>AsNoTracking</c> for catalog performance.
/// Kept as an interface in <c>Core.Domain</c> (implemented in
/// <c>Infra.Database</c>) so the LINQ/EF lives in the infra layer and
/// <c>Core.AppServices</c> handlers stay free of
/// <c>Microsoft.EntityFrameworkCore</c> (CLAUDE.md).
/// </summary>
public interface ICatalogQueries
{
    /// <summary>
    /// Paged maker list for the catalog (US-customer-0007). Only
    /// publicly-listable makers (active + user active + email confirmed)
    /// in a serviced country appear. Filters: category (via
    /// maker_categories), city (partial, case-insensitive), minimum
    /// rating. Sorted by rating average desc, then total orders desc.
    /// </summary>
    Task<PagedData<MakerListItem>> GetPagedMakersAsync(
        CatalogFilter filter,
        CancellationToken cancellationToken);

    /// <summary>
    /// Public maker profile by slug (US-customer-0008). Returns the
    /// maker's bio + rating + city, plus all active products. Null when
    /// the slug doesn't resolve to a publicly-listable maker (inactive /
    /// unconfirmed → treated as not found, same gate as the list).
    /// Includes the latest 5 active reviews with maker replies (T-0050).
    /// </summary>
    Task<MakerProfile?> GetMakerBySlugAsync(
        string slug,
        CancellationToken cancellationToken);

    /// <summary>
    /// Public product detail by id (US-customer-0009). Returns the
    /// product + all its images + the owning maker's display info (name
    /// + slug for the "by {maker}" link). Null when the product is
    /// inactive OR its maker isn't publicly-listable (active +
    /// confirmed) — a hidden product isn't probeable by id.
    /// </summary>
    Task<ProductDetail?> GetProductByIdAsync(
        string productId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Filter + paging input for <see cref="ICatalogQueries.GetPagedMakersAsync"/>.
/// All filter fields are optional; null/blank means "no constraint".
/// <see cref="Page"/> is 1-based.
/// </summary>
public sealed record CatalogFilter(
    string CountryCode,
    string? CategorySlug,
    string? City,
    int? MinRatingStars,
    int Page,
    int PageSize);

/// <summary>
/// One row in the catalog maker list. Denormalized for the card UI —
/// the city comes from the maker's registered address, the rating from
/// the maker's denormalized stats.
/// </summary>
public sealed record MakerListItem(
    string MakerId,
    string Slug,
    string CompanyName,
    string? Bio,
    string City,
    bool IsVerified,
    int RatingAverageBp,
    int RatingCount,
    int TotalOrders);

/// <summary>
/// Public maker-profile detail (US-customer-0008). The header fields +
/// the maker's active products. <see cref="Reviews"/> is empty until
/// T-0050 ships review reads — kept on the contract now so the frontend
/// + NSwag client are forward-compatible.
/// </summary>
public sealed record MakerProfile(
    string MakerId,
    string Slug,
    string CompanyName,
    string? Bio,
    string? LegalForm,
    string City,
    bool IsVerified,
    bool PersonalPickupEnabled,
    string? PickupNote,
    int RatingAverageBp,
    int RatingCount,
    int TotalOrders,
    IReadOnlyList<MakerProductItem> Products,
    IReadOnlyList<MakerReviewItem> Reviews);

/// <summary>An active product on a maker's public profile.</summary>
public sealed record MakerProductItem(
    string ProductId,
    string Title,
    long PriceAmountMinor,
    string PriceCurrency,
    string PriceType,
    string FulfillmentType,
    string? PrimaryImageBlobPath);

/// <summary>
/// A recent review on the maker profile (T-0050). The latest 5 active
/// reviews, newest-first (US-customer-0008 AC-3), each with the maker's
/// reply when one exists. Flat nullable reply fields (not a nested
/// record) — the reply is a single overwritable text per review
/// (T-0100 Q4 lock), so a nested shape would add wire noise for no
/// modelling gain. No author field: the public surface deliberately
/// carries no customer-identifying data (GDPR data minimisation,
/// mirrors the T-0081 customer-email lock).
/// </summary>
public sealed record MakerReviewItem(
    string ReviewId,
    int RatingStars,
    string? Comment,
    DateTimeOffset CreatedAt,
    string? ReplyBody,
    DateTimeOffset? ReplyCreatedAt);

/// <summary>
/// Public product-detail view (US-customer-0009). The product fields +
/// all images + the owning maker's display info for the "by {maker}"
/// link back to the profile page.
/// </summary>
public sealed record ProductDetail(
    string ProductId,
    string Title,
    string? Description,
    long PriceAmountMinor,
    string PriceCurrency,
    string PriceType,
    string FulfillmentType,
    int WeightGrams,
    string CategoryId,
    string MakerId,
    string MakerSlug,
    string MakerCompanyName,
    bool MakerIsVerified,
    IReadOnlyList<ProductImageItem> Images);

/// <summary>One image on the product detail page, ordered by sort order.</summary>
public sealed record ProductImageItem(
    string ImageId,
    string BlobPath,
    int SortOrder);
