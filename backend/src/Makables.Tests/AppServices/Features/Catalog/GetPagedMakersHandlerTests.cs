using FluentAssertions;
using Makables.Core.AppServices.Features.Catalog;
using Makables.Core.Domain.Catalog;
using Makables.Core.Domain.Common;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Catalog;

public class GetPagedMakersHandlerTests
{
    private readonly ICatalogQueries _catalog = Substitute.For<ICatalogQueries>();
    private readonly GetPagedMakers.Handler _sut;

    public GetPagedMakersHandlerTests()
    {
        _sut = new GetPagedMakers.Handler(_catalog);
    }

    [Fact]
    public async Task Forwards_filter_to_catalog_queries_and_returns_page()
    {
        var page = new PagedData<MakerListItem>(
            new[] { new MakerListItem("m1", "slug", "Co", null, "Praha", true, 40000, 5, 10, null) },
            Page: 1, PageSize: 24, TotalCount: 1);
        _catalog.GetPagedMakersAsync(Arg.Any<CatalogFilter>(), Arg.Any<CancellationToken>()).Returns(page);

        var result = await _sut.Handle(
            new GetPagedMakers.Query("CZ", "3d-tisk", "Praha", 4, 1, 24), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalCount.Should().Be(1);
        await _catalog.Received(1).GetPagedMakersAsync(
            Arg.Is<CatalogFilter>(f =>
                f.CountryCode == "CZ" && f.CategorySlug == "3d-tisk"
                && f.City == "Praha" && f.MinRatingStars == 4
                && f.Page == 1 && f.PageSize == 24),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Validator_rejects_page_below_one()
    {
        var v = new GetPagedMakers.Validator();
        var r = v.Validate(new GetPagedMakers.Query("CZ", null, null, null, 0, 24));
        r.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_rejects_oversize_page()
    {
        var v = new GetPagedMakers.Validator();
        var r = v.Validate(new GetPagedMakers.Query("CZ", null, null, null, 1, GetPagedMakers.MaxPageSize + 1));
        r.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_rejects_rating_out_of_range()
    {
        var v = new GetPagedMakers.Validator();
        v.Validate(new GetPagedMakers.Query("CZ", null, null, 6, 1, 24)).IsValid.Should().BeFalse();
        v.Validate(new GetPagedMakers.Query("CZ", null, null, 0, 1, 24)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_accepts_valid_query()
    {
        var v = new GetPagedMakers.Validator();
        v.Validate(new GetPagedMakers.Query("CZ", "3d-tisk", "Praha", 3, 2, 24)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validator_rejects_page_that_would_overflow_skip_offset()
    {
        // (Page - 1) * MaxPageSize would overflow Int32 — reject with a
        // typed validation failure instead of letting Skip go negative
        // (T-0043 Copilot review).
        var v = new GetPagedMakers.Validator();
        v.Validate(new GetPagedMakers.Query("CZ", null, null, null, int.MaxValue, 24))
            .IsValid.Should().BeFalse();
    }
}
