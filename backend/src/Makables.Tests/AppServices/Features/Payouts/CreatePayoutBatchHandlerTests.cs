using FluentAssertions;
using Makables.Core.AppServices.Features.Auth;
using Makables.Core.AppServices.Features.Payouts;
using Makables.Core.Domain.Auditing;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Numbering;
using Makables.Core.Domain.Observability;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Payouts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Payouts;

/// <summary>
/// T-0102a <see cref="CreatePayoutBatch.Handler"/> contract: fail-closed
/// session check, the eligible/excluded partition (via the pure
/// <see cref="PayoutEligibility"/> spec), the re-run / week / empty /
/// currency guards, the claim mechanics (sum of MakerPayout, set-once
/// AssignToPayoutBatch per order), and the handler-written audit row.
/// The artifact service is mocked (its own tests live in T-0102b).
/// </summary>
public class CreatePayoutBatchHandlerTests
{
    private const string CountryCode = "CZ";
    private const string AdminUserId = "user-admin-1";

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-15T10:00:00Z"); // Monday W25

    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IPayoutBatchRepository _payoutBatches = Substitute.For<IPayoutBatchRepository>();
    private readonly IPayoutBatchNumberGenerator _numberGenerator = Substitute.For<IPayoutBatchNumberGenerator>();
    private readonly ICountryConfigurationRepository _countries = Substitute.For<ICountryConfigurationRepository>();
    private readonly IAdminAuditLogWriter _auditWriter = Substitute.For<IAdminAuditLogWriter>();
    private readonly IUserSessionProvider _session = Substitute.For<IUserSessionProvider>();
    private readonly IIdGenerator _ids = Substitute.For<IIdGenerator>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly IPayoutMetrics _metrics = Substitute.For<IPayoutMetrics>();
    private readonly IPayoutArtifactService _artifacts = Substitute.For<IPayoutArtifactService>();
    private readonly CreatePayoutBatch.Handler _sut;

    public CreatePayoutBatchHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _session.GetUserId().Returns(AdminUserId);
        _ids.Next().Returns(_ => Guid.NewGuid().ToString("N"));
        _numberGenerator.For(CountryCode, Arg.Any<DateOnly>()).Returns("VYP-CZ-2026-W25");
        _countries.GetByCodeAsync(CountryCode, Arg.Any<CancellationToken>()).Returns(BuildConfig());
        _artifacts.GenerateAsync(Arg.Any<PayoutBatch>(), Arg.Any<CancellationToken>())
            .Returns(new PayoutArtifactResult(Complete: true, FeeInvoiceCount: 2, CsvReady: true));

        var defaultCountry = Options.Create(new AuthDefaultCountryOptions { CountryCodePrimary = CountryCode });

