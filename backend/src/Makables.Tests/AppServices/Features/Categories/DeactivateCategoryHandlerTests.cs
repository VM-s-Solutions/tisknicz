using FluentAssertions;
using Makables.Core.AppServices.Features.Categories;
using Makables.Core.Domain.Categories;
using Makables.Core.Domain.Common;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Categories;

/// <summary>T-0040 — pins DeactivateCategory admin command (US-admin-0013 AC-3).</summary>
public class DeactivateCategoryHandlerTests
{
    private static readonly DateTimeOffset DeactivatedAt = new(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly ICategoryRepository _categories = Substitute.For<ICategoryRepository>();
    private readonly IUserSessionProvider _session = Substitute.For<IUserSessionProvider>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly DeactivateCategory.Handler _sut;

    public DeactivateCategoryHandlerTests()
    {
        _clock.UtcNow.Returns(DeactivatedAt);
        _session.GetUserId().Returns("admin-1");
        _sut = new DeactivateCategory.Handler(_categories, _session, _clock);
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

        var result = await _sut.Handle(new DeactivateCategory.Command("cat-1", null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);
        await _categories.DidNotReceive().GetByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Returns_NotFound_when_category_is_missing_or_already_deactivated()
    {
        // The global soft-delete query filter makes already-deactivated
        // rows invisible to GetByIdAsync — same shape as DeactivateMaker.
        _categories.GetByIdAsync("cat-1", Arg.Any<CancellationToken>()).Returns((Category?)null);

        var result = await _sut.Handle(new DeactivateCategory.Command("cat-1", null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Happy_path_soft_deletes_and_stamps_audit_fields()
    {
        var category = ExistingCategory();
        _categories.GetByIdAsync("cat-1", Arg.Any<CancellationToken>()).Returns(category);

        var result = await _sut.Handle(new DeactivateCategory.Command("cat-1", "duplicate"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        category.IsActive.Should().BeFalse();
        category.DeactivatedBy.Should().Be("admin-1");
        category.DeactivatedAt.Should().Be(DeactivatedAt);
    }

    [Fact]
    public void Command_carries_admin_audit_metadata()
    {
        var cmd = new DeactivateCategory.Command("cat-1", "duplicate");
        cmd.ActionCode.Should().Be("category.deactivate");
        cmd.TargetEntity.Should().Be("category");
        cmd.TargetId.Should().Be("cat-1");
    }
}
