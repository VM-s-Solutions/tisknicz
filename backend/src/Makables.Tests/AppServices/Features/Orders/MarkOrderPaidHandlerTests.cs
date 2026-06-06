using System.Text.Json;
using FluentAssertions;
using Makables.Core.AppServices.Common;
using Makables.Core.AppServices.Features.Orders;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using MakerEntity = Makables.Core.Domain.Makers.Maker;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Outbox;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Orders;

/// <summary>
/// Pins the T-0067 <see cref="MarkOrderPaid.Handler"/> flow:
/// resolve by providerRef, defence-in-depth ref check, state transition
/// via the extended 4-arg <see cref="Order.MarkAsPaid"/>, enqueue
/// customer + maker email outbox events (and NO invoice.generate — that's
/// T-0068). NSubstitute over the repositories + clock + outbox; the
/// aggregate's own coverage is in the domain tests.
/// </summary>
public class MarkOrderPaidHandlerTests
{
    private const string OrderId = "ord-1";
    private const string ProviderRef = "comgate-tx-1";
    private const string OtherProviderRef = "comgate-tx-2";
    private const string PaymentMethod = "CARD_CZ";
    private const string CustomerUserId = "user-1";
    private const string MakerId = "maker-1";
    private const string MakerUserId = "maker-user-1";
    private const string MakerEmail = "maker@example.cz";
    private const string WebBaseUrl = "https://makables.test";

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-06T10:00:00Z");
    private static readonly DateTimeOffset PaidAt = DateTimeOffset.Parse("2026-06-06T09:59:30Z");

    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IMakerRepository _makers = Substitute.For<IMakerRepository>();
    private readonly IOutbox _outbox = Substitute.For<IOutbox>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly ILanguageResolver _languageResolver = Substitute.For<ILanguageResolver>();
    private readonly MarkOrderPaid.Handler _sut;

    public MarkOrderPaidHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _languageResolver
            .ResolveForUserAsync(Arg.Any<User>(), Arg.Any<CancellationToken>())
            .Returns("cs-CZ");

        // Wire a default customer + maker so the happy path resolves; tests
        // that exercise other branches don't need to re-wire.
        var customer = User.Create(
            id: CustomerUserId,
            email: "anna@example.cz",
            role: UserRole.Customer,
            fullName: "Anna",
            countryCodePrimary: "CZ",
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        _users.GetByIdAsync(CustomerUserId, Arg.Any<CancellationToken>()).Returns(customer);

        var makerUser = User.Create(
            id: MakerUserId,
            email: MakerEmail,
            role: UserRole.Maker,
            fullName: "Maker",
            countryCodePrimary: "CZ",
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        _users.GetByIdAsync(MakerUserId, Arg.Any<CancellationToken>()).Returns(makerUser);

        var maker = MakerEntity.Create(
            id: MakerId, userId: MakerUserId,
            registrationNumber: "27074358", vatId: null,
            companyName: "Avast s.r.o.", legalForm: null,
            registeredAddressId: "addr-1",
            incorporatedOn: null, isActiveInRegistry: true,
            sourceRegistry: "ares",
            snapshotFetchedAt: Now, snapshotIsStale: false,
            countryCode: "CZ", slug: "avast");
        _makers.GetByIdAsync(MakerId, Arg.Any<CancellationToken>()).Returns(maker);

        var urls = Options.Create(new PublicAppUrlsOptions
        {
            WebBaseUrl = WebBaseUrl,
            MagicLinkPath = "/auth/magic?token={token}",
            EmailConfirmationPath = "/auth/confirm?token={token}",
            PasswordResetPath = "/auth/reset?token={token}",
        });

        _sut = new MarkOrderPaid.Handler(
            _orders, _users, _makers, _outbox, _clock,
            _languageResolver, urls,
            NullLogger<MarkOrderPaid.Handler>.Instance);
    }

    private static Order BuildOrderInState(
        OrderState target,
        string id = OrderId,
        string? presetRef = null)
    {
        var o = Order.Create(
            id: id,
            orderNumber: "M-CZ-20260042",
            customerUserId: CustomerUserId,
            makerId: MakerId,
            productId: "prod-1",
            contactName: "Anna",
            contactEmail: "a@b.cz",
            contactPhone: "+420",
            productPriceAmountMinor: 50000,
            shippingPriceAmountMinor: 7900,
            platformFeeAmountMinor: 7500,
            makerPayoutAmountMinor: 50400,
            totalAmountMinor: 57900,
            currency: "CZK",
            vatRateBp: 2100,
            shippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
            zasilkovnaPickupPointId: "pp-42",
            countryCode: "CZ");

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);

        if (target == OrderState.PendingPayment)
        {
            // Optionally seed a ReservePaymentSession so the order has
            // a (different) PaymentProviderRef already set — used by
            // the "different ref" defence-in-depth test below.
            if (presetRef is not null)
            {
                o.ReservePaymentSession(presetRef, "https://x", clock);
            }
            return o;
        }

        o.MarkAsPaid(clock, presetRef ?? "preset-tx");
        if (target == OrderState.Paid) return o;
        o.Accept(clock);
        if (target == OrderState.Accepted) return o;
        o.Cancel(clock); // unreachable here; just suppresses the warning
        if (target == OrderState.Cancelled) return o;
        throw new ArgumentOutOfRangeException(nameof(target), target, "Unsupported.");
    }

