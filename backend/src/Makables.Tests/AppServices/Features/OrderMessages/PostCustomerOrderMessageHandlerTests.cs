using System.Text.Json;
using FluentAssertions;
using Makables.Core.AppServices.Common;
using Makables.Core.AppServices.Features.OrderMessages;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Makers;
using MakerEntity = Makables.Core.Domain.Makers.Maker;
using Makables.Core.Domain.OrderMessages;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Outbox;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.OrderMessages;

/// <summary>
/// T-0079 customer-host post-message handler: pins IDOR shield (cross-
/// tenant returns OrderNotFound), debounce (1st post emits + 2nd inside
/// 5min suppresses), outbox payload shape, and unread-counter increment.
/// </summary>
public class PostCustomerOrderMessageHandlerTests
{
    private const string OrderId = "ord-1";
    private const string CustomerUserId = "user-customer-1";
    private const string MakerUserId = "user-maker-1";
    private const string MakerId = "maker-1";
    private const string WebBaseUrl = "https://makables.test";
    private const string NewMessageId = "msg-1";

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-09T10:00:00Z");

    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IOrderMessageRepository _messages = Substitute.For<IOrderMessageRepository>();
    private readonly IMakerRepository _makers = Substitute.For<IMakerRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IUserSessionProvider _session = Substitute.For<IUserSessionProvider>();
    private readonly IOutbox _outbox = Substitute.For<IOutbox>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly IIdGenerator _ids = Substitute.For<IIdGenerator>();
    private readonly ILanguageResolver _languageResolver = Substitute.For<ILanguageResolver>();
    private readonly PostCustomerOrderMessage.Handler _sut;

    public PostCustomerOrderMessageHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _session.GetUserId().Returns(CustomerUserId);
        _ids.Next().Returns(NewMessageId);
        _languageResolver.ResolveForUserAsync(Arg.Any<User>(), Arg.Any<CancellationToken>())
            .Returns("cs-CZ");

        var maker = MakerEntity.Create(
            id: MakerId, userId: MakerUserId,
            registrationNumber: "27074358", vatId: null,
            companyName: "Maker s.r.o.", legalForm: null,
            registeredAddressId: "addr-1",
            incorporatedOn: null, isActiveInRegistry: true,
            sourceRegistry: "ares",
            snapshotFetchedAt: Now, snapshotIsStale: false,
            countryCode: "CZ", slug: "maker");
        _makers.GetByIdAsync(MakerId, Arg.Any<CancellationToken>()).Returns(maker);

