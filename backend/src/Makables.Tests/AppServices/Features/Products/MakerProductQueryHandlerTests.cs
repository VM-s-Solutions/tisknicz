using FluentAssertions;
using Makables.Core.AppServices.Features.Products;
using Makables.Core.Domain.Catalog;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Products;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Products;

/// <summary>
/// Unit tests for the T-0049a maker dashboard read handlers.
/// <c>GetMyProducts</c> and <c>GetMyProductById</c> share the
/// session → maker resolution + IDOR shield. These tests pin both
/// shields plus the happy paths; the EF projection itself
/// (soft-delete bypass, sort order, image-list ordering) is exercised
/// by <c>MakerProductQueriesTests</c> against SQLite.
/// </summary>
public class MakerProductQueryHandlerTests
{
    private static readonly DateTimeOffset SnapshotAt = new(2026, 5, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset CreatedAt = new(2026, 5, 28, 9, 0, 0, TimeSpan.Zero);

    private readonly IMakerProductQueries _queries = Substitute.For<IMakerProductQueries>();
    private readonly IMakerRepository _makers = Substitute.For<IMakerRepository>();
    private readonly IUserSessionProvider _session = Substitute.For<IUserSessionProvider>();

    public MakerProductQueryHandlerTests()
    {
        _session.GetUserId().Returns("user-1");
        _makers.GetByUserIdAsync("user-1", Arg.Any<CancellationToken>()).Returns(ExistingMaker());
    }

    private static Makables.Core.Domain.Makers.Maker ExistingMaker() =>
        Makables.Core.Domain.Makers.Maker.Create(
            id: "maker-1", userId: "user-1", registrationNumber: "27074358",
            vatId: null, companyName: "Avast s.r.o.", legalForm: null,
            registeredAddressId: "addr-1", incorporatedOn: null,
            isActiveInRegistry: true, sourceRegistry: "ares",
            snapshotFetchedAt: SnapshotAt, snapshotIsStale: false, countryCode: "CZ");

    // === GetMyProducts ===

    [Fact]
    public async Task GetMyProducts_returns_Unauthorized_when_session_has_no_user()
    {
        _session.GetUserId().Returns((string?)null);
        var sut = new GetMyProducts.Handler(_queries, _makers, _session);

        var result = await sut.Handle(new GetMyProducts.Query(1, 24), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);
    }

    [Fact]
    public async Task GetMyProducts_returns_NotFound_when_caller_has_no_maker_row()
    {
        _makers.GetByUserIdAsync("user-1", Arg.Any<CancellationToken>()).Returns((Makables.Core.Domain.Makers.Maker?)null);
        var sut = new GetMyProducts.Handler(_queries, _makers, _session);

        var result = await sut.Handle(new GetMyProducts.Query(1, 24), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be(BusinessErrorMessage.MakerNotFound);
    }

    [Fact]
    public async Task GetMyProducts_passes_session_resolved_makerId_to_queries()
    {
        // IDOR shield: the handler must NOT take a makerId from the query;
        // it MUST forward the session-resolved one. Pin the substitute on
        // the resolved id and verify the call shape.
        var expected = new PagedData<MakerProductListItem>(
            Array.Empty<MakerProductListItem>(), 1, 24, 0);
        _queries.GetMyProductsAsync("maker-1", 1, 24, Arg.Any<CancellationToken>()).Returns(expected);
        var sut = new GetMyProducts.Handler(_queries, _makers, _session);

        var result = await sut.Handle(new GetMyProducts.Query(1, 24), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(expected);
        await _queries.Received(1).GetMyProductsAsync(
            "maker-1", 1, 24, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetMyProducts_forwards_page_and_size_unchanged()
    {
        _queries.GetMyProductsAsync("maker-1", 3, 12, Arg.Any<CancellationToken>())
            .Returns(new PagedData<MakerProductListItem>(Array.Empty<MakerProductListItem>(), 3, 12, 0));
        var sut = new GetMyProducts.Handler(_queries, _makers, _session);

        var result = await sut.Handle(new GetMyProducts.Query(3, 12), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _queries.Received(1).GetMyProductsAsync(
            "maker-1", 3, 12, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void GetMyProducts_query_has_no_maker_id_field()
    {
        // IDOR shield — owning maker resolved from session, never from input.
        var props = typeof(GetMyProducts.Query).GetProperties();
        props.Should().NotContain(p => p.Name.Equals("MakerId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetMyProducts_validator_rejects_oversized_page_size()
    {
        var validator = new GetMyProducts.Validator();

        var result = validator.Validate(new GetMyProducts.Query(1, GetMyProducts.MaxPageSize + 1));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void GetMyProducts_validator_rejects_zero_page()
    {
        var validator = new GetMyProducts.Validator();

        var result = validator.Validate(new GetMyProducts.Query(0, 24));

        result.IsValid.Should().BeFalse();
    }

    // === GetMyProductById ===

    [Fact]
    public async Task GetMyProductById_returns_Unauthorized_when_session_has_no_user()
    {
        _session.GetUserId().Returns((string?)null);
        var sut = new GetMyProductById.Handler(_queries, _makers, _session);

        var result = await sut.Handle(new GetMyProductById.Query("prod-1"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);
    }

    [Fact]
    public async Task GetMyProductById_returns_MakerNotFound_when_caller_has_no_maker_row()
    {
        _makers.GetByUserIdAsync("user-1", Arg.Any<CancellationToken>()).Returns((Makables.Core.Domain.Makers.Maker?)null);
        var sut = new GetMyProductById.Handler(_queries, _makers, _session);

        var result = await sut.Handle(new GetMyProductById.Query("prod-1"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.MakerNotFound);
    }

    [Fact]
    public async Task GetMyProductById_returns_ProductNotFound_when_query_returns_null()
    {
        // Query returns null both for "no such id" AND "owned by someone
        // else" (the EF projection collapses both into the same null).
        // The handler must emit product.notFound — no oracle for cross-
        // maker probes.
        _queries.GetMyProductByIdAsync("maker-1", "prod-X", Arg.Any<CancellationToken>())
            .Returns((MakerProductDetail?)null);
        var sut = new GetMyProductById.Handler(_queries, _makers, _session);

        var result = await sut.Handle(new GetMyProductById.Query("prod-X"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be(BusinessErrorMessage.ProductNotFound);
    }

    [Fact]
    public async Task GetMyProductById_returns_detail_on_happy_path()
    {
        var detail = new MakerProductDetail(
            ProductId: "prod-1", Title: "Hrnek", Description: "popis",
            PriceAmountMinor: 25000, PriceCurrency: "CZK", PriceType: "Fixed",
            WeightGrams: 400, CategoryId: "cat-1", IsActive: true,
            CreatedOn: CreatedAt,
            Images: Array.Empty<ProductImageItem>());
        _queries.GetMyProductByIdAsync("maker-1", "prod-1", Arg.Any<CancellationToken>())
            .Returns(detail);
        var sut = new GetMyProductById.Handler(_queries, _makers, _session);

        var result = await sut.Handle(new GetMyProductById.Query("prod-1"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Title.Should().Be("Hrnek");
    }

    [Fact]
    public async Task GetMyProductById_passes_session_resolved_makerId_to_queries()
    {
        _queries.GetMyProductByIdAsync("maker-1", "prod-1", Arg.Any<CancellationToken>())
            .Returns((MakerProductDetail?)null);
        var sut = new GetMyProductById.Handler(_queries, _makers, _session);

        await sut.Handle(new GetMyProductById.Query("prod-1"), CancellationToken.None);

        await _queries.Received(1).GetMyProductByIdAsync(
            "maker-1", "prod-1", Arg.Any<CancellationToken>());
    }
}
