using System.Text.Json;
using FluentAssertions;
using Makables.Core.AppServices.Features.Invoices;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Outbox;
using Makables.Functions.Outbox;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Makables.Tests.Functions.Outbox;

/// <summary>
/// Pins T-0069 <see cref="GenerateInvoiceFunction"/> contract: load
/// outbox row → deserialise payload → dispatch <see cref="IssueInvoice.Command"/>
/// via Mediator → log on success → throw on every failure path so the
/// Azure queue retry policy fires.
///
/// <para>
/// Per T-0069's "Why GenerateInvoiceFunction throws instead of returning
/// BusinessResult" note + locked decision 5 (idempotency inherited):
/// the function is a thin queue-trigger wrapper. <see cref="IssueInvoice.Handler"/>
/// owns the duplicate-suppression check via <c>GetByOrderIdAsync</c>.
/// </para>
/// </summary>
public class GenerateInvoiceFunctionTests
{
    private const string OutboxEventId = "ob-inv-1";
    private const string OrderId = "ord-1";
    private const string InvoiceId = "inv-1";
    private const string InvoiceNumber = "FV-CZ-20260042";
    private const string BlobPath = "cz/orders/ord-1/FV-CZ-20260042.pdf";

    private readonly IOutboxConsumerRepository _outbox = Substitute.For<IOutboxConsumerRepository>();
    private readonly ISender _mediator = Substitute.For<ISender>();
    private readonly GenerateInvoiceFunction _sut;

    public GenerateInvoiceFunctionTests()
    {
        _sut = new GenerateInvoiceFunction(_outbox, _mediator,
            NullLogger<GenerateInvoiceFunction>.Instance);
    }

    private static OutboxEvent CreateEnqueuedEvent(string payloadJson) =>
        OutboxEvent.Enqueue(
            id: OutboxEventId,
            aggregateId: OrderId,
            eventType: OutboxEventTypes.InvoiceGenerate,
            payloadJson: payloadJson,
            now: DateTimeOffset.Parse("2026-06-07T10:00:00Z"));

    private static string ValidPayload(string orderId = OrderId, string languageCode = "cs-CZ") =>
        JsonSerializer.Serialize(new InvoiceGenerateOutboxPayload(orderId, languageCode));

    [Fact]
    public async Task Happy_path_dispatches_IssueInvoice_for_payload_OrderId()
    {
        // AC-2: thin Mediator dispatch wrapper. The Function loads the row,
        // deserialises the payload, dispatches IssueInvoice, logs the
        // returned InvoiceNumber.
        var evt = CreateEnqueuedEvent(ValidPayload());
        _outbox.GetByIdAsync(OutboxEventId, Arg.Any<CancellationToken>()).Returns(evt);
        _mediator.Send(Arg.Any<IssueInvoice.Command>(), Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Success(new IssueInvoice.Response(InvoiceId, InvoiceNumber, BlobPath)));

        await _sut.Run(OutboxEventId, CancellationToken.None);

        await _mediator.Received(1).Send(
            Arg.Is<IssueInvoice.Command>(c => c.OrderId == OrderId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Empty_queue_message_throws_for_poison_routing()
    {
        // Mirrors T-0029's SendEmailFunction poison-routing pattern: a
        // bad-shaped queue message dead-letters after maxDequeueCount.
        var act = async () => await _sut.Run("", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await _outbox.DidNotReceive().GetByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _mediator.DidNotReceive().Send(Arg.Any<IssueInvoice.Command>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Outbox_row_not_found_throws_for_queue_retry()
    {
        // A race between row delete + queue delivery — throw so the queue
        // retry policy redelivers and (1) the row reappears, or (2) the
        // message poison-routes after maxDequeueCount.
        _outbox.GetByIdAsync(OutboxEventId, Arg.Any<CancellationToken>())
            .Returns((OutboxEvent?)null);

        var act = async () => await _sut.Run(OutboxEventId, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"OutboxEvent {OutboxEventId} not found.");
        await _mediator.DidNotReceive().Send(Arg.Any<IssueInvoice.Command>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Malformed_payload_json_throws_for_queue_retry_then_poison()
    {
        var evt = CreateEnqueuedEvent("{not valid json");
        _outbox.GetByIdAsync(OutboxEventId, Arg.Any<CancellationToken>()).Returns(evt);

        var act = async () => await _sut.Run(OutboxEventId, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*Malformed InvoiceGenerateOutboxPayload for {OutboxEventId}*");
        await _mediator.DidNotReceive().Send(Arg.Any<IssueInvoice.Command>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Payload_with_blank_OrderId_throws_without_dispatch()
    {
        // Defence: producer-side bug puts an empty OrderId on the payload.
        // The Function refuses to dispatch IssueInvoice with an empty id
        // (the handler's validator would refuse it anyway, but failing
        // fast here keeps the queue-retry budget intact).
        var evt = CreateEnqueuedEvent(ValidPayload(orderId: ""));
        _outbox.GetByIdAsync(OutboxEventId, Arg.Any<CancellationToken>()).Returns(evt);

        var act = async () => await _sut.Run(OutboxEventId, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await _mediator.DidNotReceive().Send(Arg.Any<IssueInvoice.Command>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IssueInvoice_returns_Transient_failure_throws_for_queue_retry()
    {
        // E.g. blob upload returned Transient. The queue retry policy
        // retries until maxDequeueCount; on the happy retry, IssueInvoice
        // succeeds idempotently per T-0068b AC-5.
        var evt = CreateEnqueuedEvent(ValidPayload());
        _outbox.GetByIdAsync(OutboxEventId, Arg.Any<CancellationToken>()).Returns(evt);
        _mediator.Send(Arg.Any<IssueInvoice.Command>(), Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Failure<IssueInvoice.Response>(
                Error.Transient(BusinessErrorMessage.InvoiceBlobUploadFailed)));

        var act = async () => await _sut.Run(OutboxEventId, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*IssueInvoice failed for outbox {OutboxEventId}*");
    }

    [Fact]
    public async Task IssueInvoice_returns_Permanent_failure_throws_for_queue_dead_letter()
    {
        // E.g. InvoicingMode.ReverseCharge — Permanent, retry won't help.
        // Throw anyway: the queue retry policy will dead-letter after
        // maxDequeueCount; admin then investigates the poison queue.
        var evt = CreateEnqueuedEvent(ValidPayload());
        _outbox.GetByIdAsync(OutboxEventId, Arg.Any<CancellationToken>()).Returns(evt);
        _mediator.Send(Arg.Any<IssueInvoice.Command>(), Arg.Any<CancellationToken>())
            .Returns(BusinessResult.Failure<IssueInvoice.Response>(
                Error.Permanent(BusinessErrorMessage.InvoicingModeNotImplemented)));

        var act = async () => await _sut.Run(OutboxEventId, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Cancellation_propagates_through_to_mediator_send()
    {
        // The trigger's CancellationToken is the linked host-shutdown +
        // per-message timeout source; the Function must forward it so a
        // shutdown mid-render doesn't strand the message.
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var evt = CreateEnqueuedEvent(ValidPayload());
        _outbox.GetByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(evt);
        _mediator.Send(Arg.Any<IssueInvoice.Command>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var act = async () => await _sut.Run(OutboxEventId, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
