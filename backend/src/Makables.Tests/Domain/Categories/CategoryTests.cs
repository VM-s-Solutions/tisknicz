using FluentAssertions;
using Makables.Core.Domain.Categories;

namespace Makables.Tests.Domain.Categories;

public class CategoryTests
{
    private static Category ValidDefaults() => Category.Create(
        id: "cat-1",
        name: "3D tisk",
        slug: null,
        icon: null,
        description: null,
        sortOrder: 10,
        countryCode: "CZ");

    [Fact]
    public void Create_trims_name_and_uppercases_country_code()
    {
        var c = Category.Create(
            id: "cat-1",
            name: "  3D tisk  ",
            slug: null,
            icon: "  print  ",
            description: "  Trojrozměrný tisk  ",
            sortOrder: 10,
            countryCode: "cz");

        c.Name.Should().Be("3D tisk");
        c.Icon.Should().Be("print");
        c.Description.Should().Be("Trojrozměrný tisk");
        c.CountryCode.Should().Be("CZ");
    }

    [Fact]
    public void Create_derives_slug_from_name_when_not_supplied()
    {
        var c = Category.Create(
            id: "cat-1",
            name: "Klasický tisk",
            slug: null,
            icon: null,
            description: null,
            sortOrder: 10,
            countryCode: "CZ");

        c.Slug.Should().Be("klasicky-tisk");
    }

    [Fact]
    public void Create_accepts_admin_supplied_slug_override()
    {
        var c = Category.Create(
            id: "cat-1",
            name: "Laser & CNC",
            slug: "laser-and-cnc",
            icon: null,
            description: null,
            sortOrder: 10,
            countryCode: "CZ");

        c.Slug.Should().Be("laser-and-cnc");
    }

    [Theory]
    [InlineData("3D tisk", "3d-tisk")]
    [InlineData("Klasický tisk", "klasicky-tisk")]
    [InlineData("Potisk textilu", "potisk-textilu")]
    [InlineData("Laser & CNC", "laser-cnc")]
    [InlineData("Velkoformát", "velkoformat")]
    [InlineData("Handmade", "handmade")]
    [InlineData("  Žluťoučký KŮŇ  ", "zlutoucky-kun")]
    [InlineData("Multiple   spaces!!", "multiple-spaces")]
    [InlineData("trailing-", "trailing")]
    public void Slugify_strips_diacritics_lowercases_and_collapses_separators(string input, string expected)
    {
        Category.Slugify(input).Should().Be(expected);
    }

    [Fact]
    public void Slugify_of_whitespace_returns_empty()
    {
        Category.Slugify("   ").Should().BeEmpty();
    }

    [Fact]
    public void Create_rejects_empty_id()
    {
        var act = () => Category.Create("", "Name", null, null, null, 0, "CZ");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_rejects_empty_name()
    {
        var act = () => Category.Create("cat-1", "   ", null, null, null, 0, "CZ");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_rejects_name_whose_slug_would_be_empty()
    {
        // Punctuation-only name has no slug-able chars.
        var act = () => Category.Create("cat-1", "***", null, null, null, 0, "CZ");
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("UPPER")]
    [InlineData("with space")]
    [InlineData("-leading")]
    [InlineData("trailing-")]
    [InlineData("double--dash")]
    [InlineData("with_underscore")]
    public void Create_rejects_invalid_slug_overrides(string slug)
    {
        var act = () => Category.Create("cat-1", "Name", slug, null, null, 0, "CZ");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_rejects_invalid_country_code()
    {
        var act = () => Category.Create("cat-1", "Name", null, null, null, 0, "CZE");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_starts_active()
    {
        var c = ValidDefaults();
        c.IsActive.Should().BeTrue();
    }

    [Fact]
    public void UpdateMetadata_renames_without_touching_slug()
    {
        var c = ValidDefaults();
        var originalSlug = c.Slug;

        c.UpdateMetadata(name: "3D modelování + tisk", icon: "model", description: "Updated", sortOrder: 15);

        c.Name.Should().Be("3D modelování + tisk");
        c.Slug.Should().Be(originalSlug, "rename keeps the URL segment stable per US-admin-0013 AC-2");
        c.Icon.Should().Be("model");
        c.SortOrder.Should().Be(15);
    }

    [Fact]
    public void UpdateMetadata_rejects_empty_name()
    {
        var c = ValidDefaults();

        var act = () => c.UpdateMetadata(name: "   ", icon: null, description: null, sortOrder: 0);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void MarkDeactivated_soft_deletes()
    {
        var c = ValidDefaults();

        c.MarkDeactivated("admin-1", new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));

        c.IsActive.Should().BeFalse();
        c.DeactivatedBy.Should().Be("admin-1");
    }
}
