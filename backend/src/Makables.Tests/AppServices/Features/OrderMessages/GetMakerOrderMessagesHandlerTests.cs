using FluentAssertions;
using Makables.Core.AppServices.Features.OrderMessages;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Makers;
using MakerEntity = Makables.Core.Domain.Makers.Maker;
using Makables.Core.Domain.OrderMessages;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.OrderMessages;

/// <summary>
/// T-0079 maker-host paged read handler — twin of
/// <see cref="GetCustomerOrderMessagesHandlerTests"/> (review-fold
/// BLOCKER-2). Pins the makerId resolution via
/// <c>IMakerRepository.GetByUserIdAsync</c> (never raw session id as
/// makerId), the no-session Unauthorized path, and the
/// no-maker-row → empty page (no leak) branch.
/// </summary>
public class GetMakerOrderMessagesHandlerTests
{
    private const string OrderId = "ord-1";
    private const string MakerUserId = "user-maker-1";
    private const string MakerId = "maker-1";

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-09T10:00:00Z");

    private readonly IOrderMessageQueries _queries = Substitute.For<IOrderMessageQueries>();
    private readonly IMakerRepository _makers = Substitute.For<IMakerRepository>();
    private readonly IUserSessionProvider _session = Substitute.For<IUserSessionProvider>();
    private readonly GetMakerOrderMessages.Handler _sut;

    public GetMakerOrderMessagesHandlerTests()
    {
        _session.GetUserId().Returns(MakerUserId);

        var maker = MakerEntity.Create(
            id: MakerId, userId: MakerUserId,
            registrationNumber: "27074358", vatId: null,
            companyName: "Maker s.r.o.", legalForm: null,
            registeredAddressId: "addr-1",
            incorporatedOn: null, isActiveInRegistry: true,
            sourceRegistry: "ares",
            snapshotFetchedAt: Now, snapshotIsStale: false,
            countryCode: "CZ", slug: "maker");
        _makers.GetByUserIdAsync(MakerUserId, Arg.Any<CancellationToken>()).Returns(maker);

        _sut = new GetMakerOrderMessages.Handler(_queries, _makers, _session);
    }

    [Fact]
    public async Task Happy_path_resolves_makerId_and_forwards_to_query()
    {
        var paged = new PagedData<OrderMessageDto>(
            Array.Empty<OrderMessageDto>(), Page: 1, PageSize: 50, TotalCount: 0);
        _queries.GetByOrderForMakerAsync(OrderId, MakerId, 1, 50, Arg.Any<CancellationToken>())
            .Returns(paged);

        var result = await _sut.Handle(
            new GetMakerOrderMessages.Query(OrderId, 1, 50),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Messages.TotalCount.Should().Be(0);
        // The query is scoped by the RESOLVED maker id — never the raw
        // session user id (compile-time IDOR shield contract).
        await _queries.Received(1).GetByOrderForMakerAsync(
            OrderId, MakerId, 1, 50, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task No_session_returns_Unauthorized()
    {
        _session.GetUserId().Returns((string?)null);

        var result = await _sut.Handle(
            new GetMakerOrderMessages.Query(OrderId, 1, 50),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);
        await _queries.DidNotReceive().GetByOrderForMakerAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Maker_audience_jwt_without_maker_row_returns_empty_page_without_querying()
    {
        // Leak-free contract: the response is indistinguishable from a
        // cross-tenant probe (empty page), and the query seam is never hit.
        _makers.GetByUserIdAsync(MakerUserId, Arg.Any<CancellationToken>())
            .Returns((MakerEntity?)null);

        var result = await _sut.Handle(
            new GetMakerOrderMessages.Query(OrderId, 1, 50),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Messages.Items.Should().BeEmpty();
        result.Value.Messages.TotalCount.Should().Be(0);
        await _queries.DidNotReceive().GetByOrderForMakerAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }
}
