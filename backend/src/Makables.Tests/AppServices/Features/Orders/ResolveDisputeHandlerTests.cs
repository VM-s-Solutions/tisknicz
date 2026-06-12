using System.Text.Json;
using FluentAssertions;
using Makables.Core.AppServices.Common;
using Makables.Core.AppServices.Features.Orders;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Outbox;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Orders;

/// <summary>
/// T-0106 <see cref="ResolveDispute.Handler"/> outcome matrix (§C.3):
/// Resumed = restore only; Refunded = nested <c>RefundOrder.Command</c>
/// for the FULL remaining amount (failure leaves the dispute OPEN);
/// Cancelled = <c>Cancel(Admin)</c> from a Paid/Accepted restore but
/// <c>order.invalidTransition</c> from Shipped/Delivered; non-Disputed →
/// loud <c>order.dispute.notOpen</c>; the customer email rides every
/// success path.
/// </summary>
public class ResolveDisputeHandlerTests
{
    private const string OrderId = "ord-1";
    private const string CustomerUserId = "user-cust-1";
    private const string AdminUserId = "user-admin-1";
    private const long Total = 57900;

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-12T10:00:00Z");

    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IDisputeRepository _disputes = Substitute.For<IDisputeRepository>();
    private readonly ISender _mediator = Substitute.For<ISender>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IOutbox _outbox = Substitute.For<IOutbox>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly IUserSessionProvider _session = Substitute.For<IUserSessionProvider>();
    private readonly ILanguageResolver _languageResolver = Substitute.For<ILanguageResolver>();
    private readonly ResolveDispute.Handler _sut;

    public ResolveDisputeHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _session.GetUserId().Returns(AdminUserId);
        _languageResolver.ResolveForUserAsync(Arg.Any<User>(), Arg.Any<CancellationToken>())
            .Returns("cs-CZ");

        var customer = User.Create(
            id: CustomerUserId, email: "anna@example.cz", role: UserRole.Customer,
            fullName: "Anna", countryCodePrimary: "CZ",
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        _users.GetByIdAsync(CustomerUserId, Arg.Any<CancellationToken>()).Returns(customer);

        var urls = Options.Create(new PublicAppUrlsOptions { WebBaseUrl = "https://makables.test" });
        _sut = new ResolveDispute.Handler(
            _orders, _disputes, _mediator, _users, _outbox, _clock, _session,
            _languageResolver, urls,
            NullLogger<ResolveDispute.Handler>.Instance);
    }

