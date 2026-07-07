using System.Text.Json;
using FluentAssertions;
using Makables.Core.AppServices.Common;
using Makables.Core.AppServices.Features.Orders;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.OrderMessages;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Outbox;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Orders;

/// <summary>
/// T-0145 <see cref="EscalateDispute"/> contract: enqueues the
/// <c>dispute.autoEscalated.adminEmail</c> notification exactly once for
/// an open, customer-sourced dispute past its 7-day response window with
/// no maker reply — and never mutates <c>ResolvedAt</c> / the Order
/// state (notification only). Every guard re-check (resolved /
/// already-escalated / maker-replied / missing row) is a silent no-op,
/// since a Function sweep has no client waiting on a 4xx.
/// </summary>
public class EscalateDisputeHandlerTests
{
    private const string DisputeId = "dsp-1";
    private const string OrderId = "ord-1";
    private const string WebBaseUrl = "https://makables.test";

    private static readonly DateTimeOffset DisputeCreatedAt = DateTimeOffset.Parse("2026-07-01T10:00:00Z");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-09T08:00:00Z"); // +8 days

    private readonly IDisputeRepository _disputes = Substitute.For<IDisputeRepository>();
    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IOrderMessageRepository _orderMessages = Substitute.For<IOrderMessageRepository>();
    private readonly IOutbox _outbox = Substitute.For<IOutbox>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly ILanguageResolver _languageResolver = Substitute.For<ILanguageResolver>();
    private readonly EscalateDispute.Handler _sut;