        _sut = new CreatePayoutBatch.Handler(
            _orders, _payoutBatches, _numberGenerator, _countries, defaultCountry,
            _auditWriter, _session, _ids, _clock, _metrics, _artifacts,
            NullLogger<CreatePayoutBatch.Handler>.Instance);
    }

    private static CountryConfiguration BuildConfig() => CountryConfiguration.Create(
        countryId: "CZ", defaultCurrencyCode: "CZK", defaultLanguageCode: "cs-CZ",
        timeZoneId: "Europe/Prague", phonePrefix: "+420", dateFormat: "d. M. yyyy",
        standardVatRateBp: 2100, taxIdLabel: "DIČ", vatIdLabel: "DIČ", registrationNumberLabel: "IČO",
        defaultPaymentProvider: "comgate", defaultShippingCarrier: "packeta",
        defaultRegistry: "ares", defaultEmailProvider: "sendgrid",
        issuerName: "JVM YORE s.r.o.", issuerIco: "00000000");

    private static Order DeliveredOrder(string id, string makerId, long payout, string currency = "CZK")
    {
        // product + shipping == total; makerPayout + platformFee == total.
        var product = payout; // keep platformFee 0 → maker payout == total for arithmetic simplicity
        var o = Order.Create(
            id: id, orderNumber: $"M-CZ-{id}",
            customerUserId: "user-1", makerId: makerId, productId: "prod-1",
            contactName: "Anna", contactEmail: "anna@example.cz", contactPhone: "+420",
            productPriceAmountMinor: product, shippingPriceAmountMinor: 0,
            platformFeeAmountMinor: 0, makerPayoutAmountMinor: payout,
            totalAmountMinor: product, currency: currency, vatRateBp: 2100,
            shippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
            zasilkovnaPickupPointId: "pp-1", countryCode: "CZ");
        var c = Substitute.For<IClock>();
        c.UtcNow.Returns(Now.AddDays(-10));
        o.MarkAsPaid(c, $"tx-{id}");
        o.Accept(c);
        o.Ship(c, $"PKT-{id}", 7);
        o.MarkAsDelivered(c, OrderDeliverySource.Auto);
        return o;
    }

    private static PayoutCandidate Candidate(Order order, string makerId, string? bank, string company = "Co s.r.o.") =>
        new(order, makerId, company, bank);

    // === 1. Happy path ===

    [Fact]
    public async Task Happy_path_claims_eligible_orders_across_makers()
    {
        var o1 = DeliveredOrder("o1", "maker-A", 10000);
        var o2 = DeliveredOrder("o2", "maker-A", 5000);
        var o3 = DeliveredOrder("o3", "maker-B", 7000);
        _orders.GetPayoutEligibleUnscopedAsync(CountryCode, Arg.Any<CancellationToken>())
            .Returns(new List<PayoutCandidate>
            {
                Candidate(o1, "maker-A", "111/0100"),
                Candidate(o2, "maker-A", "111/0100"),
                Candidate(o3, "maker-B", "222/0800"),
            });

        PayoutBatch? captured = null;
        await _payoutBatches.AddAsync(Arg.Do<PayoutBatch>(b => captured = b), Arg.Any<CancellationToken>());

        var result = await _sut.Handle(new CreatePayoutBatch.Command(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.State.Should().Be(PayoutBatchState.Processing);
        result.Value.TotalAmountMinor.Should().Be(22000);
        result.Value.OrderCount.Should().Be(3);
        result.Value.MakerCount.Should().Be(2);
        result.Value.AlreadyExisted.Should().BeFalse();

        o1.PayoutBatchId.Should().NotBeNull();
        o2.PayoutBatchId.Should().NotBeNull();
        o3.PayoutBatchId.Should().NotBeNull();

        await _payoutBatches.Received(1).AddAsync(Arg.Any<PayoutBatch>(), Arg.Any<CancellationToken>());
        await _auditWriter.Received(1).AppendAsync(
            Arg.Is<AdminAuditLogEntry>(e => e.ActionCode == "payoutBatch.create" && e.TargetEntity == "payout_batch"),
            Arg.Any<CancellationToken>());
        _metrics.Received(1).RecordRun("created");
    }

    // === 2. Re-run with an open batch ===

    [Fact]
    public async Task Re_run_with_open_batch_returns_existing_without_claim()
    {
        var open = PayoutBatch.Create("pb-open", "VYP-CZ-2026-W25", "CZ", 5000, "CZK", 1, 1, 0, 0, 0);
        _payoutBatches.GetOpenBatchAsync(CountryCode, Arg.Any<CancellationToken>()).Returns(open);

        var result = await _sut.Handle(new CreatePayoutBatch.Command(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AlreadyExisted.Should().BeTrue();
        result.Value.BatchId.Should().Be("pb-open");

        await _payoutBatches.DidNotReceive().AddAsync(Arg.Any<PayoutBatch>(), Arg.Any<CancellationToken>());
        await _auditWriter.DidNotReceive().AppendAsync(Arg.Any<AdminAuditLogEntry>(), Arg.Any<CancellationToken>());
        _metrics.Received(1).RecordRun("already_open");
    }

    // === 3. Empty run ===

    [Fact]
    public async Task Empty_run_returns_payoutBatch_empty_with_no_row()
    {
        var refunded = DeliveredOrder("o1", "maker-A", 10000);
        refunded.Refund(Substitute.For<IClock>(), 1, acknowledgePostPayout: false);
        _orders.GetPayoutEligibleUnscopedAsync(CountryCode, Arg.Any<CancellationToken>())
            .Returns(new List<PayoutCandidate> { Candidate(refunded, "maker-A", "111/0100") });

        var result = await _sut.Handle(new CreatePayoutBatch.Command(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.PayoutBatchEmpty);
        await _payoutBatches.DidNotReceive().AddAsync(Arg.Any<PayoutBatch>(), Arg.Any<CancellationToken>());
        _metrics.Received(1).RecordRun("empty");
    }

    // === 4. Exclusions partitioned ===

    [Fact]
    public async Task Exclusions_partitioned_only_eligible_claimed()
    {
        var eligible = DeliveredOrder("o1", "maker-A", 10000);
        var refunded = DeliveredOrder("o2", "maker-B", 5000);
        refunded.Refund(Substitute.For<IClock>(), 1, acknowledgePostPayout: false);
        var noBank = DeliveredOrder("o3", "maker-C", 3000);

        _orders.GetPayoutEligibleUnscopedAsync(CountryCode, Arg.Any<CancellationToken>())
            .Returns(new List<PayoutCandidate>
            {
                Candidate(eligible, "maker-A", "111/0100"),
                Candidate(refunded, "maker-B", "222/0800"),
                Candidate(noBank, "maker-C", null),
            });

        var result = await _sut.Handle(new CreatePayoutBatch.Command(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.OrderCount.Should().Be(1);
        result.Value.TotalAmountMinor.Should().Be(10000);
        result.Value.ExcludedPartiallyRefundedOrderCount.Should().Be(1);
        result.Value.ExcludedNoBankAccountOrderCount.Should().Be(1);
        result.Value.ExcludedNoBankAccountMakerCount.Should().Be(1);

        eligible.PayoutBatchId.Should().NotBeNull();
        refunded.PayoutBatchId.Should().BeNull();
        noBank.PayoutBatchId.Should().BeNull();
    }

    // === 5. Week guard ===

    [Fact]
    public async Task Week_guard_returns_weekAlreadyProcessed()
    {
        var closed = PayoutBatch.Create("pb-closed", "VYP-CZ-2026-W25", "CZ", 5000, "CZK", 1, 1, 0, 0, 0);
        _payoutBatches.GetByNumberAsync(CountryCode, "VYP-CZ-2026-W25", Arg.Any<CancellationToken>())
            .Returns(closed);

        var result = await _sut.Handle(new CreatePayoutBatch.Command(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.PayoutBatchWeekAlreadyProcessed);
        await _payoutBatches.DidNotReceive().AddAsync(Arg.Any<PayoutBatch>(), Arg.Any<CancellationToken>());
        _metrics.Received(1).RecordRun("week_already_processed");
    }

    // === 6. TZ week boundary ===

    [Fact]
    public async Task Tz_boundary_sunday_2330_utc_uses_monday_local_date()
    {
        // Sunday 2026-06-14 23:30 UTC = Monday 2026-06-15 01:30 Prague (CEST).
        _clock.UtcNow.Returns(DateTimeOffset.Parse("2026-06-14T23:30:00Z"));
        _orders.GetPayoutEligibleUnscopedAsync(CountryCode, Arg.Any<CancellationToken>())
            .Returns(new List<PayoutCandidate>
            {
                Candidate(DeliveredOrder("o1", "maker-A", 10000), "maker-A", "111/0100"),
            });

        await _sut.Handle(new CreatePayoutBatch.Command(), CancellationToken.None);

        _numberGenerator.Received().For(
            CountryCode,
            Arg.Is<DateOnly>(d => d == new DateOnly(2026, 6, 15)));
    }

    // === 7. Fail-closed session ===

    [Fact]
    public async Task Empty_session_fails_closed_unauthorized()
    {
        _session.GetUserId().Returns((string?)null);

        var result = await _sut.Handle(new CreatePayoutBatch.Command(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);
        await _payoutBatches.DidNotReceive().AddAsync(Arg.Any<PayoutBatch>(), Arg.Any<CancellationToken>());
    }

    // === 8. Currency mismatch ===

    [Fact]
    public async Task Currency_mismatch_returns_failure()
    {
        _orders.GetPayoutEligibleUnscopedAsync(CountryCode, Arg.Any<CancellationToken>())
            .Returns(new List<PayoutCandidate>
            {
                Candidate(DeliveredOrder("o1", "maker-A", 10000, "CZK"), "maker-A", "111/0100"),
                Candidate(DeliveredOrder("o2", "maker-B", 5000, "EUR"), "maker-B", "222/0800"),
            });

        var result = await _sut.Handle(new CreatePayoutBatch.Command(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.PayoutBatchCurrencyMismatch);
        _metrics.Received(1).RecordRun("currency_mismatch");
    }
}
