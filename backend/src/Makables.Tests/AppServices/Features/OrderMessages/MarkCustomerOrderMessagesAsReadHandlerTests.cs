using FluentAssertions;
using Makables.Core.AppServices.Features.OrderMessages;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.OrderMessages;
using Makables.Core.Domain.Orders;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.OrderMessages;

/// <summary>
/// T-0079 customer-host MarkAsRead handler: pins counter reset + pending
/// pointer clear + IDOR shield. Idempotent (second call returns 0).
/// </summary>
public class MarkCustomerOrderMessagesAsReadHandlerTests
{
    private const string OrderId = "ord-1";
    private const string CustomerUserId = "user-customer-1";

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-09T10:00:00Z");

    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IOrderMessageRepository _messages = Substitute.For<IOrderMessageRepository>();
    private readonly IUserSessionProvider _session = Substitute.For<IUserSessionProvider>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly MarkCustomerOrderMessagesAsRead.Handler _sut;

    public MarkCustomerOrderMessagesAsReadHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _session.GetUserId().Returns(CustomerUserId);
        _sut = new MarkCustomerOrderMessagesAsRead.Handler(
            _orders, _messages, _session, _clock);
    }

    private static Order BuildOrder() => Order.Create(
        id: OrderId, orderNumber: "M-CZ-20260042",
        customerUserId: CustomerUserId, makerId: "maker-1", productId: "prod-1",
        contactName: "Anna", contactEmail: "anna@example.cz", contactPhone: "+420",
        productPriceAmountMinor: 50000, shippingPriceAmountMinor: 7900,
        platformFeeAmountMinor: 7500, makerPayoutAmountMinor: 50400,
        totalAmountMinor: 57900, currency: "CZK", vatRateBp: 2100,
        shippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
        zasilkovnaPickupPointId: "pp-42", countryCode: "CZ");

    [Fact]
    public async Task Happy_path_resets_counter_and_clears_pending_pointer()
    {
        var order = BuildOrder();
        order.IncrementUnreadFor(OrderMessageAuthorRole.Maker);  // customer unread = 1
        order.IncrementUnreadFor(OrderMessageAuthorRole.Maker);  // = 2
        order.IncrementUnreadFor(OrderMessageAuthorRole.Maker);  // = 3
        order.MarkNotificationEmittedFor(OrderMessageAuthorRole.Maker, Now.AddMinutes(-1));
        _orders.GetByIdForCustomerAsync(OrderId, CustomerUserId, Arg.Any<CancellationToken>())
            .Returns(order);
        _messages.MarkAsReadForCustomerAsync(OrderId, CustomerUserId, Now, Arg.Any<CancellationToken>())
            .Returns(3);

        var result = await _sut.Handle(
            new MarkCustomerOrderMessagesAsRead.Command(OrderId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.MarkedCount.Should().Be(3);
        order.CustomerUnreadMessageCount.Should().Be(0);
        order.CustomerPendingNotificationEmailAt.Should().BeNull();
    }

    [Fact]
    public async Task Idempotent_second_call_returns_zero_marked()
    {
        var order = BuildOrder();
        _orders.GetByIdForCustomerAsync(OrderId, CustomerUserId, Arg.Any<CancellationToken>())
            .Returns(order);
        _messages.MarkAsReadForCustomerAsync(OrderId, CustomerUserId, Now, Arg.Any<CancellationToken>())
            .Returns(0);

        var result = await _sut.Handle(
            new MarkCustomerOrderMessagesAsRead.Command(OrderId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.MarkedCount.Should().Be(0);
        order.CustomerUnreadMessageCount.Should().Be(0);
        order.CustomerPendingNotificationEmailAt.Should().BeNull();
    }

    [Fact]
    public async Task Cross_tenant_returns_OrderNotFound()
    {
        _orders.GetByIdForCustomerAsync(OrderId, CustomerUserId, Arg.Any<CancellationToken>())
            .Returns((Order?)null);

        var result = await _sut.Handle(
            new MarkCustomerOrderMessagesAsRead.Command(OrderId),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderNotFound);
        await _messages.DidNotReceive().MarkAsReadForCustomerAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }
}
