using FluentAssertions;
using Makables.Core.AppServices.Features.Orders;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Orders.Queries;
using Makables.Core.Domain.Orders.Sorting;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Orders;

/// <summary>
/// Unit tests pinning T-0080 <see cref="GetCustomerOrders"/> behaviour:
/// - IDOR shield (handler resolves customerId from session, never from input)
/// - Filter / sort / page forwarding to <see cref="IOrderQueries"/>
/// - Validator clamps on Page / PageSize / inverted date range
/// - Empty result surfaces as Success with empty PagedData (no 404).
/// </summary>
public class GetCustomerOrdersHandlerTests
{
    private const string CustomerUserId = "user-customer-1";

    private readonly IOrderQueries _orderQueries = Substitute.For<IOrderQueries>();
    private readonly IUserSessionProvider _session = Substitute.For<IUserSessionProvider>();
    private readonly GetCustomerOrders.Handler _sut;

    public GetCustomerOrdersHandlerTests()
    {
        _session.GetUserId().Returns(CustomerUserId);
        _sut = new GetCustomerOrders.Handler(_orderQueries, _session);
    }

    private static CustomerOrderListItemDto BuildItem(string id, OrderState state, long total) =>
        new(
            OrderId: id,
            OrderNumber: $"M-CZ-2026{id}",
            State: state,
            TotalAmountMinor: total,
            Currency: "CZK",
            CreatedAt: new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero),
            MakerName: "Avast s.r.o.",
            ProductTitle: "Hrnek");

    // === Handler — happy path + forwarding ===

    [Fact]
    public async Task Happy_path_returns_paged_data_with_default_sort()
    {
        var paged = new PagedData<CustomerOrderListItemDto>(
            new[]
            {
                BuildItem("ord-1", OrderState.Paid, 1000),
                BuildItem("ord-2", OrderState.Shipped, 2000),
                BuildItem("ord-3", OrderState.Delivered, 3000),
            },
            Page: 1, PageSize: 20, TotalCount: 3);
        _orderQueries.GetCustomerOrdersPagedAsync(
                CustomerUserId,
                Arg.Any<OrderFilter>(),
                OrderSort.CreatedAtDesc,
                1, 20,
                Arg.Any<CancellationToken>())
            .Returns(paged);

        var result = await _sut.Handle(
            new GetCustomerOrders.Query(1, 20, null, null, null, OrderSort.CreatedAtDesc),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Orders.Items.Should().HaveCount(3);
        result.Value.Orders.TotalCount.Should().Be(3);
        await _orderQueries.Received(1).GetCustomerOrdersPagedAsync(
            CustomerUserId,
            Arg.Is<OrderFilter>(f => f.State == null && f.DateRangeStart == null && f.DateRangeEnd == null),
            OrderSort.CreatedAtDesc,
            1, 20,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Filter_by_State_passes_through_to_queries()
    {
        _orderQueries.GetCustomerOrdersPagedAsync(
                CustomerUserId, Arg.Any<OrderFilter>(), Arg.Any<OrderSort>(), 1, 20, Arg.Any<CancellationToken>())
            .Returns(PagedData<CustomerOrderListItemDto>.Empty(1, 20));

        var result = await _sut.Handle(
            new GetCustomerOrders.Query(1, 20, OrderState.Paid, null, null, OrderSort.CreatedAtDesc),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _orderQueries.Received(1).GetCustomerOrdersPagedAsync(
            CustomerUserId,
            Arg.Is<OrderFilter>(f => f.State == OrderState.Paid
                && f.DateRangeStart == null
                && f.DateRangeEnd == null),
            OrderSort.CreatedAtDesc,
            1, 20,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Filter_by_date_range_passes_through_to_queries()
    {
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        _orderQueries.GetCustomerOrdersPagedAsync(
                CustomerUserId, Arg.Any<OrderFilter>(), Arg.Any<OrderSort>(), 1, 20, Arg.Any<CancellationToken>())
            .Returns(PagedData<CustomerOrderListItemDto>.Empty(1, 20));

        var result = await _sut.Handle(
            new GetCustomerOrders.Query(1, 20, null, from, to, OrderSort.CreatedAtDesc),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _orderQueries.Received(1).GetCustomerOrdersPagedAsync(
            CustomerUserId,
            Arg.Is<OrderFilter>(f => f.State == null
                && f.DateRangeStart == from
                && f.DateRangeEnd == to),
            OrderSort.CreatedAtDesc,
            1, 20,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Sort_variant_TotalAmountDesc_passes_through()
    {
        _orderQueries.GetCustomerOrdersPagedAsync(
                CustomerUserId, Arg.Any<OrderFilter>(), OrderSort.TotalAmountDesc, 1, 20, Arg.Any<CancellationToken>())
            .Returns(PagedData<CustomerOrderListItemDto>.Empty(1, 20));

        var result = await _sut.Handle(
            new GetCustomerOrders.Query(1, 20, null, null, null, OrderSort.TotalAmountDesc),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _orderQueries.Received(1).GetCustomerOrdersPagedAsync(
            CustomerUserId, Arg.Any<OrderFilter>(), OrderSort.TotalAmountDesc, 1, 20, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Empty_result_returns_empty_PagedData_with_TotalCount_zero()
    {
        // Cross-tenant probe path — predicate IS the IDOR shield, so SQL
        // simply returns no rows. Handler MUST surface this as Success
        // (not 404) per AC-5.
        _orderQueries.GetCustomerOrdersPagedAsync(
                CustomerUserId, Arg.Any<OrderFilter>(), Arg.Any<OrderSort>(), 1, 20, Arg.Any<CancellationToken>())
            .Returns(PagedData<CustomerOrderListItemDto>.Empty(1, 20));

        var result = await _sut.Handle(
            new GetCustomerOrders.Query(1, 20, null, null, null, OrderSort.CreatedAtDesc),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Orders.Items.Should().BeEmpty();
        result.Value.Orders.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Unauthorized_when_session_has_no_user()
    {
        _session.GetUserId().Returns((string?)null);
        var sut = new GetCustomerOrders.Handler(_orderQueries, _session);

        var result = await sut.Handle(
            new GetCustomerOrders.Query(1, 20, null, null, null, OrderSort.CreatedAtDesc),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);
        await _orderQueries.DidNotReceive().GetCustomerOrdersPagedAsync(
            Arg.Any<string>(), Arg.Any<OrderFilter>(), Arg.Any<OrderSort>(),
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Query_has_no_customer_id_field()
    {
        // IDOR shield — owning customer resolved from session, never from input.
        var props = typeof(GetCustomerOrders.Query).GetProperties();
        props.Should().NotContain(p =>
            p.Name.Equals("CustomerId", StringComparison.OrdinalIgnoreCase)
            || p.Name.Equals("CustomerUserId", StringComparison.OrdinalIgnoreCase));
    }

    // === Validator ===

    [Fact]
    public void Validator_rejects_Page_below_1()
    {
        var validator = new GetCustomerOrders.Validator();

        var result = validator.Validate(
            new GetCustomerOrders.Query(0, 20, null, null, null, OrderSort.CreatedAtDesc));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(GetCustomerOrders.Query.Page));
    }

    [Fact]
    public void Validator_rejects_PageSize_above_max()
    {
        var validator = new GetCustomerOrders.Validator();

        var result = validator.Validate(
            new GetCustomerOrders.Query(1, GetCustomerOrders.MaxPageSize + 1, null, null, null, OrderSort.CreatedAtDesc));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(GetCustomerOrders.Query.PageSize));
    }

    [Fact]
    public void Validator_rejects_inverted_date_range()
    {
        var validator = new GetCustomerOrders.Validator();
        var from = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

        var result = validator.Validate(
            new GetCustomerOrders.Query(1, 20, null, from, to, OrderSort.CreatedAtDesc));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName.Contains("DateFrom"));
    }

    [Fact]
    public void Validator_accepts_happy_path()
    {
        var validator = new GetCustomerOrders.Validator();

        var result = validator.Validate(
            new GetCustomerOrders.Query(1, 20, OrderState.Paid,
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
                OrderSort.CreatedAtDesc));

        result.IsValid.Should().BeTrue();
    }
}
