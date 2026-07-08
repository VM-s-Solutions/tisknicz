using FluentAssertions;
using Makables.Core.AppServices.Services;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Money;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Products;
using NSubstitute;

namespace Makables.Tests.AppServices.Services;

/// <summary>
/// T-0061 — pins the orchestrator contract that <c>CreateOrder.Validator</c>
/// (T-0063), the customer checkout preview (T-0099), the platform-fee
/// invoice composer (T-0068), and the Comgate session amount (T-0065)
/// all depend on. Mocks the repositories the service touches; the pricing
/// math itself is covered exhaustively in
/// <see cref="Domain.Orders.OrderPricingTests"/>. T-0140 adds the
/// <see cref="IMakerRepository"/> mock for the per-maker fee-rate-override
/// resolution branch.
/// </summary>
public class PricingServiceTests
{
    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly IMakerRepository _makers = Substitute.For<IMakerRepository>();
    private readonly ICountryConfigurationRepository _configs = Substitute.For<ICountryConfigurationRepository>();
    private readonly PricingService _sut;

    public PricingServiceTests()
    {
        _sut = new PricingService(_products, _makers, _configs);

        // Default fixture: a maker with NO override, so existing tests
        // (pre-dating T-0140) keep passing unmodified — AC-3, no behavior
        // change for makers without an override.
        _makers.GetByIdAsync("maker-1", Arg.Any<CancellationToken>())
            .Returns(BuildMaker());
    }

    // === Fixtures ===

    private static Maker BuildMaker(string id = "maker-1", int? feeRateOverrideBp = null)
    {
        var maker = Maker.Create(
            id: id,
            userId: "user-1",
            registrationNumber: "12345678",
            vatId: null,
            companyName: "Dílna Novák",
            legalForm: null,
            registeredAddressId: "addr-1",
            incorporatedOn: null,
            isActiveInRegistry: true,
            sourceRegistry: "ares",
            snapshotFetchedAt: DateTimeOffset.UtcNow,
            snapshotIsStale: false,
            countryCode: "CZ");
        maker.SetFeeRateOverride(feeRateOverrideBp);
        return maker;
    }

    private static CountryConfiguration BuildCz(
        InvoicingMode invoicingMode = InvoicingMode.StandardVat,
        long defaultShippingPriceMinor = 7900,
        string currency = "CZK") =>
        CountryConfiguration.Create(
            countryId: "CZ",
            defaultCurrencyCode: currency,
            defaultLanguageCode: "cs-CZ",
            timeZoneId: "Europe/Prague",
            phonePrefix: "+420",
            dateFormat: "d. M. yyyy",
            standardVatRateBp: 2100,
            taxIdLabel: "DIČ",
            vatIdLabel: "DIČ",
            registrationNumberLabel: "IČO",
            defaultPaymentProvider: "comgate",
            defaultShippingCarrier: "packeta",
            defaultRegistry: "ares",
            defaultEmailProvider: "resend",
            issuerName: "JVM YORE s.r.o.",
            issuerIco: "00000000",
            invoicingMode: invoicingMode,
            platformFeeRateBp: 1500,
            defaultShippingPriceMinor: defaultShippingPriceMinor);

    private static Product BuildProduct(
        string id = "prod-1",
        long priceMinor = 50000,
        string currency = "CZK",
        PriceType priceType = PriceType.Fixed,
        string countryCode = "CZ") =>
        Product.Create(
            id: id,
            makerId: "maker-1",
            categoryId: "cat-1",
            title: "Vase",
            description: null,
            price: new Money(priceMinor, currency),
            priceType: priceType,
            weightGrams: 300,
            countryCode: countryCode);

    // === Tests ===

