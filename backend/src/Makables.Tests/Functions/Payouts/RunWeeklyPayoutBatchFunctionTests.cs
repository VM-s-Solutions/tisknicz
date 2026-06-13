using FluentAssertions;
using Makables.Core.AppServices.Features.Payouts;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Payouts;
using Makables.Functions.Payouts;
using MediatR;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Makables.Tests.Functions.Payouts;

/// <summary>
/// Pins T-0104 <see cref="RunWeeklyPayoutBatchFunction"/> as a thin
/// MediatR-dispatch wrapper with the four-branch response interpretation
/// (§C): created / AlreadyExisted → Information; payoutBatch.empty →
/// Information (quiet week, NOT Warning); any other failure → Error. The
/// timer path never throws.
/// </summary>
public class RunWeeklyPayoutBatchFunctionTests
{
    private static readonly TimerInfo Timer = new();

    private readonly ISender _mediator = Substitute.For<ISender>();
    private readonly ILogger<RunWeeklyPayoutBatchFunction> _logger =
        Substitute.For<ILogger<RunWeeklyPayoutBatchFunction>>();
    private readonly RunWeeklyPayoutBatchFunction _sut;

    public RunWeeklyPayoutBatchFunctionTests()
    {
        _sut = new RunWeeklyPayoutBatchFunction(_mediator, _logger);
    }

    private static CreatePayoutBatch.CreatePayoutBatchResponse Response(bool alreadyExisted) => new(
        BatchId: "pb-1", BatchNumber: "VYP-CZ-2026-W25", State: PayoutBatchState.Processing,
        TotalAmountMinor: 30000, Currency: "CZK", OrderCount: 3, MakerCount: 2,
        ExcludedPartiallyRefundedOrderCount: 1, ExcludedNoBankAccountOrderCount: 1,
        ExcludedNoBankAccountMakerCount: 1, AlreadyExisted: alreadyExisted,
        ArtifactsComplete: true, FeeInvoiceCount: 2, CsvReady: true);

    private int LogCount(LogLevel level) =>
        _logger.ReceivedCalls()
            .Count(c => c.GetMethodInfo().Name == nameof(ILogger.Log)
                     && (LogLevel)c.GetArguments()[0]! == level);

    [Fact]
    public async Task Created_response_dispatches_once_and_logs_information_only()
    {
        _mediator.Send(Arg.Any<CreatePayoutBatch.Command>(), Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(Response(alreadyExisted: false)));

        await _sut.RunTimer(Timer, CancellationToken.None);

        await _mediator.Received(1).Send(Arg.Any<CreatePayoutBatch.Command>(), Arg.Any<CancellationToken>());
        LogCount(LogLevel.Information).Should().BeGreaterThanOrEqualTo(1);
        LogCount(LogLevel.Warning).Should().Be(0);
        LogCount(LogLevel.Error).Should().Be(0);
    }

    [Fact]
    public async Task Already_existed_logs_information_no_second_dispatch()
    {
        _mediator.Send(Arg.Any<CreatePayoutBatch.Command>(), Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(Response(alreadyExisted: true)));

        await _sut.RunTimer(Timer, CancellationToken.None);

        await _mediator.Received(1).Send(Arg.Any<CreatePayoutBatch.Command>(), Arg.Any<CancellationToken>());
        LogCount(LogLevel.Information).Should().BeGreaterThanOrEqualTo(1);
        LogCount(LogLevel.Warning).Should().Be(0);
        LogCount(LogLevel.Error).Should().Be(0);
    }

    [Fact]
    public async Task Empty_failure_is_information_quiet_week_not_warning_or_error()
    {
        _mediator.Send(Arg.Any<CreatePayoutBatch.Command>(), Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Failure<CreatePayoutBatch.CreatePayoutBatchResponse>(
                Error.Conflict("orders", BusinessErrorMessage.PayoutBatchEmpty)));

        await _sut.RunTimer(Timer, CancellationToken.None);

        LogCount(LogLevel.Information).Should().BeGreaterThanOrEqualTo(1);
        LogCount(LogLevel.Warning).Should().Be(0);
        LogCount(LogLevel.Error).Should().Be(0);
    }

    [Fact]
    public async Task Other_failure_logs_error_and_timer_does_not_throw()
    {
        _mediator.Send(Arg.Any<CreatePayoutBatch.Command>(), Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Failure<CreatePayoutBatch.CreatePayoutBatchResponse>(
                Error.Conflict("batchNumber", BusinessErrorMessage.PayoutBatchWeekAlreadyProcessed)));

        var act = async () => await _sut.RunTimer(Timer, CancellationToken.None);

        await act.Should().NotThrowAsync();
        LogCount(LogLevel.Error).Should().Be(1);
    }
}
