using FluentAssertions;
using Makables.Core.AppServices.Features.Orders;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Orders.Queries;
using Makables.Core.Domain.Orders.Sorting;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Orders;

/// <summary>
/// Unit tests pinning T-0081 <see cref="GetMakerOrders"/> behaviour:
/// - IDOR shield enforced TWICE (session → makerId resolution + projection predicate),
/// - MakerNotFound short-circuits before query dispatch,
/// - Filter / sort / page forwarding,
/// - DTO carries MakerPayoutAmountMinor and reserves UnreadMessageCount = null,
/// - DTO type-shape pins absence of CustomerEmail + PlatformFeeAmountMinor.
/// </summary>
public class GetMakerOrdersHandlerTests
{
    private const string UserId = "user-maker-1";
    private const string MakerId = "maker-1";

    private static readonly DateTimeOffset SnapshotAt = new(2026, 5, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly IOrderQueries _orderQueries = Substitute.For<IOrderQueries>();
    private readonly IMakerRepository _makers = Substitute.For<IMakerRepository>();
    private readonly IUserSessionProvider _session = Substitute.For<IUserSessionProvider>();
    private readonly GetMakerOrders.Handler _sut;

    public GetMakerOrdersHandlerTests()
    {
        _session.GetUserId().Returns(UserId);
        _makers.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(ExistingMaker());
        _sut = new GetMakerOrders.Handler(_orderQueries, _makers, _session);
    }

    private static Makables.Core.Domain.Makers.Maker ExistingMaker() =>
        Makables.Core.Domain.Makers.Maker.Create(
            id: MakerId, userId: UserId, registrationNumber: "27074358",
            vatId: null, companyName: "Avast s.r.o.", legalForm: null,
            registeredAddressId: "addr-1", incorporatedOn: null,
            isActiveInRegistry: true, sourceRegistry: "ares",
            snapshotFetchedAt: SnapshotAt, snapshotIsStale: false, countryCode: "CZ");

    private static MakerOrderListItemDto BuildItem(string id, OrderState state, long total, long payout) =>
        new(
            OrderId: id,
            OrderNumber: $"M-CZ-2026{id}",
            State: state,
            TotalAmountMinor: total,
            MakerPayoutAmountMinor: payout,
            Currency: "CZK",
            CreatedAt: new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero),
            CustomerContactName: "Anna",
            ShippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
            ProductTitle: "Hrnek",
            UnreadMessageCount: null);

    // === Handler ===

