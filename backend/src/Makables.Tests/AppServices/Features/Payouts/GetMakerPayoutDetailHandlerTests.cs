using FluentAssertions;
using Makables.Core.AppServices.Features.Payouts;
using Makables.Core.AppServices.Features.Payouts.DTOs;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Payouts;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Payouts;

/// <summary>
/// T-0112 <see cref="GetMakerPayoutDetail"/> handler contract: maker
/// resolution short-circuit, the "no oracle" null projection → NotFound, and
/// the per-order breakdown DTO-shape reconciliation invariant.
/// </summary>
public class GetMakerPayoutDetailHandlerTests
{
    private const string UserId = "user-maker-1";
    private const string MakerId = "maker-1";
    private const string BatchId = "pb-1";

    private static readonly DateTimeOffset SnapshotAt = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly IPayoutQueries _queries = Substitute.For<IPayoutQueries>();
    private readonly IMakerRepository _makers = Substitute.For<IMakerRepository>();
    private readonly IUserSessionProvider _session = Substitute.For<IUserSessionProvider>();
    private readonly GetMakerPayoutDetail.Handler _sut;

    public GetMakerPayoutDetailHandlerTests()
    {
        _session.GetUserId().Returns(UserId);
        _makers.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(ExistingMaker());
        _sut = new GetMakerPayoutDetail.Handler(_queries, _makers, _session);
    }

    private static Maker ExistingMaker() => Maker.Create(
        id: MakerId, userId: UserId, registrationNumber: "27074358", vatId: null,
        companyName: "Avast s.r.o.", legalForm: null, registeredAddressId: "addr-1",
        incorporatedOn: null, isActiveInRegistry: true, sourceRegistry: "ares",
        snapshotFetchedAt: SnapshotAt, snapshotIsStale: false, countryCode: "CZ");

    [Fact]
    public async Task Happy_path_returns_detail_with_orders_intact()
    {
        var detail = new MakerPayoutDetailDto(
            BatchId: BatchId, BatchNumber: "VYP-CZ-2026-W24",
            State: PayoutBatchState.Completed, MakerTotalPaidMinor: 15000, Currency: "CZK",
            CompletedAt: new DateTimeOffset(2026, 6, 11, 0, 0, 0, TimeSpan.Zero),
            FeeInvoiceId: "inv-1",
            Orders: new[]
            {
                new MakerPayoutOrderLineDto("o1", "M-CZ-1", 11000, 0, 1000, 10000, "CZK"),
                new MakerPayoutOrderLineDto("o2", "M-CZ-2", 5500, 0, 500, 5000, "CZK"),
            });
        _queries.GetMakerPayoutDetailAsync(MakerId, BatchId, Arg.Any<CancellationToken>()).Returns(detail);

        var result = await _sut.Handle(new GetMakerPayoutDetail.Query(BatchId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Detail.Orders.Should().HaveCount(2);
        result.Value.Detail.Orders.Sum(o => o.MakerPayoutAmountMinor)
            .Should().Be(result.Value.Detail.MakerTotalPaidMinor);
    }

    [Fact]
    public async Task No_maker_row_returns_MakerNotFound_and_queries_untouched()
    {
        _makers.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns((Maker?)null);

        var result = await _sut.Handle(new GetMakerPayoutDetail.Query(BatchId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.MakerNotFound);
        await _queries.DidNotReceive().GetMakerPayoutDetailAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Null_projection_returns_NotFound_no_oracle()
    {
        _queries.GetMakerPayoutDetailAsync(MakerId, BatchId, Arg.Any<CancellationToken>())
            .Returns((MakerPayoutDetailDto?)null);

        var result = await _sut.Handle(new GetMakerPayoutDetail.Query(BatchId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public void OrderLine_dto_reconciliation_fields_present_no_contact_pii()
    {
        var props = typeof(MakerPayoutOrderLineDto).GetProperties()
            .Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        props.Should().Contain("ProductPriceMinor");
        props.Should().Contain("PlatformFeeAmountMinor");
        props.Should().Contain("MakerPayoutAmountMinor");
        props.Should().NotContain(p => p.Contains("Email", StringComparison.OrdinalIgnoreCase));
        props.Should().NotContain(p => p.Contains("Address", StringComparison.OrdinalIgnoreCase));
    }
}