    private static MarkOrderPaid.Command ValidCommand(
        string orderId = OrderId,
        string? paymentMethod = PaymentMethod,
        DateTimeOffset? paidAt = null) =>
        new(OrderId: orderId, ProviderRef: ProviderRef, PaymentMethod: paymentMethod, PaidAt: paidAt ?? PaidAt);

    [Fact]
    public async Task Happy_path_transitions_order_to_Paid_and_returns_OrderId()
    {
        var order = BuildOrderInState(OrderState.PendingPayment);
        _orders.GetByPaymentProviderRefAsync(ProviderRef, Arg.Any<CancellationToken>())
            .Returns(order);

        var result = await _sut.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.OrderId.Should().Be(OrderId);
        order.State.Should().Be(OrderState.Paid);
        order.PaymentProviderRef.Should().Be(ProviderRef);
    }

    [Fact]
    public async Task Order_not_found_returns_OrderNotFound()
    {
        _orders.GetByPaymentProviderRefAsync(ProviderRef, Arg.Any<CancellationToken>())
            .Returns((Order?)null);

        var result = await _sut.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderNotFound);
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Resolved_order_Id_does_not_match_Command_OrderId_returns_RefIdMismatch()
    {
        var order = BuildOrderInState(OrderState.PendingPayment);
        _orders.GetByPaymentProviderRefAsync(ProviderRef, Arg.Any<CancellationToken>())
            .Returns(order);

        var command = new MarkOrderPaid.Command(
            OrderId: "ord-different",
            ProviderRef: ProviderRef,
            PaymentMethod: PaymentMethod,
            PaidAt: PaidAt);

        var result = await _sut.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.PaymentWebhookRefIdMismatch);
        result.Error.Type.Should().Be(ErrorType.Conflict);
        order.State.Should().Be(OrderState.PendingPayment, "no mutation when ref check fails");
    }

    [Fact]
    public async Task Order_already_Paid_surfaces_OrderInvalidTransition()
    {
        var order = BuildOrderInState(OrderState.Paid);
        _orders.GetByPaymentProviderRefAsync(ProviderRef, Arg.Any<CancellationToken>())
            .Returns(order);

        var result = await _sut.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderInvalidTransition);
    }

    [Fact]
    public async Task Order_already_Cancelled_surfaces_OrderInvalidTransition()
    {
        // PendingPayment → Cancelled to make this state legal.
        var o = Order.Create(
            id: OrderId, orderNumber: "M-CZ-20260042",
            customerUserId: CustomerUserId, makerId: MakerId, productId: "prod-1",
            contactName: "Anna", contactEmail: "a@b.cz", contactPhone: "+420",
            productPriceAmountMinor: 50000, shippingPriceAmountMinor: 7900,
            platformFeeAmountMinor: 7500, makerPayoutAmountMinor: 50400,
            totalAmountMinor: 57900, currency: "CZK", vatRateBp: 2100,
            shippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
            zasilkovnaPickupPointId: "pp-42", countryCode: "CZ");
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        o.Cancel(clock);

        _orders.GetByPaymentProviderRefAsync(ProviderRef, Arg.Any<CancellationToken>())
            .Returns(o);

        var result = await _sut.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderInvalidTransition);
    }

    [Fact]
    public async Task Existing_PaymentProviderRef_set_to_different_ref_surfaces_OrderInvalidTransition()
    {
        // The set-once invariant on PaymentProviderRef belongs to
        // Order.MarkAsPaid (T-0060 R2-1). We exercise it via a
        // PendingPayment order with a pre-existing ref.
        var order = BuildOrderInState(OrderState.PendingPayment, presetRef: OtherProviderRef);
        _orders.GetByPaymentProviderRefAsync(ProviderRef, Arg.Any<CancellationToken>())
            .Returns(order);

        var result = await _sut.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderInvalidTransition);
        order.PaymentProviderRef.Should().Be(OtherProviderRef,
            "the set-once invariant must preserve the original ref");
    }

    // === T-0067 new tests — Command fields now persisted. ===

    [Fact]
    public async Task Command_fields_PaymentMethod_PaidAt_now_persisted_via_extended_MarkAsPaid_signature()
    {
        // Was the T-0066 "accepted-and-ignored" pinning test; T-0067
        // flips it to "persisted via the 4-arg MarkAsPaid signature".
        var order = BuildOrderInState(OrderState.PendingPayment);
        _orders.GetByPaymentProviderRefAsync(ProviderRef, Arg.Any<CancellationToken>())
            .Returns(order);

        var commandWithMethod = new MarkOrderPaid.Command(
            OrderId: OrderId,
            ProviderRef: ProviderRef,
            PaymentMethod: "BANK_CZ_RB",
            PaidAt: PaidAt);

        var result = await _sut.Handle(commandWithMethod, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        order.PaymentMethod.Should().Be("BANK_CZ_RB",
            "T-0067 persists Command.PaymentMethod onto the entity");
        order.PaidAt.Should().Be(PaidAt,
            "T-0067 prefers Command.PaidAt (Comgate-authoritative) over clock.UtcNow");
    }

    [Fact]
    public async Task Null_Command_PaidAt_falls_back_to_clock_UtcNow_T_0066_semantic()
    {
        // Q1 plumbing: when the provider's PAID timestamp is missing, the
        // handler still must persist a paid_at. clock.UtcNow is the
        // documented fallback (preserves the T-0066 semantic for
        // providers that don't expose a capture timestamp).
        var order = BuildOrderInState(OrderState.PendingPayment);
        _orders.GetByPaymentProviderRefAsync(ProviderRef, Arg.Any<CancellationToken>())
            .Returns(order);

        var commandNoPaidAt = new MarkOrderPaid.Command(
            OrderId: OrderId,
            ProviderRef: ProviderRef,
            PaymentMethod: PaymentMethod,
            PaidAt: null);

        var result = await _sut.Handle(commandNoPaidAt, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        order.PaidAt.Should().Be(Now,
            "null PaidAt falls back to clock.UtcNow per Order.MarkAsPaid semantics");
    }

    [Fact]
    public async Task Handler_enqueues_OrderPaidCustomerEmail_outbox_row()
    {
        var order = BuildOrderInState(OrderState.PendingPayment);
        _orders.GetByPaymentProviderRefAsync(ProviderRef, Arg.Any<CancellationToken>())
            .Returns(order);

        await _sut.Handle(ValidCommand(), CancellationToken.None);

        _outbox.Received(1).Enqueue(
            OrderId,
            OutboxEventTypes.OrderPaidCustomerEmail,
            Arg.Is<string>(json => DeserializeCustomerPayload(json)!.OrderId == OrderId));
    }

    [Fact]
    public async Task Handler_enqueues_OrderPlacedMakerEmail_outbox_row()
    {
        var order = BuildOrderInState(OrderState.PendingPayment);
        _orders.GetByPaymentProviderRefAsync(ProviderRef, Arg.Any<CancellationToken>())
            .Returns(order);

        await _sut.Handle(ValidCommand(), CancellationToken.None);

        _outbox.Received(1).Enqueue(
            OrderId,
            OutboxEventTypes.OrderPlacedMakerEmail,
            Arg.Is<string>(json => DeserializeMakerPayload(json)!.MakerId == MakerId));
    }

    [Fact]
    public async Task Handler_does_NOT_enqueue_invoice_generate_yet()
    {
        // Q2 negative pin — T-0068 owns the third outbox.Enqueue call.
        var order = BuildOrderInState(OrderState.PendingPayment);
        _orders.GetByPaymentProviderRefAsync(ProviderRef, Arg.Any<CancellationToken>())
            .Returns(order);

        await _sut.Handle(ValidCommand(), CancellationToken.None);

        _outbox.DidNotReceive().Enqueue(
            Arg.Any<string>(),
            "invoice.generate",
            Arg.Any<string>());
        // And the total Enqueue count is exactly 2.
        _outbox.Received(2).Enqueue(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Handler_payloads_contain_pre_baked_action_urls()
    {
        // Q4 pin — producer pre-bakes the URL into the payload; consumer
        // is a stateless drainer.
        var order = BuildOrderInState(OrderState.PendingPayment);
        _orders.GetByPaymentProviderRefAsync(ProviderRef, Arg.Any<CancellationToken>())
            .Returns(order);

        await _sut.Handle(ValidCommand(), CancellationToken.None);

        _outbox.Received(1).Enqueue(
            OrderId,
            OutboxEventTypes.OrderPaidCustomerEmail,
            Arg.Is<string>(json =>
                DeserializeCustomerPayload(json)!.ActionUrl ==
                $"{WebBaseUrl}/objednavka/{OrderId}"));
        _outbox.Received(1).Enqueue(
            OrderId,
            OutboxEventTypes.OrderPlacedMakerEmail,
            Arg.Is<string>(json =>
                DeserializeMakerPayload(json)!.ActionUrl ==
                $"{WebBaseUrl}/dashboard/maker/objednavky/{OrderId}"));
    }

    [Fact]
    public async Task Customer_payload_carries_total_amount_and_currency_and_language()
    {
        var order = BuildOrderInState(OrderState.PendingPayment);
        _orders.GetByPaymentProviderRefAsync(ProviderRef, Arg.Any<CancellationToken>())
            .Returns(order);

        await _sut.Handle(ValidCommand(), CancellationToken.None);

        _outbox.Received(1).Enqueue(
            OrderId,
            OutboxEventTypes.OrderPaidCustomerEmail,
            Arg.Is<string>(json =>
                DeserializeCustomerPayload(json)!.TotalAmountMinor == 57900
                && DeserializeCustomerPayload(json)!.Currency == "CZK"
                && DeserializeCustomerPayload(json)!.LanguageCode == "cs-CZ"));
    }

    [Fact]
    public async Task Maker_payload_carries_maker_email_resolved_from_linked_User()
    {
        var order = BuildOrderInState(OrderState.PendingPayment);
        _orders.GetByPaymentProviderRefAsync(ProviderRef, Arg.Any<CancellationToken>())
            .Returns(order);

        await _sut.Handle(ValidCommand(), CancellationToken.None);

        _outbox.Received(1).Enqueue(
            OrderId,
            OutboxEventTypes.OrderPlacedMakerEmail,
            Arg.Is<string>(json => DeserializeMakerPayload(json)!.MakerEmail == MakerEmail));
    }

    [Fact]
    public async Task Customer_user_missing_returns_OrderCustomerUserMissing_and_skips_outbox()
    {
        // T-0067 reviewer M-3: FK invariant violation must refuse to commit
        // (symmetric with the maker-user-missing path) so Comgate retries
        // and ops investigates. Distinct error code from MakerUserMissing
        // so triage separates the two corruption modes.
        var order = BuildOrderInState(OrderState.PendingPayment);
        _orders.GetByPaymentProviderRefAsync(ProviderRef, Arg.Any<CancellationToken>())
            .Returns(order);
        _users.GetByIdAsync(CustomerUserId, Arg.Any<CancellationToken>())
            .Returns((User?)null);

        var result = await _sut.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderCustomerUserMissing);
        result.Error.Type.Should().Be(ErrorType.NotFound);
        _outbox.DidNotReceive().Enqueue(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Maker_user_missing_returns_MakerUserMissing_distinct_from_MakerNotFound()
    {
        // T-0067 reviewer M-2: a maker that exists but whose linked User is
        // null is a different incident class from a soft-deleted maker.
        // Pin the distinct error code so the T-0029 triage UI can route the
        // two cases differently.
        var order = BuildOrderInState(OrderState.PendingPayment);
        _orders.GetByPaymentProviderRefAsync(ProviderRef, Arg.Any<CancellationToken>())
            .Returns(order);
        _users.GetByIdAsync(MakerUserId, Arg.Any<CancellationToken>())
            .Returns((User?)null);

        var result = await _sut.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.MakerUserMissing);
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Handler_fails_idempotently_when_MarkAsPaid_returns_InvalidTransition()
    {
        // Order is already Paid; handler must NOT enqueue side effects.
        var order = BuildOrderInState(OrderState.Paid);
        _orders.GetByPaymentProviderRefAsync(ProviderRef, Arg.Any<CancellationToken>())
            .Returns(order);

        var result = await _sut.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderInvalidTransition);
        _outbox.DidNotReceive().Enqueue(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    private static OrderPaidCustomerEmailPayload? DeserializeCustomerPayload(string json) =>
        JsonSerializer.Deserialize<OrderPaidCustomerEmailPayload>(json);

    private static OrderPlacedMakerEmailPayload? DeserializeMakerPayload(string json) =>
        JsonSerializer.Deserialize<OrderPlacedMakerEmailPayload>(json);
}
