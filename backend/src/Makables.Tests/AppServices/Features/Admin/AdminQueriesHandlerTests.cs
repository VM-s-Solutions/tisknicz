using FluentAssertions;
using Makables.Core.AppServices.Features.Admin;
using Makables.Core.Domain.Admin;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Invoices;
using Makables.Core.Domain.Orders;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Admin;

/// <summary>
/// Unit tests for the T-0111 admin read handlers: filter pass-through +
/// paging into <see cref="IAdminQueries"/>, plus the Validator clamps.
/// </summary>
public sealed class AdminQueriesHandlerTests
{
    private readonly IAdminQueries _admin = Substitute.For<IAdminQueries>();

    private static PagedData<AdminOrderListItemDto> EmptyOrders() =>
        PagedData<AdminOrderListItemDto>.Empty(1, 20);

    private static PagedData<AdminInvoiceListItemDto> EmptyInvoices() =>
        PagedData<AdminInvoiceListItemDto>.Empty(1, 20);

    private static PagedData<AdminAuditLogItemDto> EmptyAudit() =>
        PagedData<AdminAuditLogItemDto>.Empty(1, 20);

    // === GetAllOrders ===

    [Fact]
    public async Task GetAllOrders_happy_path_passes_default_filter()
    {
        _admin.GetAllOrdersPagedAsync(Arg.Any<AdminOrderFilter>(), 1, 20, Arg.Any<CancellationToken>())
            .Returns(EmptyOrders());
        var handler = new GetAllOrders.Handler(_admin);

        var result = await handler.Handle(
            new GetAllOrders.Query(1, 20, null, null, null, null), default);

        result.IsSuccess.Should().BeTrue();
        await _admin.Received(1).GetAllOrdersPagedAsync(
            Arg.Is<AdminOrderFilter>(f =>
                f.State == null && f.CountryCode == null && f.MakerId == null && f.CustomerEmail == null),
            1, 20, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAllOrders_filter_by_state_and_country_passes_through()
    {
        _admin.GetAllOrdersPagedAsync(Arg.Any<AdminOrderFilter>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(EmptyOrders());
        var handler = new GetAllOrders.Handler(_admin);

        await handler.Handle(new GetAllOrders.Query(1, 20, OrderState.Paid, "CZ", null, null), default);

        await _admin.Received(1).GetAllOrdersPagedAsync(
            Arg.Is<AdminOrderFilter>(f => f.State == OrderState.Paid && f.CountryCode == "CZ"),
            1, 20, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAllOrders_filter_by_maker_and_customerEmail_passes_through()
    {
        _admin.GetAllOrdersPagedAsync(Arg.Any<AdminOrderFilter>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(EmptyOrders());
        var handler = new GetAllOrders.Handler(_admin);

        await handler.Handle(new GetAllOrders.Query(1, 20, null, null, "maker-7", "jana"), default);

        await _admin.Received(1).GetAllOrdersPagedAsync(
            Arg.Is<AdminOrderFilter>(f => f.MakerId == "maker-7" && f.CustomerEmail == "jana"),
            1, 20, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void GetAllOrders_validator_rejects_pageSize_above_50()
    {
        var result = new GetAllOrders.Validator().Validate(
            new GetAllOrders.Query(1, 51, null, null, null, null));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(GetAllOrders.Query.PageSize));
    }

    // === GetAllInvoices ===

    [Fact]
    public async Task GetAllInvoices_filter_by_type_and_dateRange_passes_through()
    {
        _admin.GetAllInvoicesPagedAsync(Arg.Any<AdminInvoiceFilter>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(EmptyInvoices());
        var handler = new GetAllInvoices.Handler(_admin);
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

        await handler.Handle(new GetAllInvoices.Query(1, 20, InvoiceType.Fee, null, null, from, to), default);

        await _admin.Received(1).GetAllInvoicesPagedAsync(
            Arg.Is<AdminInvoiceFilter>(f =>
                f.Type == InvoiceType.Fee && f.DateRangeStart == from && f.DateRangeEnd == to),
            1, 20, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAllInvoices_filter_by_recipient_passes_through()
    {
        _admin.GetAllInvoicesPagedAsync(Arg.Any<AdminInvoiceFilter>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(EmptyInvoices());
        var handler = new GetAllInvoices.Handler(_admin);

        await handler.Handle(new GetAllInvoices.Query(1, 20, null, null, "jvm", null, null), default);

        await _admin.Received(1).GetAllInvoicesPagedAsync(
            Arg.Is<AdminInvoiceFilter>(f => f.Recipient == "jvm"),
            1, 20, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void GetAllInvoices_validator_rejects_inverted_date_range()
    {
        var from = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var result = new GetAllInvoices.Validator().Validate(
            new GetAllInvoices.Query(1, 20, null, null, null, from, to));

        result.IsValid.Should().BeFalse();
    }

    // === GetAdminAuditLog ===

    [Fact]
    public async Task GetAdminAuditLog_filter_by_adminUser_and_actionCode_passes_through()
    {
        _admin.GetAdminAuditLogPagedAsync(Arg.Any<AdminAuditLogFilter>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(EmptyAudit());
        var handler = new GetAdminAuditLog.Handler(_admin);

        await handler.Handle(new GetAdminAuditLog.Query(1, 20, "admin-1", "order.refund", null, null, null), default);

        await _admin.Received(1).GetAdminAuditLogPagedAsync(
            Arg.Is<AdminAuditLogFilter>(f => f.AdminUserId == "admin-1" && f.ActionCode == "order.refund"),
            1, 20, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAdminAuditLog_filter_by_targetEntity_and_dateRange_passes_through()
    {
        _admin.GetAdminAuditLogPagedAsync(Arg.Any<AdminAuditLogFilter>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(EmptyAudit());
        var handler = new GetAdminAuditLog.Handler(_admin);
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

        await handler.Handle(new GetAdminAuditLog.Query(1, 20, null, null, "order", from, to), default);

        await _admin.Received(1).GetAdminAuditLogPagedAsync(
            Arg.Is<AdminAuditLogFilter>(f =>
                f.TargetEntity == "order" && f.DateRangeStart == from && f.DateRangeEnd == to),
            1, 20, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void GetAdminAuditLog_validator_rejects_page_below_1()
    {
        var result = new GetAdminAuditLog.Validator().Validate(
            new GetAdminAuditLog.Query(0, 20, null, null, null, null, null));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(GetAdminAuditLog.Query.Page));
    }
}
