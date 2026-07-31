namespace Makables.Core.Domain.Storage;

/// <summary>
/// The four launch containers per ADR 0011 §"One Azure Blob Storage
/// account, multiple containers". Names match Azure's container-name
/// rules (3..63 lowercase chars, digits, hyphens; must start with a
/// letter/digit) so they're usable verbatim by the AzureBlobStorageClient.
///
/// <list type="bullet">
///   <item><description><see cref="ProductImages"/> — <b>public read</b> (CDN-cacheable). Backend writes; anyone can read via the public-host file endpoint.</description></item>
///   <item><description><see cref="OrderAttachments"/> — private. STL / 3MF / PDF customer uploads on custom orders.</description></item>
///   <item><description><see cref="Invoices"/> — private. PDFs generated server-side.</description></item>
///   <item><description><see cref="MakerDocuments"/> — private. Tax IDs / contracts (future).</description></item>
///   <item><description><see cref="ProfileImages"/> — <b>public read</b>. Maker logos + user avatars.</description></item>
/// </list>
///
/// Even the "public" containers are served by the backend per ADR 0011 —
/// the public access just lets the future image-proxy / CDN edge fetch
/// without our own credentials. The browser only ever sees backend URLs.
/// </summary>
public static class BlobContainer
{
    public const string ProductImages = "product-images";
    public const string OrderAttachments = "order-attachments";
    public const string Invoices = "invoices";
    public const string MakerDocuments = "maker-documents";

    /// <summary>
    /// <b>Public read.</b> Maker logos (<c>{country}/makers/{makerId}/{ulid}.{ext}</c>)
    /// and user avatars (<c>{country}/avatars/{userId}/{ulid}.{ext}</c>).
    /// Both are catalog-facing identity images — a maker logo heads their
    /// public profile, an avatar sits next to the reviews they write — so
    /// they share the product-image access model: public-read container,
    /// anonymous backend streaming endpoint, CDN-cacheable.
    /// </summary>
    public const string ProfileImages = "profile-images";

    /// <summary>
    /// Private container for the weekly payout CSVs (T-0102b). Streamed to
    /// the admin host only; never directly linked from the browser.
    /// </summary>
    public const string Payouts = "payouts";

    public static readonly string[] All =
    {
        ProductImages,
        OrderAttachments,
        Invoices,
        MakerDocuments,
        Payouts,
        ProfileImages,
    };

    /// <summary>True for the two public-read containers; false for the private three.</summary>
    public static bool IsPublicRead(string container) =>
        container is ProductImages or ProfileImages;
}
