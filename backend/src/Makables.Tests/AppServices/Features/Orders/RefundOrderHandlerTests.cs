using System.Text.Json;
using FluentAssertions;
using Makables.Core.AppServices.Common;
using Makables.Core.AppServices.Features.Orders;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Outbox;
using Makables.Core.Domain.Payments;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Orders;

/// <summary>
/// T-0105 <see cref="RefundOrder.Handler"/> contract: fail-closed
/// session check, provider-first order of operations (locked decision
/// A.5 — pre-flight failures NEVER reach the provider; provider
/// failures leave the order untouched with no outbox), Silent Success
/// on an already-Refunded order, and the enrichment-at-enqueue email
/// payload shape.
/// </summary>
public class RefundOrderHandlerTests
{
    private const string OrderId = "ord-1";
    private const string CustomerUserId = "user-customer-1";
    private const string AdminUserId = "user-admin-1";
    private const string ProviderRef = "AB1C-D34E";
    private const string WebBaseUrl = "https://makables.test";
    private const long Total = 57900;

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-12T10:00:00Z");

    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IPaymentProviderFactory _providerFactory = Substitute.For<IPaymentProviderFactory>();
    private readonly IPaymentProvider _provider = Substitute.For<IPaymentProvider>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IOutbox _outbox = Substitute.For<IOutbox>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly ILanguageResolver _languageResolver = Substitute.For<ILanguageResolver>();
    private readonly IUserSessionProvider _session = Substitute.For<IUserSessionProvider>();
    private readonly RefundOrder.Handler _sut;

    public RefundOrderHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _session.GetUserId().Returns(AdminUserId);
        _providerFactory.ResolveAsync("CZ", Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(_provider));
        _languageResolver.ResolveForUserAsync(Arg.Any<User>(), Arg.Any<CancellationToken>())
            .Returns("cs-CZ");

        var customer = User.Create(
            id: CustomerUserId, email: "anna@example.cz", role: UserRole.Customer,
            fullName: "Anna", countryCodePrimary: "CZ",
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        _users.GetByIdAsync(CustomerUserId, Arg.Any<CancellationToken>()).Returns(customer);

        var urls = Options.Create(new PublicAppUrlsOptions { WebBaseUrl = WebBaseUrl });

        _sut = new RefundOrder.Handler(
            _orders, _providerFactory, _users, _outbox, _clock,
            _languageResolver, urls, _session,
            NullLogger<RefundOrder.Handler>.Instance);
    }

