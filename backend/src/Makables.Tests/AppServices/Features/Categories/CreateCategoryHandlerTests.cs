using FluentAssertions;
using Makables.Core.AppServices.Features.Categories;
using Makables.Core.Domain.Categories;
using Makables.Core.Domain.Common;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Categories;

/// <summary>T-0040 — pins CreateCategory admin command (US-admin-0013 AC-1).</summary>
public class CreateCategoryHandlerTests
{
    private readonly ICategoryRepository _categories = Substitute.For<ICategoryRepository>();
    private readonly IUserSessionProvider _session = Substitute.For<IUserSessionProvider>();
    private readonly CreateCategory.Handler _sut;

    public CreateCategoryHandlerTests()
    {
        _session.GetUserId().Returns("admin-1");
        _sut = new CreateCategory.Handler(_categories, _session);
    }

    private static CreateCategory.Command ValidCommand(string? slug = null) => new(
        Id: "cat-1",
        Name: "Velkoformát",
        Slug: slug,
        Icon: null,
        Description: null,
        SortOrder: 10,
        CountryCode: "CZ",
        Notes: null);

    [Fact]
    public async Task Returns_Unauthorized_when_session_has_no_user()
    {
        _session.GetUserId().Returns((string?)null);

        var result = await _sut.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);
        await _categories.DidNotReceive().SlugExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Rejects_duplicate_slug_with_CategorySlugAlreadyExists()
    {
        _categories.SlugExistsAsync("velkoformat", Arg.Any<CancellationToken>()).Returns(true);

        var result = await _sut.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.CategorySlugAlreadyExists);
        result.Error.Type.Should().Be(ErrorType.Conflict);
        _categories.DidNotReceive().Add(Arg.Any<Category>());
    }

    [Fact]
    public async Task Happy_path_derives_slug_from_name_and_adds_the_aggregate()
    {
        _categories.SlugExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await _sut.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be("cat-1");
        result.Value.Slug.Should().Be("velkoformat");
        _categories.Received(1).Add(Arg.Is<Category>(c =>
            c.Id == "cat-1" && c.Slug == "velkoformat" && c.Name == "Velkoformát" && c.IsActive));
    }

    [Fact]
    public async Task Happy_path_accepts_admin_supplied_slug_override()
    {
        _categories.SlugExistsAsync("custom-slug", Arg.Any<CancellationToken>()).Returns(false);

        var result = await _sut.Handle(ValidCommand(slug: "custom-slug"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Slug.Should().Be("custom-slug");
    }

    [Fact]
    public void Command_carries_admin_audit_metadata()
    {
        var cmd = ValidCommand();
        cmd.ActionCode.Should().Be("category.create");
        cmd.TargetEntity.Should().Be("category");
        cmd.TargetId.Should().Be("cat-1");
    }
}
