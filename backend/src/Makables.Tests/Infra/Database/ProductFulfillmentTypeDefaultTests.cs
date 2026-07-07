using FluentAssertions;
using Makables.Core.Domain.Products;
using Microsoft.EntityFrameworkCore;
using DomainMoney = Makables.Core.Domain.Money.Money;

namespace Makables.Tests.Infra.Database;

/// <summary>
/// Pins the T-0144 AC-6 contract: the <c>AddProductFulfillmentType</c>
/// migration adds the column with <c>DEFAULT 'MadeToOrder'</c> so every
/// pre-existing product row defaults to the safer legal posture with no
/// manual backfill. Two checks: the EF model's configured default value
/// (would catch a future edit to <c>ProductEntityConfiguration</c> that
/// silently changes the default), and an end-to-end round trip proving a
/// product that never set the property still persists/reads back as
/// <see cref="FulfillmentType.MadeToOrder"/> — the entity's own optional-
/// parameter default (see <c>Product.Create</c>) plus the column default
/// agree on the same value.
/// </summary>
public class ProductFulfillmentTypeDefaultTests
{
    [Fact]
    public void ProductConfiguration_default_value_is_MadeToOrder()
    {
        using var h = TestDbHarness.Create();

        var property = h.Db.Model.FindEntityType(typeof(Product))!
            .FindProperty(nameof(Product.FulfillmentType))!;

        property.GetDefaultValue().Should().Be(FulfillmentType.MadeToOrder);
    }

    [Fact]
    public async Task Product_created_without_explicit_FulfillmentType_round_trips_as_MadeToOrder()
    {
        using var h = TestDbHarness.Create();

        // Mirrors a pre-T-0144 caller: no fulfillmentType argument supplied.
        var product = Product.Create(
            id: "prod-1", makerId: "maker-1", categoryId: "cat-1",
            title: "Hrnek", description: null,
            price: new DomainMoney(25000, "CZK"), priceType: PriceType.Fixed,
            weightGrams: 400, countryCode: "CZ");

        h.Db.Set<Product>().Add(product);
        await h.Db.SaveChangesAsync(default);
        h.Db.ChangeTracker.Clear();

        var reloaded = await h.Db.Set<Product>().FirstAsync(p => p.Id == "prod-1");
        reloaded.FulfillmentType.Should().Be(FulfillmentType.MadeToOrder);
    }
}
