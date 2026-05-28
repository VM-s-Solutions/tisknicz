namespace Makables.Core.Domain.Products.Validators;

/// <summary>
/// Server-side image validation for product-image uploads per ADR 0011
/// §"Uploads" + US-maker-0004 AC-2. Pure helper — no I/O — so the
/// upload controller can call it against the buffered header bytes
/// without taking a dependency on a service.
///
/// <para>
/// Three checks:
/// <list type="number">
///   <item><description>Size ≤ <see cref="MaxSizeBytes"/> (5 MB for product images).</description></item>
///   <item><description>Declared content-type is in the allow-list (jpeg / png / webp).</description></item>
///   <item><description>Magic-byte sniff confirms the actual bytes match the declared type — don't trust the <c>Content-Type</c> header alone (ADR 0011).</description></item>
/// </list>
/// </para>
/// </summary>
public static class ImageUploadValidator
{
    /// <summary>5 MB per ADR 0011 §"Uploads" (product images).</summary>
    public const long MaxSizeBytes = 5 * 1024 * 1024;

    public static readonly IReadOnlySet<string> AllowedContentTypes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "image/jpeg",
            "image/png",
            "image/webp",
        };

    public enum Result
    {
        Valid,
        TooLarge,
        UnsupportedType,
        /// <summary>Header bytes don't match the declared content type (possible spoofing).</summary>
        MagicByteMismatch,
    }

    /// <summary>
    /// Validate a product image. <paramref name="headerBytes"/> must be
    /// at least the first 12 bytes of the file (the WebP signature needs
    /// 12). Pass the declared <paramref name="contentType"/> and the
    /// full <paramref name="sizeBytes"/>.
    /// </summary>
    public static Result Validate(string? contentType, long sizeBytes, ReadOnlySpan<byte> headerBytes)
    {
        if (sizeBytes <= 0 || sizeBytes > MaxSizeBytes)
            return Result.TooLarge;

        if (string.IsNullOrWhiteSpace(contentType) || !AllowedContentTypes.Contains(contentType))
            return Result.UnsupportedType;

        if (!MagicBytesMatch(contentType, headerBytes))
            return Result.MagicByteMismatch;

        return Result.Valid;
    }

    private static bool MagicBytesMatch(string contentType, ReadOnlySpan<byte> b)
    {
        return contentType.ToLowerInvariant() switch
        {
            // JPEG: FF D8 FF
            "image/jpeg" => b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF,

            // PNG: 89 50 4E 47 0D 0A 1A 0A
            "image/png" => b.Length >= 8
                && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47
                && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A,

            // WebP: "RIFF" .... "WEBP" (bytes 0-3 = RIFF, bytes 8-11 = WEBP)
            "image/webp" => b.Length >= 12
                && b[0] == 0x52 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x46
                && b[8] == 0x57 && b[9] == 0x45 && b[10] == 0x42 && b[11] == 0x50,

            _ => false,
        };
    }

    /// <summary>
    /// The number of header bytes the magic-byte sniff needs (WebP's
    /// signature spans the first 12 bytes). The controller buffers at
    /// least this many before calling <see cref="Validate"/>.
    /// </summary>
    public const int RequiredHeaderBytes = 12;

    /// <summary>Canonical file extension for a validated content type — used to build the blob filename.</summary>
    public static string ExtensionFor(string contentType) =>
        contentType.ToLowerInvariant() switch
        {
            "image/jpeg" => "jpg",
            "image/png" => "png",
            "image/webp" => "webp",
            _ => "bin",
        };
}
