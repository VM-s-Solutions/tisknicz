using FluentAssertions;
using Makables.Core.AppServices.Features.Reviews;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Products;
using Makables.Core.Domain.Reviews;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using MakerEntity = Makables.Core.Domain.Makers.Maker;

namespace Makables.Tests.AppServices.Features.Reviews;

/// <summary>
/// T-0100 <see cref="SubmitReview"/> contract: the customer-scoped IDOR
/// shield (404, no existence leak), the eligibility gate
/// (<c>ReviewOrderNotDelivered</c>), the per-order guard
/// (<c>ReviewAlreadyExists</c>), and the happy-path Review insert + the
/// recompute-from-rows mutation against a row-locked Maker.
/// </summary>
public class SubmitReviewHandlerTests
{
    private const string OrderId = "ord-1";
    private const string MakerId = "maker-1";
    private const string CustomerUserId = "user-cust-1";

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-14T10:00:00Z");

    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IReviewRepository _reviews = Substitute.For<IReviewRepository>();
    private readonly IMakerRepository _makers = Substitute.For<IMakerRepository>();
    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly IUserSessionProvider _session = Substitute.For<IUserSessionProvider>();
    private readonly IIdGenerator _ids = Substitute.For<IIdGenerator>();
    private readonly SubmitReview.Handler _sut;

    public SubmitReviewHandlerTests()
    {
        _session.GetUserId().Returns(CustomerUserId);
        _ids.Next().Returns("rev-1");
        _sut = new SubmitReview.Handler(
            _orders, _reviews, _makers, _products, _session, _ids,
            NullLogger<SubmitReview.Handler>.Instance);
    }

