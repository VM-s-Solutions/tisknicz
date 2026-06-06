using System.Collections.Concurrent;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Payments;
using Microsoft.AspNetCore.Http;

namespace Makables.IntegrationTests.Common;

/// <summary>
/// In-memory <see cref="IPaymentProvider"/> for integration tests. Records
/// calls so assertions can verify the verify-then-recreate decision tree
/// from <c>CreatePaymentSessionTests</c>; returns scripted responses
/// queued by the test setup.
///
/// <para>
/// Parallel to <see cref="FakeBlobStorageClient"/> — same in-process,
/// test-isolated pattern. The provider is injected by overriding the
/// keyed <see cref="IPaymentProvider"/> registration for the
/// <c>"comgate"</c> key in the WebApplicationFactory.
/// </para>
/// </summary>
public sealed class FakeComgatePaymentProvider : IPaymentProvider
{
    public string Code => "comgate";

    public ConcurrentQueue<BusinessResult<PaymentSession>> CreatePaymentResponses { get; } = new();

    public ConcurrentQueue<BusinessResult<PaymentStatus>> VerifyPaymentResponses { get; } = new();

    /// <summary>
    /// T-0066: scripted responses for the webhook adapter call. The
    /// production adapter reads the form body and re-fetches via
    /// VerifyPaymentAsync; the fake replays whatever the test queues so
    /// we exercise the controller flow without an HTTP round-trip.
    /// </summary>
    public ConcurrentQueue<BusinessResult<WebhookPayload>> WebhookResponses { get; } = new();

    public ConcurrentBag<Order> CreatePaymentCalls { get; } = new();

    public ConcurrentBag<string> VerifyPaymentCalls { get; } = new();

    /// <summary>
    /// T-0066: counts the number of times the webhook controller called
    /// <see cref="ParseAndVerifyWebhookAsync"/>. The fake doesn't expose
    /// the request itself because <see cref="HttpRequest"/> isn't
    /// snapshot-safe across the async boundary.
    /// </summary>
    public int WebhookCallCount { get; private set; }

    public int CreatePaymentCallCount => CreatePaymentCalls.Count;
    public int VerifyPaymentCallCount => VerifyPaymentCalls.Count;

    public void EnqueueCreatePayment(BusinessResult<PaymentSession> response)
        => CreatePaymentResponses.Enqueue(response);

    public void EnqueueVerifyPayment(BusinessResult<PaymentStatus> response)
        => VerifyPaymentResponses.Enqueue(response);

    public void EnqueueWebhook(BusinessResult<WebhookPayload> response)
        => WebhookResponses.Enqueue(response);

    public Task<BusinessResult<PaymentSession>> CreatePaymentAsync(
        Order order, CancellationToken cancellationToken)
    {
        CreatePaymentCalls.Add(order);
        if (CreatePaymentResponses.TryDequeue(out var scripted))
        {
            return Task.FromResult(scripted);
        }
        throw new InvalidOperationException(
            "FakeComgatePaymentProvider: no CreatePayment response was queued. " +
            "Call EnqueueCreatePayment before triggering the SUT.");
    }

    public Task<BusinessResult<PaymentStatus>> VerifyPaymentAsync(
        string providerRef, CancellationToken cancellationToken)
    {
        VerifyPaymentCalls.Add(providerRef);
        if (VerifyPaymentResponses.TryDequeue(out var scripted))
        {
            return Task.FromResult(scripted);
        }
        throw new InvalidOperationException(
            "FakeComgatePaymentProvider: no VerifyPayment response was queued. " +
            "Call EnqueueVerifyPayment before triggering the SUT.");
    }

    public Task<BusinessResult<WebhookPayload>> ParseAndVerifyWebhookAsync(
        HttpRequest request, CancellationToken cancellationToken)
    {
        WebhookCallCount++;
        if (WebhookResponses.TryDequeue(out var scripted))
        {
            return Task.FromResult(scripted);
        }
        throw new InvalidOperationException(
            "FakeComgatePaymentProvider: no webhook response was queued. " +
            "Call EnqueueWebhook before triggering the SUT.");
    }

    public Task<BusinessResult<RefundReceipt>> RefundAsync(
        string providerRef, long amountMinor, string currency, CancellationToken cancellationToken) =>
        throw new NotSupportedException(
            "FakeComgatePaymentProvider: refund arrives in T-0105.");
}
