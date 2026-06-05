using Makables.Core.Domain.Common;

namespace Makables.Core.Domain.Orders;

/// <summary>
/// A customer-uploaded file attached to an <see cref="Order"/> — a reference
/// sketch, spec sheet, or other artifact the maker needs to fulfil the job
/// (US-customer-0010 AC-1 / US-maker-0010). Child of the
/// <see cref="Order"/> aggregate; cascade-deletes with the parent row at
/// the database level. Soft-delete via <see cref="Auditable.IsActive"/>
/// is independent — a soft-deleted order's attachments remain queryable
/// from the admin reconciliation path per ADR 0013.
///
/// <para>
/// <b>Path convention.</b> Stored in the <c>order-attachments</c> private
/// blob container at
/// <c>{country}/orders/{orderId}/{ulid}.{ext}</c>, where the ulid is
/// generated fresh via <see cref="IIdGenerator"/> at upload time. The
/// original filename is captured here for the download
/// <c>Content-Disposition</c> header but is NEVER embedded in the blob
/// path (sanitisation + length-cap risk + cross-tenant filename collision
/// resistance, per ADR 0011 §"Uploads").
/// </para>
///
/// <para>
/// <b>Identity is a server-issued id</b> (not the blob path) so a download
/// URL of the shape <c>/api/v1/orders/{orderId}/attachments/{attachmentId}</c>
/// does not leak the storage layout. The id is also what the maker host's
/// download endpoint (T-0064) and the future admin audit log (T-0118)
/// reference.
/// </para>
/// </summary>
public sealed class OrderAttachment : Auditable
{
    /// <summary>Wire-shape of the original-filename column.</summary>
    public const int MaxOriginalFilenameLength = 255;

    /// <summary>Wire-shape of the MIME type column. RFC 6838 caps media-type names at 127 each side; in practice
    /// a sensible upper bound for our allow-list is well under this.</summary>
    public const int MaxContentTypeLength = 127;

    /// <summary>Wire-shape of the blob-path column.</summary>
    public const int MaxBlobPathLength = 500;

    public string OrderId { get; private set; } = default!;

    /// <summary>
    /// <c>{country}/orders/{orderId}/{ulid}.{ext}</c> — opaque to this
    /// entity. The upload controller composes it; the download endpoint
    /// passes it back to <c>IBlobStorageClient.DownloadAsync</c>.
    /// </summary>
    public string BlobPath { get; private set; } = default!;

    /// <summary>
    /// Sanitized customer-supplied filename, surfaced verbatim in
    /// <c>Content-Disposition</c> on download. Sanitisation happens at the
    /// controller (strip path separators / control chars / null bytes);
    /// this property accepts the already-cleaned value.
    /// </summary>
    public string OriginalFilename { get; private set; } = default!;

    /// <summary>Allow-list MIME (PDF + JPEG + PNG + WebP at MVP).</summary>
    public string ContentType { get; private set; } = default!;

    public long SizeBytes { get; private set; }

    /// <summary>FK to the user who uploaded the file. Snapshot — never changes.</summary>
    public string UploadedByUserId { get; private set; } = default!;

    // EF Core needs a parameterless ctor.
    private OrderAttachment() { }

    /// <summary>
    /// Build a new attachment row. Validates inputs and throws
    /// <see cref="ArgumentException"/> on programmer-error inputs (per the
    /// existing <see cref="Order.Create"/> / <see cref="Products.Product.Create"/>
    /// precedent). User-input validation — size + magic-byte sniff —
    /// happens at the upload controller via
    /// <c>OrderAttachmentValidator</c>; the factory additionally enforces
    /// the MIME allow-list as a defence-in-depth invariant so a future
    /// caller that bypasses the controller (e.g. a cron / Functions
    /// caller) cannot smuggle a disallowed type past the storage layer.
    /// T-0064 reviewer N-3.
    /// </summary>
    public static OrderAttachment Create(
        string id,
        string orderId,
        string blobPath,
        string originalFilename,
        string contentType,
        long sizeBytes,
        string uploadedByUserId,
        string countryCode)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Id is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(orderId))
            throw new ArgumentException("OrderId is required.", nameof(orderId));
        if (string.IsNullOrWhiteSpace(blobPath))
            throw new ArgumentException("BlobPath is required.", nameof(blobPath));
        if (string.IsNullOrWhiteSpace(originalFilename))
            throw new ArgumentException("OriginalFilename is required.", nameof(originalFilename));
        if (string.IsNullOrWhiteSpace(contentType))
            throw new ArgumentException("ContentType is required.", nameof(contentType));
        if (string.IsNullOrWhiteSpace(uploadedByUserId))
            throw new ArgumentException("UploadedByUserId is required.", nameof(uploadedByUserId));

        var trimmedFilename = originalFilename.Trim();
        if (trimmedFilename.Length > MaxOriginalFilenameLength)
            throw new ArgumentException(
                $"OriginalFilename must be at most {MaxOriginalFilenameLength} chars.",
                nameof(originalFilename));

        var trimmedContentType = contentType.Trim();
        if (trimmedContentType.Length > MaxContentTypeLength)
            throw new ArgumentException(
                $"ContentType must be at most {MaxContentTypeLength} chars.",
                nameof(contentType));
        if (!Validators.OrderAttachmentValidator.AllowedContentTypes.Contains(trimmedContentType))
            throw new ArgumentException(
                $"ContentType '{trimmedContentType}' is not in the order-attachment allow-list. " +
                "Per ADR 0011 §\"Uploads\", only PDF / JPEG / PNG / WebP are accepted at MVP. " +
                "The upload controller's OrderAttachmentValidator should have caught this " +
                "upstream — arriving here means a future non-controller caller bypassed the " +
                "validation seam.",
                nameof(contentType));

        var trimmedBlobPath = blobPath.Trim();
        if (trimmedBlobPath.Length > MaxBlobPathLength)
            throw new ArgumentException(
                $"BlobPath must be at most {MaxBlobPathLength} chars.",
                nameof(blobPath));

        if (sizeBytes <= 0)
            throw new ArgumentException("SizeBytes must be positive.", nameof(sizeBytes));

        if (string.IsNullOrWhiteSpace(countryCode) || countryCode.Length != 2)
            throw new ArgumentException("CountryCode must be 2 chars (ISO 3166-1 alpha-2).", nameof(countryCode));

        return new OrderAttachment
        {
            Id = id,
            OrderId = orderId,
            BlobPath = trimmedBlobPath,
            OriginalFilename = trimmedFilename,
            ContentType = trimmedContentType,
            SizeBytes = sizeBytes,
            UploadedByUserId = uploadedByUserId,
            CountryCode = countryCode.ToUpperInvariant(),
        };
    }
}
