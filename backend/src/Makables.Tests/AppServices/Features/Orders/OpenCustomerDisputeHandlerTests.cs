using System.Text.Json;
using FluentAssertions;
using Makables.Core.AppServices.Common;
using Makables.Core.AppServices.Features.Orders;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Outbox;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Orders;

/// <summary>
/// T-0106 <see cref="OpenCustomerDispute"/> contract: customer-scoped
/// IDOR shield (404, no existence leak), parenthesis-state flip + a
/// Source=Customer dispute row + ONE admin-email emission, Silent-Success
/// re-open returning the existing open dispute id, and the Validator's
/// carrier-reserved category gate (§C.6) + description bounds.
/// </summary>
public class OpenCustomerDisputeHandlerTests
{
    private const string OrderId = "ord-1";
    private const string CustomerUserId = "user-cust-1";
    private const string WebBaseUrl = "https://makables.test";

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-12T10:00:00Z");

    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IDisputeRepository _disputes = Substitute.For<IDisputeRepository>();
    private readonly IOutbox _outbox = Substitute.For<IOutbox>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly IIdGenerator _ids = Substitute.For<IIdGenerator>();
    private readonly IUserSessionProvider _session = Substitute.For<IUserSessionProvider>();
    private readonly ILanguageResolver _languageResolver = Substitute.For<ILanguageResolver>();
    private readonly OpenCustomerDispute.Handler _sut;

