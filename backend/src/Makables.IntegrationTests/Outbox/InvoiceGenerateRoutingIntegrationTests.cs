using FluentAssertions;
using Makables.Core.AppServices.Common;
using Makables.Core.AppServices.Features.Outbox;
using Makables.Core.Domain.Outbox;
using Makables.Core.Domain.SeedWork;
using Makables.Infra.Database.Outbox;
using Makables.IntegrationTests.Common;
using Makables.TestUtilities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Makables.IntegrationTests.Outbox;

/// <summary>
/// T-0069 integration coverage for the routing branch on the
/// <see cref="OutboxDispatcher"/>. Seed one <c>invoice.generate</c> event
/// into Postgres via the real <see cref="OutboxConsumerRepository"/>; run
/// <see cref="OutboxDispatcher.DispatchDueAsync"/> with a fake
/// <see cref="IOutboxQueuePublisher"/>; assert that
/// <c>PublishGenerateInvoiceAsync</c> received the row's id (and
/// <c>PublishSendEmailAsync</c> did NOT).
///
/// <para>
/// This pins the contract end-to-end: the EF Core entity round-trip +
/// the dispatcher's classifier + the publisher routing branch all line
/// up against the canonical <c>OutboxEventTypes.InvoiceGenerate</c>
/// string. A regression in any of the three would fail this test — the
/// unit tests pin each piece in isolation but only the integration test
/// catches a "compiles fine but routes to the wrong queue at runtime"
/// regression.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class InvoiceGenerateRoutingIntegrationTests
{
    private readonly PostgresHarness _harness;

    public InvoiceGenerateRoutingIntegrationTests(PostgresHarness harness)
    {
        _harness = harness;
        _harness.ResetMutableTablesAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task DispatchDueAsync_routes_invoice_generate_event_to_PublishGenerateInvoiceAsync()
    {
        // Arrange: persist one invoice.generate outbox row through the
        // real DbContext, then dispatch using a fake queue publisher to
        // capture the routing decision.
        var clock = new FakeClock();
        var enqueuedAt = clock.UtcNow.AddMinutes(-5);
        const string outboxEventId = "ob-inv-routing-1";
        const string orderId = "ord-routing-1";

        await using (var seed = _harness.CreateDbContext())
        {
            var evt = OutboxEvent.Enqueue(
                id: outboxEventId,
                aggregateId: orderId,
                eventType: OutboxEventTypes.InvoiceGenerate,
                payloadJson: "{\"OrderId\":\"" + orderId + "\",\"LanguageCode\":\"cs-CZ\"}",
                now: enqueuedAt);
            seed.Add(evt);
            await seed.SaveChangesAsync();
        }

        // Build the dispatcher against a fresh scope's DbContext +
        // OutboxConsumerRepository + IUnitOfWork. Substitute the queue
        // publisher so we observe the routing call.
        await using var db = _harness.CreateDbContext();
        var consumerRepo = new OutboxConsumerRepository(db);
        // MakablesDbContext implements IUnitOfWork directly; pass through.
        IUnitOfWork unitOfWork = db;
        var fakeQueue = Substitute.For<IOutboxQueuePublisher>();
        var dispatcher = new OutboxDispatcher(
            consumerRepo,
            fakeQueue,
            unitOfWork,
            clock,
            Options.Create(new OutboxDispatcherOptions { HandoffParkMinutes = 15 }),
            NullLogger<OutboxDispatcher>.Instance);

        // Act
        var summary = await dispatcher.DispatchDueAsync(CancellationToken.None);

        // Assert: the invoice.generate event was routed to the
        // generate-invoice queue, NOT to send-email.
        summary.Loaded.Should().Be(1);
        summary.Routed.Should().Be(1);
        summary.Stalled.Should().Be(0);
        summary.FailedToPublish.Should().Be(0);

        await fakeQueue.Received(1).PublishGenerateInvoiceAsync(
            outboxEventId, Arg.Any<CancellationToken>());
        await fakeQueue.DidNotReceive().PublishSendEmailAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());

        // Defence-in-depth: the row was parked (NextRetryAt advanced past
        // the sweep window) so a concurrent sweep doesn't re-publish before
        // the consumer has confirmed processing. T-0029 sec reviewer item 8.
        await using var assertDb = _harness.CreateDbContext();
        var persisted = await new OutboxConsumerRepository(assertDb)
            .GetByIdAsync(outboxEventId, CancellationToken.None);
        persisted.Should().NotBeNull();
        persisted!.ProcessedAt.Should().BeNull(
            because: "ProcessedAt is the queue consumer's job; the dispatcher only parks the row");
        persisted.NextRetryAt.Should().BeAfter(clock.UtcNow,
            because: "parked rows are hidden from the next sweep until the consumer confirms");
    }
}
