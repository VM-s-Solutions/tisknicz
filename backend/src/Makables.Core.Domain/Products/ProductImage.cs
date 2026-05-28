namespace Makables.Core.Domain.Products;

/// <summary>
/// An image attached to a <see cref="Product"/>. Owned by the parent —
/// products cascade-delete their images. Stored in the
/// <c>product-images</c> blob container per ADR 0011 §"Blob path
/// convention" at <c>{country_code}/products/{productId}/{filename}</c>.
///
/// <para>
/// Identity is a server-issued id (not the blob path) so an image can
/// be referenced by the maker dashboard / catalog without leaking the
/// storage layout. The blob is reachable via the product-images
/// streaming endpoint mounted on Web.Public.
/// </para>
/// </summary>
public sealed class ProductImage
{
    public string Id { get; private set; } = default!;
    public string BlobPath { get; private set; } = default!;
    public int SortOrder { get; private set; }

    private ProductImage() { }

    public static ProductImage Create(string id, string blobPath, int sortOrder)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Id is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(blobPath))
            throw new ArgumentException("BlobPath is required.", nameof(blobPath));
        return new ProductImage
        {
            Id = id,
            BlobPath = blobPath,
            SortOrder = sortOrder,
        };
    }

    internal void Reorder(int sortOrder) => SortOrder = sortOrder;
}
