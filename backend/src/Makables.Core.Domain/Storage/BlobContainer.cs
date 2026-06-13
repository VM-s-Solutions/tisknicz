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
/// </list>
///
/// Even the "public" container is served by the backend per ADR 0011 —
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
    };

    /// <summary>True for the single public-read container; false for the private three.</summary>
    public static bool IsPublicRead(string container) =>
        container == ProductImages;
}