    [Fact]
    public async Task ComputeForProductAsync_returns_NotFound_when_product_missing()
    {
        _products.GetByIdAsync("prod-missing", Arg.Any<CancellationToken>())
            .Returns((Product?)null);

        var result = await _sut.ComputeForProductAsync(
            "prod-missing", ShippingMethod.PersonalPickup, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be(BusinessErrorMessage.ProductNotFound);
        result.Error.Field.Should().Be("productId");
    }

    [Fact]
    public async Task ComputeForProductAsync_returns_Validation_when_product_priceType_is_OnRequest()
    {
        // OnRequest is the "ask for a quote" flow per role/product.md —
        // T-0061 refuses it because the custom-quote pricing path is a
        // separate (post-MVP) ticket.
        var product = BuildProduct(priceType: PriceType.OnRequest);
        _products.GetByIdAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);

        var result = await _sut.ComputeForProductAsync(
            product.Id, ShippingMethod.PersonalPickup, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be(BusinessErrorMessage.ProductNotOrderable);
        result.Error.Field.Should().Be("priceType");
    }

    [Fact]
    public async Task ComputeForProductAsync_returns_NotFound_when_country_configuration_missing()
    {
        var product = BuildProduct();
        _products.GetByIdAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);
        _configs.GetByCodeAsync(product.CountryCode, Arg.Any<CancellationToken>())
            .Returns((CountryConfiguration?)null);

        var result = await _sut.ComputeForProductAsync(
            product.Id, ShippingMethod.PersonalPickup, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be(BusinessErrorMessage.CountryConfigurationNotFound);
        result.Error.Field.Should().Be("countryCode");
    }

    [Fact]
    public async Task ComputeForProductAsync_zero_shipping_for_personal_pickup()
    {
        var product = BuildProduct(priceMinor: 50000);
        var cfg = BuildCz();
        _products.GetByIdAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);
        _configs.GetByCodeAsync(product.CountryCode, Arg.Any<CancellationToken>()).Returns(cfg);

        var result = await _sut.ComputeForProductAsync(
            product.Id, ShippingMethod.PersonalPickup, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ShippingPrice.Should().Be(Money.Zero("CZK"));
        // Maker payout for personal pickup = product − fee (no shipping
        // passthrough). 500 CZK − 75 CZK fee = 425 CZK.
        result.Value.MakerPayout.Should().Be(Money.CZK(42500));
    }

    [Fact]
    public async Task ComputeForProductAsync_uses_default_shipping_price_for_zasilkovna()
    {
        var product = BuildProduct(priceMinor: 50000);
        var cfg = BuildCz(defaultShippingPriceMinor: 7900);
        _products.GetByIdAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);
        _configs.GetByCodeAsync(product.CountryCode, Arg.Any<CancellationToken>()).Returns(cfg);

        var result = await _sut.ComputeForProductAsync(
            product.Id, ShippingMethod.ZasilkovnaPickupPoint, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ShippingPrice.Should().Be(Money.CZK(7900));
        result.Value.TotalPrice.Should().Be(Money.CZK(57900));
    }

    [Fact]
    public async Task ComputeForProductAsync_throws_on_currency_mismatch()
    {
        // Programmer-error path per user decision Q7: seed-data integrity
        // bug surfaces as InvalidOperationException, not a typed
        // BusinessResult. The customer never reaches this in any sane
        // production state.
        var product = BuildProduct(currency: "EUR");
        var cfg = BuildCz(currency: "CZK");
        _products.GetByIdAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);
        _configs.GetByCodeAsync(product.CountryCode, Arg.Any<CancellationToken>()).Returns(cfg);

        var act = async () => await _sut.ComputeForProductAsync(
            product.Id, ShippingMethod.PersonalPickup, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ComputeForProductAsync_delegates_math_to_OrderPricing()
    {
        // Happy path returns the same breakdown the PROJEKT-VIZE fixture
        // expects: 500 CZK product + 79 CZK Zásilkovna shipping → fee
        // 75 CZK, payout 504 CZK, total 579 CZK, VAT 121.59 CZK @ 2100 bp.
        var product = BuildProduct(priceMinor: 50000);
        var cfg = BuildCz(defaultShippingPriceMinor: 7900);
        _products.GetByIdAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);
        _configs.GetByCodeAsync(product.CountryCode, Arg.Any<CancellationToken>()).Returns(cfg);

        var result = await _sut.ComputeForProductAsync(
            product.Id, ShippingMethod.ZasilkovnaPickupPoint, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var b = result.Value!;
        b.ProductPrice.Should().Be(Money.CZK(50000));
        b.ShippingPrice.Should().Be(Money.CZK(7900));
        b.PlatformFee.Should().Be(Money.CZK(7500));
        b.MakerPayout.Should().Be(Money.CZK(50400));
        b.TotalPrice.Should().Be(Money.CZK(57900));
        b.VatAmount.Should().Be(Money.CZK(12159));
        b.VatRateBp.Should().Be(2100);
    }

    [Fact]
    public async Task ComputeForProductAsync_propagates_CancellationToken_to_all_repos()
    {
        var product = BuildProduct();
        var cfg = BuildCz();
        _products.GetByIdAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);
        _configs.GetByCodeAsync(product.CountryCode, Arg.Any<CancellationToken>()).Returns(cfg);

        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        await _sut.ComputeForProductAsync(
            product.Id, ShippingMethod.PersonalPickup, token);

        await _products.Received(1).GetByIdAsync(product.Id, token);
        await _configs.Received(1).GetByCodeAsync(product.CountryCode, token);
        await _makers.Received(1).GetByIdAsync(product.MakerId, token);
    }

    // === T-0140: per-maker fee-rate-override resolution ===

    [Fact]
    public async Task ComputeForProductAsync_returns_NotFound_when_maker_missing()
    {
        var product = BuildProduct();
        _products.GetByIdAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);
        _configs.GetByCodeAsync(product.CountryCode, Arg.Any<CancellationToken>()).Returns(BuildCz());
        _makers.GetByIdAsync(product.MakerId, Arg.Any<CancellationToken>()).Returns((Maker?)null);

        var result = await _sut.ComputeForProductAsync(
            product.Id, ShippingMethod.PersonalPickup, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be(BusinessErrorMessage.MakerNotFound);
        result.Error.Field.Should().Be("makerId");
    }

    [Fact]
    public async Task ComputeForProductAsync_uses_maker_FeeRateOverrideBp_when_set()
    {
        // AC-2: maker.FeeRateOverrideBp = 350 bp, country default 1500 bp —
        // the resolved platform fee must reflect 350 bp, not 1500 bp.
        var product = BuildProduct(priceMinor: 50000);
        var cfg = BuildCz(); // platformFeeRateBp defaults to 1500 in BuildCz
        _products.GetByIdAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);
        _configs.GetByCodeAsync(product.CountryCode, Arg.Any<CancellationToken>()).Returns(cfg);
        _makers.GetByIdAsync(product.MakerId, Arg.Any<CancellationToken>())
            .Returns(BuildMaker(feeRateOverrideBp: 350));

        var result = await _sut.ComputeForProductAsync(
            product.Id, ShippingMethod.PersonalPickup, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // 3,5% of 500 CZK = 17,50 CZK.
        result.Value!.PlatformFee.Should().Be(Money.CZK(1750));
        result.Value.MakerPayout.Should().Be(Money.CZK(48250));
    }

    [Fact]
    public async Task ComputeForProductAsync_uses_country_default_when_maker_has_no_override()
    {
        // AC-3: no behavior change for makers without an override.
        var product = BuildProduct(priceMinor: 50000);
        var cfg = BuildCz(); // 1500 bp default
        _products.GetByIdAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);
        _configs.GetByCodeAsync(product.CountryCode, Arg.Any<CancellationToken>()).Returns(cfg);
        _makers.GetByIdAsync(product.MakerId, Arg.Any<CancellationToken>())
            .Returns(BuildMaker(feeRateOverrideBp: null));

        var result = await _sut.ComputeForProductAsync(
            product.Id, ShippingMethod.PersonalPickup, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // 15% of 500 CZK = 75 CZK — unchanged from pre-T-0140 behavior.
        result.Value!.PlatformFee.Should().Be(Money.CZK(7500));
        result.Value.MakerPayout.Should().Be(Money.CZK(42500));
    }
}
