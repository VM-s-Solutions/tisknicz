using System.Text.Json;
using FluentAssertions;
using Makables.Core.AppServices.Common;
using Makables.Core.AppServices.Features.Payouts;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Outbox;
using Makables.Core.Domain.Payouts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Payouts;

/// <summary>
/// T-0103 <see cref="MarkPayoutBatchCompleted.Handler"/> contract: fail-closed
/// session check, Silent-Success idempotency on an already-Completed batch,
/// the materialized order loop (no per-order mediator.Send — Q-0008 MARS
/// lesson), and the per-maker grouping/dedup (one payout-sent email per
/// distinct maker, summed total).
/// </summary>
public class MarkPayoutBatchCompletedHandlerTests
{
    private const string BatchId = "pb-1";
    private const string AdminUserId = "user-admin-1";
    private const string WebBaseUrl = "https://makables.test";

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-13T09:30:00Z");

    private readonly IPayoutBatchRepository _payoutBatches = Substitute.For<IPayoutBatchRepository>();
    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IMakerRepository _makers = Substitute.For<IMakerRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IOutbox _outbox = Substitute.For<IOutbox>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly ILanguageResolver _languageResolver = Substitute.For<ILanguageResolver>();
    private readonly IUserSessionProvider _session = Substitute.For<IUserSessionProvider>();
    private readonly MarkPayoutBatchCompleted.Handler _sut;

    public MarkPayoutBatchCompletedHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _session.GetUserId().Returns(AdminUserId);
        _languageResolver.ResolveForUserAsync(Arg.Any<User>(), Arg.Any<CancellationToken>())
            .Returns("cs-CZ");

        // Two makers, both resolvable.
        ScriptMaker("maker-a", "user-maker-a", "Alpha s.r.o.");
        ScriptMaker("maker-b", "user-maker-b", "Beta s.r.o.");

        var urls = Options.Create(new PublicAppUrlsOptions { WebBaseUrl = WebBaseUrl });

