using FluentAssertions;
using Makables.Core.AppServices.Features.Products;
using Makables.Core.Domain.Categories;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Products;
using Makables.Core.Domain.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using DomainMoney = Makables.Core.Domain.Money.Money;

namespace Makables.Tests.AppServices.Features.Products;

/// <summary>
/// Update / Delete / AddImage / RemoveImage share the same ownership
/// IDOR shield (load by id, then verify MakerId matches session-resolved
/// maker, else NotFound). These tests pin that shield plus the happy
/// paths.
/// </summary>
public class ProductMutationHandlerTests
{
    private static readonly DateTimeOffset SnapshotAt = new(2026, 5, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly IMakerRepository _makers = Substitute.For<IMakerRepository>();
    private readonly ICategoryRepository _categories = Substitute.For<ICategoryRepository>();
    private readonly IBlobStorageClient _blobs = Substitute.For<IBlobStorageClient>();
    private readonly IUserSessionProvider _session = Substitute.For<IUserSessionProvider>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly IIdGenerator _ids = Substitute.For<IIdGenerator>();

    public ProductMutationHandlerTests()
    {
        _session.GetUserId().Returns("user-1");
        _clock.UtcNow.Returns(Now);
        _ids.Next().Returns("img-1");
        _makers.GetByUserIdAsync("user-1", Arg.Any<CancellationToken>()).Returns(ExistingMaker());
        _categories.GetByIdAsync("cat-1", Arg.Any<CancellationToken>()).Returns(ExistingCategory());
    }

    private static Makables.Core.Domain.Makers.Maker ExistingMaker() =>
        Makables.Core.Domain.Makers.Maker.Create(
            id: "maker-1", userId: "user-1", registrationNumber: "27074358",
            vatId: null, companyName: "Avast s.r.o.", legalForm: null,
            registeredAddressId: "addr-1", incorporatedOn: null,
            isActiveInRegistry: true, sourceRegistry: "ares",
            snapshotFetchedAt: SnapshotAt, snapshotIsStale: false, countryCode: "CZ");

    private static Category ExistingCategory() =>
        Category.Create("cat-1", "3D tisk", null, null, null, 10, "CZ");

    private static Product OwnedProduct() => Product.Create(
        id: "prod-1", makerId: "maker-1", categoryId: "cat-1", title: "Hrnek",
        description: null, price: new DomainMoney(25000, "CZK"),
        priceType: PriceType.Fixed, weightGrams: 400, countryCode: "CZ");

    private static Product OtherMakersProduct() => Product.Create(
        id: "prod-2", makerId: "maker-OTHER", categoryId: "cat-1", title: "Foreign",
        description: null, price: new DomainMoney(25000, "CZK"),
        priceType: PriceType.Fixed, weightGrams: 400, countryCode: "CZ");

    // === UpdateProduct ===

    [Fact]
    public async Task Update_rejects_another_makers_product_as_NotFound()
    {
        _products.GetByIdAsync("prod-2", Arg.Any<CancellationToken>()).Returns(OtherMakersProduct());
        var sut = new UpdateProduct.Handler(_products, _makers, _categories, _session);

        var result = await sut.Handle(
            new UpdateProduct.Command("prod-2", "cat-1", "Hacked", null, 100, PriceType.Fixed, 1),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Update_happy_path_mutates_owned_product()
    {
        var product = OwnedProduct();
        _products.GetByIdAsync("prod-1", Arg.Any<CancellationToken>()).Returns(product);
        var sut = new UpdateProduct.Handler(_products, _makers, _categories, _session);

        var result = await sut.Handle(
            new UpdateProduct.Command("prod-1", "cat-1", "Nový hrnek", "popis", 30000, PriceType.From, 500),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        product.Title.Should().Be("Nový hrnek");
        product.PriceAmountMinor.Should().Be(30000);
    }

    // === DeleteProduct ===

    [Fact]
    public async Task Delete_rejects_another_makers_product_as_NotFound()
    {
        _products.GetByIdAsync("prod-2", Arg.Any<CancellationToken>()).Returns(OtherMakersProduct());
        var sut = new DeleteProduct.Handler(_products, _makers, _session, _clock);

        var result = await sut.Handle(new DeleteProduct.Command("prod-2"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Delete_soft_deletes_owned_product()
    {
        var product = OwnedProduct();
        _products.GetByIdAsync("prod-1", Arg.Any<CancellationToken>()).Returns(product);
        var sut = new DeleteProduct.Handler(_products, _makers, _session, _clock);

        var result = await sut.Handle(new DeleteProduct.Command("prod-1"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        product.IsActive.Should().BeFalse();
        product.DeactivatedBy.Should().Be("user-1");
        product.DeactivatedAt.Should().Be(Now);
    }

    // === AddProductImage ===

    [Fact]
    public async Task AddImage_rejects_when_cap_reached()
    {
        var product = OwnedProduct();
        for (var i = 0; i < Product.MaxImageCount; i++)
        {
            product.AddImage($"existing-{i}", $"cz/products/prod-1/{i}.jpg");
        }
        _products.GetByIdAsync("prod-1", Arg.Any<CancellationToken>()).Returns(product);
        var sut = new AddProductImage.Handler(_products, _makers, _session, _ids);

        var result = await sut.Handle(
            new AddProductImage.Command("prod-1", "cz/products/prod-1/new.jpg"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.ProductImageLimitReached);
    }

    [Fact]
    public async Task AddImage_happy_path_attaches_and_returns_id()
    {
        var product = OwnedProduct();
        _products.GetByIdAsync("prod-1", Arg.Any<CancellationToken>()).Returns(product);
        var sut = new AddProductImage.Handler(_products, _makers, _session, _ids);

        var result = await sut.Handle(
            new AddProductImage.Command("prod-1", "cz/products/prod-1/a.jpg"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ImageId.Should().Be("img-1");
        product.Images.Should().ContainSingle(i => i.BlobPath == "cz/products/prod-1/a.jpg");
    }

    // === RemoveProductImage ===

    [Fact]
    public async Task RemoveImage_deletes_blob_and_removes_from_aggregate()
    {
        var product = OwnedProduct();
        product.AddImage("img-9", "cz/products/prod-1/a.jpg");
        _products.GetByIdAsync("prod-1", Arg.Any<CancellationToken>()).Returns(product);
        _blobs.DeleteAsync(BlobContainer.ProductImages, "cz/products/prod-1/a.jpg", Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success());
        var sut = new RemoveProductImage.Handler(_products, _makers, _blobs, _session, NullLogger<RemoveProductImage.Handler>.Instance);

        var result = await sut.Handle(
            new RemoveProductImage.Command("prod-1", "img-9"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        product.Images.Should().BeEmpty();
        await _blobs.Received(1).DeleteAsync(
            BlobContainer.ProductImages, "cz/products/prod-1/a.jpg", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveImage_returns_NotFound_for_unknown_image()
    {
        var product = OwnedProduct();
        _products.GetByIdAsync("prod-1", Arg.Any<CancellationToken>()).Returns(product);
        var sut = new RemoveProductImage.Handler(_products, _makers, _blobs, _session, NullLogger<RemoveProductImage.Handler>.Instance);

        var result = await sut.Handle(
            new RemoveProductImage.Command("prod-1", "nope"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.NotFound);
        // Canonical code (not the auto-derived "productimage.notFound").
        result.Error.Code.Should().Be(BusinessErrorMessage.ProductImageNotFound);
        await _blobs.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveImage_succeeds_even_when_blob_delete_fails()
    {
        var product = OwnedProduct();
        product.AddImage("img-9", "cz/products/prod-1/a.jpg");
        _products.GetByIdAsync("prod-1", Arg.Any<CancellationToken>()).Returns(product);
        _blobs.DeleteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Failure(Error.Transient(BusinessErrorMessage.BlobOperationFailed)));
        var sut = new RemoveProductImage.Handler(_products, _makers, _blobs, _session, NullLogger<RemoveProductImage.Handler>.Instance);

        var result = await sut.Handle(
            new RemoveProductImage.Command("prod-1", "img-9"), CancellationToken.None);

        // User-visible outcome (image detached) succeeded; orphan blob logged.
        result.IsSuccess.Should().BeTrue();
        product.Images.Should().BeEmpty();
    }
}
