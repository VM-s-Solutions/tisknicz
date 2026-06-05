using FluentAssertions;
using Makables.Core.Domain.Orders;

namespace Makables.Tests.Domain.Orders;

/// <summary>
/// <see cref="OrderAttachment.Create"/> factory invariants per T-0064.
/// The factory is the only path that produces an <see cref="OrderAttachment"/>
/// — every per-row guard (id / orderId / blob path / filename / content
/// type / size / country code) ships as an <see cref="ArgumentException"/>
/// at the boundary so a programmer-error reaches a typed throw before any
/// EF tracking or HTTP response surface.
/// </summary>
public class OrderAttachmentTests
{
    private static OrderAttachment ValidDefaults() =>
        OrderAttachment.Create(
            id: "att-1",
            orderId: "ord-1",
            blobPath: "cz/orders/ord-1/01HX.pdf",
            originalFilename: "spec.pdf",
            contentType: "application/pdf",
            sizeBytes: 1024,
            uploadedByUserId: "user-1",
            countryCode: "CZ");

    [Fact]
    public void Create_initialises_all_fields_and_uppercases_country_code()
    {
        var att = OrderAttachment.Create(
            id: "att-1",
            orderId: "ord-1",
            blobPath: "  cz/orders/ord-1/01HX.pdf  ",
            originalFilename: "  spec.pdf  ",
            contentType: "  application/pdf  ",
            sizeBytes: 1024,
            uploadedByUserId: "user-1",
            countryCode: "cz");

        att.Id.Should().Be("att-1");
        att.OrderId.Should().Be("ord-1");
        att.BlobPath.Should().Be("cz/orders/ord-1/01HX.pdf");
        att.OriginalFilename.Should().Be("spec.pdf");
        att.ContentType.Should().Be("application/pdf");
        att.SizeBytes.Should().Be(1024);
        att.UploadedByUserId.Should().Be("user-1");
        att.CountryCode.Should().Be("CZ");
        att.IsActive.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Create_rejects_blank_id(string blank)
    {
        var act = () => OrderAttachment.Create(
            id: blank,
            orderId: "ord-1",
            blobPath: "cz/orders/ord-1/01HX.pdf",
            originalFilename: "spec.pdf",
            contentType: "application/pdf",
            sizeBytes: 1024,
            uploadedByUserId: "user-1",
            countryCode: "CZ");
        act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("id");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Create_rejects_blank_filename(string blank)
    {
        var act = () => OrderAttachment.Create(
            id: "att-1",
            orderId: "ord-1",
            blobPath: "cz/orders/ord-1/01HX.pdf",
            originalFilename: blank,
            contentType: "application/pdf",
            sizeBytes: 1024,
            uploadedByUserId: "user-1",
            countryCode: "CZ");
        act.Should().Throw<ArgumentException>()
            .Which.ParamName.Should().Be("originalFilename");
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void Create_rejects_non_positive_size(long size)
    {
        var act = () => OrderAttachment.Create(
            id: "att-1",
            orderId: "ord-1",
            blobPath: "cz/orders/ord-1/01HX.pdf",
            originalFilename: "spec.pdf",
            contentType: "application/pdf",
            sizeBytes: size,
            uploadedByUserId: "user-1",
            countryCode: "CZ");
        act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("sizeBytes");
    }

    [Fact]
    public void Create_rejects_filename_exceeding_max_length()
    {
        var longName = new string('a', OrderAttachment.MaxOriginalFilenameLength + 1) + ".pdf";
        var act = () => OrderAttachment.Create(
            id: "att-1",
            orderId: "ord-1",
            blobPath: "cz/orders/ord-1/01HX.pdf",
            originalFilename: longName,
            contentType: "application/pdf",
            sizeBytes: 1024,
            uploadedByUserId: "user-1",
            countryCode: "CZ");
        act.Should().Throw<ArgumentException>()
            .Which.ParamName.Should().Be("originalFilename");
    }

    [Theory]
    [InlineData("")]
    [InlineData("C")]
    [InlineData("CZE")]
    public void Create_rejects_invalid_country_code(string country)
    {
        var act = () => OrderAttachment.Create(
            id: "att-1",
            orderId: "ord-1",
            blobPath: "cz/orders/ord-1/01HX.pdf",
            originalFilename: "spec.pdf",
            contentType: "application/pdf",
            sizeBytes: 1024,
            uploadedByUserId: "user-1",
            countryCode: country);
        act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("countryCode");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Create_rejects_blank_uploaded_by(string blank)
    {
        var act = () => OrderAttachment.Create(
            id: "att-1",
            orderId: "ord-1",
            blobPath: "cz/orders/ord-1/01HX.pdf",
            originalFilename: "spec.pdf",
            contentType: "application/pdf",
            sizeBytes: 1024,
            uploadedByUserId: blank,
            countryCode: "CZ");
        act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("uploadedByUserId");
    }

    [Fact]
    public void ValidDefaults_helper_produces_a_well_formed_attachment()
    {
        // Sanity check the test helper itself so a regression in
        // ValidDefaults doesn't silently mask every other test in this file.
        var att = ValidDefaults();
        att.OriginalFilename.Should().Be("spec.pdf");
        att.ContentType.Should().Be("application/pdf");
        att.CountryCode.Should().Be("CZ");
    }

    [Theory]
    [InlineData("application/zip")]
    [InlineData("text/plain")]
    [InlineData("application/octet-stream")]
    [InlineData("model/stl")]
    [InlineData("image/gif")]
    [InlineData("video/mp4")]
    public void Create_rejects_content_types_outside_the_allow_list(string disallowed)
    {
        // T-0064 reviewer N-3 — defence-in-depth: the upload controller's
        // OrderAttachmentValidator already enforces the allow-list, but if
        // a future non-controller caller (cron / Functions / direct
        // handler call) tries to smuggle a disallowed type past the
        // storage layer, the aggregate-child factory rejects it.
        var act = () => OrderAttachment.Create(
            id: "att-1",
            orderId: "ord-1",
            blobPath: "cz/orders/ord-1/01HX.bin",
            originalFilename: "thing.bin",
            contentType: disallowed,
            sizeBytes: 1024,
            uploadedByUserId: "user-1",
            countryCode: "CZ");

        act.Should().Throw<ArgumentException>()
            .Which.ParamName.Should().Be("contentType");
    }

    [Theory]
    [InlineData("application/pdf")]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    [InlineData("image/webp")]
    [InlineData("APPLICATION/PDF")] // case-insensitive — matches the validator's set comparer
    public void Create_accepts_every_content_type_in_the_allow_list(string allowed)
    {
        var act = () => OrderAttachment.Create(
            id: "att-1",
            orderId: "ord-1",
            blobPath: "cz/orders/ord-1/01HX.bin",
            originalFilename: "thing.bin",
            contentType: allowed,
            sizeBytes: 1024,
            uploadedByUserId: "user-1",
            countryCode: "CZ");

        act.Should().NotThrow();
    }
}