        var makerUser = User.Create(
            id: MakerUserId, email: "maker@example.cz", role: UserRole.Maker,
            fullName: "M Owner", countryCodePrimary: "CZ",
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        _users.GetByIdAsync(MakerUserId, Arg.Any<CancellationToken>()).Returns(makerUser);

        var urls = Options.Create(new PublicAppUrlsOptions
        {
            WebBaseUrl = WebBaseUrl,
            MagicLinkPath = "/auth/magic?token={token}",
            EmailConfirmationPath = "/auth/confirm?token={token}",
            PasswordResetPath = "/auth/reset?token={token}",
        });

        _sut = new PostCustomerOrderMessage.Handler(
            _orders, _messages, _makers, _users, _session, _outbox, _clock, _ids,
            _languageResolver, urls,
            NullLogger<PostCustomerOrderMessage.Handler>.Instance);
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
    public async Task Happy_path_persists_increments_counter_and_emits_outbox_event()
    {
        var order = BuildOrder();
        _orders.GetByIdForCustomerAsync(OrderId, CustomerUserId, Arg.Any<CancellationToken>())
            .Returns(order);

        var result = await _sut.Handle(
            new PostCustomerOrderMessage.Command(OrderId, "Hello maker"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.MessageId.Should().Be(NewMessageId);
        order.MakerUnreadMessageCount.Should().Be(1);
        order.MakerPendingNotificationEmailAt.Should().Be(Now);
        await _messages.Received(1).AddAsync(Arg.Any<OrderMessage>(), Arg.Any<CancellationToken>());
        _outbox.Received(1).Enqueue(
            OrderId,
            OutboxEventTypes.OrderMessagePostedMakerEmail,
            Arg.Any<string>());
    }

    [Fact]
    public async Task Cross_tenant_returns_OrderNotFound_without_persisting()
    {
        // Repo returns null for both unknown ids AND cross-tenant ids.
        _orders.GetByIdForCustomerAsync(OrderId, CustomerUserId, Arg.Any<CancellationToken>())
            .Returns((Order?)null);

        var result = await _sut.Handle(
            new PostCustomerOrderMessage.Command(OrderId, "Hi"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderNotFound);
        await _messages.DidNotReceive().AddAsync(Arg.Any<OrderMessage>(), Arg.Any<CancellationToken>());
        _outbox.DidNotReceive().Enqueue(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Second_post_within_5_minutes_suppresses_outbox_emit()
    {
        var order = BuildOrder();
        // Simulate a prior emit 3 min ago by stamping the pointer.
        order.MarkNotificationEmittedFor(OrderMessageAuthorRole.Customer, Now.AddMinutes(-3));
        _orders.GetByIdForCustomerAsync(OrderId, CustomerUserId, Arg.Any<CancellationToken>())
            .Returns(order);

        var result = await _sut.Handle(
            new PostCustomerOrderMessage.Command(OrderId, "Quick follow-up"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // Message still inserted + counter bumped.
        await _messages.Received(1).AddAsync(Arg.Any<OrderMessage>(), Arg.Any<CancellationToken>());
        order.MakerUnreadMessageCount.Should().Be(1);
        // Debounce suppressed the outbox enqueue.
        _outbox.DidNotReceive().Enqueue(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
        // Pointer stays at 3 min ago, not refreshed.
        order.MakerPendingNotificationEmailAt.Should().Be(Now.AddMinutes(-3));
    }

    [Fact]
    public async Task Post_after_debounce_window_emits_new_outbox_event_and_refreshes_pointer()
    {
        var order = BuildOrder();
        order.MarkNotificationEmittedFor(OrderMessageAuthorRole.Customer, Now.AddMinutes(-6));
        _orders.GetByIdForCustomerAsync(OrderId, CustomerUserId, Arg.Any<CancellationToken>())
            .Returns(order);

        var result = await _sut.Handle(
            new PostCustomerOrderMessage.Command(OrderId, "Later follow-up"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _outbox.Received(1).Enqueue(
            OrderId, OutboxEventTypes.OrderMessagePostedMakerEmail, Arg.Any<string>());
        order.MakerPendingNotificationEmailAt.Should().Be(Now);
    }

    [Fact]
    public async Task Outbox_payload_carries_expected_fields()
    {
        var order = BuildOrder();
        _orders.GetByIdForCustomerAsync(OrderId, CustomerUserId, Arg.Any<CancellationToken>())
            .Returns(order);
        string? capturedJson = null;
        _outbox.Enqueue(
            Arg.Any<string>(),
            OutboxEventTypes.OrderMessagePostedMakerEmail,
            Arg.Do<string>(j => capturedJson = j));

        await _sut.Handle(
            new PostCustomerOrderMessage.Command(OrderId, "Hello"),
            CancellationToken.None);

        capturedJson.Should().NotBeNull();
        var payload = JsonSerializer.Deserialize<OrderMessagePostedMakerEmailPayload>(capturedJson!);
        payload!.OrderId.Should().Be(OrderId);
        payload.OrderNumber.Should().Be(order.OrderNumber);
        payload.MakerEmail.Should().Be("maker@example.cz");
        payload.SenderName.Should().Be(order.ContactName);
        payload.UnreadMessageCount.Should().Be(1);
        payload.LanguageCode.Should().Be("cs-CZ");
        payload.ActionUrl.Should().Be($"{WebBaseUrl}/maker/orders/{OrderId}");
    }

    [Fact]
    public async Task No_session_returns_Unauthorized()
    {
        _session.GetUserId().Returns((string?)null);

        var result = await _sut.Handle(
            new PostCustomerOrderMessage.Command(OrderId, "Hi"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);
        _outbox.DidNotReceive().Enqueue(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }
}
