using FluentAssertions;
using Makables.Core.Domain.Addresses;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Products;
using Makables.Infra.Database.Products;
using Makables.Tests.Infra.Database;
using Microsoft.EntityFrameworkCore;
using DomainMoney = Makables.Core.Domain.Money.Money;

namespace Makables.Tests.Infra.Products;

/// <summary>
/// DB round-trip tests for the T-0049a maker dashboard projection
/// (<see cref="MakerProductQueries"/>) against the SQLite harness.
/// Exercises the deliberate soft-delete filter bypass, the IDOR shield
/// at the predicate layer, ordering (IsActive desc, CreatedAt desc) and
/// the image-list ordering on the detail query.
/// </summary>
public class MakerProductQueriesTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-05-28T10:00:00Z");

    /// <summary>
    /// Seeds a maker with a user + address. Email confirmation / user
    /// active status are deliberately NOT checked by the dashboard
    /// queries (the maker reads their OWN data — gating against their
    /// own email-confirmation would be a UX nightmare), so the harness
    /// keeps them at sensible defaults.
    /// </summary>
    private static void SeedMaker(TestDbHarness h, string suffix)
    {
        var user = User.Create(
            id: $"user-{suffix}", email: $"{suffix}@example.cz", role: UserRole.Maker,
            fullName: "Owner", countryCodePrimary: "CZ",
            emailAlreadyConfirmed: true, confirmedAt: Now);
        h.Db.Set<User>().Add(user);

        h.Db.Set<Address>().Add(Address.Create(
            id: $"addr-{suffix}", street: "Ulice", houseNumber: "1", city: "Praha",
            zip: "10000", countryCodeIso: "CZ", auditCountryCode: "CZ"));

        var maker = Maker.Create(
            id: $"maker-{suffix}", userId: $"user-{suffix}", registrationNumber: $"1000000{suffix}",
            vatId: null, companyName: $"Maker {suffix}", legalForm: null,
            registeredAddressId: $"addr-{suffix}", incorporatedOn: null,
            isActiveInRegistry: true, sourceRegistry: "ares", snapshotFetchedAt: Now,
            snapshotIsStale: false, countryCode: "CZ", slug: $"maker-{suffix}");
        h.Db.Set<Maker>().Add(maker);
    }

    private static Product SeedProduct(
        TestDbHarness h,
        string id,
        string makerId,
        string title,
        int imageCount = 0)
    {
        var product = Product.Create(
            id: id, makerId: makerId, categoryId: "cat-1", title: title, description: "popis",
            price: new DomainMoney(25000, "CZK"), priceType: PriceType.Fixed,
            weightGrams: 400, countryCode: "CZ");
        for (var i = 0; i < imageCount; i++)
        {
            product.AddImage($"{id}-img-{i}", $"cz/products/{id}/{i}.jpg");
        }
        h.Db.Set<Product>().Add(product);
        return product;
    }

    // === GetMyProductsAsync ===

    [Fact]
    public async Task GetMyProducts_returns_owned_active_and_inactive_products()
    {
        // Core dashboard contract: the owner sees their soft-deleted
        // products too. This is the deliberate bypass of the global
        // Auditable query filter.
        using var h = TestDbHarness.Create();
        SeedMaker(h, "1");
        SeedProduct(h, "prod-active", "maker-1", "Live");
        var draft = SeedProduct(h, "prod-draft", "maker-1", "Deleted");
        await h.Db.SaveChangesAsync(default);
        draft.MarkDeactivated("maker", Now);
        await h.Db.SaveChangesAsync(default);

        var sut = new MakerProductQueries(h.Db);
        var result = await sut.GetMyProductsAsync("maker-1", 1, 24, default);

        result.TotalCount.Should().Be(2);
        result.Items.Should().HaveCount(2);
        result.Items.Select(i => i.ProductId).Should().Contain(new[] { "prod-active", "prod-draft" });
    }

    [Fact]
    public async Task GetMyProducts_excludes_other_makers_products()
    {
        // IDOR shield at the predicate level: even if a caller forwarded
        // another maker's id (the handler MUST resolve from session, but
        // belt-and-braces) the projection only returns the requested
        // maker's rows.
        using var h = TestDbHarness.Create();
        SeedMaker(h, "1");
        SeedMaker(h, "2");
        SeedProduct(h, "prod-mine", "maker-1", "Mine");
        SeedProduct(h, "prod-theirs", "maker-2", "Theirs");
        await h.Db.SaveChangesAsync(default);

        var sut = new MakerProductQueries(h.Db);
        var result = await sut.GetMyProductsAsync("maker-1", 1, 24, default);

        result.Items.Should().ContainSingle().Which.ProductId.Should().Be("prod-mine");
    }

    [Fact]
    public async Task GetMyProducts_sorts_active_first_then_newest_created()
    {
        // Active products lead so the dashboard surfaces live items
        // first; within each IsActive bucket, newest first by CreatedAt.
        using var h = TestDbHarness.Create();
        SeedMaker(h, "1");

        // Saved in order: A (active), B (active), C (active). All three
        // get the same CreatedAt from the audit interceptor (fixed
        // harness clock), so the secondary sort is the Id tiebreaker
        // (descending — ULIDs are time-ordered).
        SeedProduct(h, "prod-a", "maker-1", "A");
        SeedProduct(h, "prod-b", "maker-1", "B");
        SeedProduct(h, "prod-c", "maker-1", "C");
        await h.Db.SaveChangesAsync(default);

        // Deactivate one of the active ones — it must drop to the bottom
        // of the list regardless of its Id ordering.
        var b = await h.Db.Set<Product>().FirstAsync(p => p.Id == "prod-b");
        b.MarkDeactivated("maker", Now);
        await h.Db.SaveChangesAsync(default);

        var sut = new MakerProductQueries(h.Db);
        var result = await sut.GetMyProductsAsync("maker-1", 1, 24, default);

        // prod-c and prod-a are active (in any order at the head); prod-b
        // is the only inactive and must be last.
        result.Items.Should().HaveCount(3);
        result.Items.Last().ProductId.Should().Be("prod-b");
        result.Items.Last().IsActive.Should().BeFalse();
        result.Items.Take(2).Should().OnlyContain(i => i.IsActive);
    }

    [Fact]
    public async Task GetMyProducts_primary_image_is_lowest_sort_order()
    {
        using var h = TestDbHarness.Create();
        SeedMaker(h, "1");
        SeedProduct(h, "prod-1", "maker-1", "Has images", imageCount: 3);
        SeedProduct(h, "prod-2", "maker-1", "No images");
        await h.Db.SaveChangesAsync(default);

        var sut = new MakerProductQueries(h.Db);
        var result = await sut.GetMyProductsAsync("maker-1", 1, 24, default);

        var withImages = result.Items.Single(i => i.ProductId == "prod-1");
        withImages.PrimaryImageBlobPath.Should().Be("cz/products/prod-1/0.jpg");
        withImages.ImageCount.Should().Be(3);

        var withoutImages = result.Items.Single(i => i.ProductId == "prod-2");
        withoutImages.PrimaryImageBlobPath.Should().BeNull();
        withoutImages.ImageCount.Should().Be(0);
    }

    [Fact]
    public async Task GetMyProducts_paging_returns_correct_slice_and_total()
    {
        using var h = TestDbHarness.Create();
        SeedMaker(h, "1");
        for (var i = 1; i <= 5; i++)
        {
            SeedProduct(h, $"prod-{i}", "maker-1", $"P{i}");
        }
        await h.Db.SaveChangesAsync(default);

        var sut = new MakerProductQueries(h.Db);
        var page2 = await sut.GetMyProductsAsync("maker-1", 2, 2, default);

        page2.TotalCount.Should().Be(5);
        page2.Page.Should().Be(2);
        page2.PageSize.Should().Be(2);
        page2.TotalPages.Should().Be(3);
        page2.HasNextPage.Should().BeTrue();
        page2.HasPreviousPage.Should().BeTrue();
        page2.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetMyProducts_empty_for_maker_with_no_products()
    {
        using var h = TestDbHarness.Create();
        SeedMaker(h, "1");
        await h.Db.SaveChangesAsync(default);

        var sut = new MakerProductQueries(h.Db);
        var result = await sut.GetMyProductsAsync("maker-1", 1, 24, default);

        result.TotalCount.Should().Be(0);
        result.Items.Should().BeEmpty();
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(24);
    }

    // === GetMyProductByIdAsync ===

    [Fact]
    public async Task GetMyProductById_returns_owned_product_detail()
    {
        using var h = TestDbHarness.Create();
        SeedMaker(h, "1");
        SeedProduct(h, "prod-1", "maker-1", "Hrnek", imageCount: 2);
        await h.Db.SaveChangesAsync(default);

        var sut = new MakerProductQueries(h.Db);
        var detail = await sut.GetMyProductByIdAsync("maker-1", "prod-1", default);

        detail.Should().NotBeNull();
        detail!.Title.Should().Be("Hrnek");
        detail.PriceAmountMinor.Should().Be(25000);
        detail.PriceCurrency.Should().Be("CZK");
        detail.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task GetMyProductById_returns_soft_deleted_product()
    {
        // The dashboard edits deactivated products too. Deliberate
        // soft-delete filter bypass.
        using var h = TestDbHarness.Create();
        SeedMaker(h, "1");
        var p = SeedProduct(h, "prod-1", "maker-1", "Deactivated");
        await h.Db.SaveChangesAsync(default);
        p.MarkDeactivated("maker", Now);
        await h.Db.SaveChangesAsync(default);

        var sut = new MakerProductQueries(h.Db);
        var detail = await sut.GetMyProductByIdAsync("maker-1", "prod-1", default);

        detail.Should().NotBeNull();
        detail!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task GetMyProductById_returns_null_for_cross_maker_id()
    {
        // IDOR shield at the predicate level: a product owned by another
        // maker is invisible — same shape as "doesn't exist", no oracle.
        using var h = TestDbHarness.Create();
        SeedMaker(h, "1");
        SeedMaker(h, "2");
        SeedProduct(h, "prod-theirs", "maker-2", "Other maker");
        await h.Db.SaveChangesAsync(default);

        var sut = new MakerProductQueries(h.Db);
        var detail = await sut.GetMyProductByIdAsync("maker-1", "prod-theirs", default);

        detail.Should().BeNull();
    }

    [Fact]
    public async Task GetMyProductById_returns_null_for_unknown_id()
    {
        using var h = TestDbHarness.Create();
        SeedMaker(h, "1");
        await h.Db.SaveChangesAsync(default);

        var sut = new MakerProductQueries(h.Db);
        var detail = await sut.GetMyProductByIdAsync("maker-1", "nope", default);

        detail.Should().BeNull();
    }

    [Fact]
    public async Task GetMyProductById_returns_images_ordered_by_sort_order_ascending()
    {
        using var h = TestDbHarness.Create();
        SeedMaker(h, "1");
        SeedProduct(h, "prod-1", "maker-1", "Hrnek", imageCount: 3);
        await h.Db.SaveChangesAsync(default);

        var sut = new MakerProductQueries(h.Db);
        var detail = await sut.GetMyProductByIdAsync("maker-1", "prod-1", default);

        detail!.Images.Should().HaveCount(3);
        detail.Images.Select(i => i.SortOrder).Should().ContainInOrder(0, 1, 2);
        detail.Images[0].BlobPath.Should().Be("cz/products/prod-1/0.jpg");
    }
}
