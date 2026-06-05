using FluentAssertions;
using Makables.Core.AppServices.Features.Orders;
using Makables.Core.AppServices.Services;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Money;
using Makables.Core.Domain.Numbering;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Products;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Orders;

/// <summary>
/// Pins the T-0063 CreateOrder.Handler 8-step happy-path flow. NSubstitute
/// over every dependency so the test focuses on the branch logic; the
/// downstream collaborators have their own coverage (PricingService,
/// IOrderNumberGenerator, Order.Create).
///
/// <para>
/// Regression net for T-0062: the
/// <see cref="IOrderNumberGenerator.NextAsync(string,CancellationToken)"/>
/// call is pinned to the single-string-arg signature so a future change
/// can't accidentally re-introduce the <c>int year</c> parameter.
/// </para>
/// </summary>
public class CreateOrderHandlerTests
{
    private const string CustomerUserId = "user-1";
    private const string ProductId = "prod-1";
    private const string MakerId = "maker-1";
    private const string CountryCode = "CZ";
    private const string Currency = "CZK";
    private const string OrderNumber = "M-CZ-20260042";
    private const string NewOrderId = "ord-new";

    private readonly IUserSessionProvider _session = Substitute.For<IUserSessionProvider>();
    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly IMakerRepository _makers = Substitute.For<IMakerRepository>();
    private readonly IPricingService _pricing = Substitute.For<IPricingService>();
    private readonly IOrderNumberGenerator _numbers = Substitute.For<IOrderNumberGenerator>();
    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IIdGenerator _ids = Substitute.For<IIdGenerator>();
    private readonly CreateOrder.Handler _sut;

    public CreateOrderHandlerTests()
    {
        _session.GetUserId().Returns(CustomerUserId);
        _ids.Next().Returns(NewOrderId);
        _numbers.NextAsync(CountryCode, Arg.Any<CancellationToken>()).Returns(OrderNumber);

        _sut = new CreateOrder.Handler(
            _session, _products, _makers, _pricing, _numbers, _orders, _ids,
            NullLogger<CreateOrder.Handler>.Instance);
    }

    // === Fixtures ===

    private static Product BuildProduct(string makerId = MakerId, string countryCode = CountryCode) =>
        Product.Create(
            id: ProductId,
            makerId: makerId,
            categoryId: "cat-1",
            title: "Vase",
            description: null,
            price: new Money(50000, Currency),
            priceType: PriceType.Fixed,
            weightGrams: 300,
            countryCode: countryCode);

    private static Makables.Core.Domain.Makers.Maker BuildMaker(
        bool isVerified = true,
        bool personalPickupEnabled = true)
    {
        var maker = Makables.Core.Domain.Makers.Maker.Create(
            id: MakerId,
            userId: "user-maker",
            registrationNumber: "27074358",
            vatId: null,
            companyName: "Avast Software s.r.o.",
            legalForm: null,
            registeredAddressId: "addr-1",
            incorporatedOn: null,
            isActiveInRegistry: true,
            sourceRegistry: "ares",
            snapshotFetchedAt: new DateTimeOffset(2026, 5, 25, 10, 0, 0, TimeSpan.Zero),
            snapshotIsStale: false,
            countryCode: CountryCode,
            slug: "avast");

        if (isVerified)
        {
            maker.MarkVerified();
        }
        if (personalPickupEnabled)
        {
            maker.UpdateProfile(
                bio: null,
                bankAccount: null,
                personalPickupEnabled: true,
                pickupNote: null);
        }
        return maker;
    }

    private static PricingBreakdown BuildBreakdown(
        long productMinor = 50000,
        long shippingMinor = 7900,
        long platformFeeMinor = 7500,
        int vatBp = 2100,
        string currency = Currency)
    {
        var product = new Money(productMinor, currency);
        var shipping = new Money(shippingMinor, currency);
        var platformFee = new Money(platformFeeMinor, currency);
        var makerPayout = new Money(productMinor - platformFeeMinor + shippingMinor, currency);
        var total = new Money(productMinor + shippingMinor, currency);
        var vat = vatBp > 0
            ? total.PercentOfBp(vatBp)
            : Money.Zero(currency);
        return PricingBreakdown.Create(product, shipping, platformFee, makerPayout, total, vat, vatBp);
    }