    [Fact]
    public async Task Happy_path_returns_PagedData_with_maker_scoped_orders()
    {
        var paged = new PagedData<MakerOrderListItemDto>(
            new[]
            {
                BuildItem("ord-1", OrderState.Paid, 1000, 800),
                BuildItem("ord-2", OrderState.Shipped, 2000, 1700),
                BuildItem("ord-3", OrderState.Delivered, 3000, 2600),
            },
            Page: 1, PageSize: 20, TotalCount: 3);
        _orderQueries.GetMakerOrdersPagedAsync(
                MakerId, Arg.Any<OrderFilter>(), Arg.Any<OrderSort>(), 1, 20, Arg.Any<CancellationToken>())
            .Returns(paged);

        var result = await _sut.Handle(
            new GetMakerOrders.Query(1, 20, null, null, null, OrderSort.CreatedAtDesc),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Orders.Items.Should().HaveCount(3);
        result.Value.Orders.TotalCount.Should().Be(3);
    }

    [Fact]
    public async Task No_maker_row_for_user_returns_MakerNotFound()
    {
        _makers.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns((Makables.Core.Domain.Makers.Maker?)null);

        var result = await _sut.Handle(
            new GetMakerOrders.Query(1, 20, null, null, null, OrderSort.CreatedAtDesc),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be(BusinessErrorMessage.MakerNotFound);
        // IOrderQueries MUST NOT be invoked when the maker lookup fails.
        await _orderQueries.DidNotReceive().GetMakerOrdersPagedAsync(
            Arg.Any<string>(), Arg.Any<OrderFilter>(), Arg.Any<OrderSort>(),
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resolved_makerId_is_forwarded_to_OrderQueries()
    {
        // IDOR shield layer 1 — handler resolves the makerId from session,
        // never from input. Pin the substitute on the resolved id.
        _orderQueries.GetMakerOrdersPagedAsync(
                MakerId, Arg.Any<OrderFilter>(), Arg.Any<OrderSort>(), 1, 20, Arg.Any<CancellationToken>())
            .Returns(PagedData<MakerOrderListItemDto>.Empty(1, 20));

        await _sut.Handle(
            new GetMakerOrders.Query(1, 20, null, null, null, OrderSort.CreatedAtDesc),
            CancellationToken.None);

        await _orderQueries.Received(1).GetMakerOrdersPagedAsync(
            MakerId, Arg.Any<OrderFilter>(), Arg.Any<OrderSort>(), 1, 20, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task State_filter_is_forwarded_to_OrderFilter()
    {
        _orderQueries.GetMakerOrdersPagedAsync(
                MakerId, Arg.Any<OrderFilter>(), Arg.Any<OrderSort>(), 1, 20, Arg.Any<CancellationToken>())
            .Returns(PagedData<MakerOrderListItemDto>.Empty(1, 20));

        await _sut.Handle(
            new GetMakerOrders.Query(1, 20, OrderState.Paid, null, null, OrderSort.CreatedAtDesc),
            CancellationToken.None);

        await _orderQueries.Received(1).GetMakerOrdersPagedAsync(
            MakerId,
            Arg.Is<OrderFilter>(f => f.State == OrderState.Paid
                && f.DateRangeStart == null
                && f.DateRangeEnd == null),
            Arg.Any<OrderSort>(), 1, 20, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DateRange_filter_is_forwarded_to_OrderFilter()
    {
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 6, 30, 23, 59, 59, TimeSpan.Zero);
        _orderQueries.GetMakerOrdersPagedAsync(
                MakerId, Arg.Any<OrderFilter>(), Arg.Any<OrderSort>(), 1, 20, Arg.Any<CancellationToken>())
            .Returns(PagedData<MakerOrderListItemDto>.Empty(1, 20));

        await _sut.Handle(
            new GetMakerOrders.Query(1, 20, null, from, to, OrderSort.CreatedAtDesc),
            CancellationToken.None);

        await _orderQueries.Received(1).GetMakerOrdersPagedAsync(
            MakerId,
            Arg.Is<OrderFilter>(f => f.DateRangeStart == from && f.DateRangeEnd == to),
            Arg.Any<OrderSort>(), 1, 20, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Page_and_PageSize_are_forwarded()
    {
        _orderQueries.GetMakerOrdersPagedAsync(
                MakerId, Arg.Any<OrderFilter>(), Arg.Any<OrderSort>(), 3, 50, Arg.Any<CancellationToken>())
            .Returns(PagedData<MakerOrderListItemDto>.Empty(3, 50));

        await _sut.Handle(
            new GetMakerOrders.Query(3, 50, null, null, null, OrderSort.CreatedAtDesc),
            CancellationToken.None);

        await _orderQueries.Received(1).GetMakerOrdersPagedAsync(
            MakerId, Arg.Any<OrderFilter>(), Arg.Any<OrderSort>(), 3, 50, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void MakerOrderListItemDto_carries_MakerPayoutAmountMinor_and_has_no_CustomerEmail_or_PlatformFee()
    {
        // Compile-time gate. Adding any of these fields in a future PR
        // breaks the test.
        var props = typeof(MakerOrderListItemDto).GetProperties()
            .Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        props.Should().Contain("MakerPayoutAmountMinor");
        props.Should().Contain("UnreadMessageCount");
        props.Should().NotContain(p =>
            p.Contains("Email", StringComparison.OrdinalIgnoreCase));
        props.Should().NotContain(p =>
            p.Contains("PlatformFee", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UnreadMessageCount_is_nullable_int_until_T_0079()
    {
        // Type-level pin: future T-0079 flips only the projection logic
        // — DTO contract stays stable.
        var prop = typeof(MakerOrderListItemDto).GetProperty("UnreadMessageCount");
        prop.Should().NotBeNull();
        prop!.PropertyType.Should().Be(typeof(int?));
    }

    // === Validator ===

    [Fact]
    public void Validator_rejects_zero_page()
    {
        var validator = new GetMakerOrders.Validator();
        var result = validator.Validate(
            new GetMakerOrders.Query(0, 20, null, null, null, OrderSort.CreatedAtDesc));
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_rejects_oversized_PageSize()
    {
        var validator = new GetMakerOrders.Validator();
        var result = validator.Validate(
            new GetMakerOrders.Query(1, GetMakerOrders.MaxPageSize + 1, null, null, null, OrderSort.CreatedAtDesc));
        result.IsValid.Should().BeFalse();
    }

    // Gate 9 test-catchup item 2 — mirror the Customer-side
    // Validator_rejects_inverted_date_range test for the Maker validator.
    // Validators are coded identically; this guards against a future drift.
    [Fact]
    public void Validator_rejects_inverted_date_range()
    {
        var validator = new GetMakerOrders.Validator();
        var from = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

        var result = validator.Validate(
            new GetMakerOrders.Query(1, 20, null, from, to, OrderSort.CreatedAtDesc));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName.Contains("DateFrom"));
    }
}
