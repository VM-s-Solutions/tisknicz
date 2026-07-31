namespace Makables.Core.Domain.Common;

/// <summary>
/// Magic-byte sniffing shared by the per-concern upload validators
/// (<c>Products.Validators.ImageUploadValidator</c>,
/// <c>Orders.Validators.OrderAttachmentValidator</c>,
/// <c>Identity.Validators.ProfileImageValidator</c>). Extracted per the
/// note those validators carry: the allow-lists and size caps stay
/// per-concern (ADR 0011 §"Uploads" specifies them that way), but the
/// signature table itself is one fact about file formats and belongs in
/// exactly one place.
///
/// <para>
/// Pure — no I/O, no allocation. Callers buffer
/// <see cref="RequiredHeaderBytes"/> from the upload stream and pass the
/// span; a short read is fine, every check length-guards first and a
/// truncated header simply fails to match.
/// </para>
/// </summary>
public static class FileSignatures
{
    /// <summary>
    /// Header bytes needed to identify any supported format. WebP's
    /// signature spans the first 12 bytes (the longest); PDF, JPEG and
    /// PNG fit in fewer.
    /// </summary>
    public const int RequiredHeaderBytes = 12;

    public const string Pdf = "application/pdf";
    public const string Jpeg = "image/jpeg";
    public const string Png = "image/png";
    public const string WebP = "image/webp";

    /// <summary>
    /// The JPEG / PNG / WebP triplet every image concern admits. Exposed
    /// as a set so validators can build their allow-list from it rather
    /// than restating the strings.
    /// </summary>
    public static readonly IReadOnlySet<string> RasterImageContentTypes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Jpeg, Png, WebP };

    /// <summary>
    /// True when <paramref name="headerBytes"/> carries the signature of
    /// the declared <paramref name="contentType"/>. An unrecognized
    /// content type is always false — the caller's allow-list is the
    /// gate, this is the corroboration (never trust the
    /// <c>Content-Type</c> header alone, ADR 0011).
    /// </summary>
    public static bool Matches(string contentType, ReadOnlySpan<byte> headerBytes)
    {
        var b = headerBytes;
        return contentType.ToLowerInvariant() switch
        {
            // PDF: 25 50 44 46 ("%PDF"). The spec (§7.5.2) tolerates up to
            // 1024 bytes of header noise before the marker, but in practice
            // every real PDF starts at offset 0; defer the lenient sniff
            // until user reports come in. T-0064 §"Technical notes".
            Pdf => b.Length >= 4
                && b[0] == 0x25 && b[1] == 0x50 && b[2] == 0x44 && b[3] == 0x46,

            // JPEG: FF D8 FF
            Jpeg => b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF,

            // PNG: 89 50 4E 47 0D 0A 1A 0A
            Png => b.Length >= 8
                && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47
                && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A,

            // WebP: "RIFF" .... "WEBP" (bytes 0-3 = RIFF, bytes 8-11 = WEBP)
            WebP => b.Length >= 12
                && b[0] == 0x52 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x46
                && b[8] == 0x57 && b[9] == 0x45 && b[10] == 0x42 && b[11] == 0x50,

            _ => false,
        };
    }

    /// <summary>Canonical file extension for a validated content type — used to build the blob filename.</summary>
    public static string ExtensionFor(string contentType) =>
        contentType.ToLowerInvariant() switch
        {
            Pdf => "pdf",
            Jpeg => "jpg",
            Png => "png",
            WebP => "webp",
            _ => "bin",
        };
}
