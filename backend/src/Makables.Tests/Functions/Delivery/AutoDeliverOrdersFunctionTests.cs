using FluentAssertions;
using Makables.Core.AppServices.Common;
using Makables.Core.AppServices.Features.Orders;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Observability;
using Makables.Core.Domain.Orders;
using Makables.Functions.Delivery;
using MediatR;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Makables.Tests.Functions.Delivery;

/// <summary>
/// Pins T-0077 <see cref="AutoDeliverOrdersFunction"/> as a thin
/// MediatR-dispatch wrapper:
/// - per-Order Command dispatch with Source = Auto,
/// - fail-continue per row (one bad order does NOT stall the batch),
/// - structured end-of-sweep summary log,
/// - empty batch logs zero counts cleanly,
/// - T-0076 silent-Success on already-Delivered race surfaces as
///   dispatched (writer no-ops without re-emitting the outbox event).
/// </summary>
public class AutoDeliverOrdersFunctionTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-09T08:00:00Z");
    private static readonly TimerInfo Timer = new();

    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly ISender _mediator = Substitute.For<ISender>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly ILogger<AutoDeliverOrdersFunction> _logger =
        Substitute.For<ILogger<AutoDeliverOrdersFunction>>();
    private readonly IOrderLifecycleMetrics _metrics = Substitute.For<IOrderLifecycleMetrics>();
    private readonly AutoDeliverOrdersFunction _sut;

    public AutoDeliverOrdersFunctionTests()
    {
        _clock.UtcNow.Returns(Now);
        _sut = new AutoDeliverOrdersFunction(_orders, _mediator, _clock, _metrics, _logger);
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
    public async Task Happy_path_3_orders_dispatches_3_commands_with_Source_Auto()
    {
        _orders.GetAutoDeliverableUnscopedReadOnlyAsync(Now, Arg.Any<CancellationToken>())
            .Returns(AsAsyncEnumerable("order-1", "order-2", "order-3"));
        _mediator.Send(Arg.Any<MarkOrderDelivered.Command>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var cmd = (MarkOrderDelivered.Command)call[0];
                return Task.FromResult(BusinessResult.Success(
                    new MarkOrderDelivered.MarkOrderDeliveredResponse(cmd.OrderId, OrderState.Delivered)));
            });

        await _sut.RunAsync(Timer, CancellationToken.None);

        await _mediator.Received(1).Send(
            Arg.Is<MarkOrderDelivered.Command>(c => c.OrderId == "order-1" && c.Source == OrderDeliverySource.Auto),
            Arg.Any<CancellationToken>());
        await _mediator.Received(1).Send(
            Arg.Is<MarkOrderDelivered.Command>(c => c.OrderId == "order-2" && c.Source == OrderDeliverySource.Auto),
            Arg.Any<CancellationToken>());
        await _mediator.Received(1).Send(
            Arg.Is<MarkOrderDelivered.Command>(c => c.OrderId == "order-3" && c.Source == OrderDeliverySource.Auto),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Fail_continue_on_per_order_BusinessResult_Failure_does_not_stall_batch()
    {
        _orders.GetAutoDeliverableUnscopedReadOnlyAsync(Now, Arg.Any<CancellationToken>())
            .Returns(AsAsyncEnumerable("order-1", "order-2", "order-3"));
        _mediator.Send(Arg.Any<MarkOrderDelivered.Command>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var cmd = (MarkOrderDelivered.Command)call[0];
                return Task.FromResult(cmd.OrderId == "order-2"
                    ? BusinessResult.Failure<MarkOrderDelivered.MarkOrderDeliveredResponse>(
                        Error.Conflict("state", BusinessErrorMessage.OrderInvalidTransition))
                    : BusinessResult.Success(
                        new MarkOrderDelivered.MarkOrderDeliveredResponse(cmd.OrderId, OrderState.Delivered)));
            });

        await _sut.RunAsync(Timer, CancellationToken.None);

        // All 3 commands dispatched — NOT short-circuited at order-2.
        await _mediator.Received(3).Send(
            Arg.Any<MarkOrderDelivered.Command>(), Arg.Any<CancellationToken>());

        // Warning log fired for the failed order.
        _logger.Received().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("order-2")
                             && o.ToString()!.Contains(BusinessErrorMessage.OrderInvalidTransition)),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task Fail_continue_on_per_order_exception_does_not_stall_batch()
    {
        // Unexpected exception (not OperationCanceledException) — caught,
        // logged, batch continues.
        _orders.GetAutoDeliverableUnscopedReadOnlyAsync(Now, Arg.Any<CancellationToken>())
            .Returns(AsAsyncEnumerable("order-1", "order-2", "order-3"));
        _mediator.Send(Arg.Any<MarkOrderDelivered.Command>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var cmd = (MarkOrderDelivered.Command)call[0];
                if (cmd.OrderId == "order-2")
                    throw new InvalidOperationException("simulated handler crash");
                return Task.FromResult(BusinessResult.Success(
                    new MarkOrderDelivered.MarkOrderDeliveredResponse(cmd.OrderId, OrderState.Delivered)));
            });

        var act = async () => await _sut.RunAsync(Timer, CancellationToken.None);
        await act.Should().NotThrowAsync();

        await _mediator.Received(3).Send(
            Arg.Any<MarkOrderDelivered.Command>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Empty_batch_does_not_dispatch_and_logs_information_summary()
    {
        _orders.GetAutoDeliverableUnscopedReadOnlyAsync(Now, Arg.Any<CancellationToken>())
            .Returns(AsAsyncEnumerable());

        await _sut.RunAsync(Timer, CancellationToken.None);

        await _mediator.DidNotReceive().Send(
            Arg.Any<MarkOrderDelivered.Command>(), Arg.Any<CancellationToken>());
        _logger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("claimed 0")
                             && o.ToString()!.Contains("dispatched 0")
                             && o.ToString()!.Contains("failed 0")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task Already_delivered_race_writer_returns_Success_counts_as_dispatched()
    {
        // T-0076 Silent Success contract: customer or T-0078 hit first;
        // the writer returns Success no-op (no state change, no second
        // outbox event). The Function sees Success and counts it as
        // dispatched, NOT failed.
        _orders.GetAutoDeliverableUnscopedReadOnlyAsync(Now, Arg.Any<CancellationToken>())
            .Returns(AsAsyncEnumerable("order-1"));
        _mediator.Send(Arg.Any<MarkOrderDelivered.Command>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var cmd = (MarkOrderDelivered.Command)call[0];
                return Task.FromResult(BusinessResult.Success(
                    new MarkOrderDelivered.MarkOrderDeliveredResponse(cmd.OrderId, OrderState.Delivered)));
            });

        await _sut.RunAsync(Timer, CancellationToken.None);

        _logger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("claimed 1")
                             && o.ToString()!.Contains("dispatched 1")
                             && o.ToString()!.Contains("failed 0")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }
}
