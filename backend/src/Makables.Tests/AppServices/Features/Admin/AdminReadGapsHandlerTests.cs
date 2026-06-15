using FluentAssertions;
using Makables.Core.AppServices.Features.Admin;
using Makables.Core.AppServices.Features.CountryConfigurations;
using Makables.Core.Domain.Admin;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Outbox;
using Makables.Core.Domain.Payouts;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Admin;

/// <summary>
/// Unit tests for the T-0127 admin read-gap handlers + validators:
/// country-config GET field-set parity + 404 reuse; admin order-detail
/// projection pass-through + 404 reuse + no-redaction; admin-orders
/// <c>customerUserId</c> filter pass-through; stalled-outbox + payout-batch
/// list paging into their repos; the page-size clamp [1,50].
/// </summary>
public sealed class AdminReadGapsHandlerTests
{
    private readonly IAdminQueries _admin = Substitute.For<IAdminQueries>();
    private readonly ICountryConfigurationRepository _configs =
        Substitute.For<ICountryConfigurationRepository>();
    private readonly IOutboxConsumerRepository _outbox =
        Substitute.For<IOutboxConsumerRepository>();

    // === GetCountryConfiguration ===

    [Fact]
    public async Task GetCountryConfiguration_returns_the_full_editable_field_set()
    {
        var config = CountryConfiguration.Create(
            "CZ", "CZK", "cs-CZ", "Europe/Prague", "+420", "d. M. yyyy",
            2100, "DIČ", "DIČ DPH", "IČO",
            "comgate", "packeta", "ares", "resend",
            "JVM YORE s.r.o.", "00000000",
            reducedVatRateBp: 1200, invoicingMode: InvoicingMode.None,
            platformFeeRateBp: 1500, defaultShippingPriceMinor: 7900);
        _configs.GetByCodeAsync("CZ", Arg.Any<CancellationToken>()).Returns(config);
        var handler = new GetCountryConfiguration.Handler(_configs);

        var result = await handler.Handle(new GetCountryConfiguration.Query("CZ"), default);

        result.IsSuccess.Should().BeTrue();
        var body = result.Value!;
        body.CountryCode.Should().Be("CZ");
        body.StandardVatRateBp.Should().Be(2100);
        body.ReducedVatRateBp.Should().Be(1200);
        body.InvoicingMode.Should().Be(InvoicingMode.None);
        body.PlatformFeeRateBp.Should().Be(1500);
        body.DefaultShippingPriceMinor.Should().Be(7900);
        body.DefaultPaymentProvider.Should().Be("comgate");
        body.DefaultShippingCarrier.Should().Be("packeta");
        body.DefaultRegistry.Should().Be("ares");
        body.DefaultEmailProvider.Should().Be("resend");
    }

