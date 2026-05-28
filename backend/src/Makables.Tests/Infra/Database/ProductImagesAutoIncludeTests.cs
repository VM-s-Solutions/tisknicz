using FluentAssertions;
using Makables.Core.Domain.Products;
using Microsoft.EntityFrameworkCore;
using DomainMoney = Makables.Core.Domain.Money.Money;

namespace Makables.Tests.Infra.Database;

/// <summary>
/// Pins the T-0041 Copilot-review fix: <c>Navigation(p =&gt; p.Images).AutoInclude()</c>
/// means a Product loaded WITHOUT an explicit Include still has its
/// owned image collection populated. The image-cap check + RemoveImage
/// depend on this; a partially-loaded aggregate would silently allow
/// &gt;10 images or 404 an existing one.
/// </summary>
public class ProductImagesAutoIncludeTests
{
    private static Product NewProductWithImages(int imageCount)
    {
        var product = Product.Create(
            id: "prod-1", makerId: "maker-1", categoryId: "cat-1",
            title: "Hrnek", description: null,
            price: new DomainMoney(25000, "CZK"), priceType: PriceType.Fixed,
            weightGrams: 400, countryCode: "CZ");
        for (var i = 0; i < imageCount; i++)
        {
            product.AddImage($"img-{i}", $"cz/products/prod-1/{i}.jpg");
        }
        return product;
    }

    [Fact]
    public async Task Product_loaded_without_explicit_include_has_images_populated()
    {
        using var h = TestDbHarness.Create();
        h.Db.Set<Product>().Add(NewProductWithImages(3));
        await h.Db.SaveChangesAsync(default);
        // Detach everything so the next read comes from the DB, not the
        // change-tracker (which would have the images regardless).
        h.Db.ChangeTracker.Clear();

        // No .Include(p => p.Images) — relies on AutoInclude.
        var loaded = await h.Db.Set<Product>().FirstOrDefaultAsync(p => p.Id == "prod-1");

        loaded.Should().NotBeNull();
        loaded!.Images.Should().HaveCount(3);
        loaded.Images.Select(i => i.SortOrder).Should().BeEquivalentTo(new[] { 0, 1, 2 });
    }

    [Fact]
    public async Task RemoveImage_on_db_loaded_product_compacts_and_persists()
    {
        using var h = TestDbHarness.Create();
        h.Db.Set<Product>().Add(NewProductWithImages(3));
        await h.Db.SaveChangesAsync(default);
        h.Db.ChangeTracker.Clear();

        var loaded = await h.Db.Set<Product>().FirstAsync(p => p.Id == "prod-1");
        var removed = loaded.RemoveImage("img-1");
        await h.Db.SaveChangesAsync(default);
        h.Db.ChangeTracker.Clear();

        removed.Should().NotBeNull();
        var reloaded = await h.Db.Set<Product>().FirstAsync(p => p.Id == "prod-1");
        reloaded.Images.Should().HaveCount(2);
        // SortOrder compacted with no holes after the persisted round-trip.
        reloaded.Images.Select(i => i.SortOrder).OrderBy(x => x).Should().BeEquivalentTo(new[] { 0, 1 });
        reloaded.Images.Select(i => i.Id).Should().BeEquivalentTo(new[] { "img-0", "img-2" });
    }
}
