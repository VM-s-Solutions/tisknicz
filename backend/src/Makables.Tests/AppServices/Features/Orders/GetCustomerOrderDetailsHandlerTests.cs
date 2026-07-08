using FluentAssertions;
using Makables.Core.AppServices.Features.Orders;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Orders.Queries;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Orders;

/// <summary>
/// Unit tests pinning T-0082 <see cref="GetCustomerOrderDetails"/>:
/// - IDOR shield (handler resolves customerUserId from session, never input)
/// - Ownership mismatch / nonexistent both surface as OrderNotFound (same shape — no oracle)
/// - DTO passthrough preserves lifecycle timestamps + attachments + InvoicePdfUrl null/set.
/// </summary>
public class GetCustomerOrderDetailsHandlerTests
{
    private const string OrderId = "ord-1";
    private const string CustomerUserId = "user-customer-1";

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-09T10:00:00Z");

    private readonly IOrderQueries _orderQueries = Substitute.For<IOrderQueries>();
    private readonly IUserSessionProvider _session = Substitute.For<IUserSessionProvider>();
    private readonly GetCustomerOrderDetails.Handler _sut;

    public GetCustomerOrderDetailsHandlerTests()
    {
        _session.GetUserId().Returns(CustomerUserId);
        _sut = new GetCustomerOrderDetails.Handler(_orderQueries, _session);
    }

    private static CustomerOrderDetailDto BuildDto(
        string? invoicePdfUrl = null,
        string? returnLabelUrl = null,
        IReadOnlyList<OrderAttachmentSummaryDto>? attachments = null) =>
        new(
            OrderId: OrderId,
            OrderNumber: "M-CZ-20260001",
            State: OrderState.Delivered,
            PaidAt: Now.AddDays(-5),
            AcceptedAt: Now.AddDays(-4),
            ShippedAt: Now.AddDays(-2),
            DeliveredAt: Now.AddDays(-1),
            CancelledAt: null,
            TotalAmountMinor: 57900,
            ProductPriceMinor: 50000,
            ShippingPriceMinor: 7900,
            VatAmountMinor: 10049,
            VatRateBp: 2100,
            Currency: "CZK",
            ContactName: "Anna",
            ContactPhone: "+420 723 456 789",
            MakerName: "Avast s.r.o.",
            ProductTitle: "Vase",
            ShippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
            ShippingCarrierTrackingUrl: "https://tracking.packeta.com/Z1234",
            Attachments: attachments ?? Array.Empty<OrderAttachmentSummaryDto>(),
            InvoicePdfUrl: invoicePdfUrl,
            ReturnLabelUrl: returnLabelUrl,
            CreatedAt: Now.AddDays(-7),
            UpdatedAt: Now.AddDays(-1));

    [Fact]
    public async Task Happy_path_returns_dto_with_all_lifecycle_timestamps_preserved()
    {
        var dto = BuildDto();
        _orderQueries.GetCustomerOrderDetailsAsync(OrderId, CustomerUserId, Arg.Any<CancellationToken>())
            .Returns(dto);

        var result = await _sut.Handle(new GetCustomerOrderDetails.Query(OrderId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Detail.PaidAt.Should().Be(dto.PaidAt);
        result.Value.Detail.AcceptedAt.Should().Be(dto.AcceptedAt);
        result.Value.Detail.ShippedAt.Should().Be(dto.ShippedAt);
        result.Value.Detail.DeliveredAt.Should().Be(dto.DeliveredAt);
        result.Value.Detail.CancelledAt.Should().BeNull();
    }

    [Fact]
    public async Task Customer_userId_mismatch_returns_NotFound()
    {
        // Same null shape as nonexistent — no oracle distinguishes "not yours" from "doesn't exist".
        _orderQueries.GetCustomerOrderDetailsAsync(OrderId, CustomerUserId, Arg.Any<CancellationToken>())
            .Returns((CustomerOrderDetailDto?)null);

        var result = await _sut.Handle(new GetCustomerOrderDetails.Query(OrderId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be(BusinessErrorMessage.OrderNotFound);
    }

    [Fact]
    public async Task Order_not_found_returns_NotFound()
    {
        _orderQueries.GetCustomerOrderDetailsAsync("ord-X", CustomerUserId, Arg.Any<CancellationToken>())
            .Returns((CustomerOrderDetailDto?)null);

        var result = await _sut.Handle(new GetCustomerOrderDetails.Query("ord-X"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderNotFound);
    }

    [Fact]
    public async Task Attachments_field_correctness_preserves_order_and_count()
    {
        var attachments = new[]
        {
            new OrderAttachmentSummaryDto("a-1", "spec.pdf", "application/pdf", 1024,
                "/api/v1/orders/ord-1/attachments/a-1"),
            new OrderAttachmentSummaryDto("a-2", "mockup.jpg", "image/jpeg", 2048,
                "/api/v1/orders/ord-1/attachments/a-2"),
            new OrderAttachmentSummaryDto("a-3", "notes.png", "image/png", 512,
                "/api/v1/orders/ord-1/attachments/a-3"),
        };
        var dto = BuildDto(attachments: attachments);
        _orderQueries.GetCustomerOrderDetailsAsync(OrderId, CustomerUserId, Arg.Any<CancellationToken>())
            .Returns(dto);

        var result = await _sut.Handle(new GetCustomerOrderDetails.Query(OrderId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Detail.Attachments.Should().HaveCount(3);
        result.Value.Detail.Attachments[0].Id.Should().Be("a-1");
        result.Value.Detail.Attachments[2].Id.Should().Be("a-3");
    }

    [Fact]
    public async Task InvoicePdfUrl_nullable_when_invoice_not_yet_generated()
    {
        _orderQueries.GetCustomerOrderDetailsAsync(OrderId, CustomerUserId, Arg.Any<CancellationToken>())
            .Returns(BuildDto(invoicePdfUrl: null));

        var nullResult = await _sut.Handle(new GetCustomerOrderDetails.Query(OrderId), CancellationToken.None);
        nullResult.Value!.Detail.InvoicePdfUrl.Should().BeNull();

        _orderQueries.GetCustomerOrderDetailsAsync(OrderId, CustomerUserId, Arg.Any<CancellationToken>())
            .Returns(BuildDto(invoicePdfUrl: "/api/v1/orders/ord-1/invoice"));
        var setResult = await _sut.Handle(new GetCustomerOrderDetails.Query(OrderId), CancellationToken.None);
        setResult.Value!.Detail.InvoicePdfUrl.Should().Be("/api/v1/orders/ord-1/invoice");
    }

    [Fact]
    public async Task Session_userId_passed_to_query_not_request_input()
    {
        // IDOR shield wiring: handler does NOT take a customerUserId from
        // the request; it forwards the session-resolved one.
        _orderQueries.GetCustomerOrderDetailsAsync(OrderId, CustomerUserId, Arg.Any<CancellationToken>())
            .Returns(BuildDto());

        await _sut.Handle(new GetCustomerOrderDetails.Query(OrderId), CancellationToken.None);

        await _orderQueries.Received(1).GetCustomerOrderDetailsAsync(
            OrderId, CustomerUserId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unauthorized_when_session_has_no_user()
    {
        _session.GetUserId().Returns((string?)null);

        var result = await _sut.Handle(new GetCustomerOrderDetails.Query(OrderId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);
    }
}
