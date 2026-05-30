using FluentAssertions;
using Makables.Core.AppServices.Features.Catalog;
using Makables.Core.Domain.Catalog;
using Makables.Core.Domain.Common;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Catalog;

public class CatalogDetailHandlerTests
{
    private readonly ICatalogQueries _catalog = Substitute.For<ICatalogQueries>();

    // === GetMakerBySlug ===

    [Fact]
    public async Task GetMakerBySlug_returns_NotFound_when_query_returns_null()
    {
        _catalog.GetMakerBySlugAsync("nope", Arg.Any<CancellationToken>()).Returns((MakerProfile?)null);
        var sut = new GetMakerBySlug.Handler(_catalog);

        var result = await sut.Handle(new GetMakerBySlug.Query("nope"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task GetMakerBySlug_returns_profile()
    {
        var profile = new MakerProfile(
            "m1", "keramika", "Keramika s.r.o.", "bio", "s.r.o.", "Praha",
            true, false, null, 45000, 12, 30,
            Array.Empty<MakerProductItem>(), Array.Empty<MakerReviewItem>());
        _catalog.GetMakerBySlugAsync("keramika", Arg.Any<CancellationToken>()).Returns(profile);
        var sut = new GetMakerBySlug.Handler(_catalog);

        var result = await sut.Handle(new GetMakerBySlug.Query("keramika"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Slug.Should().Be("keramika");
    }

    // === GetProductById ===

    [Fact]
    public async Task GetProductById_returns_NotFound_when_query_returns_null()
    {
        _catalog.GetProductByIdAsync("nope", Arg.Any<CancellationToken>()).Returns((ProductDetail?)null);
        var sut = new GetProductById.Handler(_catalog);

        var result = await sut.Handle(new GetProductById.Query("nope"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task GetProductById_returns_detail()
    {
        var detail = new ProductDetail(
            "p1", "Hrnek", "popis", 25000, "CZK", "Fixed", 400, "cat-1",
            "m1", "keramika", "Keramika s.r.o.", true,
            Array.Empty<ProductImageItem>());
        _catalog.GetProductByIdAsync("p1", Arg.Any<CancellationToken>()).Returns(detail);
        var sut = new GetProductById.Handler(_catalog);

        var result = await sut.Handle(new GetProductById.Query("p1"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Title.Should().Be("Hrnek");
    }
}
