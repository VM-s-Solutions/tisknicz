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
/// T-0079 maker-host post-message handler — symmetric pins.
/// </summary>
public class PostMakerOrderMessageHandlerTests
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
    private readonly PostMakerOrderMessage.Handler _sut;

    public PostMakerOrderMessageHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _session.GetUserId().Returns(MakerUserId);
        _ids.Next().Returns(NewMessageId);
        _languageResolver.ResolveForUserAsync(Arg.Any<User>(), Arg.Any<CancellationToken>())
            .Returns("cs-CZ");

        var customer = User.Create(
            id: CustomerUserId, email: "anna@example.cz", role: UserRole.Customer,
            fullName: "Anna", countryCodePrimary: "CZ",
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        _users.GetByIdAsync(CustomerUserId, Arg.Any<CancellationToken>()).Returns(customer);

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

        var urls = Options.Create(new PublicAppUrlsOptions
        {
            WebBaseUrl = WebBaseUrl,
            MagicLinkPath = "/auth/magic?token={token}",
            EmailConfirmationPath = "/auth/confirm?token={token}",
            PasswordResetPath = "/auth/reset?token={token}",
        });

        _sut = new PostMakerOrderMessage.Handler(
            _orders, _messages, _makers, _users, _session, _outbox, _clock, _ids,
            _languageResolver, urls,
            NullLogger<PostMakerOrderMessage.Handler>.Instance);
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
    public async Task Happy_path_persists_and_emits_customer_email_event()
    {
        var order = BuildOrder();
        _orders.GetByIdForMakerAsync(OrderId, MakerId, Arg.Any<CancellationToken>())
            .Returns(order);

        var result = await _sut.Handle(
            new PostMakerOrderMessage.Command(OrderId, "Update on production"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        order.CustomerUnreadMessageCount.Should().Be(1);
        order.CustomerPendingNotificationEmailAt.Should().Be(Now);
        _outbox.Received(1).Enqueue(
            OrderId,
            OutboxEventTypes.OrderMessagePostedCustomerEmail,
            Arg.Any<string>());
    }

    [Fact]
    public async Task Cross_maker_returns_OrderNotFound()
    {
        _orders.GetByIdForMakerAsync(OrderId, MakerId, Arg.Any<CancellationToken>())
            .Returns((Order?)null);

        var result = await _sut.Handle(
            new PostMakerOrderMessage.Command(OrderId, "Hi"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderNotFound);
        _outbox.DidNotReceive().Enqueue(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Outbox_payload_uses_maker_company_name_and_customer_email()
    {
        var order = BuildOrder();
        _orders.GetByIdForMakerAsync(OrderId, MakerId, Arg.Any<CancellationToken>())
            .Returns(order);
        string? capturedJson = null;
        _outbox.Enqueue(
            Arg.Any<string>(),
            OutboxEventTypes.OrderMessagePostedCustomerEmail,
            Arg.Do<string>(j => capturedJson = j));

        await _sut.Handle(new PostMakerOrderMessage.Command(OrderId, "Hi"), CancellationToken.None);

        capturedJson.Should().NotBeNull();
        var payload = JsonSerializer.Deserialize<OrderMessagePostedCustomerEmailPayload>(capturedJson!);
        payload!.SenderName.Should().Be("Maker s.r.o.");
        payload.Email.Should().Be(order.ContactEmail);
        payload.UnreadMessageCount.Should().Be(1);
    }
}
