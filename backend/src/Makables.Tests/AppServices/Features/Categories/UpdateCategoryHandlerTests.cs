using FluentAssertions;
using Makables.Core.AppServices.Features.Categories;
using Makables.Core.Domain.Categories;
using Makables.Core.Domain.Common;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Categories;

/// <summary>T-0040 — pins UpdateCategory admin command (US-admin-0013 AC-2: slug stable on rename).</summary>
public class UpdateCategoryHandlerTests
{
    private readonly ICategoryRepository _categories = Substitute.For<ICategoryRepository>();
    private readonly IUserSessionProvider _session = Substitute.For<IUserSessionProvider>();
    private readonly UpdateCategory.Handler _sut;

    public UpdateCategoryHandlerTests()
    {
        _session.GetUserId().Returns("admin-1");
        _sut = new UpdateCategory.Handler(_categories, _session);
    }

    private static Category ExistingCategory() => Category.Create(
        id: "cat-1",
        name: "Velkoformát",
        slug: null,
        icon: null,
        description: null,
        sortOrder: 10,
        countryCode: "CZ");

    [Fact]
    public async Task Returns_Unauthorized_when_session_has_no_user()
    {
        _session.GetUserId().Returns((string?)null);

        var result = await _sut.Handle(
            new UpdateCategory.Command("cat-1", "New name", null, null, 0, null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);
        await _categories.DidNotReceive().GetByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Returns_NotFound_when_category_is_missing()
    {
        _categories.GetByIdAsync("cat-1", Arg.Any<CancellationToken>()).Returns((Category?)null);

        var result = await _sut.Handle(
            new UpdateCategory.Command("cat-1", "New name", null, null, 0, null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Happy_path_renames_without_touching_slug()
    {
        var category = ExistingCategory();
        var originalSlug = category.Slug;
        _categories.GetByIdAsync("cat-1", Arg.Any<CancellationToken>()).Returns(category);

        var result = await _sut.Handle(
            new UpdateCategory.Command("cat-1", "Velký formát", "format", "Updated", 15, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        category.Name.Should().Be("Velký formát");
        category.Slug.Should().Be(originalSlug, "rename keeps the URL segment stable");
        category.Icon.Should().Be("format");
        category.SortOrder.Should().Be(15);
    }

    [Fact]
    public void Command_carries_admin_audit_metadata()
    {
        var cmd = new UpdateCategory.Command("cat-1", "Name", null, null, 0, null);
        cmd.ActionCode.Should().Be("category.update");
        cmd.TargetEntity.Should().Be("category");
        cmd.TargetId.Should().Be("cat-1");
    }
}
