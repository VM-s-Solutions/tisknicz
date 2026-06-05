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

    public ConcurrentBag<Order> CreatePaymentCalls { get; } = new();

    public ConcurrentBag<string> VerifyPaymentCalls { get; } = new();

    public int CreatePaymentCallCount => CreatePaymentCalls.Count;
    public int VerifyPaymentCallCount => VerifyPaymentCalls.Count;

    public void EnqueueCreatePayment(BusinessResult<PaymentSession> response)
        => CreatePaymentResponses.Enqueue(response);

    public void EnqueueVerifyPayment(BusinessResult<PaymentStatus> response)
        => VerifyPaymentResponses.Enqueue(response);

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
        HttpRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException(
            "FakeComgatePaymentProvider: webhook parsing arrives in T-0066.");

    public Task<BusinessResult<RefundReceipt>> RefundAsync(
        string providerRef, long amountMinor, string currency, CancellationToken cancellationToken) =>
        throw new NotSupportedException(
            "FakeComgatePaymentProvider: refund arrives in T-0105.");
}
