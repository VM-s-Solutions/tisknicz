using Makables.Core.Domain.Common;

namespace Makables.Core.Domain.Products;

// Money lives in the namespace `Makables.Core.Domain.Money`; the
// type's short name clashes with the sibling namespace from inside
// `Makables.Core.Domain.Products`, so signatures here use the fully-
// qualified form rather than a using alias (which loses to implicit
// sibling-namespace resolution).

/// <summary>
/// A catalog entry a maker offers for sale per role/product.md.
/// Inherits <see cref="Auditable"/> so soft-delete drops it from the
/// public catalog without losing history (existing orders still
/// reference it by FK).
///
/// <para>
/// Pricing semantics — the entity stores price as a <see cref="Money"/>
/// pair (<see cref="PriceAmountMinor"/>, <see cref="PriceCurrency"/>)
/// plus a <see cref="PriceType"/> enum. Free products require
/// <see cref="PriceType.OnRequest"/> (the order layer refuses to
/// place an order on a fixed-zero product). Currency must match the
/// maker's tenant <c>Country.DefaultCurrencyCode</c> — the
/// <c>CreateProduct</c> handler reads that from
/// <c>CountryConfiguration</c> before invoking <see cref="Create"/>.
/// </para>
///
/// <para>
/// Images are owned (cascade-delete with the product). The hard cap
/// is enforced at the application boundary via
/// <see cref="MaxImageCount"/> — the handler refuses
/// <c>AddImage</c> when the cap is reached.
/// </para>
/// </summary>
public sealed class Product : Auditable
{
    public const int MaxTitleLength = 200;
    public const int MaxDescriptionLength = 4000;
    public const int MaxImageCount = 10;

    /// <summary>FK to the owning Maker. Reassignment is not supported (delete + recreate).</summary>
    public string MakerId { get; private set; } = default!;

    /// <summary>FK to the product's primary category. Many products → one category.</summary>
    public string CategoryId { get; private set; } = default!;

    public string Title { get; private set; } = default!;
    public string? Description { get; private set; }

    public long PriceAmountMinor { get; private set; }
    public string PriceCurrency { get; private set; } = default!;
    public PriceType PriceType { get; private set; }

    /// <summary>
    /// "Na zakázku" vs. "skladem" (T-0144). Drives the checkout withdrawal-
    /// right notice; defaults to <see cref="Products.FulfillmentType.MadeToOrder"/>
    /// (the safer legal posture, and the platform's dominant use case).
    /// </summary>
    public FulfillmentType FulfillmentType { get; private set; }

    /// <summary>Item shipping weight in grams (Zásilkovna / Comgate input).</summary>
    public int WeightGrams { get; private set; }

    /// <summary>
    /// Denormalized average rating in basis points of a star (0..50000,
    /// i.e. 0..5.0 stars) over the product's ACTIVE reviews. Written only
    /// via <see cref="RecomputeRating"/> (recompute-from-rows, same Q5
    /// discipline as <c>Maker.RecomputeRating</c>).
    /// </summary>
    public int RatingAverageBp { get; private set; }

    /// <summary>Denormalized count of the product's ACTIVE reviews.</summary>
    public int RatingCount { get; private set; }

    private readonly List<ProductImage> _images = new();
    public IReadOnlyList<ProductImage> Images => _images;

    public global::Makables.Core.Domain.Money.Money Price => new(PriceAmountMinor, PriceCurrency);

    private Product() { }