    private static Order BuildOrder(OrderState targetState)
    {
        var o = Order.Create(
            id: OrderId, orderNumber: "M-CZ-20260042",
            customerUserId: CustomerUserId, makerId: MakerId, productId: "prod-1",
            contactName: "Anna", contactEmail: "anna@example.cz", contactPhone: "+420",
            productPriceAmountMinor: 50000, shippingPriceAmountMinor: 7900,
            platformFeeAmountMinor: 7500, makerPayoutAmountMinor: 50400,
            totalAmountMinor: 57900, currency: "CZK", vatRateBp: 2100,
            shippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
            zasilkovnaPickupPointId: "pp-42", countryCode: "CZ");
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now.AddDays(-5));
        if (targetState is OrderState.Paid or OrderState.Accepted or OrderState.Shipped
            or OrderState.Delivered or OrderState.Completed)
        {
            o.MarkAsPaid(clock, "tx-1");
        }
        if (targetState is OrderState.Accepted or OrderState.Shipped
            or OrderState.Delivered or OrderState.Completed)
        {
            o.Accept(clock);
        }
        if (targetState is OrderState.Shipped or OrderState.Delivered or OrderState.Completed)
        {
            o.Ship(clock, "PKT-1", 7);
        }
        if (targetState is OrderState.Delivered or OrderState.Completed)
        {
            o.MarkAsDelivered(clock, OrderDeliverySource.Carrier);
        }
        if (targetState is OrderState.Completed)
        {
            o.Complete(clock);
        }
        return o;
    }

    private static MakerEntity BuildMaker()
    {
        var m = MakerEntity.Create(
            id: MakerId, userId: "user-maker-1",
            registrationNumber: "27074358", vatId: null,
            companyName: "Avast s.r.o.", legalForm: null,
            registeredAddressId: "addr-1", incorporatedOn: null,
            isActiveInRegistry: true, sourceRegistry: "ares",
            snapshotFetchedAt: Now, snapshotIsStale: false,
            countryCode: "CZ", slug: "avast");
        m.SetCatalogStats(0, 0, 3);
        return m;
    }

    [Fact]
    public async Task Happy_path_creates_review_and_recomputes_maker_rating()
    {
        var order = BuildOrder(OrderState.Delivered);
        var maker = BuildMaker();
        _orders.GetByIdForCustomerAsync(OrderId, CustomerUserId, Arg.Any<CancellationToken>())
            .Returns(order);
        _reviews.ExistsForOrderAsync(OrderId, Arg.Any<CancellationToken>()).Returns(false);
        _makers.GetByIdForUpdateAsync(MakerId, Arg.Any<CancellationToken>()).Returns(maker);
        // Existing active set is empty (this is the first review) — the
        // handler folds the new 4-star review → avg 4.0 → 40000 bp.
        _reviews.GetMakerRatingAggregateAsync(MakerId, Arg.Any<CancellationToken>())
            .Returns((0, 0d));
        Review? added = null;
        await _reviews.AddAsync(Arg.Do<Review>(r => added = r), Arg.Any<CancellationToken>());

        var result = await _sut.Handle(
            new SubmitReview.Command(OrderId, 4, "  Pěkné.  "), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ReviewId.Should().Be("rev-1");
        result.Value.Rating.Should().Be(4);
        added.Should().NotBeNull();
        added!.MakerId.Should().Be(MakerId, "maker_id is denormalized off the order");
        added.Body.Should().Be("Pěkné.");
        maker.RatingCount.Should().Be(1);
        maker.RatingAverageBp.Should().Be(40_000, "one 4-star review → AVG 4.0 → 40000 bp");
        maker.TotalOrders.Should().Be(3, "the recompute leaves TotalOrders untouched");
    }

    [Fact]
    public async Task Folds_new_review_into_existing_active_average()
    {
        var order = BuildOrder(OrderState.Completed);
        var maker = BuildMaker();
        _orders.GetByIdForCustomerAsync(OrderId, CustomerUserId, Arg.Any<CancellationToken>())
            .Returns(order);
        _reviews.ExistsForOrderAsync(OrderId, Arg.Any<CancellationToken>()).Returns(false);
        _makers.GetByIdForUpdateAsync(MakerId, Arg.Any<CancellationToken>()).Returns(maker);
        // 2 existing active reviews averaging 5.0; the new 2-star review
        // folds to (5*2 + 2) / 3 = 4.0 → 40000 bp, count 3.
        _reviews.GetMakerRatingAggregateAsync(MakerId, Arg.Any<CancellationToken>())
            .Returns((2, 5.0d));

        var result = await _sut.Handle(
            new SubmitReview.Command(OrderId, 2, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        maker.RatingCount.Should().Be(3);
        maker.RatingAverageBp.Should().Be(40_000, "(5*2 + 2)/3 = 4.0 → 40000 bp");
    }

    [Fact]
    public async Task Recomputes_product_rating_when_order_has_catalog_product()
    {
        var order = BuildOrder(OrderState.Delivered);
        var maker = BuildMaker();
        var product = Product.Create(
            id: "prod-1", makerId: MakerId, categoryId: "cat-1",
            title: "Hrnek", description: null,
            price: new Core.Domain.Money.Money(25000, "CZK"),
            priceType: PriceType.Fixed, weightGrams: 400, countryCode: "CZ");
        _orders.GetByIdForCustomerAsync(OrderId, CustomerUserId, Arg.Any<CancellationToken>())
            .Returns(order);
        _reviews.ExistsForOrderAsync(OrderId, Arg.Any<CancellationToken>()).Returns(false);
        _makers.GetByIdForUpdateAsync(MakerId, Arg.Any<CancellationToken>()).Returns(maker);
        _reviews.GetMakerRatingAggregateAsync(MakerId, Arg.Any<CancellationToken>())
            .Returns((0, 0d));
        _products.GetByIdForUpdateAsync("prod-1", Arg.Any<CancellationToken>()).Returns(product);
        // 1 existing active 5-star product review; the new 4-star folds
        // to (5*1 + 4) / 2 = 4.5 → 45000 bp, count 2.
        _reviews.GetProductRatingAggregateAsync("prod-1", Arg.Any<CancellationToken>())
            .Returns((1, 5.0d));
        Review? added = null;
        await _reviews.AddAsync(Arg.Do<Review>(r => added = r), Arg.Any<CancellationToken>());

        var result = await _sut.Handle(
            new SubmitReview.Command(OrderId, 4, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        added!.ProductId.Should().Be("prod-1", "product_id is denormalized off the order");
        product.RatingCount.Should().Be(2);
        product.RatingAverageBp.Should().Be(45_000, "(5*1 + 4)/2 = 4.5 → 45000 bp");
    }

    [Fact]
    public async Task Missing_or_inactive_product_skips_product_recompute_but_still_succeeds()
    {
        var order = BuildOrder(OrderState.Delivered);
        var maker = BuildMaker();
        _orders.GetByIdForCustomerAsync(OrderId, CustomerUserId, Arg.Any<CancellationToken>())
            .Returns(order);
        _reviews.ExistsForOrderAsync(OrderId, Arg.Any<CancellationToken>()).Returns(false);
        _makers.GetByIdForUpdateAsync(MakerId, Arg.Any<CancellationToken>()).Returns(maker);
        _reviews.GetMakerRatingAggregateAsync(MakerId, Arg.Any<CancellationToken>())
            .Returns((0, 0d));
        _products.GetByIdForUpdateAsync("prod-1", Arg.Any<CancellationToken>())
            .Returns((Product?)null);

        var result = await _sut.Handle(
            new SubmitReview.Command(OrderId, 4, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue("a deactivated product must not block the review");
        maker.RatingCount.Should().Be(1, "the maker recompute still applies");
        await _reviews.DidNotReceive()
            .GetProductRatingAggregateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pre_delivery_order_returns_orderNotDelivered_without_writes()
    {
        var order = BuildOrder(OrderState.Shipped);
        _orders.GetByIdForCustomerAsync(OrderId, CustomerUserId, Arg.Any<CancellationToken>())
            .Returns(order);

        var result = await _sut.Handle(
            new SubmitReview.Command(OrderId, 5, null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.ReviewOrderNotDelivered);
        await _reviews.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task Existing_review_returns_alreadyExists_without_second_row()
    {
        var order = BuildOrder(OrderState.Delivered);
        _orders.GetByIdForCustomerAsync(OrderId, CustomerUserId, Arg.Any<CancellationToken>())
            .Returns(order);
        _reviews.ExistsForOrderAsync(OrderId, Arg.Any<CancellationToken>()).Returns(true);

        var result = await _sut.Handle(
            new SubmitReview.Command(OrderId, 5, null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.ReviewAlreadyExists);
        await _reviews.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task Cross_tenant_order_returns_notFound_without_writes()
    {
        // Scoped repo returns null for a foreign order — same shape as
        // unknown id (IDOR shield, no existence oracle).
        _orders.GetByIdForCustomerAsync(OrderId, CustomerUserId, Arg.Any<CancellationToken>())
            .Returns((Order?)null);

        var result = await _sut.Handle(
            new SubmitReview.Command(OrderId, 5, null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderNotFound);
        await _reviews.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public void Validator_rejects_out_of_range_rating_and_oversize_body_accepts_valid()
    {
        var validator = new SubmitReview.Validator();

        validator.Validate(new SubmitReview.Command(OrderId, 0, null))
            .Errors.Should().Contain(e => e.ErrorCode == BusinessErrorMessage.ReviewRatingOutOfRange);
        validator.Validate(new SubmitReview.Command(OrderId, 6, null))
            .Errors.Should().Contain(e => e.ErrorCode == BusinessErrorMessage.ReviewRatingOutOfRange);
        validator.Validate(new SubmitReview.Command(OrderId, 4, new string('x', Review.MaxBodyLength + 1)))
            .Errors.Should().Contain(e => e.ErrorCode == BusinessErrorMessage.ReviewBodyTooLong);

        validator.Validate(new SubmitReview.Command(OrderId, 5, null)).IsValid.Should().BeTrue();
        validator.Validate(new SubmitReview.Command(OrderId, 3, "Krátký komentář.")).IsValid.Should().BeTrue();
    }
}
