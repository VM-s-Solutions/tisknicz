using FluentAssertions;
using Makables.Core.Domain.Addresses;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Products;
using Makables.Core.Domain.Reviews;
using Makables.Infra.Database.Catalog;
using Makables.Tests.Infra.Database;
using DomainMoney = Makables.Core.Domain.Money.Money;

namespace Makables.Tests.Infra.Catalog;

/// <summary>
/// DB round-trips for the T-0044 GetMakerBySlug + T-0045 GetProductById
/// detail projections against the SQLite harness.
/// </summary>
public class CatalogDetailQueriesTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-05-28T10:00:00Z");

    private static void SeedListableMaker(TestDbHarness h, string suffix, string slug, bool emailConfirmed = true)
    {
        var user = User.Create(
            id: $"user-{suffix}", email: $"{suffix}@example.cz", role: UserRole.Maker,
            fullName: "Owner", countryCodePrimary: "CZ",
            emailAlreadyConfirmed: emailConfirmed, confirmedAt: emailConfirmed ? Now : null);
        h.Db.Set<User>().Add(user);

        h.Db.Set<Address>().Add(Address.Create(
            id: $"addr-{suffix}", street: "Ulice", houseNumber: "1", city: "Praha",
            zip: "10000", countryCodeIso: "CZ", auditCountryCode: "CZ"));

        var maker = Maker.Create(
            id: $"maker-{suffix}", userId: $"user-{suffix}", registrationNumber: $"1000000{suffix}",
            vatId: null, companyName: "Keramika s.r.o.", legalForm: "s.r.o.",
            registeredAddressId: $"addr-{suffix}", incorporatedOn: null,
            isActiveInRegistry: true, sourceRegistry: "ares", snapshotFetchedAt: Now,
            snapshotIsStale: false, countryCode: "CZ", slug: slug);
        maker.SetCatalogStats(45000, 12, 30);
        h.Db.Set<Maker>().Add(maker);
    }

    private static Product SeedProduct(TestDbHarness h, string id, string makerId, string title, int imageCount = 0)
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

    // === GetMakerBySlug (T-0044) ===

    [Fact]
    public async Task GetMakerBySlug_returns_profile_with_active_products()
    {
        using var h = TestDbHarness.Create();
        SeedListableMaker(h, "1", "keramika");
        SeedProduct(h, "prod-1", "maker-1", "Hrnek", imageCount: 2);
        SeedProduct(h, "prod-2", "maker-1", "Talíř");
        await h.Db.SaveChangesAsync(default);

        var sut = new CatalogQueries(h.Db);
        var profile = await sut.GetMakerBySlugAsync("keramika", default);

        profile.Should().NotBeNull();
        profile!.CompanyName.Should().Be("Keramika s.r.o.");
        profile.RatingAverageBp.Should().Be(45000);
        profile.Products.Should().HaveCount(2);
        profile.Reviews.Should().BeEmpty("no reviews seeded");
        // The 2-image product exposes its lowest-sort-order image as primary.
        profile.Products.Single(p => p.ProductId == "prod-1").PrimaryImageBlobPath
            .Should().Be("cz/products/prod-1/0.jpg");
    }

    // === GetMakerBySlug reviews (T-0050) ===

    private static void SeedReviewCustomer(TestDbHarness h)
    {
        h.Db.Set<User>().Add(User.Create(
            id: "user-cust-1", email: "cust@example.cz", role: UserRole.Customer,
            fullName: "Zákazník", countryCodePrimary: "CZ",
            emailAlreadyConfirmed: true, confirmedAt: Now));
    }

    /// <summary>Reviews carry a real FK to orders, so each seeded review needs its own order row.</summary>
    private static Review SeedReview(TestDbHarness h, string id, string makerId, short rating, string? body)
    {
        h.Db.Set<Order>().Add(Order.Create(
            id: $"ord-{id}", orderNumber: $"M-CZ-2026{id}",
            customerUserId: "user-cust-1", makerId: makerId, productId: "prod-1",
            contactName: "Anna", contactEmail: "anna@example.cz", contactPhone: "+420 723 456 789",
            productPriceAmountMinor: 25000, shippingPriceAmountMinor: 7900,
            platformFeeAmountMinor: 1750, makerPayoutAmountMinor: 31150,
            totalAmountMinor: 32900, currency: "CZK", vatRateBp: 2100,
            shippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
            zasilkovnaPickupPointId: "pp-1", countryCode: "CZ"));

        var review = Review.Create(
            id: id, orderId: $"ord-{id}", makerId: makerId,
            customerUserId: "user-cust-1", rating: rating, body: body, countryCode: "CZ");
        h.Db.Set<Review>().Add(review);
        return review;
    }

    [Fact]
    public async Task GetMakerBySlug_returns_latest_five_reviews_newest_first_with_reply()
    {
        using var h = TestDbHarness.Create();
        SeedListableMaker(h, "1", "keramika");
        SeedReviewCustomer(h);
        SeedProduct(h, "prod-1", "maker-1", "Hrnek");
        for (var i = 1; i <= 6; i++)
        {
            // Ids are ULIDs in production (time-ordered); zero-padded
            // suffixes give the same lexicographic ordering here.
            SeedReview(h, $"rev-{i:D2}", "maker-1", 4, $"Recenze {i}");
        }
        var replied = SeedReview(h, "rev-07", "maker-1", 5, "S odpovědí");
        replied.AddReply("Děkujeme!", Now);
        await h.Db.SaveChangesAsync(default);

        var sut = new CatalogQueries(h.Db);
        var profile = await sut.GetMakerBySlugAsync("keramika", default);

        profile!.Reviews.Should().HaveCount(5, "the public list caps at the latest 5 (US-customer-0008 AC-3)");
        profile.Reviews.Select(r => r.ReviewId)
            .Should().ContainInOrder("rev-07", "rev-06", "rev-05", "rev-04", "rev-03");
        var withReply = profile.Reviews.Single(r => r.ReviewId == "rev-07");
        withReply.RatingStars.Should().Be(5);
        withReply.Comment.Should().Be("S odpovědí");
        withReply.ReplyBody.Should().Be("Děkujeme!");
        withReply.ReplyCreatedAt.Should().Be(Now);
        profile.Reviews.Single(r => r.ReviewId == "rev-06").ReplyBody.Should().BeNull();
    }

    [Fact]
    public async Task GetMakerBySlug_excludes_soft_deleted_reviews()
    {
        using var h = TestDbHarness.Create();
        SeedListableMaker(h, "1", "keramika");
        SeedReviewCustomer(h);
        SeedProduct(h, "prod-1", "maker-1", "Hrnek");
        SeedReview(h, "rev-01", "maker-1", 5, "Zůstává");
        var removed = SeedReview(h, "rev-02", "maker-1", 1, "Skrytá");
        await h.Db.SaveChangesAsync(default);
        removed.MarkDeactivated("admin", Now);
        await h.Db.SaveChangesAsync(default);

        var sut = new CatalogQueries(h.Db);
        var profile = await sut.GetMakerBySlugAsync("keramika", default);

        profile!.Reviews.Should().ContainSingle().Which.ReviewId.Should().Be("rev-01");
    }

    [Fact]
    public async Task GetMakerBySlug_excludes_soft_deleted_products()
    {
        using var h = TestDbHarness.Create();
        SeedListableMaker(h, "1", "keramika");
        SeedProduct(h, "prod-1", "maker-1", "Active");
        var deleted = SeedProduct(h, "prod-2", "maker-1", "Deleted");
        await h.Db.SaveChangesAsync(default);
        deleted.MarkDeactivated("maker", Now);
        await h.Db.SaveChangesAsync(default);

        var sut = new CatalogQueries(h.Db);
        var profile = await sut.GetMakerBySlugAsync("keramika", default);

        profile!.Products.Should().ContainSingle().Which.Title.Should().Be("Active");
    }

    [Fact]
    public async Task GetMakerBySlug_returns_null_for_unconfirmed_maker()
    {
        using var h = TestDbHarness.Create();
        SeedListableMaker(h, "1", "hidden", emailConfirmed: false);
        await h.Db.SaveChangesAsync(default);

        var sut = new CatalogQueries(h.Db);
        (await sut.GetMakerBySlugAsync("hidden", default)).Should().BeNull();
    }

    [Fact]
    public async Task GetMakerBySlug_returns_null_for_unknown_slug()
    {
        using var h = TestDbHarness.Create();
        var sut = new CatalogQueries(h.Db);
        (await sut.GetMakerBySlugAsync("nope", default)).Should().BeNull();
    }

    // === GetProductById (T-0045) ===

    [Fact]
    public async Task GetProductById_returns_detail_with_images_and_maker_info()
    {
        using var h = TestDbHarness.Create();
        SeedListableMaker(h, "1", "keramika");
        SeedProduct(h, "prod-1", "maker-1", "Hrnek", imageCount: 3);
        await h.Db.SaveChangesAsync(default);

        var sut = new CatalogQueries(h.Db);
        var detail = await sut.GetProductByIdAsync("prod-1", default);

        detail.Should().NotBeNull();
        detail!.Title.Should().Be("Hrnek");
        detail.PriceAmountMinor.Should().Be(25000);
        detail.PriceCurrency.Should().Be("CZK");
        detail.PriceType.Should().Be("Fixed");
        detail.MakerSlug.Should().Be("keramika");
        detail.MakerCompanyName.Should().Be("Keramika s.r.o.");
        detail.Images.Should().HaveCount(3);
        detail.Images.Select(i => i.SortOrder).Should().ContainInOrder(0, 1, 2);
    }

    [Fact]
    public async Task GetProductById_returns_null_for_soft_deleted_product()
    {
        using var h = TestDbHarness.Create();
        SeedListableMaker(h, "1", "keramika");
        var p = SeedProduct(h, "prod-1", "maker-1", "Gone");
        await h.Db.SaveChangesAsync(default);
        p.MarkDeactivated("maker", Now);
        await h.Db.SaveChangesAsync(default);

        var sut = new CatalogQueries(h.Db);
        (await sut.GetProductByIdAsync("prod-1", default)).Should().BeNull();
    }

    [Fact]
    public async Task GetProductById_returns_null_when_maker_not_publicly_listable()
    {
        using var h = TestDbHarness.Create();
        SeedListableMaker(h, "1", "hidden", emailConfirmed: false);
        SeedProduct(h, "prod-1", "maker-1", "Behind unconfirmed maker");
        await h.Db.SaveChangesAsync(default);

        var sut = new CatalogQueries(h.Db);
        (await sut.GetProductByIdAsync("prod-1", default)).Should().BeNull();
    }
}
