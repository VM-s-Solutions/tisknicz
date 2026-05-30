using FluentAssertions;
using Makables.Core.Domain.Addresses;
using Makables.Core.Domain.Catalog;
using Makables.Core.Domain.Categories;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Makers;
using Makables.Infra.Database.Catalog;
using Makables.Tests.Infra.Database;

namespace Makables.Tests.Infra.Catalog;

/// <summary>
/// DB round-trip tests for the T-0043 catalog projection against the
/// in-memory SQLite harness. Exercises the publicly-listable gate
/// (active maker + active user + confirmed email), the category / city /
/// rating filters, the default sort, and paging.
/// </summary>
public class CatalogQueriesTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-05-28T10:00:00Z");

    /// <summary>
    /// Seeds a listable maker: active user with confirmed email, an
    /// active address (for the city), and the maker row with stats.
    /// </summary>
    private static void SeedMaker(
        TestDbHarness h,
        string suffix,
        string companyName,
        string city,
        int ratingBp,
        int ratingCount,
        int totalOrders,
        bool emailConfirmed = true,
        bool userActive = true,
        string? categoryId = null)
    {
        var user = User.Create(
            id: $"user-{suffix}", email: $"{suffix}@example.cz", role: UserRole.Maker,
            fullName: "Maker Owner", countryCodePrimary: "CZ",
            emailAlreadyConfirmed: emailConfirmed,
            confirmedAt: emailConfirmed ? Now : null);
        if (!userActive) user.MarkDeactivated("admin", Now);
        h.Db.Set<User>().Add(user);

        var address = Address.Create(
            id: $"addr-{suffix}", street: "Ulice", houseNumber: "1",
            city: city, zip: "10000", countryCodeIso: "CZ", auditCountryCode: "CZ");
        h.Db.Set<Address>().Add(address);

        var maker = Maker.Create(
            id: $"maker-{suffix}", userId: $"user-{suffix}", registrationNumber: $"1000000{suffix}",
            vatId: null, companyName: companyName, legalForm: null,
            registeredAddressId: $"addr-{suffix}", incorporatedOn: null,
            isActiveInRegistry: true, sourceRegistry: "ares",
            snapshotFetchedAt: Now, snapshotIsStale: false, countryCode: "CZ",
            slug: $"maker-{suffix}");
        maker.SetCatalogStats(ratingBp, ratingCount, totalOrders);
        h.Db.Set<Maker>().Add(maker);

        if (categoryId is not null)
        {
            h.Db.Set<MakerCategory>().Add(
                MakerCategory.Link($"maker-{suffix}", categoryId, "CZ", Now));
        }
    }

    private static CatalogFilter Filter(
        string? category = null, string? city = null, int? minStars = null,
        int page = 1, int pageSize = 24) =>
        new("CZ", category, city, minStars, page, pageSize);

    [Fact]
    public async Task Returns_only_publicly_listable_makers()
    {
        using var h = TestDbHarness.Create();
        SeedMaker(h, "1", "Listable s.r.o.", "Praha", 40000, 10, 5);
        SeedMaker(h, "2", "Unconfirmed s.r.o.", "Praha", 45000, 8, 3, emailConfirmed: false);
        SeedMaker(h, "3", "InactiveUser s.r.o.", "Praha", 50000, 20, 9, userActive: false);
        await h.Db.SaveChangesAsync(default);

        var sut = new CatalogQueries(h.Db);
        var result = await sut.GetPagedMakersAsync(Filter(), default);

        result.TotalCount.Should().Be(1);
        result.Items.Should().ContainSingle().Which.CompanyName.Should().Be("Listable s.r.o.");
    }

    [Fact]
    public async Task Sorts_by_rating_then_total_orders()
    {
        using var h = TestDbHarness.Create();
        SeedMaker(h, "1", "Mid rating", "Praha", 40000, 5, 100);
        SeedMaker(h, "2", "Top rating", "Praha", 48000, 3, 1);
        SeedMaker(h, "3", "Mid rating more orders", "Praha", 40000, 5, 200);
        await h.Db.SaveChangesAsync(default);

        var sut = new CatalogQueries(h.Db);
        var result = await sut.GetPagedMakersAsync(Filter(), default);

        result.Items.Select(i => i.CompanyName).Should().ContainInOrder(
            "Top rating",                 // 48000 bp wins
            "Mid rating more orders",     // 40000 bp, 200 orders
            "Mid rating");                // 40000 bp, 100 orders
    }

    [Fact]
    public async Task City_filter_is_partial_and_case_insensitive()
    {
        using var h = TestDbHarness.Create();
        SeedMaker(h, "1", "A", "Praha 2", 40000, 1, 1);
        SeedMaker(h, "2", "B", "Brno", 40000, 1, 1);
        await h.Db.SaveChangesAsync(default);

        var sut = new CatalogQueries(h.Db);
        var result = await sut.GetPagedMakersAsync(Filter(city: "praha"), default);

        result.Items.Should().ContainSingle().Which.City.Should().Be("Praha 2");
    }

    [Fact]
    public async Task Min_rating_filter_excludes_lower_rated()
    {
        using var h = TestDbHarness.Create();
        SeedMaker(h, "1", "Four stars", "Praha", 40000, 1, 1);
        SeedMaker(h, "2", "Three stars", "Praha", 30000, 1, 1);
        await h.Db.SaveChangesAsync(default);

        var sut = new CatalogQueries(h.Db);
        var result = await sut.GetPagedMakersAsync(Filter(minStars: 4), default);

        result.Items.Should().ContainSingle().Which.CompanyName.Should().Be("Four stars");
    }

    [Fact]
    public async Task Category_filter_matches_membership()
    {
        using var h = TestDbHarness.Create();
        h.Db.Set<Category>().Add(Category.Create("cat-3d", "3D tisk", "3d-tisk", null, null, 10, "CZ"));
        await h.Db.SaveChangesAsync(default);

        SeedMaker(h, "1", "Offers 3D", "Praha", 40000, 1, 1, categoryId: "cat-3d");
        SeedMaker(h, "2", "No categories", "Praha", 45000, 1, 1);
        await h.Db.SaveChangesAsync(default);

        var sut = new CatalogQueries(h.Db);
        var result = await sut.GetPagedMakersAsync(Filter(category: "3d-tisk"), default);

        result.Items.Should().ContainSingle().Which.CompanyName.Should().Be("Offers 3D");
    }

    [Fact]
    public async Task Unknown_category_slug_returns_empty()
    {
        using var h = TestDbHarness.Create();
        SeedMaker(h, "1", "Anything", "Praha", 40000, 1, 1);
        await h.Db.SaveChangesAsync(default);

        var sut = new CatalogQueries(h.Db);
        var result = await sut.GetPagedMakersAsync(Filter(category: "nonexistent"), default);

        result.TotalCount.Should().Be(0);
        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Paging_returns_correct_slice_and_total()
    {
        using var h = TestDbHarness.Create();
        for (var i = 1; i <= 5; i++)
        {
            // Descending rating so order is deterministic: 1 highest.
            SeedMaker(h, i.ToString(), $"Maker {i}", "Praha", 50000 - i * 1000, 1, 1);
        }
        await h.Db.SaveChangesAsync(default);

        var sut = new CatalogQueries(h.Db);
        var page2 = await sut.GetPagedMakersAsync(Filter(page: 2, pageSize: 2), default);

        page2.TotalCount.Should().Be(5);
        page2.Page.Should().Be(2);
        page2.TotalPages.Should().Be(3);
        page2.HasNextPage.Should().BeTrue();
        page2.HasPreviousPage.Should().BeTrue();
        page2.Items.Should().HaveCount(2);
        // Page 1 = makers 1,2; page 2 = makers 3,4.
        page2.Items.Select(i => i.CompanyName).Should().ContainInOrder("Maker 3", "Maker 4");
    }
}
