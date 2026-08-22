using FluentAssertions;
using Makables.Core.AppServices.Common;
using Makables.Core.AppServices.Features.Orders;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Outbox;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Orders;

/// <summary>
/// T-0181 / Q-0041 — the customer's cancellation of an UNPAID order.
///
/// <para>
/// The scope boundary is the point of these tests: the customer may
/// cancel from <c>PendingPayment</c> ONLY. A paid order is the maker's
/// "refuse" action, never the customer's — on made-to-order goods a
/// customer-triggered refund would return money after production may
/// have started. The aggregate would happily accept the transition, so
/// the role rule is enforced in the handler on purpose and asserted here.
/// </para>
/// </summary>
public sealed class CancelPendingOrderHandlerTests
{
    private const string OrderId = "order-1";
    private const string CustomerUserId = "user-1";
    private static readonly DateTimeOffset Now = new(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);

    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IOutbox _outbox = Substitute.For<IOutbox>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly ILanguageResolver _languageResolver = Substitute.For<ILanguageResolver>();
    private readonly CancelPendingOrder.Handler _sut;

    public CancelPendingOrderHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _languageResolver.ResolveForUserAsync(Arg.Any<User>(), Arg.Any<CancellationToken>())
            .Returns("cs-CZ");
        _users.GetByIdAsync(CustomerUserId, Arg.Any<CancellationToken>()).Returns(
            User.Create(CustomerUserId, "a@b.cz", UserRole.Customer, "Anna", "CZ",
                "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB"));

        _sut = new CancelPendingOrder.Handler(
            _orders, _users, _outbox, _clock, _languageResolver,
            Options.Create(new PublicAppUrlsOptions { WebBaseUrl = "https://makables.test" }),
            NullLogger<CancelPendingOrder.Handler>.Instance);
    }

    private Order Pending()
    {
        var order = Order.Create(
            id: OrderId, orderNumber: "M-CZ-20260042",
            customerUserId: CustomerUserId, makerId: "maker-1", productId: "prod-1",
            contactName: "Anna", contactEmail: "anna@example.cz", contactPhone: "+420",
            productPriceAmountMinor: 50000, shippingPriceAmountMinor: 7900,
            platformFeeAmountMinor: 7500, makerPayoutAmountMinor: 50400,
            totalAmountMinor: 57900, currency: "CZK", vatRateBp: 2100,
            shippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
            zasilkovnaPickupPointId: "pp-42", countryCode: "CZ");
        _orders.GetByIdForCustomerAsync(OrderId, CustomerUserId, Arg.Any<CancellationToken>())
            .Returns(order);
        return order;
    }

    [Fact]
    public async Task Cancels_an_unpaid_order_and_notifies_without_moving_money()
    {
        var order = Pending();

        var result = await _sut.Handle(new CancelPendingOrder.Command(OrderId, CustomerUserId), default);

        result.IsSuccess.Should().BeTrue();
        order.State.Should().Be(OrderState.Cancelled);
        order.CancellationSource.Should().Be(OrderCancellationSource.Customer);
        order.RefundedAmountMinor.Should().Be(0, "nothing was captured on an unpaid order");
        _outbox.Received(1).Enqueue(OrderId, OutboxEventTypes.OrderCancelledCustomerEmail, Arg.Any<string>());
    }

    [Fact]
    public async Task A_paid_order_is_not_the_customers_to_cancel()
    {
        var order = Pending();
        order.MarkAsPaid(_clock, "tx-1");

        var result = await _sut.Handle(new CancelPendingOrder.Command(OrderId, CustomerUserId), default);

        result.IsSuccess.Should().BeFalse(
            "a paid order is the maker's 'refuse' action — a customer refund here could follow production");
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderInvalidTransition);
        order.State.Should().Be(OrderState.Paid);
        _outbox.DidNotReceive().Enqueue(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Re_running_on_an_already_cancelled_order_emits_nothing_twice()
    {
        var order = Pending();
        await _sut.Handle(new CancelPendingOrder.Command(OrderId, CustomerUserId), default);
        _outbox.ClearReceivedCalls();

        var second = await _sut.Handle(new CancelPendingOrder.Command(OrderId, CustomerUserId), default);

        second.IsSuccess.Should().BeTrue("a re-run is Silent Success, not an error");
        order.State.Should().Be(OrderState.Cancelled);
        _outbox.DidNotReceive().Enqueue(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Another_customers_order_is_not_found_never_a_403()
    {
        _orders.GetByIdForCustomerAsync(OrderId, CustomerUserId, Arg.Any<CancellationToken>())
            .Returns((Order?)null);

        var result = await _sut.Handle(new CancelPendingOrder.Command(OrderId, CustomerUserId), default);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderNotFound,
            "a 403 would confirm the order exists — the scoped repository IS the shield (ADR 0013)");
    }
}
