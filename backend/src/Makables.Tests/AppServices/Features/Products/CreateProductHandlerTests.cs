using FluentAssertions;
using Makables.Core.AppServices.Features.Products;
using Makables.Core.Domain.Categories;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Products;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Products;

public class CreateProductHandlerTests
{
    private static readonly DateTimeOffset SnapshotAt = new(2026, 5, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly IMakerRepository _makers = Substitute.For<IMakerRepository>();
    private readonly ICategoryRepository _categories = Substitute.For<ICategoryRepository>();
    private readonly ICountryConfigurationRepository _countries = Substitute.For<ICountryConfigurationRepository>();
    private readonly IUserSessionProvider _session = Substitute.For<IUserSessionProvider>();
    private readonly IIdGenerator _ids = Substitute.For<IIdGenerator>();
    private readonly CreateProduct.Handler _sut;

    public CreateProductHandlerTests()
    {
        _session.GetUserId().Returns("user-1");
        _ids.Next().Returns("prod-1");
        _sut = new CreateProduct.Handler(_products, _makers, _categories, _countries, _session, _ids);
    }

    private static Makables.Core.Domain.Makers.Maker ExistingMaker() =>
        Makables.Core.Domain.Makers.Maker.Create(
            id: "maker-1", userId: "user-1", registrationNumber: "27074358",
            vatId: null, companyName: "Avast s.r.o.", legalForm: null,
            registeredAddressId: "addr-1", incorporatedOn: null,
            isActiveInRegistry: true, sourceRegistry: "ares",
            snapshotFetchedAt: SnapshotAt, snapshotIsStale: false, countryCode: "CZ");

    private static Category ExistingCategory() =>
        Category.Create("cat-1", "3D tisk", null, null, null, 10, "CZ");

    private static CountryConfiguration CzConfig() =>
        CountryConfiguration.Create(
            countryId: "CZ", defaultCurrencyCode: "CZK", defaultLanguageCode: "cs-CZ",
            timeZoneId: "Europe/Prague", phonePrefix: "+420", dateFormat: "d. M. yyyy",
            standardVatRateBp: 2100, taxIdLabel: "DIČ", vatIdLabel: "DIČ",
            registrationNumberLabel: "IČO", defaultPaymentProvider: "comgate",
            defaultShippingCarrier: "packeta", defaultRegistry: "ares",
            defaultEmailProvider: "sendgrid",    issuerName: "JVM YORE s.r.o.",    issuerIco: "00000000");

    private static CreateProduct.Command ValidCommand(long price = 25000, PriceType type = PriceType.Fixed) =>
        new(CategoryId: "cat-1", Title: "Hrnek", Description: "popis",
            PriceAmountMinor: price, PriceType: type, FulfillmentType: FulfillmentType.MadeToOrder, WeightGrams: 400);

    private void WireHappyPath()
    {
        _makers.GetByUserIdAsync("user-1", Arg.Any<CancellationToken>()).Returns(ExistingMaker());
        _categories.GetByIdAsync("cat-1", Arg.Any<CancellationToken>()).Returns(ExistingCategory());
        _countries.GetByCodeAsync("CZ", Arg.Any<CancellationToken>()).Returns(CzConfig());
    }

    [Fact]
    public async Task Returns_Unauthorized_when_session_has_no_user()
    {
        _session.GetUserId().Returns((string?)null);
        var result = await _sut.Handle(ValidCommand(), CancellationToken.None);
        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);
    }

    [Fact]
    public async Task Returns_NotFound_when_caller_has_no_maker_row()
    {
        _makers.GetByUserIdAsync("user-1", Arg.Any<CancellationToken>()).Returns((Makables.Core.Domain.Makers.Maker?)null);
        var result = await _sut.Handle(ValidCommand(), CancellationToken.None);
        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Rejects_inactive_or_missing_category()
    {
        _makers.GetByUserIdAsync("user-1", Arg.Any<CancellationToken>()).Returns(ExistingMaker());
        _categories.GetByIdAsync("cat-1", Arg.Any<CancellationToken>()).Returns((Category?)null);

        var result = await _sut.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.CategoryNotActive);
        _products.DidNotReceive().Add(Arg.Any<Product>());
    }

    [Fact]
    public async Task Rejects_free_fixed_product()
    {
        WireHappyPath();
        var result = await _sut.Handle(ValidCommand(price: 0, type: PriceType.Fixed), CancellationToken.None);
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.ProductFreeRequiresOnRequest);
    }

    [Fact]
    public async Task Happy_path_creates_product_with_tenant_currency()
    {
        WireHappyPath();

        var result = await _sut.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be("prod-1");
        _products.Received(1).Add(Arg.Is<Product>(p =>
            p.Id == "prod-1"
            && p.MakerId == "maker-1"
            && p.CategoryId == "cat-1"
            && p.PriceAmountMinor == 25000
            && p.PriceCurrency == "CZK"   // from CountryConfiguration, NOT caller
            && p.CountryCode == "CZ"));
    }

    [Fact]
    public void Command_has_no_maker_id_field()
    {
        // IDOR shield — owning maker resolved from session.
        var props = typeof(CreateProduct.Command).GetProperties();
        props.Should().NotContain(p => p.Name.Equals("MakerId", StringComparison.OrdinalIgnoreCase));
    }

    // === Validator (T-0144) ===

    [Fact]
    public void Validator_accepts_MadeToOrder_and_InStock()
    {
        var validator = new CreateProduct.Validator();

        validator.Validate(ValidCommand() with { FulfillmentType = FulfillmentType.MadeToOrder })
            .IsValid.Should().BeTrue();
        validator.Validate(ValidCommand() with { FulfillmentType = FulfillmentType.InStock })
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validator_rejects_out_of_range_FulfillmentType()
    {
        var validator = new CreateProduct.Validator();

        var result = validator.Validate(ValidCommand() with { FulfillmentType = (FulfillmentType)99 });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == nameof(CreateProduct.Command.FulfillmentType)
            && e.ErrorCode == BusinessErrorMessage.InvalidEnumValue);
    }
}
