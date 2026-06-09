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
/// Pins T-0076 <see cref="MarkOrderDelivered.Handler"/> contract:
/// - per-source lookup branch (Customer scoped vs Auto/Carrier unscoped),
/// - Shipped → Delivered transition + DeliverySource + DeliveredAt stamp,
/// - Silent Success on already-Delivered re-call (no outbox emission),
/// - exactly one OrderDeliveredCustomerEmail outbox row per successful
///   non-idempotent transition,
/// - DeliveredAtOverride wins over clock when supplied (T-0078 carrier path),
/// - no SaveChangesAsync (UoW pipeline commits).
/// </summary>
public class MarkOrderDeliveredHandlerTests
{
    private const string OrderId = "ord-1";
    private const string CustomerUserId = "user-cust-1";
    private const string MakerId = "maker-1";
    private const string WebBaseUrl = "https://makables.test";

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-09T10:00:00Z");

    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IUserSessionProvider _session = Substitute.For<IUserSessionProvider>();
    private readonly IOutbox _outbox = Substitute.For<IOutbox>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly ILanguageResolver _languageResolver = Substitute.For<ILanguageResolver>();
    private readonly MarkOrderDelivered.Handler _sut;

    public MarkOrderDeliveredHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _session.GetUserId().Returns(CustomerUserId);
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
            MagicLinkPath = "/auth/magic?token={token}",
            EmailConfirmationPath = "/auth/confirm?token={token}",
            PasswordResetPath = "/auth/reset?token={token}",
        });

        _sut = new MarkOrderDelivered.Handler(
            _orders, _users, _session, _outbox, _clock,
            _languageResolver, urls,
            NullLogger<MarkOrderDelivered.Handler>.Instance);
    }

    private static Order BuildShippedOrder(string id = OrderId)
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
        clock.UtcNow.Returns(Now.AddDays(-5));
        o.MarkAsPaid(clock, "tx-1");
        o.Accept(clock);
        o.Ship(clock, "PKT-1", 7);
        return o;
    }

    private static Order BuildDeliveredOrder()
    {
        var o = BuildShippedOrder();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now.AddDays(-1));
        // Stamped by the first writer with a different source so we can
        // assert the second writer doesn't overwrite it.
        o.MarkAsDelivered(clock, OrderDeliverySource.Customer);
        return o;
    }

    [Fact]
    public async Task Happy_path_Customer_source_transitions_to_Delivered_enqueues_1_outbox_event()
    {
        var order = BuildShippedOrder();
        _orders.GetByIdForCustomerAsync(OrderId, CustomerUserId, Arg.Any<CancellationToken>())
            .Returns(order);

        var result = await _sut.Handle(
            new MarkOrderDelivered.Command(OrderId, OrderDeliverySource.Customer),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.OrderId.Should().Be(OrderId);
        result.Value.State.Should().Be(OrderState.Delivered);
        order.State.Should().Be(OrderState.Delivered);
        order.DeliveredAt.Should().Be(Now);
        order.DeliverySource.Should().Be(OrderDeliverySource.Customer);

        _outbox.Received(1).Enqueue(
            OrderId,
            OutboxEventTypes.OrderDeliveredCustomerEmail,
            Arg.Any<string>());
    }

    [Fact]
    public async Task Happy_path_Auto_source_uses_unscoped_lookup_and_stamps_Auto()
    {
        // Function context — no user session.
        _session.GetUserId().Returns((string?)null);
        var order = BuildShippedOrder();
        _orders.GetByIdUnscopedAsync(OrderId, Arg.Any<CancellationToken>()).Returns(order);

        var result = await _sut.Handle(
            new MarkOrderDelivered.Command(OrderId, OrderDeliverySource.Auto),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        order.State.Should().Be(OrderState.Delivered);
        order.DeliverySource.Should().Be(OrderDeliverySource.Auto);
        order.DeliveredAt.Should().Be(Now);

        // Customer-scoped lookup MUST NOT be called when Source = Auto.
        await _orders.DidNotReceive().GetByIdForCustomerAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        _outbox.Received(1).Enqueue(
            OrderId, OutboxEventTypes.OrderDeliveredCustomerEmail, Arg.Any<string>());
    }

    [Fact]
    public async Task Happy_path_Carrier_source_with_deliveredAtOverride_stamps_override_timestamp()
    {
        var carrierTimestamp = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        _session.GetUserId().Returns((string?)null);
        var order = BuildShippedOrder();
        _orders.GetByIdUnscopedAsync(OrderId, Arg.Any<CancellationToken>()).Returns(order);

        var result = await _sut.Handle(
            new MarkOrderDelivered.Command(OrderId, OrderDeliverySource.Carrier, carrierTimestamp),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        order.DeliveredAt.Should().Be(carrierTimestamp,
            "carrier-provided authoritative timestamp wins over clock");
        order.DeliverySource.Should().Be(OrderDeliverySource.Carrier);
        _outbox.Received(1).Enqueue(
            OrderId, OutboxEventTypes.OrderDeliveredCustomerEmail, Arg.Any<string>());
    }

    [Fact]
    public async Task Already_Delivered_silent_success_no_outbox_emission()
    {
        // T-0076 A.3: customer + T-0078 carrier-sync race — second writer
        // sees Delivered state and returns Success without re-emitting the
        // email outbox event and without overwriting the first writer's
        // DeliverySource.
        _session.GetUserId().Returns((string?)null);
        var order = BuildDeliveredOrder();  // DeliverySource = Customer
        _orders.GetByIdUnscopedAsync(OrderId, Arg.Any<CancellationToken>()).Returns(order);

        var result = await _sut.Handle(
            new MarkOrderDelivered.Command(OrderId, OrderDeliverySource.Carrier),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.OrderId.Should().Be(OrderId);
        result.Value.State.Should().Be(OrderState.Delivered);
        // First writer's DeliverySource is preserved.
        order.DeliverySource.Should().Be(OrderDeliverySource.Customer);
        // CRITICAL: no second outbox row, no duplicate email.
        _outbox.DidNotReceive().Enqueue(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Customer_source_with_ownership_mismatch_returns_NotFound()
    {
        // Order owned by a different customer — GetByIdForCustomerAsync
        // returns null. The IDOR shield surfaces as 404 (same shape so
        // order ids aren't enumerable across customers).
        _orders.GetByIdForCustomerAsync(OrderId, CustomerUserId, Arg.Any<CancellationToken>())
            .Returns((Order?)null);

        var result = await _sut.Handle(
            new MarkOrderDelivered.Command(OrderId, OrderDeliverySource.Customer),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderNotFound);
        _outbox.DidNotReceive().Enqueue(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Customer_source_without_session_returns_Unauthorized()
    {
        _session.GetUserId().Returns((string?)null);

        var result = await _sut.Handle(
            new MarkOrderDelivered.Command(OrderId, OrderDeliverySource.Customer),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);
        await _orders.DidNotReceive().GetByIdForCustomerAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        _outbox.DidNotReceive().Enqueue(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Non_Shipped_non_Delivered_state_returns_OrderInvalidTransition()
    {
        // Order in Accepted state (not yet shipped). Source = Customer.
        // Genuine "wrong state" → 409 (distinguished from the A.3 silent
        // Delivered branch above).
        var order = Order.Create(
            id: OrderId, orderNumber: "M-CZ-20260042",
            customerUserId: CustomerUserId, makerId: MakerId, productId: "prod-1",
            contactName: "Anna", contactEmail: "anna@example.cz", contactPhone: "+420",
            productPriceAmountMinor: 50000, shippingPriceAmountMinor: 7900,
            platformFeeAmountMinor: 7500, makerPayoutAmountMinor: 50400,
            totalAmountMinor: 57900, currency: "CZK", vatRateBp: 2100,
            shippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
            zasilkovnaPickupPointId: "pp-42", countryCode: "CZ");
        var clk = Substitute.For<IClock>();
        clk.UtcNow.Returns(Now);
        order.MarkAsPaid(clk, "tx-1");
        order.Accept(clk);
        _orders.GetByIdForCustomerAsync(OrderId, CustomerUserId, Arg.Any<CancellationToken>())
            .Returns(order);

        var result = await _sut.Handle(
            new MarkOrderDelivered.Command(OrderId, OrderDeliverySource.Customer),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderInvalidTransition);
        order.State.Should().Be(OrderState.Accepted);
        order.DeliverySource.Should().BeNull();
        _outbox.DidNotReceive().Enqueue(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Outbox_event_payload_carries_expected_fields()
    {
        // AC-2: payload deserializes to OrderDeliveredCustomerEmailPayload
        // with all 6 fields correctly populated.
        var order = BuildShippedOrder();
        _orders.GetByIdForCustomerAsync(OrderId, CustomerUserId, Arg.Any<CancellationToken>())
            .Returns(order);
        string? capturedJson = null;
        _outbox.Enqueue(
            Arg.Any<string>(),
            OutboxEventTypes.OrderDeliveredCustomerEmail,
            Arg.Do<string>(j => capturedJson = j));

        await _sut.Handle(
            new MarkOrderDelivered.Command(OrderId, OrderDeliverySource.Customer),
            CancellationToken.None);

        capturedJson.Should().NotBeNull();
        var payload = JsonSerializer.Deserialize<OrderDeliveredCustomerEmailPayload>(capturedJson!);
        payload!.OrderId.Should().Be(OrderId);
        payload.OrderNumber.Should().Be(order.OrderNumber);
        payload.Email.Should().Be(order.ContactEmail);
        payload.ContactName.Should().Be(order.ContactName);
        payload.LanguageCode.Should().Be("cs-CZ");
        payload.ActionUrl.Should().Be($"{WebBaseUrl}/objednavka/{OrderId}");
    }

    [Fact]
    public async Task Missing_customer_user_returns_OrderCustomerUserMissing_without_enqueue()
    {
        var order = BuildShippedOrder();
        _orders.GetByIdForCustomerAsync(OrderId, CustomerUserId, Arg.Any<CancellationToken>())
            .Returns(order);
        _users.GetByIdAsync(CustomerUserId, Arg.Any<CancellationToken>())
            .Returns((User?)null);

        var result = await _sut.Handle(
            new MarkOrderDelivered.Command(OrderId, OrderDeliverySource.Customer),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderCustomerUserMissing);
        _outbox.DidNotReceive().Enqueue(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }
}
