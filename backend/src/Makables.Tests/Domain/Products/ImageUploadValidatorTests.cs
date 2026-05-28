using FluentAssertions;
using Makables.Core.Domain.Products.Validators;

namespace Makables.Tests.Domain.Products;

public class ImageUploadValidatorTests
{
    private static readonly byte[] JpegHeader = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0, 0, 0, 0];
    private static readonly byte[] PngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];
    private static readonly byte[] WebpHeader = [0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50];

    [Fact]
    public void Valid_jpeg_passes()
    {
        ImageUploadValidator.Validate("image/jpeg", 1024, JpegHeader)
            .Should().Be(ImageUploadValidator.Result.Valid);
    }

    [Fact]
    public void Valid_png_passes()
    {
        ImageUploadValidator.Validate("image/png", 1024, PngHeader)
            .Should().Be(ImageUploadValidator.Result.Valid);
    }

    [Fact]
    public void Valid_webp_passes()
    {
        ImageUploadValidator.Validate("image/webp", 1024, WebpHeader)
            .Should().Be(ImageUploadValidator.Result.Valid);
    }

    [Fact]
    public void Oversize_file_rejected()
    {
        ImageUploadValidator.Validate("image/jpeg", ImageUploadValidator.MaxSizeBytes + 1, JpegHeader)
            .Should().Be(ImageUploadValidator.Result.TooLarge);
    }

    [Fact]
    public void Zero_size_rejected()
    {
        ImageUploadValidator.Validate("image/jpeg", 0, JpegHeader)
            .Should().Be(ImageUploadValidator.Result.TooLarge);
    }

    [Theory]
    [InlineData("image/gif")]
    [InlineData("application/pdf")]
    [InlineData("")]
    [InlineData(null)]
    public void Unsupported_content_type_rejected(string? contentType)
    {
        ImageUploadValidator.Validate(contentType, 1024, JpegHeader)
            .Should().Be(ImageUploadValidator.Result.UnsupportedType);
    }

    [Fact]
    public void Declared_jpeg_but_png_bytes_is_magic_mismatch()
    {
        // Spoofing: client claims jpeg but the bytes are a PNG.
        ImageUploadValidator.Validate("image/jpeg", 1024, PngHeader)
            .Should().Be(ImageUploadValidator.Result.MagicByteMismatch);
    }

    [Fact]
    public void Declared_png_but_garbage_bytes_is_magic_mismatch()
    {
        var garbage = new byte[12];  // all zeros — matches nothing
        ImageUploadValidator.Validate("image/png", 1024, garbage)
            .Should().Be(ImageUploadValidator.Result.MagicByteMismatch);
    }

    [Fact]
    public void Truncated_header_for_webp_is_magic_mismatch()
    {
        // Only 4 bytes available — WebP needs 12.
        ImageUploadValidator.Validate("image/webp", 1024, WebpHeader.AsSpan(0, 4))
            .Should().Be(ImageUploadValidator.Result.MagicByteMismatch);
    }

    [Theory]
    [InlineData("image/jpeg", "jpg")]
    [InlineData("image/png", "png")]
    [InlineData("image/webp", "webp")]
    [InlineData("image/unknown", "bin")]
    public void ExtensionFor_maps_content_type(string contentType, string expected)
    {
        ImageUploadValidator.ExtensionFor(contentType).Should().Be(expected);
    }
}
