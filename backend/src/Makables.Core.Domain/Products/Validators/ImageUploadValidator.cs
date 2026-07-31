using Makables.Core.Domain.Common;

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
        FileSignatures.RasterImageContentTypes;

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

        if (!FileSignatures.Matches(contentType, headerBytes))
            return Result.MagicByteMismatch;

        return Result.Valid;
    }

    /// <summary>
    /// The number of header bytes the magic-byte sniff needs (WebP's
    /// signature spans the first 12 bytes). The controller buffers at
    /// least this many before calling <see cref="Validate"/>.
    /// </summary>
    public const int RequiredHeaderBytes = FileSignatures.RequiredHeaderBytes;

    /// <summary>Canonical file extension for a validated content type — used to build the blob filename.</summary>
    public static string ExtensionFor(string contentType) =>
        FileSignatures.ExtensionFor(contentType);
}
