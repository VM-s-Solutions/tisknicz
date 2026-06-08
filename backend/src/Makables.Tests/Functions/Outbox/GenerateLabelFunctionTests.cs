using System.Text.Json;
using FluentAssertions;
using Makables.Core.AppServices.Features.Shipping;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Outbox;
using Makables.Functions.Outbox;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Makables.Tests.Functions.Outbox;

/// <summary>
/// Pins T-0074 <see cref="GenerateLabelFunction"/> as a thin queue-trigger
/// wrapper: load outbox row → deserialise payload → dispatch
/// <see cref="FetchAndStoreShippingLabel.Command"/> via Mediator → log on
/// success → throw on every failure path so the Azure queue retry policy
/// fires.
/// </summary>
public class GenerateLabelFunctionTests
{
    private const string OutboxEventId = "ob-lbl-1";
    private const string OrderId = "ord-1";
    private const string BlobPath = "cz/orders/ord-1/label.pdf";

    private readonly IOutboxConsumerRepository _outbox = Substitute.For<IOutboxConsumerRepository>();
    private readonly ISender _mediator = Substitute.For<ISender>();
    private readonly GenerateLabelFunction _sut;

    public GenerateLabelFunctionTests()
    {
        _sut = new GenerateLabelFunction(_outbox, _mediator,
            NullLogger<GenerateLabelFunction>.Instance);
    }

    private static OutboxEvent CreateEnqueuedEvent(string payloadJson) =>
        OutboxEvent.Enqueue(
            id: OutboxEventId,
            aggregateId: OrderId,
            eventType: OutboxEventTypes.ShippingGenerateLabel,
            payloadJson: payloadJson,
            now: DateTimeOffset.Parse("2026-06-08T10:00:00Z"));

    private static string ValidPayload(string orderId = OrderId) =>
        JsonSerializer.Serialize(new GenerateLabelOutboxPayload(orderId));

    [Fact]
    public async Task Happy_path_dispatches_FetchAndStoreShippingLabel_for_payload_OrderId()
    {
        var evt = CreateEnqueuedEvent(ValidPayload());
        _outbox.GetByIdAsync(OutboxEventId, Arg.Any<CancellationToken>()).Returns(evt);
        _mediator.Send(Arg.Any<FetchAndStoreShippingLabel.Command>(), Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(new FetchAndStoreShippingLabel.Response(BlobPath)));

        await _sut.Run(OutboxEventId, CancellationToken.None);

        await _mediator.Received(1).Send(
            Arg.Is<FetchAndStoreShippingLabel.Command>(c => c.OrderId == OrderId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Empty_queue_message_throws_for_poison_routing()
    {
        var act = async () => await _sut.Run("", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await _outbox.DidNotReceive().GetByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Outbox_row_not_found_throws_for_queue_retry()
    {
        _outbox.GetByIdAsync(OutboxEventId, Arg.Any<CancellationToken>())
            .Returns((OutboxEvent?)null);

        var act = async () => await _sut.Run(OutboxEventId, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"OutboxEvent {OutboxEventId} not found.");
        await _mediator.DidNotReceive()
            .Send(Arg.Any<FetchAndStoreShippingLabel.Command>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Malformed_payload_json_throws_for_queue_retry()
    {
        var evt = CreateEnqueuedEvent("{not valid json");
        _outbox.GetByIdAsync(OutboxEventId, Arg.Any<CancellationToken>()).Returns(evt);

        var act = async () => await _sut.Run(OutboxEventId, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*Malformed GenerateLabelOutboxPayload for {OutboxEventId}*");
        await _mediator.DidNotReceive()
            .Send(Arg.Any<FetchAndStoreShippingLabel.Command>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Payload_with_blank_OrderId_throws_without_dispatch()
    {
        var evt = CreateEnqueuedEvent(ValidPayload(orderId: ""));
        _outbox.GetByIdAsync(OutboxEventId, Arg.Any<CancellationToken>()).Returns(evt);

        var act = async () => await _sut.Run(OutboxEventId, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await _mediator.DidNotReceive()
            .Send(Arg.Any<FetchAndStoreShippingLabel.Command>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Mediator_failure_re_throws_for_queue_retry()
    {
        var evt = CreateEnqueuedEvent(ValidPayload());
        _outbox.GetByIdAsync(OutboxEventId, Arg.Any<CancellationToken>()).Returns(evt);
        _mediator.Send(Arg.Any<FetchAndStoreShippingLabel.Command>(), Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Failure<FetchAndStoreShippingLabel.Response>(
                Error.Transient(BusinessErrorMessage.ShippingCarrierUnavailable)));

        var act = async () => await _sut.Run(OutboxEventId, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*FetchAndStoreShippingLabel failed for outbox {OutboxEventId}*");
    }
}