    public static Product Create(
        string id,
        string makerId,
        string categoryId,
        string title,
        string? description,
        global::Makables.Core.Domain.Money.Money price,
        PriceType priceType,
        int weightGrams,
        string countryCode,
        FulfillmentType fulfillmentType = FulfillmentType.MadeToOrder)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Id is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(makerId))
            throw new ArgumentException("MakerId is required.", nameof(makerId));
        if (string.IsNullOrWhiteSpace(categoryId))
            throw new ArgumentException("CategoryId is required.", nameof(categoryId));
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Title is required.", nameof(title));

        var trimmedTitle = title.Trim();
        if (trimmedTitle.Length > MaxTitleLength)
            throw new ArgumentException($"Title must be at most {MaxTitleLength} chars (post-trim).", nameof(title));

        var trimmedDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (trimmedDescription is not null && trimmedDescription.Length > MaxDescriptionLength)
            throw new ArgumentException($"Description must be at most {MaxDescriptionLength} chars.", nameof(description));

        // Pricing invariants per role/product.md.
        if (price.AmountMinor < 0)
            throw new ArgumentException("Price cannot be negative.", nameof(price));
        if (price.AmountMinor == 0 && priceType != PriceType.OnRequest)
            throw new ArgumentException("Free products require PriceType.OnRequest.", nameof(priceType));

        if (weightGrams < 0)
            throw new ArgumentException("WeightGrams cannot be negative.", nameof(weightGrams));

        if (string.IsNullOrWhiteSpace(countryCode) || countryCode.Length != 2)
            throw new ArgumentException("CountryCode must be 2 chars (ISO 3166-1 alpha-2).", nameof(countryCode));

        return new Product
        {
            Id = id,
            MakerId = makerId,
            CategoryId = categoryId,
            Title = trimmedTitle,
            Description = trimmedDescription,
            PriceAmountMinor = price.AmountMinor,
            PriceCurrency = price.Currency,
            PriceType = priceType,
            FulfillmentType = fulfillmentType,
            WeightGrams = weightGrams,
            CountryCode = countryCode.ToUpperInvariant(),
        };
    }

    /// <summary>
    /// Maker self-service patch. Existing orders are unaffected — they
    /// hold the pricing snapshot from order time (per US-maker-0004 AC-3).
    /// </summary>
    public Product Update(
        string categoryId,
        string title,
        string? description,
        global::Makables.Core.Domain.Money.Money price,
        PriceType priceType,
        int weightGrams,
        FulfillmentType fulfillmentType = FulfillmentType.MadeToOrder)
    {
        if (string.IsNullOrWhiteSpace(categoryId))
            throw new ArgumentException("CategoryId is required.", nameof(categoryId));
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Title is required.", nameof(title));

        var trimmedTitle = title.Trim();
        if (trimmedTitle.Length > MaxTitleLength)
            throw new ArgumentException($"Title must be at most {MaxTitleLength} chars.", nameof(title));

        var trimmedDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (trimmedDescription is not null && trimmedDescription.Length > MaxDescriptionLength)
            throw new ArgumentException($"Description must be at most {MaxDescriptionLength} chars.", nameof(description));

        if (price.AmountMinor < 0)
            throw new ArgumentException("Price cannot be negative.", nameof(price));
        if (price.AmountMinor == 0 && priceType != PriceType.OnRequest)
            throw new ArgumentException("Free products require PriceType.OnRequest.", nameof(priceType));
        if (!string.Equals(price.Currency, PriceCurrency, StringComparison.Ordinal))
            throw new InvalidOperationException("Currency mismatch — currency is immutable after creation.");

        if (weightGrams < 0)
            throw new ArgumentException("WeightGrams cannot be negative.", nameof(weightGrams));

        CategoryId = categoryId;
        Title = trimmedTitle;
        Description = trimmedDescription;
        PriceAmountMinor = price.AmountMinor;
        PriceType = priceType;
        FulfillmentType = fulfillmentType;
        WeightGrams = weightGrams;
        return this;
    }

    /// <summary>
    /// Recompute the denormalized rating from the review rows. The caller
    /// (the <c>SubmitReview</c> handler) supplies values computed by an
    /// <c>AVG(rating)</c> over the product's ACTIVE reviews —
    /// recompute-from-rows is self-healing under soft-delete, a running
    /// average would drift after any admin deactivation (mirrors
    /// <c>Maker.RecomputeRating</c>).
    /// </summary>
    public Product RecomputeRating(int ratingCount, int ratingAverageBp)
    {
        if (ratingAverageBp is < 0 or > 50_000)
            throw new ArgumentOutOfRangeException(nameof(ratingAverageBp), "Rating average must be 0..50000 bp (0..5.0 stars).");
        if (ratingCount < 0)
            throw new ArgumentOutOfRangeException(nameof(ratingCount), "Rating count cannot be negative.");

        RatingCount = ratingCount;
        RatingAverageBp = ratingAverageBp;
        return this;
    }

    /// <summary>
    /// Append a new image. Throws if the per-product cap is reached —
    /// caller MUST pre-check by inspecting <see cref="Images"/>.Count
    /// and surface <see cref="BusinessErrorMessage.ProductImageLimitReached"/>
    /// to the user instead of letting the throw bubble.
    /// </summary>
    public ProductImage AddImage(string id, string blobPath)
    {
        if (_images.Count >= MaxImageCount)
            throw new InvalidOperationException($"Cannot attach more than {MaxImageCount} images to a product.");

        var nextSort = _images.Count == 0 ? 0 : _images.Max(i => i.SortOrder) + 1;
        var image = ProductImage.Create(id, blobPath, nextSort);
        _images.Add(image);
        return image;
    }

    /// <summary>
    /// Remove the image by id. Returns the removed image so the caller
    /// can delete the underlying blob (the entity doesn't know about
    /// <c>IBlobStorageClient</c>; cascade-deleting the blob is the
    /// caller's job).
    /// </summary>
    public ProductImage? RemoveImage(string imageId)
    {
        var existing = _images.FirstOrDefault(i => i.Id == imageId);
        if (existing is null) return null;
        _images.Remove(existing);
        // Compact the SortOrder so a UI rendering by SortOrder doesn't
        // get holes. Order by the PERSISTED SortOrder first — EF Core
        // doesn't guarantee the loaded collection's order, so compacting
        // by raw list position could reshuffle the gallery (T-0041
        // Copilot review). Pure in-memory rewrite; EF detects each
        // surviving image's property change.
        var ordered = _images.OrderBy(i => i.SortOrder).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].Reorder(i);
        }
        return existing;
    }
}