    public OpenCustomerDisputeHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _ids.Next().Returns("dsp-1");
        _session.GetUserId().Returns(CustomerUserId);
        _languageResolver.ResolveAsync(null, "CZ", Arg.Any<CancellationToken>())
            .Returns("cs-CZ");
        var urls = Options.Create(new PublicAppUrlsOptions { WebBaseUrl = WebBaseUrl });
        _sut = new OpenCustomerDispute.Handler(
            _orders, _disputes, _outbox, _clock, _ids, _session,
            _languageResolver, urls,
            NullLogger<OpenCustomerDispute.Handler>.Instance);
    }

    private static Order BuildDeliveredOrder()
    {
        var o = Order.Create(
            id: OrderId, orderNumber: "M-CZ-20260042",
            customerUserId: CustomerUserId, makerId: "maker-1", productId: "prod-1",
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
        o.MarkAsDelivered(clock, OrderDeliverySource.Carrier);
        return o;
    }

    [Fact]
    public async Task Happy_path_flips_state_adds_customer_sourced_row_and_enqueues_admin_email()
    {
        var order = BuildDeliveredOrder();
        _orders.GetByIdForCustomerAsync(OrderId, CustomerUserId, Arg.Any<CancellationToken>())
            .Returns(order);
        Dispute? addedDispute = null;
        await _disputes.AddAsync(Arg.Do<Dispute>(d => addedDispute = d), Arg.Any<CancellationToken>());
        string? capturedJson = null;
        _outbox.Enqueue(
            Arg.Any<string>(),
            OutboxEventTypes.OrderDisputedAdminEmail,
            Arg.Do<string>(j => capturedJson = j));

        var result = await _sut.Handle(
            new OpenCustomerDispute.Command(OrderId, DisputeCategory.DamagedItem, "  Rozbitá váza.  "),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.DisputeId.Should().Be("dsp-1");
        order.State.Should().Be(OrderState.Disputed);
        order.PreDisputeState.Should().Be(OrderState.Delivered);

        addedDispute.Should().NotBeNull();
        addedDispute!.Source.Should().Be(DisputeSource.Customer, "source is stamped server-side");
        addedDispute.Description.Should().Be("Rozbitá váza.", "the factory trims the description");

        var payload = JsonSerializer.Deserialize<OrderDisputedAdminEmailPayload>(capturedJson!);
        payload!.ActionUrl.Should().Be($"{WebBaseUrl}/dashboard/admin/orders/{OrderId}",
            "the admin URL is pre-baked at enqueue time");
        _outbox.Received(1).Enqueue(
            OrderId, OutboxEventTypes.OrderDisputedAdminEmail, Arg.Any<string>());
    }

    [Fact]
    public async Task IDOR_foreign_order_returns_notFound_without_writes()
    {
        // The scoped repository returns null for cross-customer ids —
        // same shape as unknown ids, no existence oracle (ADR 0013).
        _orders.GetByIdForCustomerAsync(OrderId, CustomerUserId, Arg.Any<CancellationToken>())
            .Returns((Order?)null);

        var result = await _sut.Handle(
            new OpenCustomerDispute.Command(OrderId, DisputeCategory.NotDelivered, "Nedorazilo."),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderNotFound);
        await _disputes.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        _outbox.DidNotReceive().Enqueue(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Reopen_on_disputed_order_is_silent_success_returning_existing_dispute_id()
    {
        var order = BuildDeliveredOrder();
        order.OpenDispute(_clock);
        _orders.GetByIdForCustomerAsync(OrderId, CustomerUserId, Arg.Any<CancellationToken>())
            .Returns(order);
        var existing = Dispute.Open(
            "dsp-existing", OrderId, DisputeCategory.NotDelivered, "Původní reklamace.",
            DisputeSource.Customer, "CZ");
        _disputes.GetOpenByOrderIdAsync(OrderId, Arg.Any<CancellationToken>()).Returns(existing);

        var result = await _sut.Handle(
            new OpenCustomerDispute.Command(OrderId, DisputeCategory.DamagedItem, "Druhý pokus."),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.DisputeId.Should().Be("dsp-existing");
        await _disputes.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        _outbox.DidNotReceive().Enqueue(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Theory]
    [InlineData(OrderState.PendingPayment)]
    [InlineData(OrderState.Completed)]
    public async Task Non_disputable_state_surfaces_invalidTransition_without_writes(OrderState state)
    {
        var order = state == OrderState.PendingPayment
            ? PendingOrder()
            : CompletedOrder();
        _orders.GetByIdForCustomerAsync(OrderId, CustomerUserId, Arg.Any<CancellationToken>())
            .Returns(order);

        var result = await _sut.Handle(
            new OpenCustomerDispute.Command(OrderId, DisputeCategory.Other, "Stížnost."),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderInvalidTransition);
        await _disputes.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        _outbox.DidNotReceive().Enqueue(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());

        static Order PendingOrder()
        {
            return Order.Create(
                id: OrderId, orderNumber: "M-CZ-20260043",
                customerUserId: CustomerUserId, makerId: "maker-1", productId: "prod-1",
                contactName: "Anna", contactEmail: "anna@example.cz", contactPhone: "+420",
                productPriceAmountMinor: 50000, shippingPriceAmountMinor: 7900,
                platformFeeAmountMinor: 7500, makerPayoutAmountMinor: 50400,
                totalAmountMinor: 57900, currency: "CZK", vatRateBp: 2100,
                shippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
                zasilkovnaPickupPointId: "pp-42", countryCode: "CZ");
        }
        static Order CompletedOrder()
        {
            var o = BuildDeliveredOrder();
            var clock = Substitute.For<IClock>();
            clock.UtcNow.Returns(Now.AddDays(-1));
            o.Complete(clock);
            return o;
        }
    }

    [Fact]
    public async Task Missing_session_fails_closed()
    {
        _session.GetUserId().Returns((string?)null);

        var result = await _sut.Handle(
            new OpenCustomerDispute.Command(OrderId, DisputeCategory.Other, "Bez přihlášení."),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);
    }

    // === Validator (§C.6 carrier-reserved gate + §C.7 description bounds) ===

    [Theory]
    [InlineData(DisputeCategory.CarrierReturned)]
    [InlineData(DisputeCategory.CarrierFailed)]
    public void Validator_rejects_carrier_reserved_categories(DisputeCategory category)
    {
        var validator = new OpenCustomerDispute.Validator();
        var result = validator.Validate(
            new OpenCustomerDispute.Command(OrderId, category, "Popis."));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.ErrorCode == BusinessErrorMessage.OrderDisputeCategoryNotAllowed);
    }

    [Fact]
    public void Validator_rejects_empty_and_oversize_description_and_accepts_valid_input()
    {
        var validator = new OpenCustomerDispute.Validator();

        var empty = validator.Validate(
            new OpenCustomerDispute.Command(OrderId, DisputeCategory.Other, ""));
        empty.IsValid.Should().BeFalse();
        empty.Errors.Should().Contain(e => e.ErrorCode == BusinessErrorMessage.Required);

        var oversize = validator.Validate(new OpenCustomerDispute.Command(
            OrderId, DisputeCategory.Other, new string('x', Dispute.MaxDescriptionLength + 1)));
        oversize.IsValid.Should().BeFalse();
        oversize.Errors.Should().Contain(e => e.ErrorCode == BusinessErrorMessage.MaxLength);

        // must-cover-tests §5: one positive per Validator.
        validator.Validate(new OpenCustomerDispute.Command(
            OrderId, DisputeCategory.DamagedItem, "Rozbité zboží."))
            .IsValid.Should().BeTrue();
    }
}
