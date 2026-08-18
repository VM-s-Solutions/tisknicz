using FluentAssertions;
using Makables.Core.AppServices.Features.Orders;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Observability;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Payments;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Orders;

/// <summary>
/// Pins the T-0065 <see cref="CreatePaymentSession.Handler"/> 7-step
/// flow, with full coverage of the Q1 verify-then-recreate decision
/// tree.
/// </summary>
public class CreatePaymentSessionHandlerTests
{
    private const string CustomerUserId = "user-customer-1";
    private const string OrderId = "ord-1";
    private const string CountryCode = "CZ";
    private const string TransId1 = "AB1C-D34E";
    private const string TransId2 = "FFFF-9999";
    private const string Redirect1 = "https://payments.comgate.cz/pay/AB1C-D34E";
    private const string Redirect2 = "https://payments.comgate.cz/pay/FFFF-9999";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-05T10:00:00Z");

    private readonly IUserSessionProvider _session = Substitute.For<IUserSessionProvider>();
    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IPaymentProviderFactory _providerFactory = Substitute.For<IPaymentProviderFactory>();
    private readonly IPaymentProvider _provider = Substitute.For<IPaymentProvider>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly IPaymentMetrics _metrics = Substitute.For<IPaymentMetrics>();
    private readonly CreatePaymentSession.Handler _sut;

    public CreatePaymentSessionHandlerTests()
    {
        _session.GetUserId().Returns(CustomerUserId);
        _clock.UtcNow.Returns(Now);
        _providerFactory.ResolveAsync(CountryCode, Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(_provider));

        _sut = new CreatePaymentSession.Handler(
            _session, _orders, _providerFactory, _clock, _metrics,
            NullLogger<CreatePaymentSession.Handler>.Instance);
    }

    private static Order BuildOrderInState(OrderState target)
    {
        var o = Order.Create(
            id: OrderId,
            orderNumber: "M-CZ-20260042",
            customerUserId: CustomerUserId,
            makerId: "maker-1",
            productId: "prod-1",
            contactName: "Anna",
            contactEmail: "a@b.cz",
            contactPhone: "+420",
            productPriceAmountMinor: 50000,
            shippingPriceAmountMinor: 7900,
            platformFeeAmountMinor: 7500,
            makerPayoutAmountMinor: 50400,
            totalAmountMinor: 57900,
            currency: "CZK",
            vatRateBp: 2100,
            shippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
            zasilkovnaPickupPointId: "pp-42",
            countryCode: CountryCode);

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);

        if (target == OrderState.PendingPayment) return o;
        o.MarkAsPaid(clock, "preset-tx");
        if (target == OrderState.Paid) return o;
        o.Accept(clock);
        if (target == OrderState.Accepted) return o;