    private static CreateOrder.Command ValidCommand(
        ShippingMethod method = ShippingMethod.ZasilkovnaPickupPoint,
        string? pickupPointId = "pp-42") =>
        new(
            ProductId: ProductId,
            Quantity: 1,
            ShippingMethod: method,
            ZasilkovnaPickupPointId: pickupPointId,
            CustomerName: "Anna Nováková",
            CustomerEmail: "anna@example.cz",
            CustomerPhone: "+420 723 456 789",
            CustomerNotes: null);

    private void WireHappyPath(
        Product? product = null,
        Makables.Core.Domain.Makers.Maker? maker = null,
        PricingBreakdown? breakdown = null)
    {
        product ??= BuildProduct();
        maker ??= BuildMaker();
        breakdown ??= BuildBreakdown();

        _products.GetByIdAsync(ProductId, Arg.Any<CancellationToken>()).Returns(product);
        _makers.GetByIdAsync(MakerId, Arg.Any<CancellationToken>()).Returns(maker);
        _pricing.ComputeForProductAsync(ProductId, Arg.Any<ShippingMethod>(), Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(breakdown));
    }

    // === Tests ===

    [Fact]
    public async Task Returns_Unauthorized_when_session_has_no_user()
    {
        _session.GetUserId().Returns((string?)null);

        var result = await _sut.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);
        await _products.DidNotReceive().GetByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Returns_ProductNotFound_when_repository_returns_null()
    {
        _products.GetByIdAsync(ProductId, Arg.Any<CancellationToken>())
            .Returns((Product?)null);

        var result = await _sut.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be(BusinessErrorMessage.ProductNotFound);
        await _orders.DidNotReceive().AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Returns_ProductNotActive_when_product_is_deactivated()
    {
        var product = BuildProduct();
        product.MarkDeactivated("admin-1", DateTimeOffset.UtcNow);
        _products.GetByIdAsync(ProductId, Arg.Any<CancellationToken>()).Returns(product);

        var result = await _sut.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.Conflict);
        result.Error.Code.Should().Be(BusinessErrorMessage.ProductNotActive);
        await _orders.DidNotReceive().AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Returns_MakerDeactivated_when_maker_is_null()
    {
        _products.GetByIdAsync(ProductId, Arg.Any<CancellationToken>()).Returns(BuildProduct());
        _makers.GetByIdAsync(MakerId, Arg.Any<CancellationToken>()).Returns((Makables.Core.Domain.Makers.Maker?)null);

        var result = await _sut.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.Conflict);
        result.Error.Code.Should().Be(BusinessErrorMessage.MakerDeactivated);
    }

