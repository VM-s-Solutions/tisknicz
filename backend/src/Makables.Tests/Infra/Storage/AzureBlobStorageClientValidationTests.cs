using Azure.Storage.Blobs;
using FluentAssertions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Storage;
using Makables.Infra.Azure.Storage.Blobs;
using Microsoft.Extensions.Logging.Abstractions;

namespace Makables.Tests.Infra.Storage;

/// <summary>
/// Unit tests for the container + path validation guard paths in
/// <see cref="AzureBlobStorageClient"/>. The real SDK calls require
/// Azurite or a recorded HTTP fixture; those are out of scope for
/// T-0042 (the integration-test backend host already proves the DI
/// wiring against a connection-string stub).
///
/// <para>
/// The <see cref="BlobServiceClient"/> here is a real instance pointed
/// at <c>UseDevelopmentStorage=true</c> — it never gets called because
/// the validation guards short-circuit before any HTTP. If a test's
/// expectations drift such that a guard doesn't fire, the test will
/// fail on a connection error to localhost:10000 (which is also a
/// signal that the guard regressed).
/// </para>
/// </summary>
public class AzureBlobStorageClientValidationTests
{
    private static AzureBlobStorageClient NewSut()
    {
        var azureClient = new BlobServiceClient("UseDevelopmentStorage=true");
        return new AzureBlobStorageClient(
            azureClient,
            NullLogger<AzureBlobStorageClient>.Instance);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown-container")]
    [InlineData("Product-Images")]            // wrong case — Azure container names are lowercase
    [InlineData("public")]                    // not one of the four launch containers
    public async Task UploadAsync_rejects_invalid_container(string? container)
    {
        var sut = NewSut();
        using var stream = new MemoryStream();

        var result = await sut.UploadAsync(
            container: container!,
            path: "cz/products/p1/x.jpg",
            content: stream,
            contentType: "image/jpeg",
            cancellationToken: CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.BlobInvalidContainer);
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/leading-slash")]
    [InlineData("backslash\\path")]
    [InlineData("traversal/../escape")]
    [InlineData("dot/./segment")]
    [InlineData("double//slash")]
    public async Task UploadAsync_rejects_invalid_path(string? path)
    {
        var sut = NewSut();
        using var stream = new MemoryStream();

        var result = await sut.UploadAsync(
            container: BlobContainer.ProductImages,
            path: path!,
            content: stream,
            contentType: "image/jpeg",
            cancellationToken: CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.BlobInvalidPath);
    }

    [Fact]
    public async Task UploadAsync_rejects_path_exceeding_azure_limit()
    {
        var sut = NewSut();
        using var stream = new MemoryStream();
        var tooLongPath = "cz/products/p1/" + new string('x', 1024);

        var result = await sut.UploadAsync(
            container: BlobContainer.ProductImages,
            path: tooLongPath,
            content: stream,
            contentType: "image/jpeg",
            cancellationToken: CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.BlobInvalidPath);
    }

    [Theory]
    [InlineData("unknown-container", "cz/x.jpg")]
    public async Task DownloadAsync_rejects_invalid_container(string container, string path)
    {
        var sut = NewSut();
        var result = await sut.DownloadAsync(container, path, CancellationToken.None);
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.BlobInvalidContainer);
    }

    [Fact]
    public async Task DeleteAsync_rejects_traversal_path()
    {
        var sut = NewSut();
        var result = await sut.DeleteAsync(
            BlobContainer.OrderAttachments, "cz/../escape", CancellationToken.None);
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.BlobInvalidPath);
    }

    [Fact]
    public async Task ExistsAsync_rejects_traversal_path()
    {
        var sut = NewSut();
        var result = await sut.ExistsAsync(
            BlobContainer.Invoices, "cz/../escape", CancellationToken.None);
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.BlobInvalidPath);
    }

    // === BlobContainer helpers ===

    [Fact]
    public void BlobContainer_All_lists_the_launch_containers()
    {
        // T-0102b adds the private "payouts" container for the weekly CSVs.
        BlobContainer.All.Should().BeEquivalentTo(new[]
        {
            "product-images",
            "order-attachments",
            "invoices",
            "maker-documents",
            "payouts",
        });
    }

    [Theory]
    [InlineData("product-images", true)]
    [InlineData("order-attachments", false)]
    [InlineData("invoices", false)]
    [InlineData("maker-documents", false)]
    [InlineData("unknown", false)]
    public void BlobContainer_IsPublicRead_only_true_for_product_images(string container, bool expected)
    {
        BlobContainer.IsPublicRead(container).Should().Be(expected);
    }
}