        throw new ArgumentOutOfRangeException(nameof(target), target,
            "Helper covers PendingPayment / Paid / Accepted only.");
    }

    private static CreatePaymentSession.Command ValidCommand() => new(OrderId);

    // ---- T-0165 (Q-0033): payment-session outcome emission ----

    [Fact]
    public async Task Successful_session_creation_records_the_created_outcome()
    {
        _provider.Code.Returns("comgate");
        var order = BuildOrderInState(OrderState.PendingPayment);
        _orders.GetByIdForCustomerAsync(OrderId, CustomerUserId, Arg.Any<CancellationToken>())
            .Returns(order);
        _provider.CreatePaymentAsync(order, Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(new PaymentSession(TransId1, Redirect1)));

        await _sut.Handle(ValidCommand(), CancellationToken.None);

        _metrics.Received(1).RecordSessionCreated("comgate", PaymentSessionOutcome.Created);
    }

    [Fact]
    public async Task Transient_provider_failure_records_the_transient_outcome()
    {
        // The bucket that says "wait" rather than "page someone".
        _provider.Code.Returns("comgate");
        var order = BuildOrderInState(OrderState.PendingPayment);
        _orders.GetByIdForCustomerAsync(OrderId, CustomerUserId, Arg.Any<CancellationToken>())
            .Returns(order);
        _provider.CreatePaymentAsync(order, Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Failure<PaymentSession>(
                Error.Transient(BusinessErrorMessage.PaymentProviderUnavailable)));

        var result = await _sut.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        _metrics.Received(1).RecordSessionCreated("comgate", PaymentSessionOutcome.Transient);
    }

    [Theory]
    [InlineData(ErrorType.Permanent)]
    [InlineData(ErrorType.Configuration)]
    [InlineData(ErrorType.Unknown)]
    public async Task Non_transient_provider_failures_all_fold_into_the_permanent_outcome(ErrorType type)
    {
        // Deliberate: only the retry-worthy split matters operationally, and
        // the exact code is already in the logs. Folding keeps tag cardinality
        // at two failure values instead of four.
        _provider.Code.Returns("comgate");
        var order = BuildOrderInState(OrderState.PendingPayment);
        _orders.GetByIdForCustomerAsync(OrderId, CustomerUserId, Arg.Any<CancellationToken>())
            .Returns(order);
        _provider.CreatePaymentAsync(order, Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Failure<PaymentSession>(
                new Error("payment", BusinessErrorMessage.PaymentUnknownError, type)));

        await _sut.Handle(ValidCommand(), CancellationToken.None);

        _metrics.Received(1).RecordSessionCreated("comgate", PaymentSessionOutcome.Permanent);
    }

    [Fact]
    public async Task A_cached_session_records_nothing_no_provider_call_was_made()
    {
        _provider.Code.Returns("comgate");
        var order = BuildOrderInState(OrderState.PendingPayment);
        order.ReservePaymentSession(TransId1, Redirect1, _clock);
        _orders.GetByIdForCustomerAsync(OrderId, CustomerUserId, Arg.Any<CancellationToken>())
            .Returns(order);
        _provider.VerifyPaymentAsync(TransId1, Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(
                new PaymentStatus(PaymentState.Pending, null, null)));

        await _sut.Handle(ValidCommand(), CancellationToken.None);

        _metrics.DidNotReceive().RecordSessionCreated(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Happy_path_no_existing_ref_creates_new_session()
    {
        var order = BuildOrderInState(OrderState.PendingPayment);
        _orders.GetByIdForCustomerAsync(OrderId, CustomerUserId, Arg.Any<CancellationToken>())
            .Returns(order);
        _provider.CreatePaymentAsync(order, Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(new PaymentSession(TransId1, Redirect1)));

        var result = await _sut.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.PaymentProviderRef.Should().Be(TransId1);
        result.Value.RedirectUrl.Should().Be(Redirect1);
        order.PaymentProviderRef.Should().Be(TransId1);
        order.PaymentRedirectUrl.Should().Be(Redirect1);

        await _provider.DidNotReceive().VerifyPaymentAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _provider.Received(1).CreatePaymentAsync(order, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(PaymentState.Pending)]
    [InlineData(PaymentState.Authorized)]
    public async Task Existing_ref_with_live_status_returns_cached_url(PaymentState live)
    {
        var order = BuildOrderInState(OrderState.PendingPayment);
        order.ReservePaymentSession(TransId1, Redirect1, _clock);

        _orders.GetByIdForCustomerAsync(OrderId, CustomerUserId, Arg.Any<CancellationToken>())
            .Returns(order);
        _provider.VerifyPaymentAsync(TransId1, Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(new PaymentStatus(live, null, null)));

        var result = await _sut.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.PaymentProviderRef.Should().Be(TransId1);
        result.Value.RedirectUrl.Should().Be(Redirect1);
        // Critical: no second Comgate roundtrip.
        await _provider.DidNotReceive().CreatePaymentAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(PaymentState.Cancelled)]
    [InlineData(PaymentState.Failed)]
    public async Task Existing_ref_with_dead_status_recreates_session(PaymentState dead)
    {
        var order = BuildOrderInState(OrderState.PendingPayment);
        order.ReservePaymentSession(TransId1, Redirect1, _clock);

        _orders.GetByIdForCustomerAsync(OrderId, CustomerUserId, Arg.Any<CancellationToken>())
            .Returns(order);
        _provider.VerifyPaymentAsync(TransId1, Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(new PaymentStatus(dead, null, null)));
        _provider.CreatePaymentAsync(order, Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(new PaymentSession(TransId2, Redirect2)));

        var result = await _sut.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.PaymentProviderRef.Should().Be(TransId2);
        result.Value.RedirectUrl.Should().Be(Redirect2);
        order.PaymentProviderRef.Should().Be(TransId2, "ref overwritten");
        order.PaymentRedirectUrl.Should().Be(Redirect2);
    }

    [Fact]
    public async Task Existing_ref_with_PAID_status_returns_OrderPaymentAlreadyCaptured()
    {
        var order = BuildOrderInState(OrderState.PendingPayment);
        order.ReservePaymentSession(TransId1, Redirect1, _clock);

        _orders.GetByIdForCustomerAsync(OrderId, CustomerUserId, Arg.Any<CancellationToken>())
            .Returns(order);
        _provider.VerifyPaymentAsync(TransId1, Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(new PaymentStatus(PaymentState.Paid, "CARD", Now)));

        var result = await _sut.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderPaymentAlreadyCaptured);
        result.Error.Type.Should().Be(ErrorType.Conflict);
        await _provider.DidNotReceive().CreatePaymentAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Existing_ref_with_REFUNDED_status_returns_OrderPaymentAlreadyCaptured()
    {
        var order = BuildOrderInState(OrderState.PendingPayment);
        order.ReservePaymentSession(TransId1, Redirect1, _clock);

        _orders.GetByIdForCustomerAsync(OrderId, CustomerUserId, Arg.Any<CancellationToken>())
            .Returns(order);
        _provider.VerifyPaymentAsync(TransId1, Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(new PaymentStatus(PaymentState.Refunded, null, null)));

        var result = await _sut.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderPaymentAlreadyCaptured);
    }

    [Fact]
    public async Task Returns_Unauthorized_when_session_has_no_user()
    {
        _session.GetUserId().Returns((string?)null);

        var result = await _sut.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);
        await _orders.DidNotReceive().GetByIdForCustomerAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Order_not_found_returns_OrderNotFound_and_does_not_call_provider()
    {
        _orders.GetByIdForCustomerAsync(OrderId, CustomerUserId, Arg.Any<CancellationToken>())
            .Returns((Order?)null);

        var result = await _sut.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderNotFound);
        result.Error.Type.Should().Be(ErrorType.NotFound);
        await _providerFactory.DidNotReceive().ResolveAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(OrderState.Paid)]
    [InlineData(OrderState.Accepted)]
    public async Task Order_past_PendingPayment_returns_OrderInvalidStateForPayment(OrderState past)
    {
        var order = BuildOrderInState(past);
        _orders.GetByIdForCustomerAsync(OrderId, CustomerUserId, Arg.Any<CancellationToken>())
            .Returns(order);

        var result = await _sut.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderInvalidStateForPayment);
        result.Error.Type.Should().Be(ErrorType.Conflict);
        await _providerFactory.DidNotReceive().ResolveAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Factory_failure_propagates_verbatim()
    {
        var order = BuildOrderInState(OrderState.PendingPayment);
        _orders.GetByIdForCustomerAsync(OrderId, CustomerUserId, Arg.Any<CancellationToken>())
            .Returns(order);
        var factoryError = Error.Configuration(BusinessErrorMessage.PaymentProviderNotRegistered);
        _providerFactory.ResolveAsync(CountryCode, Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Failure<IPaymentProvider>(factoryError));

        var result = await _sut.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.PaymentProviderNotRegistered);
        result.Error.Type.Should().Be(ErrorType.Configuration);
    }

    [Fact]
    public async Task CreatePayment_transient_failure_propagates()
    {
        var order = BuildOrderInState(OrderState.PendingPayment);
        _orders.GetByIdForCustomerAsync(OrderId, CustomerUserId, Arg.Any<CancellationToken>())
            .Returns(order);
        _provider.CreatePaymentAsync(order, Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Failure<PaymentSession>(
                Error.Transient(BusinessErrorMessage.PaymentProviderUnavailable)));

        var result = await _sut.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.PaymentProviderUnavailable);
        result.Error.Type.Should().Be(ErrorType.Transient);
        order.PaymentProviderRef.Should().BeNull("no session was reserved on failure");
    }

    [Fact]
    public async Task VerifyPayment_transient_failure_propagates_without_create_call()
    {
        var order = BuildOrderInState(OrderState.PendingPayment);
        order.ReservePaymentSession(TransId1, Redirect1, _clock);
        _orders.GetByIdForCustomerAsync(OrderId, CustomerUserId, Arg.Any<CancellationToken>())
            .Returns(order);
        _provider.VerifyPaymentAsync(TransId1, Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Failure<PaymentStatus>(
                Error.Transient(BusinessErrorMessage.PaymentProviderUnavailable)));

        var result = await _sut.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.PaymentProviderUnavailable);
        await _provider.DidNotReceive().CreatePaymentAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
    }
}
