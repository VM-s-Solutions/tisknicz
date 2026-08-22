using FluentAssertions;
using Makables.Core.AppServices.Common;
using Makables.Core.AppServices.Features.Orders;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Makers;
using MakerEntity = Makables.Core.Domain.Makers.Maker;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Outbox;
using Makables.Core.Domain.Payments;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Orders;

/// <summary>
/// T-0181 / Q-0041 — the maker's refusal of a paid order.
///
/// <para>
/// The money-touching rules are what these tests exist for: the refusal
/// WINDOW is read from <c>CountryConfiguration</c> (never a constant),
/// the provider is called BEFORE the aggregate is mutated (so a gateway
/// failure leaves the order untouched), and a re-run refunds nothing a
/// second time.
/// </para>
/// </summary>
public sealed class RefuseOrderHandlerTests
{
    private const string OrderId = "order-1";
    private const string MakerId = "maker-1";
    private const string MakerUserId = "maker-user-1";
    private const string CustomerUserId = "user-1";
    private static readonly DateTimeOffset PaidAt = new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IMakerRepository _makers = Substitute.For<IMakerRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly ICountryConfigurationRepository _configs =
        Substitute.For<ICountryConfigurationRepository>();
    private readonly IPaymentProviderFactory _providerFactory =
        Substitute.For<IPaymentProviderFactory>();
    private readonly IPaymentProvider _provider = Substitute.For<IPaymentProvider>();
    private readonly IUserSessionProvider _session = Substitute.For<IUserSessionProvider>();
    private readonly IOutbox _outbox = Substitute.For<IOutbox>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly ILanguageResolver _languageResolver = Substitute.For<ILanguageResolver>();

    /// <summary>Wall-clock is never used — the window boundary is pinned.</summary>
    private void SetNow(DateTimeOffset now) => _clock.UtcNow.Returns(now);

    private RefuseOrder.Handler Sut() => new(
        _orders, _makers, _users, _configs, _providerFactory, _session,
        _outbox, _clock, _languageResolver,
        Options.Create(new PublicAppUrlsOptions { WebBaseUrl = "https://makables.test" }),
        NullLogger<RefuseOrder.Handler>.Instance);

