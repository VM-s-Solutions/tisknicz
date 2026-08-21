using System.Text.Json;
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
/// T-0083 <see cref="CancelExpiredOrder.Handler"/> contract: load
/// unscoped, Silent Success on wrong-state, AutoExpiry source stamp,
/// customer email enqueue with reason=AutoExpiry.
/// </summary>
public class CancelExpiredOrderHandlerTests
{
    private const string OrderId = "ord-1";
    private const string CustomerUserId = "user-customer-1";
    private const string WebBaseUrl = "https://makables.test";

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-10T02:00:00Z");

    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IOutbox _outbox = Substitute.For<IOutbox>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly ILanguageResolver _languageResolver = Substitute.For<ILanguageResolver>();
    private readonly CancelExpiredOrder.Handler _sut;

    public CancelExpiredOrderHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _languageResolver.ResolveForUserAsync(Arg.Any<User>(), Arg.Any<CancellationToken>())
            .Returns("cs-CZ");

        var customer = User.Create(
            id: CustomerUserId, email: "anna@example.cz", role: UserRole.Customer,
            fullName: "Anna", countryCodePrimary: "CZ",
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        _users.GetByIdAsync(CustomerUserId, Arg.Any<CancellationToken>()).Returns(customer);

        var urls = Options.Create(new PublicAppUrlsOptions
        {
            WebBaseUrl = WebBaseUrl,
        });

        _sut = new CancelExpiredOrder.Handler(
            _orders, _users, _outbox, _clock,
            _languageResolver, urls,
            NullLogger<CancelExpiredOrder.Handler>.Instance);
    }

    private static Order BuildPendingPaymentOrder() => Order.Create(
        id: OrderId, orderNumber: "M-CZ-20260042",
        customerUserId: CustomerUserId, makerId: "maker-1", productId: "prod-1",
        contactName: "Anna", contactEmail: "anna@example.cz", contactPhone: "+420",
        productPriceAmountMinor: 50000, shippingPriceAmountMinor: 7900,
        platformFeeAmountMinor: 7500, makerPayoutAmountMinor: 50400,
        totalAmountMinor: 57900, currency: "CZK", vatRateBp: 2100,
        shippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
        zasilkovnaPickupPointId: "pp-42", countryCode: "CZ");

    [Fact]
    public async Task Happy_path_transitions_PendingPayment_to_Cancelled_with_AutoExpiry_source()
    {
        var order = BuildPendingPaymentOrder();
        _orders.GetByIdUnscopedAsync(OrderId, Arg.Any<CancellationToken>()).Returns(order);

        var result = await _sut.Handle(
            new CancelExpiredOrder.Command(OrderId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.State.Should().Be(OrderState.Cancelled);
        order.State.Should().Be(OrderState.Cancelled);
        order.CancellationSource.Should().Be(OrderCancellationSource.AutoExpiry);
        order.CancelledAt.Should().Be(Now);
        _outbox.Received(1).Enqueue(
            OrderId, OutboxEventTypes.OrderCancelledCustomerEmail, Arg.Any<string>());
    }

    [Fact]
    public async Task Silent_success_when_order_already_cancelled()
    {
        // Order transitioned to Cancelled between projection and dispatch
        // (T-0105 customer-cancel race). Handler must NOT re-emit / re-mutate.
        var order = BuildPendingPaymentOrder();
        var initialClock = Substitute.For<IClock>();
        initialClock.UtcNow.Returns(Now.AddMinutes(-1));
        order.Cancel(initialClock, OrderCancellationSource.Customer);

        _orders.GetByIdUnscopedAsync(OrderId, Arg.Any<CancellationToken>()).Returns(order);

        var result = await _sut.Handle(
            new CancelExpiredOrder.Command(OrderId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.State.Should().Be(OrderState.Cancelled);
        order.CancellationSource.Should().Be(OrderCancellationSource.Customer); // unchanged
        _outbox.DidNotReceive().Enqueue(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Silent_success_when_order_already_paid_mid_flight()
    {
        // Comgate webhook landed AFTER projection but BEFORE dispatch.
        var order = BuildPendingPaymentOrder();
        var initialClock = Substitute.For<IClock>();
        initialClock.UtcNow.Returns(Now.AddSeconds(-30));
        order.MarkAsPaid(initialClock, "tx-1");

        _orders.GetByIdUnscopedAsync(OrderId, Arg.Any<CancellationToken>()).Returns(order);

        var result = await _sut.Handle(
            new CancelExpiredOrder.Command(OrderId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.State.Should().Be(OrderState.Paid);
        order.State.Should().Be(OrderState.Paid); // unchanged
        order.CancellationSource.Should().BeNull();
        _outbox.DidNotReceive().Enqueue(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Order_not_found_returns_OrderNotFound()
    {
        _orders.GetByIdUnscopedAsync(OrderId, Arg.Any<CancellationToken>())
            .Returns((Order?)null);

        var result = await _sut.Handle(
            new CancelExpiredOrder.Command(OrderId),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderNotFound);
        _outbox.DidNotReceive().Enqueue(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Outbox_payload_carries_AutoExpiry_reason_and_expected_fields()
    {
        var order = BuildPendingPaymentOrder();
        _orders.GetByIdUnscopedAsync(OrderId, Arg.Any<CancellationToken>()).Returns(order);
        string? capturedJson = null;
        _outbox.Enqueue(
            Arg.Any<string>(),
            OutboxEventTypes.OrderCancelledCustomerEmail,
            Arg.Do<string>(j => capturedJson = j));

        await _sut.Handle(new CancelExpiredOrder.Command(OrderId), CancellationToken.None);

        capturedJson.Should().NotBeNull();
        var payload = JsonSerializer.Deserialize<OrderCancelledCustomerEmailPayload>(capturedJson!);
        payload!.OrderId.Should().Be(OrderId);
        payload.OrderNumber.Should().Be(order.OrderNumber);
        payload.Email.Should().Be(order.ContactEmail);
        payload.ContactName.Should().Be(order.ContactName);
        payload.Reason.Should().Be(OrderCancellationSource.AutoExpiry);
        payload.LanguageCode.Should().Be("cs-CZ");
        payload.ActionUrl.Should().Be($"{WebBaseUrl}/objednavka/{OrderId}");
    }
}