    [Fact]
    public async Task GetCountryConfiguration_unknown_code_reuses_notFound()
    {
        _configs.GetByCodeAsync("XX", Arg.Any<CancellationToken>())
            .Returns((CountryConfiguration?)null);
        var handler = new GetCountryConfiguration.Handler(_configs);

        var result = await handler.Handle(new GetCountryConfiguration.Query("XX"), default);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.CountryConfigurationNotFound);
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public void GetCountryConfiguration_validator_rejects_non_two_char_code()
    {
        var result = new GetCountryConfiguration.Validator()
            .Validate(new GetCountryConfiguration.Query("CZE"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(GetCountryConfiguration.Query.CountryCode));
    }

    // === GetAdminOrderDetail ===

    [Fact]
    public async Task GetAdminOrderDetail_returns_the_privileged_header_with_customerEmail()
    {
        var detail = SampleDetail();
        _admin.GetOrderDetailAsync("ord-1", Arg.Any<CancellationToken>()).Returns(detail);
        var handler = new GetAdminOrderDetail.Handler(_admin);

        var result = await handler.Handle(new GetAdminOrderDetail.Query("ord-1"), default);

        result.IsSuccess.Should().BeTrue();
        // No GDPR redaction — the privileged admin header carries the email.
        result.Value!.Order.CustomerEmail.Should().Be("anna@example.cz");
        result.Value.Order.ContactName.Should().Be("Anna");
        result.Value.Order.OrderNumber.Should().Be("M-CZ-20260001");
    }

    [Fact]
    public async Task GetAdminOrderDetail_unknown_id_reuses_OrderNotFound()
    {
        _admin.GetOrderDetailAsync("missing", Arg.Any<CancellationToken>())
            .Returns((AdminOrderDetailDto?)null);
        var handler = new GetAdminOrderDetail.Handler(_admin);

        var result = await handler.Handle(new GetAdminOrderDetail.Query("missing"), default);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.OrderNotFound);
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    // === Admin-orders customerUserId filter (the in-flight signal) ===

    [Fact]
    public async Task GetAllOrders_customerUserId_filter_passes_through()
    {
        _admin.GetAllOrdersPagedAsync(Arg.Any<AdminOrderFilter>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(PagedData<AdminOrderListItemDto>.Empty(1, 20));
        var handler = new GetAllOrders.Handler(_admin);

        await handler.Handle(
            new GetAllOrders.Query(1, 20, OrderState.Paid, null, null, null, "user-cust-a"), default);

        await _admin.Received(1).GetAllOrdersPagedAsync(
            Arg.Is<AdminOrderFilter>(f =>
                f.CustomerUserId == "user-cust-a" && f.State == OrderState.Paid),
            1, 20, Arg.Any<CancellationToken>());
    }

    // === GetStalledOutboxEvents ===

    [Fact]
    public async Task GetStalledOutboxEvents_pages_into_the_repo()
    {
        var dto = new StalledOutboxEventDto(
            "evt-1", "OrderPaidEmail", "ord-1", "email.transient", 5,
            new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero));
        _outbox.GetStalledPagedAsync(2, 10, Arg.Any<CancellationToken>())
            .Returns(new PagedData<StalledOutboxEventDto>(new[] { dto }, 2, 10, 11));
        var handler = new GetStalledOutboxEvents.Handler(_outbox);

        var result = await handler.Handle(new GetStalledOutboxEvents.Query(2, 10), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Events.TotalCount.Should().Be(11);
        result.Value.Events.Items.Single().Id.Should().Be("evt-1");
        result.Value.Events.Items.Single().LastErrorCode.Should().Be("email.transient");
    }

    [Fact]
    public void GetStalledOutboxEvents_validator_rejects_pageSize_above_50()
    {
        var result = new GetStalledOutboxEvents.Validator()
            .Validate(new GetStalledOutboxEvents.Query(1, 51));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(GetStalledOutboxEvents.Query.PageSize));
    }

    // === GetPayoutBatches ===

    [Fact]
    public async Task GetPayoutBatches_pages_into_the_admin_queries()
    {
        var dto = new AdminPayoutBatchListItemDto(
            "batch-1", "VYP-CZ-2026-W18", PayoutBatchState.Processing,
            100000, "CZK", 3, 2,
            new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero), null);
        _admin.GetPayoutBatchesPagedAsync(1, 20, Arg.Any<CancellationToken>())
            .Returns(new PagedData<AdminPayoutBatchListItemDto>(new[] { dto }, 1, 20, 1));
        var handler = new GetPayoutBatches.Handler(_admin);

        var result = await handler.Handle(new GetPayoutBatches.Query(1, 20), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Batches.Items.Single().BatchNumber.Should().Be("VYP-CZ-2026-W18");
        result.Value.Batches.Items.Single().State.Should().Be(PayoutBatchState.Processing);
    }

    [Fact]
    public void GetPayoutBatches_validator_rejects_page_below_1()
    {
        var result = new GetPayoutBatches.Validator()
            .Validate(new GetPayoutBatches.Query(0, 20));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(GetPayoutBatches.Query.Page));
    }

    private static AdminOrderDetailDto SampleDetail() => new(
        "ord-1", "M-CZ-20260001", OrderState.Paid, "CZ",
        "maker-x", "Avast s.r.o.", "prod-1", "Vase",
        "user-cust-a", "anna@example.cz", "Anna", "+420 723 456 789", null,
        50000, 7900, 7500, 50400, 57900, 0, "CZK", 2100,
        ShippingMethod.ZasilkovnaPickupPoint, "pp-42", null, null,
        null, null, null,
        new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero),
        null, null, null, null, null, null, null, null, true);
}
