using System.Text.Json;
using FluentAssertions;
using Makables.Core.AppServices.Common;
using Makables.Core.AppServices.Features.Orders;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Makers;
using MakerEntity = Makables.Core.Domain.Makers.Maker;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Outbox;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Orders;

/// <summary>
/// Pins T-0071 <see cref="AcceptOrder.Handler"/> contract: session
/// resolution → maker scoping → owner-scoped order load → Order.Accept
/// state transition → outbox enqueue, all under the UoW pipeline (handler
/// never calls SaveChangesAsync).
/// </summary>
public class AcceptOrderHandlerTests
{
    private const string OrderId = "ord-1";
    private const string MakerUserId = "user-maker-1";
    private const string MakerId = "maker-1";
    private const string CustomerUserId = "user-cust-1";
    private const string WebBaseUrl = "https://makables.test";

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-08T10:00:00Z");

    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IMakerRepository _makers = Substitute.For<IMakerRepository>();
    private readonly IUserSessionProvider _session = Substitute.For<IUserSessionProvider>();
    private readonly IOutbox _outbox = Substitute.For<IOutbox>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly ILanguageResolver _languageResolver = Substitute.For<ILanguageResolver>();
    private readonly AcceptOrder.Handler _sut;

    public AcceptOrderHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _session.GetUserId().Returns(MakerUserId);
        _languageResolver.ResolveForUserAsync(Arg.Any<User>(), Arg.Any<CancellationToken>())
            .Returns("cs-CZ");

        var customer = User.Create(
            id: CustomerUserId, email: "a@b.cz", role: UserRole.Customer,
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

        _sut = new AcceptOrder.Handler(
            _orders, _users, _makers, _session, _outbox, _clock,
            _languageResolver, urls,
            NullLogger<AcceptOrder.Handler>.Instance);
    }

    private static Order BuildPaidOrder(string id = OrderId)
    {
        var o = Order.Create(
            id: id, orderNumber: "M-CZ-20260042",
            customerUserId: CustomerUserId, makerId: MakerId, productId: "prod-1",
            contactName: "Anna", contactEmail: "anna@example.cz", contactPhone: "+420",
            productPriceAmountMinor: 50000, shippingPriceAmountMinor: 7900,
            platformFeeAmountMinor: 7500, makerPayoutAmountMinor: 50400,
            totalAmountMinor: 57900, currency: "CZK", vatRateBp: 2100,
            shippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
            zasilkovnaPickupPointId: "pp-42", countryCode: "CZ");
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        o.MarkAsPaid(clock, "tx-1");
        return o;
    }

    [Fact]
    public async Task Happy_path_transitions_Paid_to_Accepted_and_enqueues_customer_email()
    {
        var order = BuildPaidOrder();
        _orders.GetByIdForMakerAsync(OrderId, MakerId, Arg.Any<CancellationToken>())
            .Returns(order);

        var result = await _sut.Handle(new AcceptOrder.Command(OrderId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        order.State.Should().Be(OrderState.Accepted);
        order.AcceptedAt.Should().Be(Now);
        _outbox.Received(1).Enqueue(
            OrderId,
            OutboxEventTypes.OrderAcceptedCustomerEmail,
            Arg.Any<string>());
    }

    [Fact]
    public async Task Happy_path_payload_carries_expected_fields()
    {
        var order = BuildPaidOrder();
        _orders.GetByIdForMakerAsync(OrderId, MakerId, Arg.Any<CancellationToken>())
            .Returns(order);
        string? capturedJson = null;
        _outbox.Enqueue(
            Arg.Any<string>(),
            OutboxEventTypes.OrderAcceptedCustomerEmail,
            Arg.Do<string>(j => capturedJson = j));

        await _sut.Handle(new AcceptOrder.Command(OrderId), CancellationToken.None);

        capturedJson.Should().NotBeNull();
        var payload = JsonSerializer.Deserialize<OrderAcceptedCustomerEmailPayload>(capturedJson!);
        payload!.OrderId.Should().Be(OrderId);
        payload.OrderNumber.Should().Be(order.OrderNumber);
        payload.Email.Should().Be(order.ContactEmail);
        payload.ContactName.Should().Be(order.ContactName);
        payload.LanguageCode.Should().Be("cs-CZ");
        payload.ActionUrl.Should().Be($"{WebBaseUrl}/objednavka/{OrderId}");
    }

    [Fact]
    public async Task No_session_returns_Unauthorized()
    {
        _session.GetUserId().Returns((string?)null);

        var result = await _sut.Handle(new AcceptOrder.Command(OrderId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);
        _outbox.DidNotReceive().Enqueue(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task No_maker_row_returns_OrderNotFound_without_enqueue()
    {
        _makers.GetByUserIdAsync(MakerUserId, Arg.Any<CancellationToken>())
            .Returns((MakerEntity?)null);

        var result = await _sut.Handle(new AcceptOrder.Command(OrderId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderNotFound);
        _outbox.DidNotReceive().Enqueue(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Order_owned_by_other_maker_returns_OrderNotFound_without_enqueue()
    {
        _orders.GetByIdForMakerAsync(OrderId, MakerId, Arg.Any<CancellationToken>())
            .Returns((Order?)null);

        var result = await _sut.Handle(new AcceptOrder.Command(OrderId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderNotFound);
        _outbox.DidNotReceive().Enqueue(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Order_not_in_Paid_state_returns_OrderInvalidTransition_without_enqueue()
    {
        // PendingPayment (not Paid) → Accept refuses.
        var order = Order.Create(
            id: OrderId, orderNumber: "M-CZ-20260042",
            customerUserId: CustomerUserId, makerId: MakerId, productId: "prod-1",
            contactName: "Anna", contactEmail: "a@b.cz", contactPhone: "+420",
            productPriceAmountMinor: 50000, shippingPriceAmountMinor: 7900,
            platformFeeAmountMinor: 7500, makerPayoutAmountMinor: 50400,
            totalAmountMinor: 57900, currency: "CZK", vatRateBp: 2100,
            shippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
            zasilkovnaPickupPointId: "pp-42", countryCode: "CZ");
        _orders.GetByIdForMakerAsync(OrderId, MakerId, Arg.Any<CancellationToken>())
            .Returns(order);

        var result = await _sut.Handle(new AcceptOrder.Command(OrderId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderInvalidTransition);
        order.State.Should().Be(OrderState.PendingPayment);
        _outbox.DidNotReceive().Enqueue(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Missing_customer_user_returns_OrderCustomerUserMissing_without_enqueue()
    {
        var order = BuildPaidOrder();
        _orders.GetByIdForMakerAsync(OrderId, MakerId, Arg.Any<CancellationToken>())
            .Returns(order);
        _users.GetByIdAsync(CustomerUserId, Arg.Any<CancellationToken>())
            .Returns((User?)null);

        var result = await _sut.Handle(new AcceptOrder.Command(OrderId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderCustomerUserMissing);
        _outbox.DidNotReceive().Enqueue(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }
}
