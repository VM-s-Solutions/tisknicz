using FluentAssertions;
using Makables.Core.Domain.Orders.Validators;

namespace Makables.Tests.Domain.Orders;

/// <summary>
/// <see cref="OrderAttachmentValidator"/> coverage per T-0064: every
/// allowed MIME's happy path, magic-byte mismatches across the four
/// signatures, the 10 MiB upper bound, the 0-byte lower bound, and the
/// unsupported-type / null-content-type tail.
/// </summary>
public class OrderAttachmentValidatorTests
{
    private static readonly byte[] PdfHeader = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37, 0, 0, 0, 0];
    private static readonly byte[] JpegHeader = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0, 0, 0, 0];
    private static readonly byte[] PngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];
    private static readonly byte[] WebpHeader = [0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50];

    [Fact]
    public void Valid_pdf_passes()
    {
        OrderAttachmentValidator.Validate("application/pdf", 1024, PdfHeader)
            .Should().Be(OrderAttachmentValidator.Result.Valid);
    }

    [Fact]
    public void Valid_jpeg_passes()
    {
        OrderAttachmentValidator.Validate("image/jpeg", 1024, JpegHeader)
            .Should().Be(OrderAttachmentValidator.Result.Valid);
    }

    [Fact]
    public void Valid_png_passes()
    {
        OrderAttachmentValidator.Validate("image/png", 1024, PngHeader)
            .Should().Be(OrderAttachmentValidator.Result.Valid);
    }

    [Fact]
    public void Valid_webp_passes()
    {
        OrderAttachmentValidator.Validate("image/webp", 1024, WebpHeader)
            .Should().Be(OrderAttachmentValidator.Result.Valid);
    }

    [Fact]
    public void Oversize_file_rejected()
    {
        OrderAttachmentValidator.Validate(
            "application/pdf",
            OrderAttachmentValidator.MaxSizeBytes + 1,
            PdfHeader)
            .Should().Be(OrderAttachmentValidator.Result.TooLarge);
    }

    [Fact]
    public void Zero_size_rejected()
    {
        OrderAttachmentValidator.Validate("application/pdf", 0, PdfHeader)
            .Should().Be(OrderAttachmentValidator.Result.TooLarge);
    }

    [Fact]
    public void Exact_max_size_accepted()
    {
        // Boundary: equal to the cap is OK; one byte over is TooLarge.
        OrderAttachmentValidator.Validate(
            "application/pdf",
            OrderAttachmentValidator.MaxSizeBytes,
            PdfHeader)
            .Should().Be(OrderAttachmentValidator.Result.Valid);
    }

    [Theory]
    [InlineData("application/zip")]
    [InlineData("application/octet-stream")]
    [InlineData("model/stl")]
    [InlineData("image/gif")]
    [InlineData("")]
    [InlineData(null)]
    public void Unsupported_content_type_rejected(string? contentType)
    {
        OrderAttachmentValidator.Validate(contentType, 1024, PdfHeader)
            .Should().Be(OrderAttachmentValidator.Result.UnsupportedType);
    }

    [Fact]
    public void Declared_pdf_but_jpeg_bytes_is_magic_mismatch()
    {
        // The signature spoof case from AC-5.
        OrderAttachmentValidator.Validate("application/pdf", 1024, JpegHeader)
            .Should().Be(OrderAttachmentValidator.Result.MagicByteMismatch);
    }

    [Fact]
    public void Declared_jpeg_but_pdf_bytes_is_magic_mismatch()
    {
        OrderAttachmentValidator.Validate("image/jpeg", 1024, PdfHeader)
            .Should().Be(OrderAttachmentValidator.Result.MagicByteMismatch);
    }

    [Fact]
    public void Declared_png_but_garbage_bytes_is_magic_mismatch()
    {
        var garbage = new byte[12];  // all zeros — matches nothing
        OrderAttachmentValidator.Validate("image/png", 1024, garbage)
            .Should().Be(OrderAttachmentValidator.Result.MagicByteMismatch);
    }

    [Fact]
    public void Truncated_header_for_webp_is_magic_mismatch()
    {
        // Only 4 bytes available — WebP needs 12.
        OrderAttachmentValidator.Validate("image/webp", 1024, WebpHeader.AsSpan(0, 4))
            .Should().Be(OrderAttachmentValidator.Result.MagicByteMismatch);
    }

    [Fact]
    public void Truncated_header_for_pdf_is_magic_mismatch()
    {
        // Three bytes — PDF needs four.
        OrderAttachmentValidator.Validate("application/pdf", 1024, PdfHeader.AsSpan(0, 3))
            .Should().Be(OrderAttachmentValidator.Result.MagicByteMismatch);
    }

    [Theory]
    [InlineData("application/pdf", "pdf")]
    [InlineData("image/jpeg", "jpg")]
    [InlineData("image/png", "png")]
    [InlineData("image/webp", "webp")]
    [InlineData("image/unknown", "bin")]
    public void ExtensionFor_maps_content_type(string contentType, string expected)
    {
        OrderAttachmentValidator.ExtensionFor(contentType).Should().Be(expected);
    }
}
