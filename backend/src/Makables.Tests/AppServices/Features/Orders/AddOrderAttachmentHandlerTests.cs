using FluentAssertions;
using Makables.Core.AppServices.Features.Orders;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Orders;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Orders;

/// <summary>
/// Pins the T-0064 <see cref="AddOrderAttachment.Handler"/> 8-step flow.
/// NSubstitute over every dependency; <see cref="Order.AddAttachment"/>
/// has its own coverage in <c>OrderAddAttachmentTests</c>, so these
/// tests focus on the handler's branch logic — session check, ownership
/// scope, state-gate re-check, count-cap re-check, happy path.
/// </summary>
public class AddOrderAttachmentHandlerTests
{
    private const string CustomerUserId = "user-customer-1";
    private const string OrderId = "ord-1";
    private const string MakerId = "maker-1";
    private const string NewAttachmentId = "att-new";
    private const string CountryCode = "CZ";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-05T10:00:00Z");

    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IUserSessionProvider _session = Substitute.For<IUserSessionProvider>();
    private readonly IIdGenerator _ids = Substitute.For<IIdGenerator>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly AddOrderAttachment.Handler _sut;

    public AddOrderAttachmentHandlerTests()
    {
        _session.GetUserId().Returns(CustomerUserId);
        _ids.Next().Returns(NewAttachmentId);
        _clock.UtcNow.Returns(Now);

        _sut = new AddOrderAttachment.Handler(_orders, _session, _ids, _clock);
    }

    // === Fixtures ===

    private static Order BuildOrderInState(OrderState target)
    {
        var o = Order.Create(
            id: OrderId,
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
            countryCode: CountryCode);

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);

        if (target == OrderState.PendingPayment) return o;
        o.MarkAsPaid(clock, "tx-1");
        if (target == OrderState.Paid) return o;
        o.Accept(clock);
        if (target == OrderState.Accepted) return o;
        o.Ship(clock, "PKT-1", 7);
        if (target == OrderState.Shipped) return o;
        o.MarkAsDelivered(clock);
        if (target == OrderState.Delivered) return o;

        throw new ArgumentOutOfRangeException(nameof(target), target,
            "Helper covers PendingPayment / Paid / Accepted / Shipped / Delivered only.");
    }

    private static OrderAttachment NewAttachment(string id) =>
        OrderAttachment.Create(
            id: id,
            orderId: OrderId,
            blobPath: $"cz/orders/ord-1/{id}.pdf",
            originalFilename: $"{id}.pdf",
            contentType: "application/pdf",
            sizeBytes: 1024,
            uploadedByUserId: CustomerUserId,
            countryCode: CountryCode);

    private static AddOrderAttachment.Command ValidCommand() => new(
        OrderId: OrderId,
        BlobPath: $"cz/orders/{OrderId}/{NewAttachmentId}.pdf",
        OriginalFilename: "spec.pdf",
        ContentType: "application/pdf",
        SizeBytes: 2048);

    // === Tests ===

    [Fact]
    public async Task Returns_Unauthorized_when_session_has_no_user()
    {
        _session.GetUserId().Returns((string?)null);

        var result = await _sut.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);
        await _orders.DidNotReceive().GetByIdForCustomerAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Returns_OrderNotFound_when_repository_returns_null()
    {
        // Covers both the unknown-id and the cross-customer path —
        // GetByIdForCustomerAsync returns null in both cases so the
        // handler cannot distinguish (IDOR-leak-resistance).
        _orders.GetByIdForCustomerAsync(OrderId, CustomerUserId, Arg.Any<CancellationToken>())
            .Returns((Order?)null);

        var result = await _sut.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be(BusinessErrorMessage.OrderNotFound);
    }

    [Theory]
    [InlineData(OrderState.Shipped)]
    [InlineData(OrderState.Delivered)]
    public async Task Returns_OrderStateForbidsAttachment_for_closed_window(OrderState state)
    {
        var order = BuildOrderInState(state);
        _orders.GetByIdForCustomerAsync(OrderId, CustomerUserId, Arg.Any<CancellationToken>())
            .Returns(order);

        var result = await _sut.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.Conflict);
        result.Error.Code.Should().Be(BusinessErrorMessage.OrderStateForbidsAttachment);
        order.Attachments.Should().BeEmpty();
    }

    [Fact]
    public async Task Returns_OrderAttachmentLimitReached_when_count_at_cap()
    {
        var order = BuildOrderInState(OrderState.PendingPayment);
        for (var i = 0; i < Order.MaxAttachmentCount; i++)
        {
            order.AddAttachment(NewAttachment($"att-existing-{i}"));
        }
        _orders.GetByIdForCustomerAsync(OrderId, CustomerUserId, Arg.Any<CancellationToken>())
            .Returns(order);

        var result = await _sut.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.Conflict);
        result.Error.Code.Should().Be(BusinessErrorMessage.OrderAttachmentLimitReached);
        order.Attachments.Should().HaveCount(Order.MaxAttachmentCount);
    }

    [Theory]
    [InlineData(OrderState.PendingPayment)]
    [InlineData(OrderState.Paid)]
    [InlineData(OrderState.Accepted)]
    public async Task Happy_path_appends_attachment_and_returns_response(OrderState state)
    {
        var order = BuildOrderInState(state);
        _orders.GetByIdForCustomerAsync(OrderId, CustomerUserId, Arg.Any<CancellationToken>())
            .Returns(order);

        var command = ValidCommand();
        var result = await _sut.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AttachmentId.Should().Be(NewAttachmentId);
        result.Value.OriginalFilename.Should().Be(command.OriginalFilename);
        result.Value.SizeBytes.Should().Be(command.SizeBytes);
        result.Value.UploadedOn.Should().Be(Now);

        order.Attachments.Should().ContainSingle()
            .Which.Id.Should().Be(NewAttachmentId);
    }

    [Fact]
    public async Task Handler_does_not_call_AddAsync_or_SaveChanges()
    {
        // T-0064 contract: the new attachment is appended through the
        // aggregate's _attachments backing field. EF change tracking on
        // the loaded Order persists the child; UnitOfWorkPipelineBehavior
        // commits. No explicit repo call — pinned here so a future
        // refactor that introduces an explicit save shows up.
        var order = BuildOrderInState(OrderState.PendingPayment);
        _orders.GetByIdForCustomerAsync(OrderId, CustomerUserId, Arg.Any<CancellationToken>())
            .Returns(order);

        await _sut.Handle(ValidCommand(), CancellationToken.None);

        await _orders.DidNotReceive().AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
    }
}