        _sut = new MarkPayoutBatchCompleted.Handler(
            _payoutBatches, _orders, _makers, _users, _outbox, _clock,
            _languageResolver, urls, _session,
            NullLogger<MarkPayoutBatchCompleted.Handler>.Instance);
    }

    private void ScriptMaker(string makerId, string userId, string company)
    {
        var maker = Maker.Create(
            id: makerId, userId: userId, registrationNumber: makerId,
            vatId: null, companyName: company, legalForm: null,
            registeredAddressId: "addr-1", incorporatedOn: null,
            isActiveInRegistry: true, sourceRegistry: "ares",
            snapshotFetchedAt: Now, snapshotIsStale: false, countryCode: "CZ");
        _makers.GetByIdAsync(makerId, Arg.Any<CancellationToken>()).Returns(maker);

        var user = User.Create(userId, $"{makerId}@example.cz", UserRole.Maker, company, "CZ",
            passwordHash: "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        _users.GetByIdAsync(userId, Arg.Any<CancellationToken>()).Returns(user);
    }

    private static PayoutBatch ProcessingBatch() => PayoutBatch.Create(
        id: BatchId, batchNumber: "VYP-CZ-2026-W24", countryCode: "CZ",
        totalAmountMinor: 123456, currency: "CZK", orderCount: 3, makerCount: 2,
        excludedPartiallyRefundedOrderCount: 0, excludedNoBankAccountOrderCount: 0,
        excludedNoBankAccountMakerCount: 0);

    private static PayoutBatch CompletedBatch()
    {
        var batch = ProcessingBatch();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now.AddDays(-1));
        batch.Complete(clock, "REF-FIRST", new DateOnly(2026, 6, 11), "user-admin-prior");
        return batch;
    }

    private static Order DeliveredOrder(string id, string makerId, long payout)
    {
        var o = Order.Create(
            id: id, orderNumber: $"M-CZ-{id}", customerUserId: "user-customer-1",
            makerId: makerId, productId: "prod-1", contactName: "Anna",
            contactEmail: "anna@example.cz", contactPhone: "+420",
            productPriceAmountMinor: payout + 1500, shippingPriceAmountMinor: 0,
            platformFeeAmountMinor: 1500, makerPayoutAmountMinor: payout,
            totalAmountMinor: payout + 1500, currency: "CZK", vatRateBp: 2100,
            shippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
            zasilkovnaPickupPointId: "pp-1", countryCode: "CZ");
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now.AddDays(-3));
        o.MarkAsPaid(clock, $"tx-{id}");
        o.Accept(clock);
        o.Ship(clock, $"PKT-{id}", 7);
        o.MarkAsDelivered(clock, OrderDeliverySource.Auto);
        o.AssignToPayoutBatch(BatchId);
        return o;
    }

    private MarkPayoutBatchCompleted.Command Cmd(DateOnly? paymentDate = null) =>
        new(BatchId, "REF-123", paymentDate);

    [Fact]
    public async Task Happy_path_completes_batch_and_orders_and_one_email_per_maker()
    {
        var batch = ProcessingBatch();
        _payoutBatches.GetByIdUnscopedAsync(BatchId, Arg.Any<CancellationToken>()).Returns(batch);
        var orders = new[]
        {
            DeliveredOrder("o1", "maker-a", 10000),
            DeliveredOrder("o2", "maker-a", 5000),
            DeliveredOrder("o3", "maker-b", 7000),
        };
        _orders.GetByPayoutBatchIdForCompletionUnscopedAsync(BatchId, Arg.Any<CancellationToken>())
            .Returns(orders);

        var captured = new List<string>();
        _outbox.Enqueue(
            Arg.Any<string>(),
            OutboxEventTypes.PayoutBatchPayoutSentMakerEmail,
            Arg.Do<string>(captured.Add));

        var result = await _sut.Handle(Cmd(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AlreadyCompleted.Should().BeFalse();
        result.Value.State.Should().Be(PayoutBatchState.Completed);
        result.Value.OrderCount.Should().Be(3);
        result.Value.MakerCount.Should().Be(2);

        batch.State.Should().Be(PayoutBatchState.Completed);
        orders.Should().AllSatisfy(o => o.State.Should().Be(OrderState.Completed));

        // Exactly one email per distinct maker.
        captured.Should().HaveCount(2);
        var payloads = captured
            .Select(j => JsonSerializer.Deserialize<PayoutBatchPayoutSentMakerEmailPayload>(j)!)
            .ToList();
        var a = payloads.Single(p => p.MakerId == "maker-a");
        a.MakerTotalPaidMinor.Should().Be(15000);
        a.OrderCount.Should().Be(2);
        a.BatchNumber.Should().Be("VYP-CZ-2026-W24");
        a.FeeInvoiceActionUrl.Should().Be($"{WebBaseUrl}/dashboard/maker/vyplaty/{BatchId}");
        var b = payloads.Single(p => p.MakerId == "maker-b");
        b.MakerTotalPaidMinor.Should().Be(7000);
        b.OrderCount.Should().Be(1);
    }

    [Fact]
    public async Task Already_Completed_is_Silent_Success_with_no_side_effects()
    {
        var batch = CompletedBatch();
        _payoutBatches.GetByIdUnscopedAsync(BatchId, Arg.Any<CancellationToken>()).Returns(batch);

        var result = await _sut.Handle(Cmd(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AlreadyCompleted.Should().BeTrue();
        result.Value.State.Should().Be(PayoutBatchState.Completed);
        result.Value.BankReference.Should().Be("REF-FIRST");
        result.Value.CompletedAt.Should().Be(new DateTimeOffset(2026, 6, 11, 0, 0, 0, TimeSpan.Zero));

        // No order load, no email, no re-transition.
        await _orders.DidNotReceive().GetByPayoutBatchIdForCompletionUnscopedAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
        _outbox.DidNotReceive().Enqueue(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Single_maker_many_orders_gets_one_email_with_summed_total()
    {
        var batch = ProcessingBatch();
        _payoutBatches.GetByIdUnscopedAsync(BatchId, Arg.Any<CancellationToken>()).Returns(batch);
        var orders = new[]
        {
            DeliveredOrder("o1", "maker-a", 1000),
            DeliveredOrder("o2", "maker-a", 2000),
            DeliveredOrder("o3", "maker-a", 3000),
            DeliveredOrder("o4", "maker-a", 4000),
            DeliveredOrder("o5", "maker-a", 5000),
        };
        _orders.GetByPayoutBatchIdForCompletionUnscopedAsync(BatchId, Arg.Any<CancellationToken>())
            .Returns(orders);

        var captured = new List<string>();
        _outbox.Enqueue(
            Arg.Any<string>(),
            OutboxEventTypes.PayoutBatchPayoutSentMakerEmail,
            Arg.Do<string>(captured.Add));

        var result = await _sut.Handle(Cmd(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        captured.Should().HaveCount(1);
        var payload = JsonSerializer.Deserialize<PayoutBatchPayoutSentMakerEmailPayload>(captured[0])!;
        payload.MakerTotalPaidMinor.Should().Be(15000);
        payload.OrderCount.Should().Be(5);
    }

    [Fact]
    public async Task Batch_not_found_returns_NotFound()
    {
        _payoutBatches.GetByIdUnscopedAsync(BatchId, Arg.Any<CancellationToken>())
            .Returns((PayoutBatch?)null);

        var result = await _sut.Handle(Cmd(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.PayoutBatchNotFound);
        await _orders.DidNotReceive().GetByPayoutBatchIdForCompletionUnscopedAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
        _outbox.DidNotReceive().Enqueue(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Empty_session_returns_Unauthorized()
    {
        _session.GetUserId().Returns((string?)null);

        var result = await _sut.Handle(Cmd(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);
        await _payoutBatches.DidNotReceive().GetByIdUnscopedAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PaymentDate_provided_sets_CompletedAt_to_midnight_utc()
    {
        var batch = ProcessingBatch();
        _payoutBatches.GetByIdUnscopedAsync(BatchId, Arg.Any<CancellationToken>()).Returns(batch);
        _orders.GetByPayoutBatchIdForCompletionUnscopedAsync(BatchId, Arg.Any<CancellationToken>())
            .Returns(new[] { DeliveredOrder("o1", "maker-a", 1000) });

        var result = await _sut.Handle(Cmd(new DateOnly(2026, 6, 10)), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.CompletedAt.Should().Be(new DateTimeOffset(2026, 6, 10, 0, 0, 0, TimeSpan.Zero));
    }

    // === Validator ===

    [Fact]
    public void Validator_rejects_blank_BankReference()
    {
        var validator = new MarkPayoutBatchCompleted.Validator();
        validator.Validate(new MarkPayoutBatchCompleted.Command(BatchId, "", null))
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_rejects_oversize_BankReference()
    {
        var validator = new MarkPayoutBatchCompleted.Validator();
        var tooLong = new string('x', PayoutBatch.MaxBankReferenceLength + 1);
        validator.Validate(new MarkPayoutBatchCompleted.Command(BatchId, tooLong, null))
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_accepts_valid_command()
    {
        var validator = new MarkPayoutBatchCompleted.Validator();
        validator.Validate(new MarkPayoutBatchCompleted.Command(BatchId, "REF-1", new DateOnly(2026, 6, 10)))
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void Command_is_admin_auditable_with_bankReference_in_notes()
    {
        var cmd = new MarkPayoutBatchCompleted.Command(BatchId, "REF-9", null);
        cmd.ActionCode.Should().Be("payoutBatch.complete");
        cmd.TargetEntity.Should().Be("payout_batch");
        cmd.TargetId.Should().Be(BatchId);
        cmd.Notes.Should().Be("REF-9");
    }
}
