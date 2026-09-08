using FluentAssertions;
using Makables.Core.Domain.Addresses;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Products;
using Makables.Infra.Database.Catalog;
using Makables.Tests.Infra.Database;
using DomainMoney = Makables.Core.Domain.Money.Money;

namespace Makables.Tests.Infra.Catalog;

/// <summary>
/// The gate the anonymous image routes consult before streaming a blob
/// (Q-0040). These tests are the reason the maker-verification gate is no
/// longer bypassable for image bytes, so each one pins a way in which the
/// asset layer used to disagree with the catalog:
///
/// <list type="bullet">
///   <item><description>an UNVERIFIED maker's products were invisible in the
///     catalog but their image URLs still streamed 200;</description></item>
///   <item><description>the same for an unconfirmed email or a soft-deleted
///     product, maker or user.</description></item>
/// </list>
///
/// The predicates must stay identical to <see cref="CatalogQueries"/>'s. If
/// that gate changes and these do not, the storefront and the asset layer
/// diverge again — which was the original defect.
/// </summary>
public class PublicImageVisibilityQueriesTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-05-28T10:00:00Z");

    private static void SeedMaker(
        TestDbHarness h,
        string suffix,
        bool emailConfirmed = true,
        bool verified = true,
        bool userActive = true,
        bool makerActive = true)
    {
        var user = User.Create(
            id: $"user-{suffix}", email: $"{suffix}@example.cz", role: UserRole.Maker,
            fullName: "Owner", countryCodePrimary: "CZ",
            emailAlreadyConfirmed: emailConfirmed, confirmedAt: emailConfirmed ? Now : null);
        if (!userActive) user.MarkDeactivated("admin", Now);
        h.Db.Set<User>().Add(user);

        h.Db.Set<Address>().Add(Address.Create(
            id: $"addr-{suffix}", street: "Ulice", houseNumber: "1", city: "Praha",
            zip: "10000", countryCodeIso: "CZ", auditCountryCode: "CZ"));

        var maker = Maker.Create(
            id: $"maker-{suffix}", userId: $"user-{suffix}", registrationNumber: $"1000000{suffix}",
            vatId: null, companyName: "Keramika s.r.o.", legalForm: "s.r.o.",
            registeredAddressId: $"addr-{suffix}", incorporatedOn: null,
            isActiveInRegistry: true, sourceRegistry: "ares", snapshotFetchedAt: Now,
            snapshotIsStale: false, countryCode: "CZ", slug: $"maker-{suffix}");
        if (verified) maker.MarkVerified();
        if (!makerActive) maker.MarkDeactivated("admin", Now);
        h.Db.Set<Maker>().Add(maker);
    }

    private static void SeedProduct(TestDbHarness h, string id, string makerId, bool active = true)
    {
        var product = Product.Create(
            id: id, makerId: makerId, categoryId: "cat-1", title: "Hrnek", description: "popis",
            price: new DomainMoney(25000, "CZK"), priceType: PriceType.Fixed,
            weightGrams: 400, countryCode: "CZ");
        if (!active) product.MarkDeactivated("admin", Now);
        h.Db.Set<Product>().Add(product);
    }

    private static PublicImageVisibilityQueries Sut(TestDbHarness h) => new(h.Db);

    // === Product images ===

    [Fact]
    public async Task Product_image_is_visible_for_a_verified_confirmed_active_maker()
    {
        using var h = TestDbHarness.Create();
        SeedMaker(h, "1");
        SeedProduct(h, "prod-1", "maker-1");
        await h.Db.SaveChangesAsync(default);

        (await Sut(h).IsProductImageVisibleAsync("prod-1", default)).Should().BeTrue();
    }

    /// <summary>
    /// THE defect this work exists to close. Before the gate, an unverified
    /// maker's product was absent from every catalog read yet its image URL
    /// still returned 200 to anyone holding one.
    /// </summary>
    [Fact]
    public async Task Product_image_is_hidden_when_the_maker_is_not_verified()
    {
        using var h = TestDbHarness.Create();
        SeedMaker(h, "1", verified: false);
        SeedProduct(h, "prod-1", "maker-1");
        await h.Db.SaveChangesAsync(default);

        (await Sut(h).IsProductImageVisibleAsync("prod-1", default)).Should().BeFalse();
    }

    [Fact]
    public async Task Product_image_is_hidden_when_the_owning_email_is_unconfirmed()
    {
        using var h = TestDbHarness.Create();
        SeedMaker(h, "1", emailConfirmed: false);
        SeedProduct(h, "prod-1", "maker-1");
        await h.Db.SaveChangesAsync(default);

        (await Sut(h).IsProductImageVisibleAsync("prod-1", default)).Should().BeFalse();
    }

    [Fact]
    public async Task Product_image_is_hidden_when_the_product_is_soft_deleted()
    {
        using var h = TestDbHarness.Create();
        SeedMaker(h, "1");
        SeedProduct(h, "prod-1", "maker-1", active: false);
        await h.Db.SaveChangesAsync(default);

        (await Sut(h).IsProductImageVisibleAsync("prod-1", default)).Should().BeFalse();
    }

    [Fact]
    public async Task Product_image_is_hidden_when_the_owning_user_is_deactivated()
    {
        using var h = TestDbHarness.Create();
        SeedMaker(h, "1", userActive: false);
        SeedProduct(h, "prod-1", "maker-1");
        await h.Db.SaveChangesAsync(default);

        (await Sut(h).IsProductImageVisibleAsync("prod-1", default)).Should().BeFalse();
    }

    [Theory]
    [InlineData("does-not-exist")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Product_image_is_hidden_for_a_missing_or_blank_id(string productId)
    {
        using var h = TestDbHarness.Create();
        SeedMaker(h, "1");
        SeedProduct(h, "prod-1", "maker-1");
        await h.Db.SaveChangesAsync(default);

        (await Sut(h).IsProductImageVisibleAsync(productId, default)).Should().BeFalse();
    }

    // === Maker logos ===

    [Fact]
    public async Task Maker_logo_is_visible_for_a_verified_confirmed_active_maker()
    {
        using var h = TestDbHarness.Create();
        SeedMaker(h, "1");
        await h.Db.SaveChangesAsync(default);

        (await Sut(h).IsMakerImageVisibleAsync("maker-1", default)).Should().BeTrue();
    }

    [Fact]
    public async Task Maker_logo_is_hidden_when_the_maker_is_not_verified()
    {
        using var h = TestDbHarness.Create();
        SeedMaker(h, "1", verified: false);
        await h.Db.SaveChangesAsync(default);

        (await Sut(h).IsMakerImageVisibleAsync("maker-1", default)).Should().BeFalse();
    }

    [Fact]
    public async Task Maker_logo_is_hidden_when_the_maker_is_soft_deleted()
    {
        using var h = TestDbHarness.Create();
        SeedMaker(h, "1", makerActive: false);
        await h.Db.SaveChangesAsync(default);

        (await Sut(h).IsMakerImageVisibleAsync("maker-1", default)).Should().BeFalse();
    }

    // === Avatars ===

    /// <summary>
    /// An avatar is NOT gated on maker verification — it belongs beside the
    /// reviews its owner wrote, and a reviewer is a customer, not a maker.
    /// </summary>
    [Fact]
    public async Task Avatar_is_visible_for_a_plain_confirmed_customer()
    {
        using var h = TestDbHarness.Create();
        h.Db.Set<User>().Add(User.Create(
            id: "user-c", email: "c@example.cz", role: UserRole.Customer,
            fullName: "Zákazník", countryCodePrimary: "CZ",
            emailAlreadyConfirmed: true, confirmedAt: Now));
        await h.Db.SaveChangesAsync(default);

        (await Sut(h).IsUserAvatarVisibleAsync("user-c", default)).Should().BeTrue();
    }

    /// <summary>
    /// The Q-0041 mitigation. Self-service "Smazat účet" calls
    /// <c>MarkDeactivated</c>; the global soft-delete filter then excludes the
    /// row, so the photograph stops being served even though the blob itself
    /// survives in storage.
    /// </summary>
    [Fact]
    public async Task Avatar_stops_being_served_after_the_account_is_deactivated()
    {
        using var h = TestDbHarness.Create();
        var user = User.Create(
            id: "user-c", email: "c@example.cz", role: UserRole.Customer,
            fullName: "Zákazník", countryCodePrimary: "CZ",
            emailAlreadyConfirmed: true, confirmedAt: Now);
        user.MarkDeactivated("user-c", Now);
        h.Db.Set<User>().Add(user);
        await h.Db.SaveChangesAsync(default);

        (await Sut(h).IsUserAvatarVisibleAsync("user-c", default)).Should().BeFalse();
    }

    [Fact]
    public async Task Avatar_is_hidden_when_the_email_is_unconfirmed()
    {
        using var h = TestDbHarness.Create();
        h.Db.Set<User>().Add(User.Create(
            id: "user-c", email: "c@example.cz", role: UserRole.Customer,
            fullName: "Zákazník", countryCodePrimary: "CZ",
            emailAlreadyConfirmed: false, confirmedAt: null));
        await h.Db.SaveChangesAsync(default);

        (await Sut(h).IsUserAvatarVisibleAsync("user-c", default)).Should().BeFalse();
    }
}
