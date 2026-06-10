using FluentAssertions;
using Makables.Core.AppServices.Features.OrderMessages;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Makers;
using MakerEntity = Makables.Core.Domain.Makers.Maker;
using Makables.Core.Domain.OrderMessages;
using Makables.Core.Domain.Orders;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.OrderMessages;

/// <summary>
/// T-0079 maker-host MarkAsRead handler — twin of
/// <see cref="MarkCustomerOrderMessagesAsReadHandlerTests"/> (review-fold
/// BLOCKER-2). Pins counter reset + pending-pointer clear + the TWO
/// <c>OrderNotFound</c> paths (§9: no maker row for the session user, and
/// cross-tenant / unknown order). Idempotent (second call returns 0).
/// </summary>
public class MarkMakerOrderMessagesAsReadHandlerTests
{
    private const string OrderId = "ord-1";
    private const string CustomerUserId = "user-customer-1";
    private const string MakerUserId = "user-maker-1";
    private const string MakerId = "maker-1";

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-09T10:00:00Z");

    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IOrderMessageRepository _messages = Substitute.For<IOrderMessageRepository>();
    private readonly IMakerRepository _makers = Substitute.For<IMakerRepository>();
    private readonly IUserSessionProvider _session = Substitute.For<IUserSessionProvider>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly MarkMakerOrderMessagesAsRead.Handler _sut;

    public MarkMakerOrderMessagesAsReadHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _session.GetUserId().Returns(MakerUserId);

        var maker = MakerEntity.Create(
            id: MakerId, userId: MakerUserId,
            registrationNumber: "27074358", vatId: null,
            companyName: "Maker s.r.o.", legalForm: null,
            registeredAddressId: "addr-1",
            incorporatedOn: null, isActiveInRegistry: true,
            sourceRegistry: "ares",
            snapshotFetchedAt: Now, snapshotIsStale: false,
            countryCode: "CZ", slug: "maker");
        _makers.GetByUserIdAsync(MakerUserId, Arg.Any<CancellationToken>()).Returns(maker);

        _sut = new MarkMakerOrderMessagesAsRead.Handler(
            _orders, _messages, _makers, _session, _clock);
    }

    private static Order BuildOrder() => Order.Create(
        id: OrderId, orderNumber: "M-CZ-20260042",
        customerUserId: CustomerUserId, makerId: MakerId, productId: "prod-1",
        contactName: "Anna", contactEmail: "anna@example.cz", contactPhone: "+420",
        productPriceAmountMinor: 50000, shippingPriceAmountMinor: 7900,
        platformFeeAmountMinor: 7500, makerPayoutAmountMinor: 50400,
        totalAmountMinor: 57900, currency: "CZK", vatRateBp: 2100,
        shippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
        zasilkovnaPickupPointId: "pp-42", countryCode: "CZ");

    [Fact]
    public async Task Happy_path_resets_maker_counter_and_clears_maker_pending_pointer()
    {
        var order = BuildOrder();
        order.IncrementUnreadFor(OrderMessageAuthorRole.Customer);  // maker unread = 1
        order.IncrementUnreadFor(OrderMessageAuthorRole.Customer);  // = 2
        order.MarkNotificationEmittedFor(OrderMessageAuthorRole.Customer, Now.AddMinutes(-1));
        _orders.GetByIdForMakerAsync(OrderId, MakerId, Arg.Any<CancellationToken>())
            .Returns(order);
        _messages.MarkAsReadForMakerAsync(OrderId, MakerId, Now, Arg.Any<CancellationToken>())
            .Returns(2);

        var result = await _sut.Handle(
            new MarkMakerOrderMessagesAsRead.Command(OrderId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.MarkedCount.Should().Be(2);
        order.MakerUnreadMessageCount.Should().Be(0);
        order.MakerPendingNotificationEmailAt.Should().BeNull();
    }

    [Fact]
    public async Task Idempotent_second_call_returns_zero_marked()
    {
        var order = BuildOrder();
        _orders.GetByIdForMakerAsync(OrderId, MakerId, Arg.Any<CancellationToken>())
            .Returns(order);
        _messages.MarkAsReadForMakerAsync(OrderId, MakerId, Now, Arg.Any<CancellationToken>())
            .Returns(0);

        var result = await _sut.Handle(
            new MarkMakerOrderMessagesAsRead.Command(OrderId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.MarkedCount.Should().Be(0);
        order.MakerUnreadMessageCount.Should().Be(0);
        order.MakerPendingNotificationEmailAt.Should().BeNull();
    }

    [Fact]
    public async Task Cross_tenant_order_returns_OrderNotFound_without_sweeping()
    {
        // Repo returns null for both unknown ids AND another maker's order.
        _orders.GetByIdForMakerAsync(OrderId, MakerId, Arg.Any<CancellationToken>())
            .Returns((Order?)null);

        var result = await _sut.Handle(
            new MarkMakerOrderMessagesAsRead.Command(OrderId),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderNotFound);
        await _messages.DidNotReceive().MarkAsReadForMakerAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Maker_audience_jwt_without_maker_row_returns_OrderNotFound()
    {
        // §9 negative path at MarkMakerOrderMessagesAsRead.cs:53 — leaks
        // nothing about whether the order exists.
        _makers.GetByUserIdAsync(MakerUserId, Arg.Any<CancellationToken>())
            .Returns((MakerEntity?)null);

        var result = await _sut.Handle(
            new MarkMakerOrderMessagesAsRead.Command(OrderId),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderNotFound);
        await _orders.DidNotReceive().GetByIdForMakerAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _messages.DidNotReceive().MarkAsReadForMakerAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }
}