    public RefuseOrderHandlerTests()
    {
        SetNow(PaidAt.AddHours(1));
        _session.GetUserId().Returns(MakerUserId);
        _languageResolver.ResolveForUserAsync(Arg.Any<User>(), Arg.Any<CancellationToken>())
            .Returns("cs-CZ");

        _users.GetByIdAsync(CustomerUserId, Arg.Any<CancellationToken>()).Returns(
            User.Create(CustomerUserId, "a@b.cz", UserRole.Customer, "Anna", "CZ",
                "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB"));

        _makers.GetByUserIdAsync(MakerUserId, Arg.Any<CancellationToken>()).Returns(
            MakerEntity.Create(
                id: MakerId, userId: MakerUserId, registrationNumber: "27074358", vatId: null,
                companyName: "Maker s.r.o.", legalForm: null, registeredAddressId: "addr-1",
                incorporatedOn: null, isActiveInRegistry: true, sourceRegistry: "ares",
                snapshotFetchedAt: PaidAt, snapshotIsStale: false, countryCode: "CZ", slug: "maker"));

        _configs.GetByCodeAsync("CZ", Arg.Any<CancellationToken>()).Returns(CzConfig(48));

        _providerFactory.ResolveAsync("CZ", Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(_provider));
        _provider.RefundAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(new RefundReceipt("refund-1", 57900, "CZK", PaidAt)));
    }

    private static CountryConfiguration CzConfig(int windowHours)
    {
        var config = CountryConfiguration.Create(
            "CZ", "CZK", "cs-CZ", "Europe/Prague", "+420", "d. M. yyyy",
            2100, "DIČ", "DIČ DPH", "IČO",
            "comgate", "packeta", "ares", "resend",
            "JVM YORE s.r.o.", "00000000",
            reducedVatRateBp: 1200, invoicingMode: InvoicingMode.None,
            platformFeeRateBp: 1500, defaultShippingPriceMinor: 7900);
        // The window is a tunable row — the tests move it, exactly as an
        // admin would, to prove the boundary is not hard-coded.
        typeof(CountryConfiguration)
            .GetProperty(nameof(CountryConfiguration.MakerRefusalWindowHours))!
            .SetValue(config, windowHours);
        return config;
    }

    private Order PaidOrder()
    {
        var order = Order.Create(
            id: OrderId, orderNumber: "M-CZ-20260042",
            customerUserId: CustomerUserId, makerId: MakerId, productId: "prod-1",
            contactName: "Anna", contactEmail: "anna@example.cz", contactPhone: "+420",
            productPriceAmountMinor: 50000, shippingPriceAmountMinor: 7900,
            platformFeeAmountMinor: 7500, makerPayoutAmountMinor: 50400,
            totalAmountMinor: 57900, currency: "CZK", vatRateBp: 2100,
            shippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
            zasilkovnaPickupPointId: "pp-42", countryCode: "CZ");
        var paidClock = Substitute.For<IClock>();
        paidClock.UtcNow.Returns(PaidAt);
        order.MarkAsPaid(paidClock, "tx-1");
        _orders.GetByIdForMakerAsync(OrderId, MakerId, Arg.Any<CancellationToken>()).Returns(order);
        return order;
    }

    [Fact]
    public async Task Within_the_window_refunds_cancels_and_notifies()
    {
        var order = PaidOrder();

        var result = await Sut().Handle(new RefuseOrder.Command(OrderId, "Došel materiál"), default);

        result.IsSuccess.Should().BeTrue();
        order.State.Should().Be(OrderState.Cancelled);
        order.CancellationSource.Should().Be(OrderCancellationSource.Maker);
        order.RefundedAmountMinor.Should().Be(57900);
        await _provider.Received(1).RefundAsync("tx-1", 57900, "CZK", Arg.Any<CancellationToken>());
        _outbox.Received(1).Enqueue(OrderId, OutboxEventTypes.OrderCancelledCustomerEmail, Arg.Any<string>());
    }

    [Fact]
    public async Task Past_the_window_refuses_and_touches_neither_money_nor_state()
    {
        var order = PaidOrder();
        SetNow(PaidAt.AddHours(48).AddMinutes(1));   // one minute past

        var result = await Sut().Handle(new RefuseOrder.Command(OrderId, "Došel materiál"), default);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderRefusalWindowExpired);
        order.State.Should().Be(OrderState.Paid);
        await _provider.DidNotReceive().RefundAsync(
            Arg.Any<string>(), Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        _outbox.DidNotReceive().Enqueue(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Exactly_on_the_boundary_is_still_allowed()
    {
        PaidOrder();
        SetNow(PaidAt.AddHours(48));

        var result = await Sut().Handle(new RefuseOrder.Command(OrderId, "Došel materiál"), default);

        result.IsSuccess.Should().BeTrue("the window is inclusive of its final moment");
    }

    [Fact]
    public async Task The_window_comes_from_config_not_a_constant()
    {
        PaidOrder();
        _configs.GetByCodeAsync("CZ", Arg.Any<CancellationToken>()).Returns(CzConfig(72));
        SetNow(PaidAt.AddHours(60));   // past 48, inside 72

        var result = await Sut().Handle(new RefuseOrder.Command(OrderId, "Došel materiál"), default);

        result.IsSuccess.Should().BeTrue("moving the config row must move the boundary");
    }

    [Fact]
    public async Task A_gateway_failure_leaves_the_order_untouched()
    {
        var order = PaidOrder();
        _provider.RefundAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Failure<RefundReceipt>(
                Error.Transient(BusinessErrorMessage.PaymentProviderUnavailable)));

        var result = await Sut().Handle(new RefuseOrder.Command(OrderId, "Došel materiál"), default);

        result.IsSuccess.Should().BeFalse();
        order.State.Should().Be(OrderState.Paid, "money moves first — a failure must cancel nothing");
        order.RefundedAmountMinor.Should().Be(0);
        _outbox.DidNotReceive().Enqueue(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Re_running_on_an_already_cancelled_order_refunds_nothing_twice()
    {
        var order = PaidOrder();
        await Sut().Handle(new RefuseOrder.Command(OrderId, "Došel materiál"), default);
        _provider.ClearReceivedCalls();
        _outbox.ClearReceivedCalls();

        var second = await Sut().Handle(new RefuseOrder.Command(OrderId, "Došel materiál"), default);

        second.IsSuccess.Should().BeTrue("a re-run is Silent Success, not an error");
        order.RefundedAmountMinor.Should().Be(57900, "the amount must not double");
        await _provider.DidNotReceive().RefundAsync(
            Arg.Any<string>(), Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        _outbox.DidNotReceive().Enqueue(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Another_makers_order_is_not_found_never_a_403()
    {
        _orders.GetByIdForMakerAsync(OrderId, MakerId, Arg.Any<CancellationToken>())
            .Returns((Order?)null);

        var result = await Sut().Handle(new RefuseOrder.Command(OrderId, "Došel materiál"), default);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderNotFound,
            "a 403 would confirm the order exists");
    }

    [Fact]
    public void A_refusal_must_carry_a_reason()
    {
        var validator = new RefuseOrder.Validator();

        validator.Validate(new RefuseOrder.Command(OrderId, "")).IsValid.Should().BeFalse();
        validator.Validate(new RefuseOrder.Command(OrderId, "Došel materiál")).IsValid.Should().BeTrue();
    }
}