    [Fact]
    public async Task Returns_MakerDeactivated_when_maker_is_inactive()
    {
        var maker = BuildMaker();
        maker.MarkDeactivated("admin-1", DateTimeOffset.UtcNow);
        _products.GetByIdAsync(ProductId, Arg.Any<CancellationToken>()).Returns(BuildProduct());
        _makers.GetByIdAsync(MakerId, Arg.Any<CancellationToken>()).Returns(maker);

        var result = await _sut.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.MakerDeactivated);
    }

    [Fact]
    public async Task Returns_MakerNotVerified_when_maker_is_not_verified()
    {
        _products.GetByIdAsync(ProductId, Arg.Any<CancellationToken>()).Returns(BuildProduct());
        _makers.GetByIdAsync(MakerId, Arg.Any<CancellationToken>())
            .Returns(BuildMaker(isVerified: false));

        var result = await _sut.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.Conflict);
        result.Error.Code.Should().Be(BusinessErrorMessage.MakerNotVerified);
    }

    [Fact]
    public async Task Returns_MakerPersonalPickupDisabled_for_personal_pickup_when_maker_disallows_it()
    {
        var maker = BuildMaker(personalPickupEnabled: false);
        _products.GetByIdAsync(ProductId, Arg.Any<CancellationToken>()).Returns(BuildProduct());
        _makers.GetByIdAsync(MakerId, Arg.Any<CancellationToken>()).Returns(maker);

        var result = await _sut.Handle(
            ValidCommand(method: ShippingMethod.PersonalPickup, pickupPointId: null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.Conflict);
        result.Error.Code.Should().Be(BusinessErrorMessage.MakerPersonalPickupDisabled);
        await _orders.DidNotReceive().AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handler_does_not_check_PersonalPickupEnabled_for_zasilkovna_shipping()
    {
        // A maker who does NOT offer personal pickup must still accept
        // Zásilkovna orders — pickup-enabled is a separate fulfilment
        // route, not a global gate. Pinned so a future refactor doesn't
        // accidentally widen the check.
        WireHappyPath(maker: BuildMaker(personalPickupEnabled: false));

        var result = await _sut.Handle(
            ValidCommand(method: ShippingMethod.ZasilkovnaPickupPoint, pickupPointId: "pp-42"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(BusinessErrorMessage.ProductNotOrderable, ErrorType.Validation, "priceType")]
    [InlineData(BusinessErrorMessage.CountryConfigurationNotFound, ErrorType.NotFound, "countryCode")]
    public async Task Handler_surfaces_pricing_service_failure_verbatim(
        string code, ErrorType type, string field)
    {
        _products.GetByIdAsync(ProductId, Arg.Any<CancellationToken>()).Returns(BuildProduct());
        _makers.GetByIdAsync(MakerId, Arg.Any<CancellationToken>()).Returns(BuildMaker());
        var pricingError = new Error(field, code, type);
        _pricing.ComputeForProductAsync(ProductId, Arg.Any<ShippingMethod>(), Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Failure<PricingBreakdown>(pricingError));

        var result = await _sut.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(code);
        result.Error.Type.Should().Be(type);
        result.Error.Field.Should().Be(field);
        await _orders.DidNotReceive().AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handler_calls_order_number_generator_with_product_country_no_year_parameter()
    {
        WireHappyPath();

        await _sut.Handle(ValidCommand(), CancellationToken.None);

        // Pinned to the T-0062 TZ-aware signature: one string arg + ct.
        // If a future signature regresses to include `int year`, the
        // overload-resolution would break and this Received check would
        // miss the call — caught as a compile error on the call site
        // in the handler itself.
        await _numbers.Received(1).NextAsync(CountryCode, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handler_persists_order_in_PendingPayment_with_pricing_snapshot_from_breakdown()
    {
        var breakdown = BuildBreakdown(
            productMinor: 50000, shippingMinor: 7900, platformFeeMinor: 7500);
        WireHappyPath(breakdown: breakdown);

        await _sut.Handle(ValidCommand(), CancellationToken.None);

        await _orders.Received(1).AddAsync(
            Arg.Is<Order>(o =>
                o.Id == NewOrderId
                && o.OrderNumber == OrderNumber
                && o.CustomerUserId == CustomerUserId
                && o.MakerId == MakerId
                && o.ProductId == ProductId
                && o.State == OrderState.PendingPayment
                && o.ProductPriceAmountMinor == 50000
                && o.ShippingPriceAmountMinor == 7900
                && o.PlatformFeeAmountMinor == 7500
                && o.MakerPayoutAmountMinor == 50400  // 50000 - 7500 + 7900
                && o.TotalAmountMinor == 57900       // 50000 + 7900
                && o.Currency == Currency
                && o.VatRateBp == 2100
                && o.CountryCode == CountryCode),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handler_returns_response_with_orderId_orderNumber_total_currency_on_success()
    {
        WireHappyPath(breakdown: BuildBreakdown(
            productMinor: 50000, shippingMinor: 7900, platformFeeMinor: 7500));

        var result = await _sut.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.OrderId.Should().Be(NewOrderId);
        result.Value.OrderNumber.Should().Be(OrderNumber);
        result.Value.TotalPriceMinor.Should().Be(57900);
        result.Value.Currency.Should().Be(Currency);
    }

    [Fact]
    public async Task Handler_does_not_call_SaveChangesAsync()
    {
        // T-0063 contract: UnitOfWorkPipelineBehavior commits, the handler
        // never does. The test asserts no IUnitOfWork was injected; the
        // 8-step flow's `await orders.AddAsync(...)` call is the only
        // persistence-shaped action the handler performs. Pinned so a
        // future refactor that injects IUnitOfWork into this handler
        // (a smell) shows up here.
        WireHappyPath();

        await _sut.Handle(ValidCommand(), CancellationToken.None);

        // The handler shouldn't have any way to call SaveChanges. We
        // assert AddAsync was called; the rest of the contract is that
        // the handler's signature doesn't take an IUnitOfWork — verified
        // by the constructor binding in the test ctor (no IUnitOfWork
        // mock exists).
        await _orders.Received(1).AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
    }
}
