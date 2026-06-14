using FluentAssertions;
using Makables.Core.AppServices.Features.Payouts;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Payouts;
using Makables.Core.Domain.Payouts.Queries;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Payouts;

/// <summary>
/// T-0112 <see cref="GetMakerOutboxEventsForOrder"/> handler contract: maker
/// resolution short-circuit, both scoping params (makerId + orderId) forwarded
/// to the projection, and the payload-free DTO shape (no PayloadJson /
/// LastErrorCode / RetryCount).
/// </summary>
public class GetMakerOutboxEventsForOrderHandlerTests
{
    private const string UserId = "user-maker-1";
    private const string MakerId = "maker-1";
    private const string OrderId = "ord-1";

    private static readonly DateTimeOffset SnapshotAt = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly IPayoutQueries _queries = Substitute.For<IPayoutQueries>();
    private readonly IMakerRepository _makers = Substitute.For<IMakerRepository>();
    private readonly IUserSessionProvider _session = Substitute.For<IUserSessionProvider>();
    private readonly GetMakerOutboxEventsForOrder.Handler _sut;

    public GetMakerOutboxEventsForOrderHandlerTests()
    {
        _session.GetUserId().Returns(UserId);
        _makers.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(ExistingMaker());
        _sut = new GetMakerOutboxEventsForOrder.Handler(_queries, _makers, _session);
    }

    private static Makables.Core.Domain.Makers.Maker ExistingMaker() => Makables.Core.Domain.Makers.Maker.Create(
        id: MakerId, userId: UserId, registrationNumber: "27074358", vatId: null,
        companyName: "Avast s.r.o.", legalForm: null, registeredAddressId: "addr-1",
        incorporatedOn: null, isActiveInRegistry: true, sourceRegistry: "ares",
        snapshotFetchedAt: SnapshotAt, snapshotIsStale: false, countryCode: "CZ");

    [Fact]
    public async Task Happy_path_returns_PagedData()
    {
        var paged = new PagedData<MakerOutboxEventDto>(
            new[]
            {
                new MakerOutboxEventDto("order.paid.customerEmail", OutboxDeliveryStatus.Processed,
                    new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero)),
                new MakerOutboxEventDto("order.shipped.customerEmail", OutboxDeliveryStatus.Scheduled,
                    new DateTimeOffset(2026, 6, 2, 10, 0, 0, TimeSpan.Zero)),
            },
            1, 20, 2);
        _queries.GetMakerOutboxEventsForOrderAsync(MakerId, OrderId, 1, 20, Arg.Any<CancellationToken>())
            .Returns(paged);

        var result = await _sut.Handle(
            new GetMakerOutboxEventsForOrder.Query(OrderId, 1, 20), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Events.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task No_maker_row_returns_MakerNotFound_and_queries_untouched()
    {
        _makers.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns((Makables.Core.Domain.Makers.Maker?)null);

        var result = await _sut.Handle(
            new GetMakerOutboxEventsForOrder.Query(OrderId, 1, 20), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.MakerNotFound);
        await _queries.DidNotReceive().GetMakerOutboxEventsForOrderAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resolved_makerId_and_orderId_are_forwarded()
    {
        _queries.GetMakerOutboxEventsForOrderAsync(MakerId, OrderId, 1, 20, Arg.Any<CancellationToken>())
            .Returns(PagedData<MakerOutboxEventDto>.Empty(1, 20));

        await _sut.Handle(
            new GetMakerOutboxEventsForOrder.Query(OrderId, 1, 20), CancellationToken.None);

        await _queries.Received(1).GetMakerOutboxEventsForOrderAsync(
            MakerId, OrderId, 1, 20, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void OutboxEvent_dto_is_payload_free()
    {
        var props = typeof(MakerOutboxEventDto).GetProperties()
            .Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        props.Should().Contain("EventType");
        props.Should().Contain("Status");
        props.Should().Contain("OccurredAt");
        props.Should().NotContain(p => p.Contains("Payload", StringComparison.OrdinalIgnoreCase));
        props.Should().NotContain(p => p.Contains("Error", StringComparison.OrdinalIgnoreCase));
        props.Should().NotContain(p => p.Contains("Retry", StringComparison.OrdinalIgnoreCase));
    }
}