    public EscalateDisputeHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _languageResolver.ResolveAsync(null, "CZ", Arg.Any<CancellationToken>()).Returns("cs-CZ");
        var urls = Options.Create(new PublicAppUrlsOptions { WebBaseUrl = WebBaseUrl });
        _sut = new EscalateDispute.Handler(
            _disputes, _orders, _orderMessages, _outbox, _clock, _languageResolver, urls,
            NullLogger<EscalateDispute.Handler>.Instance);
    }

    /// <summary>
    /// <see cref="Auditable.CreatedAt"/> is stamped by the DB save
    /// interceptor in production, not at <see cref="Dispute.Open"/>
    /// construction — the handler only ever reads back whatever value is
    /// on the entity it loads, so the tests mock
    /// <see cref="IOrderMessageRepository.HasMakerReplySinceAsync"/>
    /// against <c>dispute.CreatedAt</c> directly rather than asserting a
    /// specific timestamp.
    /// </summary>
    private static Dispute BuildOpenCustomerDispute() =>
        Dispute.Open(
            DisputeId, OrderId, DisputeCategory.NotDelivered, "Balík nedorazil.",
            DisputeSource.Customer, "CZ");

    private static Order BuildDeliveredOrder()
    {
        var o = Order.Create(
            id: OrderId, orderNumber: "M-CZ-20260099",
            customerUserId: "user-cust-1", makerId: "maker-1", productId: "prod-1",
            contactName: "Anna", contactEmail: "anna@example.cz", contactPhone: "+420",
            productPriceAmountMinor: 50000, shippingPriceAmountMinor: 7900,
            platformFeeAmountMinor: 7500, makerPayoutAmountMinor: 50400,
            totalAmountMinor: 57900, currency: "CZK", vatRateBp: 2100,
            shippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
            zasilkovnaPickupPointId: "pp-42", countryCode: "CZ");
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now.AddDays(-10));
        o.MarkAsPaid(clock, "tx-1");
        o.Accept(clock);
        o.Ship(clock, "PKT-1", 7);
        o.MarkAsDelivered(clock, OrderDeliverySource.Carrier);
        o.OpenDispute(clock);
        return o;
    }

    [Fact]
    public async Task Open_customer_dispute_past_window_with_no_maker_reply_enqueues_escalation_once()
    {
        var dispute = BuildOpenCustomerDispute();
        var order = BuildDeliveredOrder();
        _disputes.GetByIdUnscopedAsync(DisputeId, Arg.Any<CancellationToken>()).Returns(dispute);
        _orderMessages.HasMakerReplySinceAsync(OrderId, dispute.CreatedAt, Arg.Any<CancellationToken>())
            .Returns(false);
        _orders.GetByIdUnscopedReadOnlyAsync(OrderId, Arg.Any<CancellationToken>()).Returns(order);
        string? capturedJson = null;
        _outbox.Enqueue(
            Arg.Any<string>(),
            OutboxEventTypes.DisputeAutoEscalatedAdminEmail,
            Arg.Do<string>(j => capturedJson = j));

        var result = await _sut.Handle(new EscalateDispute.Command(DisputeId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        dispute.AutoEscalatedAt.Should().NotBeNull();
        dispute.ResolvedAt.Should().BeNull("the sweep NEVER resolves the dispute");
        order.State.Should().Be(OrderState.Disputed, "the sweep NEVER changes the Order state");

        _outbox.Received(1).Enqueue(
            OrderId, OutboxEventTypes.DisputeAutoEscalatedAdminEmail, Arg.Any<string>());
        var payload = JsonSerializer.Deserialize<DisputeAutoEscalatedAdminEmailPayload>(capturedJson!);
        payload!.DisputeId.Should().Be(DisputeId);
        payload.ActionUrl.Should().Be($"{WebBaseUrl}/dashboard/admin/orders/{OrderId}");
    }

    [Fact]
    public async Task Maker_replied_since_dispute_opened_suppresses_escalation()
    {
        // AC-6: a maker reply within the window suppresses the escalation
        // even though the sweep's id-only projection surfaced the id.
        var dispute = BuildOpenCustomerDispute();
        _disputes.GetByIdUnscopedAsync(DisputeId, Arg.Any<CancellationToken>()).Returns(dispute);
        _orderMessages.HasMakerReplySinceAsync(OrderId, dispute.CreatedAt, Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await _sut.Handle(new EscalateDispute.Command(DisputeId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        dispute.AutoEscalatedAt.Should().BeNull();
        _outbox.DidNotReceive().Enqueue(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Resolved_dispute_is_excluded_even_if_dispatched()
    {
        // AC-7: a dispute resolved before the sweep runs is a no-op even
        // if the id somehow reached the handler (stale projection race).
        var dispute = BuildOpenCustomerDispute();
        var resolveClock = Substitute.For<IClock>();
        resolveClock.UtcNow.Returns(DisputeCreatedAt.AddDays(1));
        dispute.Resolve(resolveClock, DisputeResolutionOutcome.Resumed, "Vyřešeno telefonicky.");
        _disputes.GetByIdUnscopedAsync(DisputeId, Arg.Any<CancellationToken>()).Returns(dispute);

        var result = await _sut.Handle(new EscalateDispute.Command(DisputeId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _orderMessages.DidNotReceiveWithAnyArgs()
            .HasMakerReplySinceAsync(default!, default, default);
        _outbox.DidNotReceive().Enqueue(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Already_escalated_dispute_does_not_re_enqueue_idempotency()
    {
        // AC-8: a second dispatch against an already-escalated dispute
        // (e.g. the sweep re-runs before the row exits the candidate
        // list, or a retried Function invocation) must NOT double-send.
        var dispute = BuildOpenCustomerDispute();
        dispute.TryMarkAutoEscalated(_clock).Should().BeTrue();
        var firstEscalatedAt = dispute.AutoEscalatedAt;
        _disputes.GetByIdUnscopedAsync(DisputeId, Arg.Any<CancellationToken>()).Returns(dispute);

        var result = await _sut.Handle(new EscalateDispute.Command(DisputeId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        dispute.AutoEscalatedAt.Should().Be(firstEscalatedAt, "the stamp is set exactly once");
        _outbox.DidNotReceive().Enqueue(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Missing_dispute_row_is_a_silent_no_op()
    {
        _disputes.GetByIdUnscopedAsync(DisputeId, Arg.Any<CancellationToken>()).Returns((Dispute?)null);

        var result = await _sut.Handle(new EscalateDispute.Command(DisputeId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _outbox.DidNotReceive().Enqueue(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    // === Validator ===

    [Fact]
    public void Validator_rejects_empty_disputeId_and_accepts_valid_input()
    {
        var validator = new EscalateDispute.Validator();

        var empty = validator.Validate(new EscalateDispute.Command(""));
        empty.IsValid.Should().BeFalse();
        empty.Errors.Should().Contain(e => e.ErrorCode == BusinessErrorMessage.Required);

        validator.Validate(new EscalateDispute.Command(DisputeId)).IsValid.Should().BeTrue();
    }
}
