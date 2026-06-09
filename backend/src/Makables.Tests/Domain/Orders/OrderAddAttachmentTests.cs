using FluentAssertions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Orders;
using NSubstitute;

namespace Makables.Tests.Domain.Orders;

/// <summary>
/// Coverage for <see cref="Order.AddAttachment"/> + the state-gate that
/// backs it (<see cref="Order.AllowsAttachmentUpload"/>). T-0064 user
/// decision Q4 — uploads admitted only in PendingPayment / Paid /
/// Accepted; refused in every other state.
/// </summary>
public class OrderAddAttachmentTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-03T10:00:00Z");

    private static IClock FixedClock(DateTimeOffset? at = null)
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(at ?? Now);
        return clock;
    }

    private static Order ValidDefaults() => Order.Create(
        id: "ord-1",
        orderNumber: "CZ-2026-000001",
        customerUserId: "user-1",
        makerId: "maker-1",
        productId: "prod-1",
        contactName: "Jan Novák",
        contactEmail: "jan@example.cz",
        contactPhone: "+420777123456",
        productPriceAmountMinor: 25000,
        shippingPriceAmountMinor: 7900,
        platformFeeAmountMinor: 3290,
        makerPayoutAmountMinor: 29610,
        totalAmountMinor: 32900,
        currency: "CZK",
        vatRateBp: 2100,
        shippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
        zasilkovnaPickupPointId: "branch-79",
        countryCode: "cz");

    private static OrderAttachment NewAttachment(string id = "att-x") =>
        OrderAttachment.Create(
            id: id,
            orderId: "ord-1",
            blobPath: $"cz/orders/ord-1/{id}.pdf",
            originalFilename: $"{id}.pdf",
            contentType: "application/pdf",
            sizeBytes: 1024,
            uploadedByUserId: "user-1",
            countryCode: "CZ");

    /// <summary>
    /// Drive a fresh order through the happy path until it reaches
    /// <paramref name="target"/>. Mirrors the helper in
    /// <c>OrderTests.OrderInState</c> — kept local to this file so the
    /// AddAttachment matrix tests below don't grow a cross-test
    /// dependency.
    /// </summary>
    private static Order OrderInState(OrderState target)
    {
        var o = ValidDefaults();
        if (target == OrderState.PendingPayment) return o;

        o.MarkAsPaid(FixedClock(), "tx-1");
        if (target == OrderState.Paid) return o;

        o.Accept(FixedClock());
        if (target == OrderState.Accepted) return o;

        o.Ship(FixedClock(), "PKT-1", 7);
        if (target == OrderState.Shipped) return o;

        o.MarkAsDelivered(FixedClock(), OrderDeliverySource.Auto);
        if (target == OrderState.Delivered) return o;

        o.Complete(FixedClock());
        if (target == OrderState.Completed) return o;

        throw new ArgumentOutOfRangeException(
            nameof(target), target, "Helper only handles the linear happy-path states.");
    }

    // === AllowsAttachmentUpload ===

    [Theory]
    [InlineData(OrderState.PendingPayment)]
    [InlineData(OrderState.Paid)]
    [InlineData(OrderState.Accepted)]
    public void AllowsAttachmentUpload_true_in_open_window(OrderState state)
    {
        var o = OrderInState(state);
        o.AllowsAttachmentUpload().Should().BeTrue();
    }

    [Theory]
    [InlineData(OrderState.Shipped)]
    [InlineData(OrderState.Delivered)]
    [InlineData(OrderState.Completed)]
    public void AllowsAttachmentUpload_false_after_shipped(OrderState state)
    {
        var o = OrderInState(state);
        o.AllowsAttachmentUpload().Should().BeFalse();
    }

    [Fact]
    public void AllowsAttachmentUpload_false_after_cancelled()
    {
        var o = ValidDefaults();
        o.Cancel(FixedClock());
        o.AllowsAttachmentUpload().Should().BeFalse();
    }

    [Fact]
    public void AllowsAttachmentUpload_false_after_refunded()
    {
        var o = ValidDefaults();
        o.MarkAsPaid(FixedClock(), "tx-1");
        o.Refund(FixedClock());
        o.AllowsAttachmentUpload().Should().BeFalse();
    }

    [Fact]
    public void AllowsAttachmentUpload_false_after_disputed()
    {
        var o = ValidDefaults();
        o.MarkAsPaid(FixedClock(), "tx-1");
        o.Accept(FixedClock());
        o.Ship(FixedClock(), "PKT-1", 7);
        o.OpenDispute(FixedClock());
        o.AllowsAttachmentUpload().Should().BeFalse();
    }

    // === AddAttachment ===

    [Theory]
    [InlineData(OrderState.PendingPayment)]
    [InlineData(OrderState.Paid)]
    [InlineData(OrderState.Accepted)]
    public void AddAttachment_appends_in_open_window(OrderState state)
    {
        var o = OrderInState(state);

        var result = o.AddAttachment(NewAttachment("att-1"));

        result.IsSuccess.Should().BeTrue();
        o.Attachments.Should().ContainSingle().Which.Id.Should().Be("att-1");
    }

    [Theory]
    [InlineData(OrderState.Shipped)]
    [InlineData(OrderState.Delivered)]
    [InlineData(OrderState.Completed)]
    public void AddAttachment_returns_OrderStateForbidsAttachment_after_shipped(OrderState state)
    {
        var o = OrderInState(state);

        var result = o.AddAttachment(NewAttachment());

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderStateForbidsAttachment);
        result.Error.Type.Should().Be(ErrorType.Conflict);
        o.Attachments.Should().BeEmpty();
    }

    [Fact]
    public void AddAttachment_returns_OrderStateForbidsAttachment_after_cancelled()
    {
        var o = ValidDefaults();
        o.Cancel(FixedClock());

        var result = o.AddAttachment(NewAttachment());

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderStateForbidsAttachment);
        o.Attachments.Should().BeEmpty();
    }

    [Fact]
    public void AddAttachment_returns_OrderStateForbidsAttachment_after_refunded()
    {
        var o = ValidDefaults();
        o.MarkAsPaid(FixedClock(), "tx-1");
        o.Refund(FixedClock());

        var result = o.AddAttachment(NewAttachment());

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderStateForbidsAttachment);
    }

    [Fact]
    public void AddAttachment_returns_OrderStateForbidsAttachment_after_disputed()
    {
        var o = ValidDefaults();
        o.MarkAsPaid(FixedClock(), "tx-1");
        o.Accept(FixedClock());
        o.Ship(FixedClock(), "PKT-1", 7);
        o.OpenDispute(FixedClock());

        var result = o.AddAttachment(NewAttachment());

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderStateForbidsAttachment);
    }

    [Fact]
    public void AddAttachment_at_cap_returns_OrderAttachmentLimitReached()
    {
        var o = ValidDefaults();
        for (var i = 0; i < Order.MaxAttachmentCount; i++)
        {
            o.AddAttachment(NewAttachment($"att-{i}")).IsSuccess.Should().BeTrue();
        }

        var eleventh = o.AddAttachment(NewAttachment("att-11"));

        eleventh.IsSuccess.Should().BeFalse();
        eleventh.Error!.Code.Should().Be(BusinessErrorMessage.OrderAttachmentLimitReached);
        eleventh.Error.Type.Should().Be(ErrorType.Conflict);
        o.Attachments.Should().HaveCount(Order.MaxAttachmentCount);
    }

    [Fact]
    public void AddAttachment_below_cap_still_allowed()
    {
        var o = ValidDefaults();
        for (var i = 0; i < Order.MaxAttachmentCount - 1; i++)
        {
            o.AddAttachment(NewAttachment($"att-{i}")).IsSuccess.Should().BeTrue();
        }

        // 10th add — should succeed (cap is "fewer than 10 before adding").
        var tenth = o.AddAttachment(NewAttachment("att-9"));

        tenth.IsSuccess.Should().BeTrue();
        o.Attachments.Should().HaveCount(Order.MaxAttachmentCount);
    }

    [Fact]
    public void AddAttachment_rejects_null_attachment()
    {
        var o = ValidDefaults();
        var act = () => o.AddAttachment(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
