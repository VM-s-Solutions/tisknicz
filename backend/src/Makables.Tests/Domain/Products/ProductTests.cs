using FluentAssertions;
using Makables.Core.Domain.Products;
using DomainMoney = Makables.Core.Domain.Money.Money;

namespace Makables.Tests.Domain.Products;

public class ProductTests
{
    private static Product ValidDefaults() => Product.Create(
        id: "prod-1",
        makerId: "maker-1",
        categoryId: "cat-1",
        title: "Keramický hrnek",
        description: "Ručně dělaný",
        price: new DomainMoney(25000, "CZK"),
        priceType: PriceType.Fixed,
        weightGrams: 400,
        countryCode: "CZ");

    [Fact]
    public void Create_trims_title_and_uppercases_country()
    {
        var p = Product.Create(
            id: "prod-1", makerId: "maker-1", categoryId: "cat-1",
            title: "  Hrnek  ", description: "  popis  ",
            price: new DomainMoney(10000, "CZK"), priceType: PriceType.Fixed,
            weightGrams: 100, countryCode: "cz");

        p.Title.Should().Be("Hrnek");
        p.Description.Should().Be("popis");
        p.CountryCode.Should().Be("CZ");
        p.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Create_rejects_negative_price()
    {
        var act = () => Product.Create(
            "prod-1", "maker-1", "cat-1", "Title", null,
            new DomainMoney(-1, "CZK"), PriceType.Fixed, 100, "CZ");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_rejects_free_product_unless_on_request()
    {
        var act = () => Product.Create(
            "prod-1", "maker-1", "cat-1", "Title", null,
            new DomainMoney(0, "CZK"), PriceType.Fixed, 100, "CZ");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_allows_free_product_when_on_request()
    {
        var p = Product.Create(
            "prod-1", "maker-1", "cat-1", "Custom job", null,
            new DomainMoney(0, "CZK"), PriceType.OnRequest, 0, "CZ");
        p.PriceAmountMinor.Should().Be(0);
        p.PriceType.Should().Be(PriceType.OnRequest);
    }

    [Fact]
    public void Update_rejects_currency_change()
    {
        var p = ValidDefaults();
        var act = () => p.Update("cat-1", "Title", null, new DomainMoney(25000, "EUR"), PriceType.Fixed, 400);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Update_changes_mutable_fields()
    {
        var p = ValidDefaults();
        p.Update("cat-2", "Nový název", "nový popis", new DomainMoney(30000, "CZK"), PriceType.From, 500);

        p.CategoryId.Should().Be("cat-2");
        p.Title.Should().Be("Nový název");
        p.PriceAmountMinor.Should().Be(30000);
        p.PriceType.Should().Be(PriceType.From);
        p.WeightGrams.Should().Be(500);
    }

    [Fact]
    public void AddImage_appends_with_incrementing_sort_order()
    {
        var p = ValidDefaults();
        p.AddImage("img-1", "cz/products/prod-1/a.jpg");
        p.AddImage("img-2", "cz/products/prod-1/b.jpg");

        p.Images.Should().HaveCount(2);
        p.Images[0].SortOrder.Should().Be(0);
        p.Images[1].SortOrder.Should().Be(1);
    }

    [Fact]
    public void AddImage_throws_past_the_cap()
    {
        var p = ValidDefaults();
        for (var i = 0; i < Product.MaxImageCount; i++)
        {
            p.AddImage($"img-{i}", $"cz/products/prod-1/{i}.jpg");
        }

        var act = () => p.AddImage("img-overflow", "cz/products/prod-1/x.jpg");
        act.Should().Throw<InvalidOperationException>();
        p.Images.Should().HaveCount(Product.MaxImageCount);
    }

    [Fact]
    public void RemoveImage_returns_removed_and_compacts_sort_order()
    {
        var p = ValidDefaults();
        p.AddImage("img-1", "cz/products/prod-1/a.jpg");
        p.AddImage("img-2", "cz/products/prod-1/b.jpg");
        p.AddImage("img-3", "cz/products/prod-1/c.jpg");

        var removed = p.RemoveImage("img-2");

        removed.Should().NotBeNull();
        removed!.Id.Should().Be("img-2");
        p.Images.Should().HaveCount(2);
        // SortOrder compacted: no holes.
        p.Images.Select(i => i.SortOrder).Should().BeEquivalentTo(new[] { 0, 1 });
        p.Images.Select(i => i.Id).Should().BeEquivalentTo(new[] { "img-1", "img-3" });
    }

    [Fact]
    public void RemoveImage_returns_null_for_unknown_id()
    {
        var p = ValidDefaults();
        p.AddImage("img-1", "cz/products/prod-1/a.jpg");

        p.RemoveImage("nope").Should().BeNull();
        p.Images.Should().HaveCount(1);
    }
}
