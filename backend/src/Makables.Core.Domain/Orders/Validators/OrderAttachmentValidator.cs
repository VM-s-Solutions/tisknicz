using Makables.Core.Domain.Common;

namespace Makables.Core.Domain.Orders.Validators;

/// <summary>
/// Server-side validation for order-attachment uploads per ADR 0011
/// §"Uploads" + US-customer-0010 AC-1. Pure helper — no I/O — so the
/// upload controller can call it against the buffered header bytes
/// without taking a dependency on a service.
///
/// <para>
/// Parallel to <see cref="Products.Validators.ImageUploadValidator"/>
/// rather than shared: ADR 0011 §"Uploads" specifies allow-lists "per
/// concern" — order attachments admit PDF + JPEG + PNG + WebP (10 MiB),
/// product images admit JPEG + PNG + WebP only (5 MiB). Coupling the two
/// would force callers to learn both surfaces. The signature table itself
/// IS shared — see <see cref="FileSignatures"/>, extracted once profile
/// images became the third consumer.
/// </para>
///
/// <para>
/// Three checks:
/// <list type="number">
///   <item><description>Size ≤ <see cref="MaxSizeBytes"/> (10 MiB for order attachments).</description></item>
///   <item><description>Declared content-type is in the allow-list (pdf / jpeg / png / webp).</description></item>
///   <item><description>Magic-byte sniff confirms the actual bytes match the declared type — don't trust the <c>Content-Type</c> header alone (ADR 0011).</description></item>
/// </list>
/// </para>
/// </summary>
public static class OrderAttachmentValidator
{
    /// <summary>10 MiB per ADR 0011 §"Uploads" (order attachments).</summary>
    public const long MaxSizeBytes = 10 * 1024 * 1024;

    /// <summary>
    /// The number of header bytes the magic-byte sniff needs. WebP's
    /// signature spans the first 12 bytes (the longest of the four
    /// formats); PDF + JPEG + PNG fit in fewer. The controller buffers at
    /// least this many before calling <see cref="Validate"/>.
    /// </summary>
    public const int RequiredHeaderBytes = FileSignatures.RequiredHeaderBytes;

    public static readonly IReadOnlySet<string> AllowedContentTypes =
        new HashSet<string>(FileSignatures.RasterImageContentTypes, StringComparer.OrdinalIgnoreCase)
        {
            FileSignatures.Pdf,
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
    /// Validate an order-attachment upload. <paramref name="headerBytes"/>
    /// must be at least the first 12 bytes of the file (the WebP signature
    /// needs 12). Pass the declared <paramref name="contentType"/> and the
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

    /// <summary>Canonical file extension for a validated content type — used to build the blob filename.</summary>
    public static string ExtensionFor(string contentType) =>
        FileSignatures.ExtensionFor(contentType);
}
