using FluentAssertions;
using Makables.Core.AppServices.Features.Payouts;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Payouts;
using Makables.Core.Domain.Payouts.Queries;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Payouts;

/// <summary>
/// T-0112 <see cref="GetMakerPayouts"/> handler contract: IDOR shield layer 1
/// (session → makerId resolution, forwarded to the projection), MakerNotFound
/// short-circuit before any query, and page/pageSize forwarding. Plus the
/// compile-time DTO-shape gates (no BankReference / CsvBlobPath /
/// cross-maker TotalAmountMinor reference).
/// </summary>
public class GetMakerPayoutsHandlerTests
{
    private const string UserId = "user-maker-1";
    private const string MakerId = "maker-1";

    private static readonly DateTimeOffset SnapshotAt = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly IPayoutQueries _queries = Substitute.For<IPayoutQueries>();
    private readonly IMakerRepository _makers = Substitute.For<IMakerRepository>();
    private readonly IUserSessionProvider _session = Substitute.For<IUserSessionProvider>();
    private readonly GetMakerPayouts.Handler _sut;

    public GetMakerPayoutsHandlerTests()
    {
        _session.GetUserId().Returns(UserId);
        _makers.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(ExistingMaker());
        _sut = new GetMakerPayouts.Handler(_queries, _makers, _session);
    }

    private static Makables.Core.Domain.Makers.Maker ExistingMaker() => Makables.Core.Domain.Makers.Maker.Create(
        id: MakerId, userId: UserId, registrationNumber: "27074358", vatId: null,
        companyName: "Avast s.r.o.", legalForm: null, registeredAddressId: "addr-1",
        incorporatedOn: null, isActiveInRegistry: true, sourceRegistry: "ares",
        snapshotFetchedAt: SnapshotAt, snapshotIsStale: false, countryCode: "CZ");

    private static MakerPayoutListItemDto BuildItem(string batchId) => new(
        BatchId: batchId, BatchNumber: $"VYP-CZ-2026-W{batchId}",
        State: PayoutBatchState.Completed, MakerTotalPaidMinor: 10000, OrderCount: 2,
        Currency: "CZK", CompletedAt: new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
        FeeInvoiceId: "inv-1");

    [Fact]
    public async Task Happy_path_returns_PagedData()
    {
        var paged = new PagedData<MakerPayoutListItemDto>(
            new[] { BuildItem("23"), BuildItem("22"), BuildItem("21") }, 1, 20, 3);
        _queries.GetMakerPayoutsPagedAsync(MakerId, 1, 20, Arg.Any<CancellationToken>()).Returns(paged);

        var result = await _sut.Handle(new GetMakerPayouts.Query(1, 20), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Payouts.Items.Should().HaveCount(3);
        result.Value.Payouts.TotalCount.Should().Be(3);
    }

    [Fact]
    public async Task No_maker_row_returns_MakerNotFound_and_queries_untouched()
    {
        _makers.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns((Makables.Core.Domain.Makers.Maker?)null);

        var result = await _sut.Handle(new GetMakerPayouts.Query(1, 20), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.MakerNotFound);
        await _queries.DidNotReceive().GetMakerPayoutsPagedAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resolved_makerId_is_forwarded()
    {
        _queries.GetMakerPayoutsPagedAsync(MakerId, 2, 50, Arg.Any<CancellationToken>())
            .Returns(PagedData<MakerPayoutListItemDto>.Empty(2, 50));

        await _sut.Handle(new GetMakerPayouts.Query(2, 50), CancellationToken.None);

        await _queries.Received(1).GetMakerPayoutsPagedAsync(
            MakerId, 2, 50, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Validator_clamps_page_and_pageSize()
    {
        var validator = new GetMakerPayouts.Validator();
        validator.Validate(new GetMakerPayouts.Query(0, 20)).IsValid.Should().BeFalse();
        validator.Validate(new GetMakerPayouts.Query(1, 0)).IsValid.Should().BeFalse();
        validator.Validate(new GetMakerPayouts.Query(1, 51)).IsValid.Should().BeFalse();
        validator.Validate(new GetMakerPayouts.Query(1, 20)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void ListItem_dto_has_no_BankReference_or_CsvBlobPath_or_batch_total()
    {
        var props = typeof(MakerPayoutListItemDto).GetProperties()
            .Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        props.Should().Contain("MakerTotalPaidMinor");
        props.Should().NotContain(p => p.Contains("BankReference", StringComparison.OrdinalIgnoreCase));
        props.Should().NotContain(p => p.Contains("Csv", StringComparison.OrdinalIgnoreCase));
        // The cross-maker batch total (PayoutBatch.TotalAmountMinor) is never surfaced.
        props.Should().NotContain("TotalAmountMinor");
    }
}
