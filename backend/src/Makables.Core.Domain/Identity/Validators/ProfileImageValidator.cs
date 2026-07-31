using Makables.Core.Domain.Common;

namespace Makables.Core.Domain.Identity.Validators;

/// <summary>
/// Server-side validation for profile-image uploads — maker logos and
/// user avatars — per ADR 0011 §"Uploads". Pure helper, no I/O, so the
/// upload controller can call it against the buffered header bytes.
///
/// <para>
/// Its own allow-list + cap rather than a reuse of
/// <see cref="Products.Validators.ImageUploadValidator"/>: ADR 0011
/// specifies limits per concern, and profile images want a TIGHTER cap
/// than product photos. A logo renders at 48–112 px in the catalog grid
/// and gets edge-resized by <c>next/image</c>, so 2 MB is generous for
/// the source while bounding what a single account can push into
/// storage. The JPEG / PNG / WebP signature table is shared via
/// <see cref="FileSignatures"/>.
/// </para>
/// </summary>
public static class ProfileImageValidator
{
    /// <summary>2 MB — profile images are small by nature; see the class remarks.</summary>
    public const long MaxSizeBytes = 2 * 1024 * 1024;

    /// <summary>
    /// Header bytes the magic-byte sniff needs (WebP's signature spans
    /// the first 12). The controller buffers at least this many before
    /// calling <see cref="Validate"/>.
    /// </summary>
    public const int RequiredHeaderBytes = FileSignatures.RequiredHeaderBytes;

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
    /// Validate a profile-image upload. <paramref name="headerBytes"/>
    /// must be at least the first <see cref="RequiredHeaderBytes"/> bytes
    /// of the file. Pass the declared <paramref name="contentType"/> and
    /// the full <paramref name="sizeBytes"/>.
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

    /// <summary>Canonical file extension for a validated content type — used to build the blob filename.</summary>
    public static string ExtensionFor(string contentType) =>
        FileSignatures.ExtensionFor(contentType);
}
