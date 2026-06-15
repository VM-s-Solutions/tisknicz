using FluentAssertions;
using Makables.Core.AppServices.Features.Admin;
using Makables.Core.Domain.Outbox;
using Makables.Core.Domain.Payouts;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Admin;

/// <summary>
/// T-0126 / Q-0027 overview count handlers. Pins: each handler passes the
/// repository count straight through into its globally-unique response, with
/// no failure mode (read-only — empty set surfaces as { count: 0 }).
/// </summary>
public sealed class OverviewCountHandlerTests
{
    private readonly IPayoutBatchRepository _payoutBatches = Substitute.For<IPayoutBatchRepository>();
    private readonly IOutboxConsumerRepository _outbox = Substitute.For<IOutboxConsumerRepository>();

    [Fact]
    public async Task ProcessingPayoutsCount_passes_repo_count_through_for_requested_state()
    {
        _payoutBatches.CountByStateAsync(PayoutBatchState.Processing, Arg.Any<CancellationToken>())
            .Returns(3);
        var sut = new GetProcessingPayoutsCount.Handler(_payoutBatches);

        var result = await sut.Handle(
            new GetProcessingPayoutsCount.Query(PayoutBatchState.Processing), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Count.Should().Be(3);
        await _payoutBatches.Received(1).CountByStateAsync(
            PayoutBatchState.Processing, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessingPayoutsCount_empty_set_is_zero_not_failure()
    {
        _payoutBatches.CountByStateAsync(PayoutBatchState.Completed, Arg.Any<CancellationToken>())
            .Returns(0);
        var sut = new GetProcessingPayoutsCount.Handler(_payoutBatches);

        var result = await sut.Handle(
            new GetProcessingPayoutsCount.Query(PayoutBatchState.Completed), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Count.Should().Be(0);
    }

    [Fact]
    public async Task StalledOutboxCount_passes_repo_count_through()
    {
        _outbox.CountStalledAsync(Arg.Any<CancellationToken>()).Returns(5);
        var sut = new GetStalledOutboxCount.Handler(_outbox);

        var result = await sut.Handle(new GetStalledOutboxCount.Query(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Count.Should().Be(5);
        await _outbox.Received(1).CountStalledAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StalledOutboxCount_empty_set_is_zero_not_failure()
    {
        _outbox.CountStalledAsync(Arg.Any<CancellationToken>()).Returns(0);
        var sut = new GetStalledOutboxCount.Handler(_outbox);

        var result = await sut.Handle(new GetStalledOutboxCount.Query(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Count.Should().Be(0);
    }
}
