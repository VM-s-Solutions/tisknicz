using FluentAssertions;
using Makables.Core.AppServices.Common;
using Makables.Core.AppServices.Features.Orders;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Orders;
using Makables.Functions.Disputes;
using MediatR;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Makables.Tests.Functions.Disputes;

/// <summary>
/// Pins T-0145 <see cref="DisputeAutoEscalationFunction"/> as a thin
/// MediatR-dispatch wrapper mirroring
/// <see cref="Makables.Functions.Delivery.AutoDeliverOrdersFunction"/>
/// (T-0077): per-dispute Command dispatch, fail-continue per row, a
/// structured end-of-sweep summary log, and an empty batch logging zero
/// counts cleanly. The handler-level guards (resolved / already-escalated
/// / maker-replied) are covered by <c>EscalateDisputeHandlerTests</c>;
/// this suite only pins the Function's dispatch-loop shape.
/// </summary>
public class DisputeAutoEscalationFunctionTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-09T09:00:00Z");
    private static readonly TimerInfo Timer = new();

    private readonly IDisputeRepository _disputes = Substitute.For<IDisputeRepository>();
    private readonly ISender _mediator = Substitute.For<ISender>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly ILogger<DisputeAutoEscalationFunction> _logger =
        Substitute.For<ILogger<DisputeAutoEscalationFunction>>();
    private readonly DisputeAutoEscalationFunction _sut;

    public DisputeAutoEscalationFunctionTests()
    {
        _clock.UtcNow.Returns(Now);
        _sut = new DisputeAutoEscalationFunction(_disputes, _mediator, _clock, _logger);
    }

    private static async IAsyncEnumerable<string> AsAsyncEnumerable(params string[] ids)
    {
        foreach (var id in ids)
        {
            yield return id;
            await Task.Yield();
        }
    }

    [Fact]
    public async Task Happy_path_3_disputes_dispatches_3_EscalateDispute_commands()
    {
        _disputes.GetAutoEscalationCandidateIdsUnscopedReadOnlyAsync(Now, Arg.Any<CancellationToken>())
            .Returns(AsAsyncEnumerable("dsp-1", "dsp-2", "dsp-3"));
        _mediator.Send(Arg.Any<EscalateDispute.Command>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(BusinessResult.Success()));

        await _sut.RunAsync(Timer, CancellationToken.None);

        await _mediator.Received(1).Send(
            Arg.Is<EscalateDispute.Command>(c => c.DisputeId == "dsp-1"), Arg.Any<CancellationToken>());
        await _mediator.Received(1).Send(
            Arg.Is<EscalateDispute.Command>(c => c.DisputeId == "dsp-2"), Arg.Any<CancellationToken>());
        await _mediator.Received(1).Send(
            Arg.Is<EscalateDispute.Command>(c => c.DisputeId == "dsp-3"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Fail_continue_on_per_dispute_BusinessResult_Failure_does_not_stall_batch()
    {
        _disputes.GetAutoEscalationCandidateIdsUnscopedReadOnlyAsync(Now, Arg.Any<CancellationToken>())
            .Returns(AsAsyncEnumerable("dsp-1", "dsp-2", "dsp-3"));
        _mediator.Send(Arg.Any<EscalateDispute.Command>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var cmd = (EscalateDispute.Command)call[0];
                return Task.FromResult(cmd.DisputeId == "dsp-2"
                    ? BusinessResult.Failure(Error.Conflict("state", BusinessErrorMessage.OrderDisputeNotOpen))
                    : BusinessResult.Success());
            });

        await _sut.RunAsync(Timer, CancellationToken.None);

        await _mediator.Received(3).Send(Arg.Any<EscalateDispute.Command>(), Arg.Any<CancellationToken>());
        _logger.Received().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("dsp-2")
                             && o.ToString()!.Contains(BusinessErrorMessage.OrderDisputeNotOpen)),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task Fail_continue_on_per_dispute_exception_does_not_stall_batch()
    {
        _disputes.GetAutoEscalationCandidateIdsUnscopedReadOnlyAsync(Now, Arg.Any<CancellationToken>())
            .Returns(AsAsyncEnumerable("dsp-1", "dsp-2", "dsp-3"));
        _mediator.Send(Arg.Any<EscalateDispute.Command>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var cmd = (EscalateDispute.Command)call[0];
                if (cmd.DisputeId == "dsp-2")
                    throw new InvalidOperationException("simulated handler crash");
                return Task.FromResult(BusinessResult.Success());
            });

        var act = async () => await _sut.RunAsync(Timer, CancellationToken.None);
        await act.Should().NotThrowAsync();

        await _mediator.Received(3).Send(Arg.Any<EscalateDispute.Command>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Empty_batch_does_not_dispatch_and_logs_information_summary()
    {
        _disputes.GetAutoEscalationCandidateIdsUnscopedReadOnlyAsync(Now, Arg.Any<CancellationToken>())
            .Returns(AsAsyncEnumerable());

        await _sut.RunAsync(Timer, CancellationToken.None);

        await _mediator.DidNotReceive().Send(Arg.Any<EscalateDispute.Command>(), Arg.Any<CancellationToken>());
        _logger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("claimed 0")
                             && o.ToString()!.Contains("dispatched 0")
                             && o.ToString()!.Contains("failed 0")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }
}