    private static Order BuildPaidOrder()
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
        o.MarkAsPaid(clock, ProviderRef);
        return o;
    }

    private void ScriptProviderSuccess(long amountMinor) =>
        _provider.RefundAsync(ProviderRef, amountMinor, "CZK", Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(new RefundReceipt(ProviderRef, amountMinor, "CZK", Now)));

    [Fact]
    public async Task Happy_path_full_refund_calls_provider_once_and_enqueues_email()
    {
        var order = BuildPaidOrder();
        _orders.GetByIdUnscopedAsync(OrderId, Arg.Any<CancellationToken>()).Returns(order);
        ScriptProviderSuccess(Total);
        string? capturedJson = null;
        _outbox.Enqueue(
            Arg.Any<string>(),
            OutboxEventTypes.OrderRefundedCustomerEmail,
            Arg.Do<string>(j => capturedJson = j));

        var result = await _sut.Handle(
            new RefundOrder.Command(OrderId, Total, "Zboží nikdy nedorazilo.", false),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.State.Should().Be(OrderState.Refunded);
        result.Value.IsFullRefund.Should().BeTrue();
        result.Value.RemainingRefundableMinor.Should().Be(0);
        order.State.Should().Be(OrderState.Refunded);
        order.RefundedAt.Should().Be(Now);

        await _provider.Received(1).RefundAsync(
            ProviderRef, Total, "CZK", Arg.Any<CancellationToken>());

        capturedJson.Should().NotBeNull();
        var payload = JsonSerializer.Deserialize<OrderRefundedCustomerEmailPayload>(capturedJson!);
        payload!.OrderId.Should().Be(OrderId);
        payload.RefundedAmountMinor.Should().Be(Total);
        payload.Currency.Should().Be("CZK");
        payload.IsFullRefund.Should().BeTrue();
        payload.LanguageCode.Should().Be("cs-CZ");
        payload.ActionUrl.Should().Be($"{WebBaseUrl}/objednavka/{OrderId}",
            "the action URL is pre-baked at enqueue time");
    }

    [Fact]
    public async Task Partial_refund_accumulates_without_state_change()
    {
        var order = BuildPaidOrder();
        _orders.GetByIdUnscopedAsync(OrderId, Arg.Any<CancellationToken>()).Returns(order);
        ScriptProviderSuccess(10000);
        string? capturedJson = null;
        _outbox.Enqueue(
            Arg.Any<string>(),
            OutboxEventTypes.OrderRefundedCustomerEmail,
            Arg.Do<string>(j => capturedJson = j));

        var result = await _sut.Handle(
            new RefundOrder.Command(OrderId, 10000, "Kompenzace poškozeného obalu.", false),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.State.Should().Be(OrderState.Paid);
        result.Value.IsFullRefund.Should().BeFalse();
        result.Value.RefundedAmountMinor.Should().Be(10000);
        result.Value.RemainingRefundableMinor.Should().Be(Total - 10000);
        order.RefundedAt.Should().BeNull();

        var payload = JsonSerializer.Deserialize<OrderRefundedCustomerEmailPayload>(capturedJson!);
        payload!.IsFullRefund.Should().BeFalse();
        payload.RefundedAmountMinor.Should().Be(10000, "the email carries THIS refund's amount");
    }

    [Fact]
    public async Task Provider_permanent_error_surfaces_with_order_unchanged_and_no_outbox()
    {
        var order = BuildPaidOrder();
        _orders.GetByIdUnscopedAsync(OrderId, Arg.Any<CancellationToken>()).Returns(order);
        _provider.RefundAsync(ProviderRef, Total, "CZK", Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Failure<RefundReceipt>(
                Error.Permanent(BusinessErrorMessage.PaymentProviderRejected)));

        var result = await _sut.Handle(
            new RefundOrder.Command(OrderId, Total, "Pokus o refundaci.", false),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.PaymentProviderRejected);
        result.Error.Type.Should().Be(ErrorType.Permanent);
        // US-admin-0008 AC-3: order byte-identical, no outbox row.
        order.State.Should().Be(OrderState.Paid);
        order.RefundedAmountMinor.Should().Be(0);
        order.RefundedAt.Should().BeNull();
        _outbox.DidNotReceive().Enqueue(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Already_refunded_returns_silent_success_without_provider_call_or_outbox()
    {
        var order = BuildPaidOrder();
        var priorClock = Substitute.For<IClock>();
        priorClock.UtcNow.Returns(Now.AddHours(-1));
        order.Refund(priorClock, Total, acknowledgePostPayout: false);
        order.State.Should().Be(OrderState.Refunded);
        _orders.GetByIdUnscopedAsync(OrderId, Arg.Any<CancellationToken>()).Returns(order);

        var result = await _sut.Handle(
            new RefundOrder.Command(OrderId, Total, "Opakovaný pokus.", false),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.State.Should().Be(OrderState.Refunded);
        result.Value.IsFullRefund.Should().BeTrue();
        await _provider.DidNotReceiveWithAnyArgs().RefundAsync(default!, default, default!, default);
        _outbox.DidNotReceive().Enqueue(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Missing_provider_ref_blocks_before_provider_call()
    {
        // PendingPayment order — never paid, no provider ref. The state
        // gate would also block it, but the noProviderRef check fires
        // first (there is nothing to point the Comgate call at).
        var order = Order.Create(
            id: OrderId, orderNumber: "M-CZ-20260043",
            customerUserId: CustomerUserId, makerId: "maker-1", productId: "prod-1",
            contactName: "Anna", contactEmail: "anna@example.cz", contactPhone: "+420",
            productPriceAmountMinor: 50000, shippingPriceAmountMinor: 7900,
            platformFeeAmountMinor: 7500, makerPayoutAmountMinor: 50400,
            totalAmountMinor: Total, currency: "CZK", vatRateBp: 2100,
            shippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
            zasilkovnaPickupPointId: "pp-42", countryCode: "CZ");
        _orders.GetByIdUnscopedAsync(OrderId, Arg.Any<CancellationToken>()).Returns(order);

        var result = await _sut.Handle(
            new RefundOrder.Command(OrderId, 1000, "Refundace bez platby.", false),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.PaymentRefundNoProviderRef);
        await _provider.DidNotReceiveWithAnyArgs().RefundAsync(default!, default, default!, default);
        _outbox.DidNotReceive().Enqueue(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Completed_without_acknowledgement_blocks_before_provider_call()
    {
        var order = BuildPaidOrder();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now.AddDays(-1));
        order.Accept(clock);
        order.Ship(clock, "PKT-1", 7);
        order.MarkAsDelivered(clock, OrderDeliverySource.Auto);
        order.Complete(clock);
        _orders.GetByIdUnscopedAsync(OrderId, Arg.Any<CancellationToken>()).Returns(order);

        var result = await _sut.Handle(
            new RefundOrder.Command(OrderId, Total, "Refundace po výplatě.", AcknowledgePostPayout: false),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.PaymentRefundPostPayoutAckRequired);
        await _provider.DidNotReceiveWithAnyArgs().RefundAsync(default!, default, default!, default);
    }

    [Fact]
    public async Task Missing_session_fails_closed_before_any_lookup()
    {
        _session.GetUserId().Returns((string?)null);

        var result = await _sut.Handle(
            new RefundOrder.Command(OrderId, 1000, "Bez přihlášení.", false),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);
        await _orders.DidNotReceiveWithAnyArgs().GetByIdUnscopedAsync(default!, default);
    }

    [Fact]
    public void Notes_fold_the_post_payout_acknowledgement_marker()
    {
        // US-admin-0008 AC-2: the acknowledgement is recorded in the
        // audit entry via Command.Notes — the pipeline persists it verbatim.
        var plain = new RefundOrder.Command(OrderId, 100, "Důvod.", AcknowledgePostPayout: false);
        plain.Notes.Should().Be("Důvod.");

        var acknowledged = new RefundOrder.Command(OrderId, 100, "Důvod.", AcknowledgePostPayout: true);
        acknowledged.Notes.Should().Be($"Důvod. {RefundOrder.PostPayoutAcknowledgedMarker}");
    }
}