    private static Order BuildDisputedOrder(OrderState origin)
    {
        var o = Order.Create(
            id: OrderId, orderNumber: "M-CZ-20260042",
            customerUserId: CustomerUserId, makerId: "maker-1", productId: "prod-1",
            contactName: "Anna", contactEmail: "anna@example.cz", contactPhone: "+420",
            productPriceAmountMinor: 50000, shippingPriceAmountMinor: 7900,
            platformFeeAmountMinor: 7500, makerPayoutAmountMinor: 50400,
            totalAmountMinor: Total, currency: "CZK", vatRateBp: 2100,
            shippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
            zasilkovnaPickupPointId: "pp-42", countryCode: "CZ");
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now.AddDays(-3));
        o.MarkAsPaid(clock, "tx-1");
        if (origin is OrderState.Accepted or OrderState.Shipped or OrderState.Delivered)
            o.Accept(clock);
        if (origin is OrderState.Shipped or OrderState.Delivered)
            o.Ship(clock, "PKT-1", 7);
        if (origin is OrderState.Delivered)
            o.MarkAsDelivered(clock, OrderDeliverySource.Auto);
        o.OpenDispute(clock);
        return o;
    }

    private static Dispute BuildOpenDispute() => Dispute.Open(
        "dsp-1", OrderId, DisputeCategory.DamagedItem, "Rozbitá váza.",
        DisputeSource.Customer, "CZ");

    private void Wire(Order order, Dispute dispute)
    {
        _orders.GetByIdUnscopedAsync(OrderId, Arg.Any<CancellationToken>()).Returns(order);
        _disputes.GetOpenByOrderIdAsync(OrderId, Arg.Any<CancellationToken>()).Returns(dispute);
    }

    [Fact]
    public async Task Resumed_restores_state_resolves_row_and_enqueues_customer_email()
    {
        var order = BuildDisputedOrder(OrderState.Shipped);
        var dispute = BuildOpenDispute();
        Wire(order, dispute);
        string? capturedJson = null;
        _outbox.Enqueue(
            Arg.Any<string>(),
            OutboxEventTypes.OrderDisputeResolvedCustomerEmail,
            Arg.Do<string>(j => capturedJson = j));

        var result = await _sut.Handle(
            new ResolveDispute.Command(OrderId, DisputeResolutionOutcome.Resumed, "Vyřešeno dohodou."),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.State.Should().Be(OrderState.Shipped);
        order.State.Should().Be(OrderState.Shipped);
        order.PreDisputeState.Should().BeNull();
        order.DisputedAt.Should().NotBeNull("DisputedAt is a kept historical marker");

        dispute.ResolutionOutcome.Should().Be(DisputeResolutionOutcome.Resumed);
        dispute.ResolvedAt.Should().Be(Now);

        var payload = JsonSerializer.Deserialize<OrderDisputeResolvedCustomerEmailPayload>(capturedJson!);
        payload!.Outcome.Should().Be(DisputeResolutionOutcome.Resumed);
        payload.ResolutionNotes.Should().Be("Vyřešeno dohodou.");
        payload.ActionUrl.Should().Be($"https://makables.test/objednavka/{OrderId}");

        await _mediator.DidNotReceive().Send(
            Arg.Any<RefundOrder.Command>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refunded_dispatches_RefundOrder_with_the_full_remaining_amount()
    {
        var order = BuildDisputedOrder(OrderState.Delivered);
        Wire(order, BuildOpenDispute());
        RefundOrder.Command? dispatched = null;
        _mediator.Send(Arg.Do<RefundOrder.Command>(c => dispatched = c), Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(new RefundOrder.RefundOrderResponse(
                OrderId, OrderState.Refunded, Total, 0, true)));

        var result = await _sut.Handle(
            new ResolveDispute.Command(OrderId, DisputeResolutionOutcome.Refunded, "Vracíme peníze."),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        dispatched.Should().NotBeNull();
        dispatched!.AmountMinor.Should().Be(Total,
            "the dispute lane refunds the FULL remaining amount — never partials (Q1)");
        dispatched.AcknowledgePostPayout.Should().BeFalse(
            "Completed is not disputable, the paid-out warning path is unreachable");
        _outbox.Received(1).Enqueue(
            OrderId, OutboxEventTypes.OrderDisputeResolvedCustomerEmail, Arg.Any<string>());
    }

    [Fact]
    public async Task Refund_failure_propagates_and_the_dispute_stays_open_shaped()
    {
        var order = BuildDisputedOrder(OrderState.Delivered);
        var dispute = BuildOpenDispute();
        Wire(order, dispute);
        _mediator.Send(Arg.Any<RefundOrder.Command>(), Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Failure<RefundOrder.RefundOrderResponse>(
                Error.Permanent(BusinessErrorMessage.PaymentProviderRejected)));

        var result = await _sut.Handle(
            new ResolveDispute.Command(OrderId, DisputeResolutionOutcome.Refunded, "Pokus o refundaci."),
            CancellationToken.None);

        // The failure response means the UoW pipeline never commits — the
        // in-memory mutations roll back with the transaction (Risk §4);
        // the admin retries or picks another outcome.
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.PaymentProviderRejected);
    }

    [Theory]
    [InlineData(OrderState.Paid)]
    [InlineData(OrderState.Accepted)]
    public async Task Cancelled_outcome_cancels_with_Admin_source_from_pre_shipment_restores(OrderState origin)
    {
        var order = BuildDisputedOrder(origin);
        Wire(order, BuildOpenDispute());

        var result = await _sut.Handle(
            new ResolveDispute.Command(OrderId, DisputeResolutionOutcome.Cancelled, "Objednávka se ruší."),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        order.State.Should().Be(OrderState.Cancelled);
        order.CancellationSource.Should().Be(OrderCancellationSource.Admin);
        _outbox.Received(1).Enqueue(
            OrderId, OutboxEventTypes.OrderDisputeResolvedCustomerEmail, Arg.Any<string>());
    }

    [Theory]
    [InlineData(OrderState.Shipped)]
    [InlineData(OrderState.Delivered)]
    public async Task Cancelled_outcome_from_post_shipment_restore_surfaces_invalidTransition(OrderState origin)
    {
        var order = BuildDisputedOrder(origin);
        Wire(order, BuildOpenDispute());

        var result = await _sut.Handle(
            new ResolveDispute.Command(OrderId, DisputeResolutionOutcome.Cancelled, "Špatná volba."),
            CancellationToken.None);

        // AC-10: the entity's Cancel allow-list refuses; the failure
        // response rolls the whole resolution back — the dispute stays
        // OPEN and the error steers the admin to the Refunded outcome.
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderInvalidTransition);
    }

    [Fact]
    public async Task Non_disputed_order_returns_loud_notOpen_conflict()
    {
        var order = BuildDisputedOrder(OrderState.Shipped);
        order.ResolveDispute(_clock, OrderState.Shipped); // already resolved
        _orders.GetByIdUnscopedAsync(OrderId, Arg.Any<CancellationToken>()).Returns(order);

        var result = await _sut.Handle(
            new ResolveDispute.Command(OrderId, DisputeResolutionOutcome.Resumed, "Druhý pokus."),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderDisputeNotOpen);
        _outbox.DidNotReceive().Enqueue(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Missing_session_fails_closed()
    {
        _session.GetUserId().Returns((string?)null);

        var result = await _sut.Handle(
            new ResolveDispute.Command(OrderId, DisputeResolutionOutcome.Resumed, "Bez přihlášení."),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);
        await _orders.DidNotReceiveWithAnyArgs().GetByIdUnscopedAsync(default!, default);
    }
}
